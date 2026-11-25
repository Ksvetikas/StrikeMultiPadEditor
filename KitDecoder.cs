
using System;
using System.Collections.Generic;
using System.Linq;

namespace StrikeMultiPadEditor;

public sealed class KitStructure
{
    public int Index { get; init; }
    public int Offset { get; init; }
    public int Length { get; init; }
    public string Signature { get; init; } = "";
    public string HexPreview { get; init; } = "";
    public string HeaderHex { get; init; } = "";
    public string BlockAHex { get; init; } = "";
    public string BlockBHex { get; init; } = "";
    public string BlockCHex { get; init; } = "";
    public string TailHex { get; init; } = "";
}

public sealed class KitiOffsetAnalysis
{
    public int RelativeOffset { get; init; }
    public string Region { get; init; } = "";
    public string Values { get; init; } = "";
    public int DistinctValueCount { get; init; }
    public string ChangeStatus { get; init; } = "";
    public string Confidence { get; init; } = "";
}

public static class KitDecoder
{
    private const int StructureSize = 0x180;

    private const int HeaderStart = 0x000;
    private const int HeaderLength = 0x020;

    private const int BlockAStart = 0x020;
    private const int BlockBStart = 0x080;
    private const int BlockCStart = 0x0E0;
    private const int BlockLength = 0x060;

    private const int TailStart = 0x140;
    private const int TailLength = 0x040;

    private static readonly byte[] KitiSignature =
    {
        0x4B, 0x49, 0x54, 0x49,
        0x01, 0x80, 0x00, 0x00
    };

    public static List<KitStructure> FindKitiStructures(byte[] data)
    {
        var structures = new List<KitStructure>();

        if (data == null || data.Length < StructureSize)
            return structures;

        int index = 1;

        for (int offset = 0;
             offset <= data.Length - StructureSize;
             offset++)
        {
            bool matches = true;

            for (int i = 0; i < KitiSignature.Length; i++)
            {
                if (data[offset + i] != KitiSignature[i])
                {
                    matches = false;
                    break;
                }
            }

            if (!matches)
                continue;

            structures.Add(new KitStructure
            {
                Index = index++,
                Offset = offset,
                Length = StructureSize,
                Signature = "KITI",
                HexPreview = Hex(data, offset, 32),
                HeaderHex = Hex(data, offset + HeaderStart, HeaderLength),
                BlockAHex = Hex(data, offset + BlockAStart, BlockLength),
                BlockBHex = Hex(data, offset + BlockBStart, BlockLength),
                BlockCHex = Hex(data, offset + BlockCStart, BlockLength),
                TailHex = Hex(data, offset + TailStart, TailLength)
            });

            // Skip this complete record after finding it.
            offset += StructureSize - 1;
        }

        return structures;
    }

    public static List<KitiOffsetAnalysis> AnalyzeOffsets(
        byte[] data,
        IReadOnlyList<KitStructure> structures)
    {
        var rows = new List<KitiOffsetAnalysis>();

        if (data == null || structures == null || structures.Count == 0)
            return rows;

        for (int relativeOffset = 0;
             relativeOffset < StructureSize;
             relativeOffset++)
        {
            var values = new List<byte>();

            foreach (KitStructure structure in structures)
            {
                int absoluteOffset = structure.Offset + relativeOffset;

                if (absoluteOffset >= 0 && absoluteOffset < data.Length)
                    values.Add(data[absoluteOffset]);
            }

            int distinctCount = values.Distinct().Count();

            rows.Add(new KitiOffsetAnalysis
            {
                RelativeOffset = relativeOffset,
                Region = GetRegionName(relativeOffset),
                Values = string.Join("  ", values.Select(
                    (value, index) => $"#{index + 1:D2}:{value:X2}")),
                DistinctValueCount = distinctCount,
                ChangeStatus = distinctCount == 1
                    ? "Constant in this kit"
                    : "Varies across records",
                Confidence = "Raw observation - semantic meaning not established"
            });
        }

        return rows;
    }

    public static List<KitiOffsetAnalysis> GetVariableOffsets(
        byte[] data,
        IReadOnlyList<KitStructure> structures)
    {
        return AnalyzeOffsets(data, structures)
            .Where(row => row.DistinctValueCount > 1)
            .ToList();
    }

