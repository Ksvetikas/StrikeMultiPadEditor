using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace StrikeMultiPadEditor;

public sealed class FactorySampleName
{
    public int RecordNumber { get; init; } // zero-based catalogue position
    public int SequenceByte { get; init; } // wraps every 256 records
    public string Identifier { get; init; } = "";
    public int NameLength { get; init; }
    public string SampleName { get; init; } = "";
    public int RecordOffset { get; init; }
}

public static class FactorySampleNameTableScanner
{
    private const int FileHeaderSize = 0x180;
    private const int FileLengthOffset = 0x174;
    private const int TableHeaderSize = 0x20;
    private const int RecordSize = 0x90;
    private const int NameOffset = 0x10;
    private const int NameCapacity = 0x80;

    private static readonly string[] KnownReferences =
    [
        "1A D1", "1A CD", "19 34", "1B E0", "1B DE", "1B E2",
        "01 F4", "01 BF", "03 73", "03 74", "01 B3"
    ];

    public static string Scan(string backupPath)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Backup file not found.", backupPath);

        byte[] data = File.ReadAllBytes(backupPath);
        (int payloadStart, int payloadLength) = FindEmbeddedFile(data, "wave_list_sort_name.lst");
        if (payloadLength < TableHeaderSize)
            throw new InvalidDataException("The sample-name table payload is too short.");

        int declaredCount = data[payloadStart + 8] | (data[payloadStart + 9] << 8);
        int recordsStart = payloadStart + TableHeaderSize;
        int slotCount = (payloadLength - TableHeaderSize) / RecordSize;
        var entries = new List<FactorySampleName>(slotCount);

        for (int i = 0; i < slotCount; i++)
        {
            int offset = recordsStart + i * RecordSize;
            int nameLength = (data[offset + 4] << 8) | data[offset + 5];
            if (data[offset] != 0 || nameLength > NameCapacity) continue;

            int nameStart = offset + NameOffset;
            int actualLength = 0;
            while (actualLength < NameCapacity && data[nameStart + actualLength] != 0)
            {
                byte value = data[nameStart + actualLength];
                if (value < 32 || value > 126) break;
                actualLength++;
            }
            if (actualLength == 0) continue;

            string name = Encoding.ASCII.GetString(data, nameStart, actualLength).Trim();
            if (name.Length == 0) continue;

            entries.Add(new FactorySampleName
            {
                RecordNumber = i,
                SequenceByte = data[offset + 1],
                Identifier = $"{data[offset + 2]:X2} {data[offset + 3]:X2}",
                NameLength = nameLength,
                SampleName = name,
                RecordOffset = offset
            });
        }

        var output = new StringBuilder();
        output.AppendLine("STRIKE MULTIPAD FACTORY SAMPLE NAME TABLE");
        output.AppendLine($"Backup: {Path.GetFileName(backupPath)}");
        output.AppendLine($"Header declared count: {declaredCount}");
        output.AppendLine($"Catalogue slots in payload: {slotCount}");
        output.AppendLine($"Decoded named records: {entries.Count}");
        output.AppendLine("Identifier is the big-endian two-byte field at record +0x02.");
        output.AppendLine("Sequence byte at +0x01 wraps; it is not a unique record number.");
        output.AppendLine();
        output.AppendLine("POSITION | SEQ | IDENTIFIER | LENGTH | SAMPLE NAME | BACKUP OFFSET");
        foreach (FactorySampleName entry in entries)
            output.AppendLine($"{entry.RecordNumber,8} | {entry.SequenceByte,3} | {entry.Identifier} | {entry.NameLength,6} | {entry.SampleName} | 0x{entry.RecordOffset:X8}");

        output.AppendLine();
        output.AppendLine("KNOWN LAYER REFERENCE LOOKUP");
        foreach (string reference in KnownReferences)
        {
            List<FactorySampleName> matches = entries
                .Where(x => string.Equals(x.Identifier, reference, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count == 0)
                output.AppendLine($"{reference} | NO MATCH IN SAMPLE NAME CATALOGUE");
            else
                foreach (FactorySampleName match in matches)
                    output.AppendLine($"{reference} | position {match.RecordNumber} | {match.SampleName}");
        }
        output.AppendLine();
        output.AppendLine("END OF SAMPLE NAME TABLE");
        return output.ToString();
    }

    private static (int PayloadStart, int PayloadLength) FindEmbeddedFile(byte[] data, string fileName)
    {
        int namePosition = FindBytes(data, Encoding.ASCII.GetBytes(fileName));
        if (namePosition < 0) throw new InvalidDataException($"{fileName} was not found in the backup.");

        int headerPosition = namePosition - 0xE0;
        if (headerPosition < 0 || !Matches(data, headerPosition, Encoding.ASCII.GetBytes("FILEH")))
            throw new InvalidDataException($"The FILEH header for {fileName} was not found.");

        int payloadLength = ReadUInt32LE(data, headerPosition + FileLengthOffset);
        int payloadStart = checked(headerPosition + FileHeaderSize);
        if (payloadLength <= 0 || payloadStart + (long)payloadLength > data.Length)
            throw new InvalidDataException($"The payload bounds for {fileName} are invalid.");
        return (payloadStart, payloadLength);
    }

    private static int ReadUInt32LE(byte[] d, int o) =>
        d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24);

    private static bool Matches(byte[] data, int offset, byte[] pattern)
    {
        if (offset < 0 || offset + pattern.Length > data.Length) return false;
        for (int i = 0; i < pattern.Length; i++)
            if (data[offset + i] != pattern[i]) return false;
        return true;
    }

    private static int FindBytes(byte[] data, byte[] pattern)
    {
        for (int i = 0; i <= data.Length - pattern.Length; i++)
            if (Matches(data, i, pattern)) return i;
        return -1;
    }
}
