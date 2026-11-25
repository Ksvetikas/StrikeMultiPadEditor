
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace StrikeMultiPadEditor;

public static class FactoryTableCrossReferenceScanner
{
    private sealed class TableInfo
    {
        public string Name { get; init; } = "";
        public int HeaderOffset { get; init; }
        public int DataOffset { get; init; }
        public int Count { get; init; }
        public int RecordSize { get; init; }
        public int Length => Count * RecordSize;
    }

    private sealed class SampleEntry
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
    }

    private static readonly string[] LayerReferences =
    [
        "1A D1", "1A CD", "19 34", "1B E0",
        "1B DE", "1B E2", "01 F4", "01 BF",
        "03 73", "03 74", "01 B3"
    ];

    public static string Scan(string backupPath)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException(
                "Backup file not found.", backupPath);

        byte[] data = File.ReadAllBytes(backupPath);

        TableInfo wave = ReadTable(
            data, "Wave_list.lst", "WLTH", 0x20);

        TableInfo sortNumber = ReadTable(
            data, "wave_list_sort_num.lst", "SLTH", 0x20);

        TableInfo sortName = ReadTable(
            data, "wave_list_sort_name.lst", "SLTH", 0x90);

        List<SampleEntry> samples =
            ReadSampleNames(data, sortName);

        var namesById = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (SampleEntry sample in samples)
            namesById[sample.Id] = sample.Name;

        var output = new StringBuilder();

        output.AppendLine("STRIKE MULTIPAD ALIGNED FACTORY TABLE SCAN");
        output.AppendLine($"Backup: {backupPath}");
        output.AppendLine($"Factory sample names: {samples.Count}");
        output.AppendLine();

        ScanAllWaveFieldsForNames(
            data, output, wave, namesById);

        output.AppendLine("TABLES");
        output.AppendLine("------");
        WriteTableInfo(output, wave);
        WriteTableInfo(output, sortNumber);
        WriteTableInfo(output, sortName);

        output.AppendLine();
        DumpWaveNameIntersections(data, output, wave, namesById);

        output.AppendLine();
        output.AppendLine("LAYER REFERENCE LOOKUPS");
        output.AppendLine("=======================");

        foreach (string reference in LayerReferences)
        {
            byte[] id = ParsePair(reference);

            output.AppendLine();
            output.AppendLine($"LAYER REFERENCE: {reference}");

            if (namesById.TryGetValue(reference, out string? name))
                output.AppendLine($"Factory name table: {name}");
            else
                output.AppendLine(
                    "Factory name table: no matching identifier");

            DumpWaveEntries(data, output, wave, id, namesById);
            DumpSortNumberEntries(data, output, sortNumber, id);
        }

        output.AppendLine();
        output.AppendLine("SCAN COMPLETE");

        return output.ToString();
    }

    private static void DumpWaveEntries(
        byte[] data,
        StringBuilder output,
        TableInfo table,
        byte[] target,
        Dictionary<string, string> namesById)
    {
        output.AppendLine();
        output.AppendLine("  WAVE_LIST 8-BYTE ENTRIES");

        int totalEntries = table.Length / 8;
        int found = 0;

        for (int index = 0; index < totalEntries; index++)
        {
            int offset = table.DataOffset + index * 8;

            bool firstField =
                data[offset] == target[0] &&
                data[offset + 1] == target[1];

            bool lastField =
                data[offset + 6] == target[0] &&
                data[offset + 7] == target[1];

            if (!firstField && !lastField)
                continue;

            found++;

            string firstId = PairAt(data, offset);
            string middleId = PairAt(data, offset + 2);
            string lastId = PairAt(data, offset + 6);

            output.AppendLine(
                $"    Entry {index} | Offset 0x{offset:X8} | " +
                $"{Hex(data, offset, 8)}");

            output.AppendLine(
                $"      First field: {firstId}" +
                (firstField ? " [MATCH]" : ""));

            output.AppendLine(
                $"      Middle field: {middleId}");

            if (namesById.TryGetValue(middleId, out string? middleName))
                output.AppendLine($"      Middle-field name: {middleName}");
            else
                output.AppendLine("      Middle-field name: no match");

            output.AppendLine(
                $"      Last field: {lastId}" +
                (lastField ? " [MATCH]" : ""));
        }

        if (found == 0)
            output.AppendLine("    No aligned entry matches.");

        output.AppendLine($"    Matching entries: {found}");
    }

    private static void DumpSortNumberEntries(
        byte[] data,
        StringBuilder output,
        TableInfo table,
        byte[] target)
    {
        output.AppendLine();
        output.AppendLine("  SORT_NUMBER 2-BYTE IDENTIFIERS");

        int totalValues = table.Length / 2;
        int found = 0;

        for (int index = 0; index < totalValues; index++)
        {
            int offset = table.DataOffset + index * 2;

            if (data[offset] != target[0] ||
                data[offset + 1] != target[1])
                continue;

            found++;

            string previous = index > 0
                ? PairAt(data, offset - 2)
                : "(start)";

            string next = index + 1 < totalValues
                ? PairAt(data, offset + 2)
                : "(end)";

            output.AppendLine(
                $"    Value {index,4} | Offset 0x{offset:X8} | " +
                $"{PairAt(data, offset)}");

            output.AppendLine(
                $"      Previous: {previous} | Next: {next}");
        }

        if (found == 0)
            output.AppendLine("    No aligned identifier matches.");

        output.AppendLine($"    Matching values: {found}");
    }

    private static List<SampleEntry> ReadSampleNames(
        byte[] data,
        TableInfo table)
    {
        var result = new List<SampleEntry>();

        for (int i = 0; i < table.Count; i++)
        {
            int offset = table.DataOffset + i * table.RecordSize;

            string id = PairAt(data, offset + 2);

            int nameStart = offset + 0x10;
            int nameEnd = nameStart;

            while (nameEnd < offset + table.RecordSize &&
                   data[nameEnd] != 0)
            {
                nameEnd++;
            }

            string name = Encoding.ASCII.GetString(
                data, nameStart, nameEnd - nameStart).Trim();

            if (!string.IsNullOrWhiteSpace(name))
            {
                result.Add(new SampleEntry
                {
                    Id = id,
                    Name = name
                });
            }
        }

        return result;
    }

    private static TableInfo ReadTable(
        byte[] data,
        string fileName,
        string marker,
        int recordSize)
    {
        int nameOffset = FindBytes(
            data, Encoding.ASCII.GetBytes(fileName));

        if (nameOffset < 0)
            throw new InvalidDataException(
                $"{fileName} was not found.");

        int headerOffset = FindBytes(
            data,
            Encoding.ASCII.GetBytes(marker),
            nameOffset,
            Math.Min(data.Length, nameOffset + 512));

        if (headerOffset < 0 ||
            headerOffset + 10 > data.Length)
        {
            throw new InvalidDataException(
                $"Header for {fileName} was not found.");
        }

        int count = data[headerOffset + 8] |
                    (data[headerOffset + 9] << 8);

        int dataOffset = headerOffset + 0x20;

        if (count <= 0 ||
            dataOffset + (long)count * recordSize > data.Length)
        {
            throw new InvalidDataException(
                $"Invalid table size for {fileName}. Count: {count}.");
        }

        return new TableInfo
        {
            Name = fileName,
            HeaderOffset = headerOffset,
            DataOffset = dataOffset,
            Count = count,
            RecordSize = recordSize
        };
    }

    private static void WriteTableInfo(
        StringBuilder output,
        TableInfo table)
    {
        output.AppendLine(
            $"{table.Name}: data 0x{table.DataOffset:X8}, " +
            $"{table.Count} records, stride 0x{table.RecordSize:X}");
    }

    private static byte[] ParsePair(string value)
    {
        string[] parts = value.Split(' ');

        return
        [
            Convert.ToByte(parts[0], 16),
            Convert.ToByte(parts[1], 16)
        ];
    }

    private static string PairAt(byte[] data, int offset)
    {
        return $"{data[offset]:X2} {data[offset + 1]:X2}";
    }

    private static string Hex(
        byte[] data,
        int offset,
        int count)
    {
        var result = new StringBuilder();

        for (int i = 0; i < count; i++)
        {
            if (i > 0)
                result.Append(' ');

            result.Append(data[offset + i].ToString("X2"));
        }

        return result.ToString();
    }

    private static int FindBytes(
        byte[] data,
        byte[] pattern,
        int start = 0,
        int end = -1)
    {
        if (end < 0 || end > data.Length)
            end = data.Length;

        for (int i = Math.Max(0, start);
             i <= end - pattern.Length;
             i++)
        {
            bool match = true;

            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
                return i;
        }

        return -1;
    }

    private static void DumpWaveNameIntersections(
        byte[] data,
        StringBuilder output,
        TableInfo wave,
        Dictionary<string, string> namesById)
    {
        output.AppendLine();
        output.AppendLine("ALL WAVE_LIST IDENTIFIERS vs SAMPLE NAMES");
        output.AppendLine("========================================");

        int totalEntries = wave.Length / 8;
        var seen = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        int namedMatches = 0;
        int uniqueIdentifiers = 0;

        for (int i = 0; i < totalEntries; i++)
        {
            int offset = wave.DataOffset + i * 8;
            string id = PairAt(data, offset);

            if (!seen.Add(id))
                continue;

            uniqueIdentifiers++;

            if (!namesById.TryGetValue(id, out string? name))
                continue;

            namedMatches++;

            output.AppendLine(
                $"Entry {i} | Offset 0x{offset:X8} | " +
                $"ID {id} | Name: {name} | " +
                $"Record: {Hex(data, offset, 8)}");
        }

        output.AppendLine();
        output.AppendLine(
            $"Wave_list logical entries: {totalEntries}");

        output.AppendLine(
            $"Unique first-field identifiers: {uniqueIdentifiers}");

        output.AppendLine(
            $"Identifiers also found in sample name table: {namedMatches}");

        if (namedMatches == 0)
        {
            output.AppendLine(
                "No direct identifier matches between these tables.");
        }
    }
    private static void ScanAllWaveFieldsForNames(
        byte[] data,
        StringBuilder output,
        TableInfo wave,
        Dictionary<string, string> namesById)
    {
        output.AppendLine();
        output.AppendLine("ALL WAVE_LIST FIELDS MATCHED AGAINST SAMPLE NAMES");
        output.AppendLine("================================================");

        string[] fieldNames =
        [
            "Field +0 (identifier)",
            "Field +2",
            "Field +4",
            "Field +6 (previous identifier)"
        ];

        int[] fieldOffsets = [0, 2, 4, 6];
        int totalMatches = 0;

        for (int index = 0; index < wave.Length / 8; index++)
        {
            int recordOffset = wave.DataOffset + index * 8;

            for (int field = 0; field < fieldOffsets.Length; field++)
            {
                string id = PairAt(
                    data, recordOffset + fieldOffsets[field]);

                if (!namesById.TryGetValue(id, out string? name))
                    continue;

                totalMatches++;

                output.AppendLine(
                    $"Entry {index} | Offset 0x{recordOffset:X8} | " +
                    $"{fieldNames[field]} | ID {id} | {name}");

                output.AppendLine(
                    $"  Record: {Hex(data, recordOffset, 8)}");
            }
        }

        output.AppendLine();
        output.AppendLine($"Total field/name matches: {totalMatches}");
    }
    
    public static string ScanSampleReferences(string backupPath)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException(
                "Backup file not found.", backupPath);

        byte[] data = File.ReadAllBytes(backupPath);
        var output = new StringBuilder();

        output.AppendLine("STRIKE MULTIPAD SAMPLE REFERENCE SEARCH");
        output.AppendLine($"Backup: {backupPath}");
        output.AppendLine($"Backup size: {data.Length:N0} bytes");
        output.AppendLine();

        var targets = new List<(string Name, byte[] Bytes)>
        {
            ("Layer reference 1A D1", [0x1A, 0xD1]),
            ("Layer reference 1A CD", [0x1A, 0xCD]),
            ("Layer reference 19 34", [0x19, 0x34]),
            ("Layer reference 1B E0", [0x1B, 0xE0]),
            ("Layer reference 1B DE", [0x1B, 0xDE]),
            ("Layer reference 1B E2", [0x1B, 0xE2]),
            ("Layer reference 01 F4", [0x01, 0xF4]),
            ("Layer reference 01 BF", [0x01, 0xBF]),
            ("Layer reference 03 73", [0x03, 0x73]),
            ("Layer reference 03 74", [0x03, 0x74]),
            ("Layer reference 01 B3", [0x01, 0xB3]),

            ("Wave-list middle value 05 00", [0x05, 0x00]),
            ("Wave-list middle value 04 2F", [0x04, 0x2F]),
            ("Wave-list middle value 08 53", [0x08, 0x53]),
            ("Wave-list middle value 08 54", [0x08, 0x54]),
            ("Wave-list middle value 04 23", [0x04, 0x23])
        };

        const int contextBefore = 16;
        const int contextAfter = 16;
        const int maximumDisplayedHits = 40;

        foreach (var target in targets)
        {
            output.AppendLine();
            output.AppendLine("========================================");
            output.AppendLine(target.Name);
            output.AppendLine($"Bytes: {Hex(target.Bytes, 0, 2)}");
            output.AppendLine("========================================");

            int totalHits = 0;
            int displayedHits = 0;

            for (int offset = 0; offset <= data.Length - 2; offset++)
            {
                bool bigEndian =
                    data[offset] == target.Bytes[0] &&
                    data[offset + 1] == target.Bytes[1];

                bool reversed =
                    data[offset] == target.Bytes[1] &&
                    data[offset + 1] == target.Bytes[0];

                if (!bigEndian && !reversed)
                    continue;

                totalHits++;

                if (displayedHits >= maximumDisplayedHits)
                    continue;

                displayedHits++;

                int start = Math.Max(0, offset - contextBefore);
                int end = Math.Min(
                    data.Length, offset + 2 + contextAfter);

                output.AppendLine();
                output.AppendLine(
                    $"Hit {totalHits} | Offset 0x{offset:X8} " +
                    $"({offset:N0}) | " +
                    (bigEndian ? "Exact order" : "Reversed order"));

                output.AppendLine(
                    $"  Context starts: 0x{start:X8}");

                output.AppendLine(
                    $"  Bytes: {Hex(data, start, end - start)}");

                output.AppendLine(
                    $"  Match position in context: {offset - start}");
            }

            if (totalHits == 0)
            {
                output.AppendLine("No occurrences found.");
            }
            else
            {
                output.AppendLine();
                output.AppendLine($"Total occurrences: {totalHits}");

                if (totalHits > maximumDisplayedHits)
                {
                    output.AppendLine(
                        $"Only the first {maximumDisplayedHits} hits displayed.");
                }
            }
        }

        output.AppendLine();
        output.AppendLine("SEARCH COMPLETE");

        return output.ToString();
    }
}
