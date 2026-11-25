using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace StrikeMultiPadEditor;

/// <summary>
/// Creates a compact ZIP containing the backup structures needed for
/// continued sample-name and kit-reference reverse engineering.
/// Audio/sample payload data is deliberately excluded.
/// </summary>
public static class BackupAnalysisPackageExporter
{
    private const int PhysicalRecordSize = 10240;
    private const int KitPayloadOffset = 0x180;
    private const int KitPayloadSize = 9824;

    // This range contains the factory list tables and the sample/HAD/WAV
    // metadata locations previously identified in Backup005.bkf.
    private const int MetadataStart = 0x000A0000;
    private const int MetadataEnd = 0x00210000;

    private sealed record TableInfo(
        string FileName,
        string Marker,
        int HeaderOffset,
        int DataOffset,
        int Count,
        int RecordSize)
    {
        public int ByteLength => checked(Count * RecordSize);
    }

    public static void Export(string backupPath, string outputZipPath)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Backup file not found.", backupPath);

        byte[] data = File.ReadAllBytes(backupPath);

        if (data.Length < 8 ||
            !Encoding.ASCII.GetString(data, 0, 7).Equals("PACKBKF", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The selected file does not have a PACKBKF backup signature.");
        }

        string? outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputZipPath));
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        if (File.Exists(outputZipPath))
            File.Delete(outputZipPath);

        var manifest = new StringBuilder();
        manifest.AppendLine("STRIKE MULTIPAD BACKUP ANALYSIS PACKAGE");
        manifest.AppendLine("======================================");
        manifest.AppendLine($"Source file: {Path.GetFileName(backupPath)}");
        manifest.AppendLine($"Source size: {data.Length:N0} bytes");
        manifest.AppendLine($"SHA-256: {Convert.ToHexString(SHA256.HashData(data))}");
        manifest.AppendLine("Audio/sample payload data is NOT included.");
        manifest.AppendLine("The package contains kit records, factory index tables, and metadata regions.");
        manifest.AppendLine();

        using var file = new FileStream(outputZipPath, FileMode.CreateNew, FileAccess.ReadWrite);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);

        AddText(archive, "README.txt",
            "This is a compact analysis extract from an Alesis Strike MultiPad PACKBKF backup.\r\n" +
            "It includes every detected KITC/KITH physical record, the factory list tables,\r\n" +
            "the KITI-to-50xx translation run, the backup prefix, and the metadata region.\r\n" +
            "It does not include the large audio/sample payload data.\r\n" +
            "Offsets in the manifest refer to the original backup file.\r\n");

        // Preserve the file header and early structural area.
        AddRange(archive, "backup_prefix_00000000.bin",
            data, 0, Math.Min(MetadataStart, data.Length));
        manifest.AppendLine($"Prefix: 0x00000000 - 0x{Math.Min(MetadataStart, data.Length):X8}");

        // Preserve the known metadata region as one contiguous binary slice.
        int metadataLength = Math.Max(0, Math.Min(data.Length, MetadataEnd) - MetadataStart);
        if (metadataLength > 0)
        {
            AddRange(archive, "metadata/metadata_region.bin",
                data, MetadataStart, metadataLength);
            manifest.AppendLine(
                $"Metadata region: 0x{MetadataStart:X8} - 0x{MetadataStart + metadataLength:X8} ({metadataLength:N0} bytes)");
            AddAsciiRuns(archive, "metadata/metadata_ascii_strings.txt",
                data, MetadataStart, MetadataStart + metadataLength, 4);
        }

        // Export every complete physical backup record that contains a KITC/KITH payload.
        int kitCount = 0;
        var kitIndex = new StringBuilder();
        kitIndex.AppendLine("KIT RECORD INDEX");
        kitIndex.AppendLine("Number | Type | Physical offset | Payload offset | Name");

        for (int recordOffset = 0x40;
             recordOffset + PhysicalRecordSize <= data.Length;
             recordOffset += PhysicalRecordSize)
        {
            int payloadOffset = recordOffset + KitPayloadOffset;
            if (payloadOffset + 4 > data.Length)
                continue;

            string type = Encoding.ASCII.GetString(data, payloadOffset, 4);
            if (type != "KITC" && type != "KITH")
                continue;

            kitCount++;
            string kitName = type == "KITH"
                ? $"Empty Slot {kitCount:D3}"
                : ReadKitName(data, payloadOffset);

            if (string.IsNullOrWhiteSpace(kitName))
                kitName = $"Kit record {kitCount:D3}";

            string entryName =
                $"kit_records/Kit_{kitCount:D3}_{type}_at_{recordOffset:X8}.bin";

            AddRange(archive, entryName, data, recordOffset, PhysicalRecordSize);

            kitIndex.AppendLine(
                $"{kitCount:D3} | {type} | 0x{recordOffset:X8} | " +
                $"0x{payloadOffset:X8} | {Sanitize(kitName)}");
        }

        manifest.AppendLine($"KITC/KITH physical records exported: {kitCount}");
        manifest.AppendLine($"Physical record size: {PhysicalRecordSize:N0} bytes");
        manifest.AppendLine($"Payload offset within record: 0x{KitPayloadOffset:X}");
        manifest.AppendLine($"Kit payload size: {KitPayloadSize:N0} bytes");
        AddText(archive, "kit_records/kit_record_index.txt", kitIndex.ToString());

        // Export exact factory table ranges, including their 0x20-byte headers.
        var tableSpecs = new[]
        {
            ("Wave_list.lst", "WLTH", 0x20),
            ("wave_list_sort_num.lst", "SLTH", 0x20),
            ("wave_list_sort_name.lst", "SLTH", 0x90)
        };

        foreach (var spec in tableSpecs)
        {
            TableInfo table = ReadTable(data, spec.Item1, spec.Item2, spec.Item3);
            int tableStart = table.HeaderOffset;
            int tableLength = checked(0x20 + table.ByteLength);

            AddRange(archive,
                $"factory_tables/{spec.Item1}.bin",
                data, tableStart, tableLength);

            manifest.AppendLine(
                $"{table.FileName}: header 0x{table.HeaderOffset:X8}, " +
                $"data 0x{table.DataOffset:X8}, records {table.Count}, " +
                $"stride 0x{table.RecordSize:X}, bytes {table.ByteLength:N0}");
        }

        // Export the full KITI reference -> 50xx index translation run,
        // including entries whose name-table record is blank/missing.
        (int translationStart, int translationCount) = FindTranslationRun(data);
        int translationLength = checked(translationCount * 8);
        AddRange(archive, "factory_tables/kiti_to_50xx_translation.bin",
            data, translationStart, translationLength);

        manifest.AppendLine(
            $"KITI translation run: 0x{translationStart:X8}, " +
            $"{translationCount} records, {translationLength:N0} bytes");

        // Save context around each named list file's filename metadata entry.
        var listContext = new StringBuilder();
        foreach (string name in tableSpecs.Select(x => x.Item1))
        {
            byte[] pattern = Encoding.ASCII.GetBytes(name);
            List<int> hits = FindAll(data, pattern);
            listContext.AppendLine($"{name}: {hits.Count} filename occurrence(s)");
            foreach (int hit in hits)
            {
                int start = Math.Max(0, hit - 0x100);
                int end = Math.Min(data.Length, hit + 0x180);
                listContext.AppendLine($"  0x{hit:X8} context 0x{start:X8}-0x{end:X8}");
                AddRange(archive,
                    $"factory_tables/file_metadata/{SafeFileName(name)}_{hit:X8}.bin",
                    data, start, end - start);
            }
        }
        AddText(archive, "factory_tables/list_file_metadata_index.txt",
            listContext.ToString());

        // Record all .had / PATH markers in the metadata region with nearby bytes.
        AddMetadataMarkerContexts(archive, data, manifest);

        manifest.AppendLine();
        manifest.AppendLine("PACKAGE CONTENTS");
        manifest.AppendLine("- README.txt");
        manifest.AppendLine("- backup_prefix_00000000.bin");
        manifest.AppendLine("- metadata/metadata_region.bin");
        manifest.AppendLine("- metadata/metadata_ascii_strings.txt");
        manifest.AppendLine("- kit_records/*.bin and kit_record_index.txt");
        manifest.AppendLine("- factory_tables/*.bin");
        manifest.AppendLine("- manifest.txt");

        AddText(archive, "manifest.txt", manifest.ToString());
    }

    private static void AddMetadataMarkerContexts(
        ZipArchive archive,
        byte[] data,
        StringBuilder manifest)
    {
        int start = Math.Min(MetadataStart, data.Length);
        int end = Math.Min(MetadataEnd, data.Length);
        var index = new StringBuilder();
        index.AppendLine("METADATA MARKER CONTEXTS");
        index.AppendLine("Offset | Marker | Context file");

        var windows = new List<(int Start, int End, int Hit, string Marker)>();

        foreach (string marker in new[] { ".had", "PATH", "NAME" })
        {
            foreach (int hit in FindAll(data, Encoding.ASCII.GetBytes(marker), start, end))
            {
                int contextStart = Math.Max(start, hit - 0x60);
                int contextEnd = Math.Min(end, hit + 0xC0);
                windows.Add((contextStart, contextEnd, hit, marker));
            }
        }

        // Merge overlapping windows so repeated NAME/PATH markers don't
        // produce hundreds of duplicate fragments.
        var merged = windows
            .OrderBy(x => x.Start)
            .ThenBy(x => x.End)
            .ToList();

        int fileNumber = 0;
        int i = 0;
        while (i < merged.Count)
        {
            int rangeStart = merged[i].Start;
            int rangeEnd = merged[i].End;
            var markers = new List<(int Offset, string Marker)>();
            markers.Add((merged[i].Hit, merged[i].Marker));
            int j = i + 1;

            while (j < merged.Count && merged[j].Start <= rangeEnd)
            {
                rangeEnd = Math.Max(rangeEnd, merged[j].End);
                markers.Add((merged[j].Hit, merged[j].Marker));
                j++;
            }

            fileNumber++;
            string entry = $"metadata/marker_context_{fileNumber:D3}.bin";
            AddRange(archive, entry, data, rangeStart, rangeEnd - rangeStart);

            foreach (var marker in markers)
                index.AppendLine($"0x{marker.Offset:X8} | {marker.Marker} | {entry}");

            i = j;
        }

        AddText(archive, "metadata/marker_context_index.txt", index.ToString());
        manifest.AppendLine($"Metadata marker context groups: {fileNumber}");
    }

    private static TableInfo ReadTable(
        byte[] data,
        string fileName,
        string marker,
        int recordSize)
    {
        int nameOffset = FindBytes(data, Encoding.ASCII.GetBytes(fileName), 0, data.Length);
        if (nameOffset < 0)
            throw new InvalidDataException($"{fileName} filename was not found.");

        int headerOffset = FindBytes(
            data, Encoding.ASCII.GetBytes(marker),
            nameOffset, Math.Min(data.Length, nameOffset + 512));

        if (headerOffset < 0 || headerOffset + 0x20 > data.Length)
            throw new InvalidDataException($"Header for {fileName} was not found.");

        int count = data[headerOffset + 8] | (data[headerOffset + 9] << 8);
        int dataOffset = headerOffset + 0x20;
        long byteLength = (long)count * recordSize;

        if (count <= 0 || byteLength > int.MaxValue ||
            dataOffset + byteLength > data.Length)
            throw new InvalidDataException($"Invalid record count for {fileName}: {count}.");

        return new TableInfo(fileName, marker, headerOffset,
            dataOffset, count, recordSize);
    }

    private static (int Start, int Count) FindTranslationRun(byte[] data)
    {
        int bestStart = -1;
        int bestCount = 0;

        for (int start = 0; start <= data.Length - 8; start++)
        {
            if (data[start + 2] != 0x50 ||
                data[start + 3] != 0x00 ||
                data[start + 4] != 0x00 ||
                data[start + 5] != 0x00)
                continue;

            int count = 0;
            int expected = 0;
            int offset = start;

            while (offset + 8 <= data.Length &&
                   data[offset + 2] == 0x50 &&
                   data[offset + 3] == (byte)expected &&
                   data[offset + 4] == 0x00 &&
                   data[offset + 5] == 0x00)
            {
                count++;
                expected++;
                offset += 8;
                if (expected > 255)
                    break;
            }

            if (count > bestCount)
            {
                bestCount = count;
                bestStart = start;
            }
        }

        if (bestStart < 0 || bestCount < 32)
            throw new InvalidDataException(
                "Could not identify the KITI-to-50xx translation table.");

        return (bestStart, bestCount);
    }

    private static string ReadKitName(byte[] data, int payloadOffset)
    {
        const int nameOffset = 0xC0;
        const int nameLength = 32;
        int start = payloadOffset + nameOffset;
        if (start + nameLength > data.Length)
            return "";

        string value = Encoding.ASCII.GetString(data, start, nameLength);
        int nul = value.IndexOf('\0');
        if (nul >= 0)
            value = value[..nul];
        return value.Trim();
    }

    private static void AddAsciiRuns(
        ZipArchive archive,
        string entryName,
        byte[] data,
        int start,
        int end,
        int minimumLength)
    {
        var output = new StringBuilder();
        int runStart = -1;

        for (int i = start; i <= end; i++)
        {
            bool printable = i < end && data[i] >= 32 && data[i] <= 126;
            if (printable)
            {
                if (runStart < 0)
                    runStart = i;
            }
            else if (runStart >= 0)
            {
                int length = i - runStart;
                if (length >= minimumLength)
                {
                    output.Append($"0x{runStart:X8} | ");
                    output.AppendLine(Encoding.ASCII.GetString(data, runStart, length));
                }
                runStart = -1;
            }
        }

        AddText(archive, entryName, output.ToString());
    }

    private static List<int> FindAll(
        byte[] data,
        byte[] pattern,
        int start = 0,
        int end = -1)
    {
        var result = new List<int>();
        if (end < 0 || end > data.Length)
            end = data.Length;

        for (int i = Math.Max(0, start); i <= end - pattern.Length; i++)
        {
            bool found = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j])
                {
                    found = false;
                    break;
                }
            }
            if (found)
                result.Add(i);
        }
        return result;
    }

    private static int FindBytes(
        byte[] data,
        byte[] pattern,
        int start,
        int end)
    {
        for (int i = Math.Max(0, start); i <= end - pattern.Length; i++)
        {
            bool found = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j])
                {
                    found = false;
                    break;
                }
            }
            if (found)
                return i;
        }
        return -1;
    }

    private static void AddRange(
        ZipArchive archive,
        string entryName,
        byte[] data,
        int offset,
        int length)
    {
        if (offset < 0 || length < 0 || offset + (long)length > data.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        ZipArchiveEntry entry = archive.CreateEntry(
            entryName, CompressionLevel.Optimal);

        using Stream stream = entry.Open();
        stream.Write(data, offset, length);
    }

    private static void AddText(
        ZipArchive archive,
        string entryName,
        string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(
            entryName, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string SafeFileName(string value)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        return value;
    }

    private static string Sanitize(string value) =>
        value.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');
}
