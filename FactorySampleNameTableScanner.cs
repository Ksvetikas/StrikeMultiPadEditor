
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace StrikeMultiPadEditor;

public sealed class FactorySampleName
{
    public int RecordNumber { get; init; }
    public string Identifier { get; init; } = "";
    public string SampleName { get; init; } = "";
    public int RecordOffset { get; init; }
}

public static class FactorySampleNameTableScanner
{
    private const int RecordSize = 0x90;
    private const int RecordDataOffset = 0x20;
    private const int NameOffset = 0x10;
    private const int NameCapacity = 0x80;

    private static readonly string[] KnownReferences =
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

        int namePosition = FindBytes(
            data,
            Encoding.ASCII.GetBytes("wave_list_sort_name.lst"));

        if (namePosition < 0)
            throw new InvalidDataException(
                "wave_list_sort_name.lst was not found.");

        int headerPosition = FindBytes(
            data,
            Encoding.ASCII.GetBytes("SLTH"),
            namePosition);

        if (headerPosition < 0 ||
            headerPosition + RecordDataOffset > data.Length)
        {
            throw new InvalidDataException(
                "The sample-name table header was not found.");
        }

        int count = data[headerPosition + 8] |
                    (data[headerPosition + 9] << 8);

        int recordsStart = headerPosition + RecordDataOffset;

        if (count <= 0 || count > 10000 ||
            recordsStart + (long)count * RecordSize > data.Length)
        {
            throw new InvalidDataException(
                $"Invalid sample table size. Entry count: {count}.");
        }

        var entries = new List<FactorySampleName>();

        for (int i = 0; i < count; i++)
        {
            int offset = recordsStart + i * RecordSize;

            int recordNumber =
                (data[offset] << 8) | data[offset + 1];

            string identifier =
                $"{data[offset + 2]:X2} {data[offset + 3]:X2}";

            string sampleName = ReadName(data, offset);

            if (string.IsNullOrWhiteSpace(sampleName))
                continue;

            entries.Add(new FactorySampleName
            {
                RecordNumber = recordNumber,
                Identifier = identifier,
                SampleName = sampleName,
                RecordOffset = offset
            });
        }

        var output = new StringBuilder();

        output.AppendLine("STRIKE MULTIPAD FACTORY SAMPLE NAME TABLE");
        output.AppendLine($"Backup: {backupPath}");
        output.AppendLine($"Declared entries: {count}");
        output.AppendLine($"Decoded entries: {entries.Count}");
        output.AppendLine($"Record size: 0x{RecordSize:X}");
        output.AppendLine();
        output.AppendLine(
            "RECORD | IDENTIFIER | SAMPLE NAME | BACKUP OFFSET");
        output.AppendLine(
            "--------------------------------------------------------------");

        foreach (var entry in entries)
        {
            output.AppendLine(
                $"{entry.RecordNumber,6} | " +
                $"{entry.Identifier} | " +
                $"{entry.SampleName} | " +
                $"0x{entry.RecordOffset:X8}");
        }

        output.AppendLine();
        output.AppendLine("KNOWN LAYER REFERENCE LOOKUP");
        output.AppendLine("----------------------------");

        foreach (string reference in KnownReferences)
        {
            var matches = entries
                .Where(x =>
                    string.Equals(
                        x.Identifier,
                        reference,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            var reversed = string.Join(
                " ",
                reference.Split(' ').Reverse());

            matches.AddRange(entries.Where(x =>
                string.Equals(
                    x.Identifier,
                    reversed,
                    StringComparison.OrdinalIgnoreCase)));

            matches = matches
                .DistinctBy(x => x.RecordOffset)
                .ToList();

            if (matches.Count == 0)
            {
                output.AppendLine(
                    $"{reference} | NO MATCH IN SAMPLE NAME TABLE");
            }
            else
            {
                foreach (var match in matches)
                {
                    output.AppendLine(
                        $"{reference} | {match.Identifier} | " +
                        $"{match.SampleName}");
                }
            }
        }

        output.AppendLine();
        output.AppendLine("END OF SAMPLE NAME TABLE");

        return output.ToString();
    }

    private static int FindBytes(
        byte[] data,
        byte[] pattern,
        int start = 0)
    {
        for (int i = Math.Max(0, start);
             i <= data.Length - pattern.Length;
             i++)
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

    private static string ReadName(byte[] data, int recordOffset)
    {
        int start = recordOffset + NameOffset;
        int length = Math.Min(
            NameCapacity,
            data.Length - start);

        int end = start;

        while (end < start + length && data[end] != 0)
            end++;

        return Encoding.ASCII.GetString(
            data, start, end - start).Trim();
    }
}
