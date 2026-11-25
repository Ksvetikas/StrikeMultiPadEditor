
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StrikeMultiPadEditor;

public sealed class RepeatedStructureOccurrence
{
    public int Offset { get; set; }
    public int DistanceFromPrevious { get; set; }
}

public static class RepeatedStructureAnalyzer
{
    private static readonly byte[] SearchPattern =
    {
        0x00, 0x7F, 0x00, 0x00, 0xFF, 0xFF, 0xFF
    };

    public static string CreateReport(
        byte[] kitA,
        byte[] kitB,
        int contextBytes = 8)
    {
        if (kitA == null || kitB == null)
            throw new ArgumentNullException(
                "Both kit files must contain data.");

        if (contextBytes < 0)
            throw new ArgumentOutOfRangeException(
                nameof(contextBytes));

        List<RepeatedStructureOccurrence> hitsA =
            FindOccurrences(kitA);

        List<RepeatedStructureOccurrence> hitsB =
            FindOccurrences(kitB);

        var allOffsets = hitsA.Select(x => x.Offset)
            .Union(hitsB.Select(x => x.Offset))
            .OrderBy(x => x)
            .ToList();

        var report = new StringBuilder();

        report.AppendLine("REPEATED STRUCTURE ANALYSIS");
        report.AppendLine("===========================");
        report.AppendLine();

        report.AppendLine(
            "Search pattern: " + FormatBytes(SearchPattern));

        report.AppendLine(
            $"Kit A occurrences: {hitsA.Count}");

        report.AppendLine(
            $"Kit B occurrences: {hitsB.Count}");

        report.AppendLine();

        AppendOccurrenceList(report, "KIT A", hitsA);
        AppendOccurrenceList(report, "KIT B", hitsB);

        report.AppendLine();
        report.AppendLine("COMPARISON BY OFFSET");
        report.AppendLine("====================");

        if (allOffsets.Count == 0)
        {
            report.AppendLine(
                "The search pattern was not found in either kit.");

            return report.ToString();
        }

        foreach (int offset in allOffsets)
        {
            bool foundA = ContainsPatternAt(kitA, offset);
            bool foundB = ContainsPatternAt(kitB, offset);

            report.AppendLine();
            report.AppendLine($"OFFSET 0x{offset:X4}");

            report.AppendLine(
                $"Kit A: {(foundA ? "MATCH" : "NO MATCH")}");

            report.AppendLine(
                FormatContext(
                    kitA, offset, SearchPattern.Length, contextBytes));

            report.AppendLine(
                $"Kit B: {(foundB ? "MATCH" : "NO MATCH")}");

            report.AppendLine(
                FormatContext(
                    kitB, offset, SearchPattern.Length, contextBytes));
        }

        report.AppendLine();
        report.AppendLine("NOTES");
        report.AppendLine("=====");
        report.AppendLine(
            "A match means the exact seven-byte pattern was found.");

        report.AppendLine(
            "Matching offsets show where the same pattern occurs " +
            "in both files.");

        report.AppendLine(
            "Different offsets or missing matches may indicate " +
            "structural changes.");

        report.AppendLine(
            "This analysis does not identify the meaning of the bytes.");

        return report.ToString();
    }

    private static List<RepeatedStructureOccurrence>
        FindOccurrences(byte[] data)
    {
        var results = new List<RepeatedStructureOccurrence>();

        int previousOffset = -1;

        for (int i = 0;
             i <= data.Length - SearchPattern.Length;
             i++)
        {
            if (!ContainsPatternAt(data, i))
                continue;

            results.Add(new RepeatedStructureOccurrence
            {
                Offset = i,
                DistanceFromPrevious = previousOffset < 0
                    ? 0
                    : i - previousOffset
            });

            previousOffset = i;

            // Advance by one byte so overlapping matches
            // are not accidentally missed.
        }

        return results;
    }

    private static bool ContainsPatternAt(
        byte[] data,
        int offset)
    {
        if (offset < 0 ||
            offset + SearchPattern.Length > data.Length)
        {
            return false;
        }

        for (int i = 0; i < SearchPattern.Length; i++)
        {
            if (data[offset + i] != SearchPattern[i])
                return false;
        }

        return true;
    }

    private static void AppendOccurrenceList(
        StringBuilder report,
        string title,
        List<RepeatedStructureOccurrence> occurrences)
    {
        report.AppendLine(title + " OCCURRENCES");
        report.AppendLine("--------------------");

        if (occurrences.Count == 0)
        {
            report.AppendLine("None found.");
            report.AppendLine();
            return;
        }

        foreach (var occurrence in occurrences)
        {
            string distance = occurrence.DistanceFromPrevious == 0
                ? "First occurrence"
                : $"Distance from previous: " +
                  $"0x{occurrence.DistanceFromPrevious:X} " +
                  $"({occurrence.DistanceFromPrevious} bytes)";

            report.AppendLine(
                $"0x{occurrence.Offset:X4} - {distance}");
        }

        report.AppendLine();
    }

    private static string FormatContext(
        byte[] data,
        int anchorOffset,
        int patternLength,
        int contextBytes)
    {
        int start = Math.Max(0, anchorOffset - contextBytes);

        int end = Math.Min(
            data.Length,
            anchorOffset + patternLength + contextBytes);

        var parts = new List<string>();

        for (int i = start; i < end; i++)
        {
            string value = data[i].ToString("X2");

            if (i == anchorOffset)
                value = "[" + value;
            else if (i == anchorOffset + patternLength - 1)
                value = value + "]";

            parts.Add(value);
        }

        return $"  0x{start:X4}: {string.Join(" ", parts)}";
    }

    private static string FormatBytes(byte[] data)
    {
        return string.Join(
            " ",
            data.Select(value => value.ToString("X2")));
    }
}
