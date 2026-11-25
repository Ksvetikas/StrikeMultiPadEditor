
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace StrikeMultiPadEditor;

public static class FactorySampleReferenceScanner
{
    private static readonly string[] ListFiles =
    [
        "Wave_list.lst",
        "wave_list_sort_num.lst",
        "wave_list_sort_name.lst"
    ];

    private static readonly string[] References =
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
        var output = new StringBuilder();

        output.AppendLine("STRIKE MULTIPAD FACTORY SAMPLE INDEX SCAN");
        output.AppendLine($"Backup: {backupPath}");
        output.AppendLine($"Size: {data.Length:N0} bytes");
        output.AppendLine();

        foreach (string fileName in ListFiles)
        {
            byte[] nameBytes = Encoding.ASCII.GetBytes(fileName);
            List<int> locations = FindAll(data, nameBytes);

            output.AppendLine("==================================================");
            output.AppendLine($"INDEX FILE: {fileName}");
            output.AppendLine($"Occurrences: {locations.Count}");

            if (locations.Count == 0)
            {
                output.AppendLine("NOT FOUND");
                output.AppendLine();
                continue;
            }

            foreach (int location in locations)
            {
                int start = Math.Max(0, location - 512);
                int end = Math.Min(data.Length, location + 1024);

                output.AppendLine();
                output.AppendLine(
                    $"NAME OFFSET: 0x{location:X8}");

                output.AppendLine(
                    $"REGION: 0x{start:X8} - 0x{end:X8}");

                output.AppendLine();
                output.AppendLine("PRINTABLE TEXT:");

                foreach (string text in ExtractStrings(
                    data, start, end))
                {
                    output.AppendLine(text);
                }

                output.AppendLine();
                output.AppendLine("REFERENCE MATCHES:");

                int matchCount = 0;

                foreach (string reference in References)
                {
                    byte[] pattern = ConvertReference(reference);

                    foreach (int hit in FindAll(
                        data, pattern, start, end))
                    {
                        matchCount++;

                        output.AppendLine(
                            $"{reference} | " +
                            $"OFFSET 0x{hit:X8} | " +
                            $"{GetContext(data, hit)}");
                    }
                }

                if (matchCount == 0)
                    output.AppendLine("No known references in this region.");

                output.AppendLine();
                output.AppendLine("HEX DUMP:");

                for (int row = start; row < end; row += 16)
                {
                    int count = Math.Min(16, end - row);

                    byte[] line = new byte[count];
                    Array.Copy(data, row, line, 0, count);

                    output.AppendLine(
                        $"{row:X8}: " +
                        BitConverter.ToString(line).Replace("-", " ") +
                        "  " + ToText(line));
                }
            }
        }

        output.AppendLine();
        output.AppendLine("END OF FACTORY SAMPLE INDEX SCAN");

        return output.ToString();
    }

    private static byte[] ConvertReference(string reference)
    {
        return reference
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => Convert.ToByte(x, 16))
            .ToArray();
    }

    private static List<int> FindAll(
        byte[] data,
        byte[] pattern,
        int start = 0,
        int? end = null)
    {
        var results = new List<int>();

        int limit = Math.Min(end ?? data.Length, data.Length);

        if (pattern.Length == 0 ||
            start < 0 ||
            limit - start < pattern.Length)
            return results;

        for (int i = start; i <= limit - pattern.Length; i++)
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
                results.Add(i);
        }

        return results;
    }

    private static List<string> ExtractStrings(
        byte[] data,
        int start,
        int end)
    {
        var results = new List<string>();
        var current = new StringBuilder();

        for (int i = start; i < end; i++)
        {
            byte value = data[i];

            if (value >= 32 && value <= 126)
            {
                current.Append((char)value);
            }
            else
            {
                if (current.Length >= 4)
                    results.Add(current.ToString());

                current.Clear();
            }
        }

        if (current.Length >= 4)
            results.Add(current.ToString());

        return results;
    }

    private static string GetContext(byte[] data, int offset)
    {
        int start = Math.Max(0, offset - 12);
        int length = Math.Min(28, data.Length - start);

        byte[] context = new byte[length];
        Array.Copy(data, start, context, 0, length);

        return BitConverter.ToString(context).Replace("-", " ");
    }

    private static string ToText(byte[] data)
    {
        return new string(data.Select(b =>
            b >= 32 && b <= 126 ? (char)b : '.').ToArray());
    }

    public static string ScanSampleReferences(string backupPath)
    {
        if (!File.Exists(backupPath))
            return "Backup file not found.";

        byte[] data = File.ReadAllBytes(backupPath);
        var output = new StringBuilder();

        var targets = new (string Name, byte[] Bytes)[]
        {
            ("1A D1", [0x1A, 0xD1]),
            ("1A CD", [0x1A, 0xCD]),
            ("19 34", [0x19, 0x34]),
            ("1B E0", [0x1B, 0xE0]),
            ("1B DE", [0x1B, 0xDE]),
            ("1B E2", [0x1B, 0xE2]),
            ("01 F4", [0x01, 0xF4]),
            ("01 BF", [0x01, 0xBF]),
            ("03 73", [0x03, 0x73]),
            ("03 74", [0x03, 0x74]),
            ("01 B3", [0x01, 0xB3]),
            ("05 00", [0x05, 0x00]),
            ("04 2F", [0x04, 0x2F]),
            ("08 53", [0x08, 0x53]),
            ("08 54", [0x08, 0x54]),
            ("04 23", [0x04, 0x23])
        };

        var regions = new (string Name, int Start, int End)[]
        {
            ("Kits", 0x00000000, 0x000B0000),
            ("Metadata", 0x000B0000, 0x00210000)
        };

        targets = targets.Take(3).ToArray();

        foreach (var target in targets)
        {
            output.Append(target.Name).Append(": ");

            bool found = false;

            foreach (var region in regions)
            {
                int start = Math.Min(region.Start, data.Length);
                int end = Math.Min(region.End, data.Length);
                int shown = 0;

                for (int i = start; i < end - 1 && shown < 3; i++)
                {
                    if (data[i] == target.Bytes[0] &&
                        data[i + 1] == target.Bytes[1])
                    {
                        if (found) output.Append(", ");
                        output.Append(region.Name)
                              .Append(" 0x")
                              .Append(i.ToString("X"));
                        found = true;
                        shown++;
                    }
                }
            }

            if (!found) output.Append("not found");
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string Hex(byte[] data, int offset, int count)
    {
        return string.Join(
            " ",
            data.Skip(offset)
                .Take(count)
                .Select(b => b.ToString("X2"))
        );
    }
}
