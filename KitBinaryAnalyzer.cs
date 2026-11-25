
using System;
using System.Collections.Generic;
using System.Text;

namespace StrikeMultiPadEditor;

public static class KitBinaryAnalyzer
{
    private static readonly byte[] KitiSignature =
    {
        0x4B, 0x49, 0x54, 0x49, 0x01, 0x80, 0x00, 0x00
    };

    private static readonly byte[] Marker =
    {
        0x00, 0x7F, 0x00, 0x00, 0xFF, 0xFF, 0xFF
    };

    public static string Analyze(KitFile kit)
    {
        byte[] data = kit.Data;
        var output = new StringBuilder();

        output.AppendLine("STRIKE MULTIPAD - KIT BINARY ANALYSIS");
        output.AppendLine("====================================");
        output.AppendLine($"Kit: {kit.FileName}");
        output.AppendLine($"Type: {kit.KitType}");
        output.AppendLine($"Payload size: {data.Length:N0} bytes");
        output.AppendLine();

        List<int> kitiOffsets = FindAll(data, KitiSignature);
        int firstKiti = kitiOffsets.Count > 0
            ? kitiOffsets[0]
            : data.Length;

        output.AppendLine("1. KITI RECORDS");
        output.AppendLine("---------------");
        output.AppendLine($"Records found: {kitiOffsets.Count}");
        output.AppendLine($"First KITI offset: 0x{firstKiti:X4}");

        foreach (int offset in kitiOffsets)
        {
            output.AppendLine(
                $"KITI at 0x{offset:X4} ({offset})");
        }

        output.AppendLine();
        output.AppendLine("2. PREFIX BEFORE FIRST KITI");
        output.AppendLine("---------------------------");
        output.AppendLine(
            $"Prefix length: {firstKiti} bytes (0x{firstKiti:X})");

        AppendAsciiStrings(output, data, 0, firstKiti);

        output.AppendLine();
        output.AppendLine("3. REPEATED MARKER OCCURRENCES");
        output.AppendLine("------------------------------");

        List<int> markerOffsets = FindAll(data, Marker);

        output.AppendLine(
            $"Total marker occurrences: {markerOffsets.Count}");

        int prefixMarkers = 0;

        foreach (int offset in markerOffsets)
        {
            bool inPrefix = offset < firstKiti;

            if (inPrefix)
                prefixMarkers++;

            output.AppendLine();
            output.AppendLine(
                $"Marker at 0x{offset:X4} " +
                (inPrefix ? "[PREFIX]" : "[AFTER KITI START]"));

            if (offset >= 2)
            {
                byte a = data[offset - 2];
                byte b = data[offset - 1];

                ushort bigEndian =
                    (ushort)((a << 8) | b);

                ushort littleEndian =
                    (ushort)(a | (b << 8));

                output.AppendLine(
                    $"  Previous bytes: {a:X2} {b:X2}");

                output.AppendLine(
                    $"  Big-endian value: {bigEndian} " +
                    $"(0x{bigEndian:X4})");

                output.AppendLine(
                    $"  Little-endian value: {littleEndian} " +
                    $"(0x{littleEndian:X4})");
            }

            output.AppendLine(
                $"  Byte after marker: " +
                (offset + Marker.Length < data.Length
                    ? data[offset + Marker.Length].ToString("X2")
                    : "Unavailable"));

            int blockStart = offset - 0x42;

            if (blockStart >= 0)
            {
                output.AppendLine(
                    $"  Candidate 0x60 block start: 0x{blockStart:X4}");

                output.AppendLine(
                    DumpHex(data, blockStart, 0x60));
            }
        }

        output.AppendLine();
        output.AppendLine(
            $"Markers before first KITI: {prefixMarkers}");
        output.AppendLine();
        AppendKitiVariationAnalysis(output, data, kitiOffsets);
        AppendCandidateLayerAnalysis(output, data, kitiOffsets);

        output.AppendLine();
        output.AppendLine("4. PREVIOUSLY OBSERVED CHANGING OFFSETS");
        output.AppendLine("---------------------------------------");

        AppendOffset(output, data, 0x0761);
        AppendOffset(output, data, 0x0769);
        AppendOffset(output, data, 0x08E0);
        AppendOffset(output, data, 0x08E1);
        AppendOffset(output, data, 0x0BE1);
        AppendOffset(output, data, 0x0D60);
        AppendOffset(output, data, 0x0D61);

        output.AppendLine();
        output.AppendLine("5. INTERPRETATION");
        output.AppendLine("-----------------");
        output.AppendLine(
            "These are observed binary structures, not confirmed " +
            "pad parameter mappings.");
        output.AppendLine(
            "The two bytes before a marker are displayed in both " +
            "byte orders.");
        output.AppendLine(
            "A candidate 0x60 block is based on the observed " +
            "marker position at block offset 0x42.");
        output.AppendLine(
            "No byte is labelled Sensitivity, Volume, Pan or " +
            "another setting without supporting evidence.");

        return output.ToString();
    }

    private static List<int> FindAll(byte[] data, byte[] pattern)
    {
        var results = new List<int>();

        if (pattern.Length == 0 || data.Length < pattern.Length)
            return results;

        for (int i = 0; i <= data.Length - pattern.Length; i++)
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
                results.Add(i);
        }

