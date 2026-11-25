
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace StrikeMultiPadEditor;

public sealed class BackupReferenceMatch
{
    public string Reference { get; init; } = "";
    public string ByteOrder { get; init; } = "";
    public long FileOffset { get; init; }

    public string OffsetText => $"0x{FileOffset:X8}";

    public string HexContext { get; init; } = "";
    public string TextContext { get; init; } = "";
}

public static class BackupReferenceScanner
{
    private static readonly string[] References =
    [
        "1A D1", "1A CD", "19 34", "1B E0",
        "1B DE", "1B E2", "01 F4", "01 BF",
        "03 73", "03 74", "01 B3"
    ];

    public static List<BackupReferenceMatch> Scan(string backupPath)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException(
                "Backup file not found.", backupPath);

        byte[] data = File.ReadAllBytes(backupPath);

        var matches = new List<BackupReferenceMatch>();

        foreach (string reference in References)
        {
            byte[] forward = reference
                .Split(' ')
                .Select(x => Convert.ToByte(x, 16))
                .ToArray();

            byte[] reversed = forward.Reverse().ToArray();

            Search(data, forward, reference, "Forward", matches);
            Search(data, reversed, reference, "Reversed", matches);
        }

        return matches
            .OrderBy(x => x.FileOffset)
            .ThenBy(x => x.Reference)
            .ToList();
    }

    private static void Search(
        byte[] data,
        byte[] pattern,
        string reference,
        string byteOrder,
        List<BackupReferenceMatch> matches)
    {
        for (int offset = 0;
             offset <= data.Length - pattern.Length;
             offset++)
        {
            if (data[offset] != pattern[0] ||
                data[offset + 1] != pattern[1])
                continue;

            int contextStart = Math.Max(0, offset - 24);
            int contextLength = Math.Min(
                48,
                data.Length - contextStart);

            byte[] context = new byte[contextLength];

            Array.Copy(
                data,
                contextStart,
                context,
                0,
                contextLength);

            matches.Add(new BackupReferenceMatch
            {
                Reference = reference,
                ByteOrder = byteOrder,
                FileOffset = offset,
                HexContext = BitConverter.ToString(context)
                    .Replace("-", " "),
                TextContext = ToReadableText(context)
            });
        }
    }

    private static string ToReadableText(byte[] data)
    {
        var result = new StringBuilder();

        foreach (byte value in data)
        {
            result.Append(
                value >= 32 && value <= 126
                    ? (char)value
                    : '.');
        }

        return result.ToString();
    }

    public static void ExportResults(
        List<BackupReferenceMatch> matches,
        string outputPath)
    {
        var output = new StringBuilder();

        output.AppendLine("STRIKE MULTIPAD BACKUP REFERENCE SCAN");
        output.AppendLine($"Matches: {matches.Count}");
        output.AppendLine();

        foreach (var match in matches)
        {
            output.AppendLine(
                $"{match.Reference} | {match.ByteOrder} | " +
                $"{match.OffsetText}");

            output.AppendLine($"HEX:  {match.HexContext}");
            output.AppendLine($"TEXT: {match.TextContext}");
            output.AppendLine();
        }

        File.WriteAllText(outputPath, output.ToString());
    }
}