    public static string GetRegionName(int relativeOffset)
    {
        if (relativeOffset >= HeaderStart &&
            relativeOffset < HeaderStart + HeaderLength)
            return "Header";

        if (relativeOffset >= BlockAStart &&
            relativeOffset < BlockAStart + BlockLength)
            return "Block A";

        if (relativeOffset >= BlockBStart &&
            relativeOffset < BlockBStart + BlockLength)
            return "Block B";

        if (relativeOffset >= BlockCStart &&
            relativeOffset < BlockCStart + BlockLength)
            return "Block C";

        if (relativeOffset >= TailStart &&
            relativeOffset < TailStart + TailLength)
            return "Tail";

        return "Outside known regions";
    }

    public static string GetHeaderPreview(byte[] data)
    {
        if (data == null || data.Length == 0)
            return "";

        return Hex(data, 0, Math.Min(128, data.Length));
    }

    public static string FormatRecordReport(
        byte[] data,
        IReadOnlyList<KitStructure> structures)
    {
        if (structures == null || structures.Count == 0)
            return "No complete KITI records detected.";

        var lines = new List<string>
        {
            $"KITI records detected: {structures.Count}",
            $"Record size: 0x{StructureSize:X} ({StructureSize} bytes)",
            "Observed structural regions:",
            "  0x000-0x01F  Header (32 bytes)",
            "  0x020-0x07F  Block A (96 bytes)",
            "  0x080-0x0DF  Block B (96 bytes)",
            "  0x0E0-0x13F  Block C (96 bytes)",
            "  0x140-0x17F  Tail (64 bytes)",
            "",
            "Record offsets:"
        };

        foreach (KitStructure structure in structures)
        {
            lines.Add(
                $"  KITI #{structure.Index:D2}: " +
                $"kit offset 0x{structure.Offset:X4}, " +
                $"length 0x{structure.Length:X}");
        }

        lines.Add("");
        lines.Add("Interpretation cautions:");
        lines.Add("  - The three 96-byte regions are structural regions only.");
        lines.Add("  - They are not proven to represent physical pads or sound layers.");
        lines.Add("  - A changing byte is not automatically a decoded setting.");
        lines.Add("  - Pad assignment and field meanings still require evidence.");

        return string.Join(Environment.NewLine, lines);
    }

    private static string Hex(byte[] data, int offset, int length)
    {
        if (length <= 0)
            return "";

        return BitConverter.ToString(data, offset, length);
    }
    public static string BuildFullDumpReport(
    byte[] data,
    IReadOnlyList<KitStructure> structures)
    {
        if (structures == null)
            structures = Array.Empty<KitStructure>();

        if (data == null || data.Length == 0)
            return "No kit data available.";

        var lines = new List<string>
        {
            "STRIKE MULTIPAD KIT ANALYSIS",
            "============================",
            $"Kit data size: {data.Length:N0} bytes",
            $"KITI structures found: {structures.Count}",
            "",
            FormatRecordReport(data, structures),
            "",
            "FULL KIT HEX DUMP",
            "================="
        };

        const int bytesPerLine = 16;

        for (int offset = 0; offset < data.Length; offset += bytesPerLine)
        {
            int count = Math.Min(bytesPerLine, data.Length - offset);

            string hex = BitConverter.ToString(data, offset, count)
                .Replace("-", " ");

            string ascii = new string(
                data.Skip(offset).Take(count)
                    .Select(b => b >= 32 && b <= 126 ? (char)b : '.')
                    .ToArray());

            lines.Add($"{offset:X4}  {hex,-47}  {ascii}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    
    public static string DescribeKitiLayout(
        byte[] data,
        IReadOnlyList<KitStructure> structures)
    {
        if (structures.Count == 0)
        {
            return "No KITI records found.";
        }

        int firstOffset = structures[0].Offset;
        int lastOffset = structures[^1].Offset;

        int stride = structures.Count > 1
            ? structures[1].Offset - structures[0].Offset
            : 0;

        bool consistentStride = true;

        for (int i = 1; i < structures.Count; i++)
        {
            if (structures[i].Offset - structures[i - 1].Offset != stride)
            {
                consistentStride = false;
                break;
            }
        }

        int endOffset = lastOffset + StructureSize;
        int prefixSize = firstOffset;
        int remaining = data.Length - endOffset;

        return
            $"KITI records: {structures.Count}\n" +
            $"First record: 0x{firstOffset:X4}\n" +
            $"Last record: 0x{lastOffset:X4}\n" +
            $"Record size: 0x{StructureSize:X}\n" +
            $"Record stride: {(stride > 0 ? $"0x{stride:X}" : "Unknown")}\n" +
            $"Consistent stride: {consistentStride}\n" +
            $"Prefix size: {prefixSize:N0} bytes\n" +
            $"Remaining data: {remaining:N0} bytes";
    }

}