        return results;
    }

    private static void AppendAsciiStrings(
        StringBuilder output,
        byte[] data,
        int start,
        int end)
    {
        output.AppendLine("Readable strings:");

        int runStart = -1;

        for (int i = start; i < end; i++)
        {
            bool printable =
                data[i] >= 0x20 && data[i] <= 0x7E;

            if (printable)
            {
                if (runStart < 0)
                    runStart = i;
            }
            else
            {
                AppendStringRun(output, data, runStart, i);
                runStart = -1;
            }
        }

        AppendStringRun(output, data, runStart, end);
    }

    private static void AppendStringRun(
        StringBuilder output,
        byte[] data,
        int start,
        int end)
    {
        if (start < 0 || end - start < 4)
            return;

        string value = Encoding.ASCII.GetString(
            data, start, end - start);

        output.AppendLine(
            $"  0x{start:X4}: \"{value}\"");
    }

    private static void AppendOffset(
        StringBuilder output,
        byte[] data,
        int offset)
    {
        if (offset >= data.Length)
            return;

        int count = Math.Min(8, data.Length - offset);

        output.AppendLine(
            $"0x{offset:X4}: {DumpHex(data, offset, count)}");
    }

    private static string DumpHex(
        byte[] data,
        int offset,
        int count)
    {
        int available = Math.Min(count, data.Length - offset);

        if (available <= 0)
            return "";

        var result = new StringBuilder();

        for (int i = 0; i < available; i++)
        {
            if (i > 0)
                result.Append(' ');

            result.Append(data[offset + i].ToString("X2"));
        }

        return result.ToString();
    }

    private static void AppendKitiVariationAnalysis(
        StringBuilder output,
        byte[] data,
        List<int> kitiOffsets)
    {
        output.AppendLine();
        output.AppendLine("6. KITI RECORD FIELD VARIATION");
        output.AppendLine("==============================");

        if (kitiOffsets.Count < 2)
        {
            output.AppendLine(
                "At least two KITI records are required.");
            return;
        }

        const int recordSize = 0x180;

        output.AppendLine(
            $"Comparing {kitiOffsets.Count} records, " +
            $"each {recordSize} bytes long.");

        output.AppendLine(
            "Only offsets with different values are displayed.");

        int variableCount = 0;

        for (int relativeOffset = 0;
             relativeOffset < recordSize;
             relativeOffset++)
        {
            var values = new List<byte>();

            foreach (int recordOffset in kitiOffsets)
            {
                int absoluteOffset = recordOffset + relativeOffset;

                if (absoluteOffset >= data.Length)
                    continue;

                values.Add(data[absoluteOffset]);
            }

            int distinctCount = new HashSet<byte>(values).Count;

            if (distinctCount <= 1)
                continue;

            variableCount++;

            output.AppendLine();
            output.Append(
                $"+0x{relativeOffset:X3}: ");

            for (int i = 0; i < values.Count; i++)
            {
                output.Append(
                    $"R{i + 1:D2}={values[i]:X2}");

                if (i < values.Count - 1)
                    output.Append("  ");
            }

            output.Append(
                $"  [{distinctCount} distinct values]");
        }

        output.AppendLine();
        output.AppendLine();

        output.AppendLine(
            $"Total variable byte positions: {variableCount}");

        output.AppendLine(
            "These positions are candidates for record-specific " +
            "settings, identifiers or references. Their meanings " +
            "must be established through further comparison.");
    }

    private static void AppendKitiRecordHeaders(
        StringBuilder output,
        byte[] data,
        List<int> kitiOffsets)
    {
        output.AppendLine();
        output.AppendLine("KITI RECORD HEADERS");

        foreach (int offset in kitiOffsets)
        {
            output.AppendLine(
                $"Record at 0x{offset:X4}: " +
                DumpHex(data, offset, 0x40));
        }
    }

    private static void AppendCandidateLayerAnalysis(
        StringBuilder output,
        byte[] data,
        List<int> kitiOffsets)
    {
        const int waveASubOffset = 0x80;
        const int waveBSubOffset = 0xE0;
        const int blockSize = 0x60;

        output.AppendLine();
        output.AppendLine("7. CANDIDATE WAVE A / WAVE B STRUCTURES");
        output.AppendLine("=======================================");
        output.AppendLine(
            "Comparing corresponding positions in blocks " +
            "+0x080 and +0x0E0.");

        foreach (int recordOffset in kitiOffsets)
        {
            int waveA = recordOffset + waveASubOffset;
            int waveB = recordOffset + waveBSubOffset;

            if (waveA + blockSize > data.Length ||
                waveB + blockSize > data.Length)
                continue;

            output.AppendLine();
            output.AppendLine(
                $"KITI record at 0x{recordOffset:X4}");

            output.AppendLine(
                $"Candidate Wave A: 0x{waveA:X4}");

            output.AppendLine(
                $"Candidate Wave B: 0x{waveB:X4}");

            output.AppendLine(
                "Relative offset | Wave A | Wave B");

            output.AppendLine(
                "--------------------------------");

            int differences = 0;

            for (int i = 0; i < blockSize; i++)
            {
                byte a = data[waveA + i];
                byte b = data[waveB + i];

                if (a == b)
                    continue;

                differences++;

                output.AppendLine(
                    $"+0x{i:X2}           | " +
                    $"{a:X2}      | {b:X2}");
            }

            output.AppendLine(
                $"Different byte positions: {differences}");

            output.AppendLine();
            output.AppendLine("WAVE A RAW DATA:");

            AppendHexBlock(output, data, waveA, blockSize);

            output.AppendLine();
            output.AppendLine("WAVE B RAW DATA:");

            AppendHexBlock(output, data, waveB, blockSize);
        }
    }

    private static void AppendHexBlock(
        StringBuilder output,
        byte[] data,
        int offset,
        int length)
    {
        for (int i = 0; i < length; i += 16)
        {
            output.Append(
                $"  +0x{i:X2}: ");

            int count = Math.Min(16, length - i);

            for (int j = 0; j < count; j++)
            {
                output.Append(
                    data[offset + i + j].ToString("X2"));

                output.Append(' ');
            }

            output.AppendLine();
        }
    }

}
