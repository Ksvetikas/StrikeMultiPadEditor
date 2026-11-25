
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StrikeMultiPadEditor;

public sealed class DecodedPadCandidate
{
    public int Index { get; init; }
    public int Offset { get; init; }
    public int MarkerOffset { get; init; }

    public byte FieldA { get; init; }
    public byte FieldB { get; init; }

    public ushort FieldValue =>
        (ushort)((FieldA << 8) | FieldB);

    public byte ValueAfterMarker { get; init; }

    public string CandidateValue =>
        $"{FieldA:X2} {FieldB:X2}";

    public string Context { get; init; } = "";

    public string Interpretation { get; init; } =
        "Unidentified field";
}

public static class PadParameterDecoder
{
    private const int StructureSize = 0x60;

    private static readonly byte[] Marker =
    {
        0x00, 0x7F, 0x00, 0x00,
        0xFF, 0xFF, 0xFF
    };

    public static List<DecodedPadCandidate> Decode(
        byte[] kitData)
    {
        var results = new List<DecodedPadCandidate>();

        if (kitData == null ||
            kitData.Length < Marker.Length)
            return results;

        int index = 1;

        for (int offset = 0;
             offset <= kitData.Length - Marker.Length;
             offset++)
        {
            if (!MatchesMarker(kitData, offset))
                continue;

            // The marker occurs at +0x42 in the
            // repeated 96-byte structures.
            int structureOffset = offset - 0x42;

            if (structureOffset < 0 ||
                structureOffset + StructureSize > kitData.Length)
                continue;

            byte fieldA = kitData[offset - 2];
            byte fieldB = kitData[offset - 1];

            byte afterMarker =
                offset + Marker.Length < kitData.Length
                    ? kitData[offset + Marker.Length]
                    : (byte)0;

            results.Add(new DecodedPadCandidate
            {
                Index = index++,
                Offset = structureOffset,
                MarkerOffset = offset,
                FieldA = fieldA,
                FieldB = fieldB,
                ValueAfterMarker = afterMarker,
                Context = GetHex(
                    kitData,
                    Math.Max(0, structureOffset + 0x38),
                    24)
            });

            offset += Marker.Length - 1;
        }

        return results;
    }

    public static string CreatePadReport(byte[] kitData)
    {
        var candidates = Decode(kitData);

        var lines = new List<string>
        {
            "STRIKE MULTIPAD PAD STRUCTURE DECODER",
            "=====================================",
            $"Kit size: {kitData?.Length ?? 0:N0} bytes",
            $"Structures detected: {candidates.Count}",
            ""
        };

        if (candidates.Count == 0)
        {
            lines.Add("No recognised structures found.");
            return string.Join(Environment.NewLine, lines);
        }

        foreach (var candidate in candidates)
        {
            lines.Add(
                $"STRUCTURE #{candidate.Index:D2}");

            lines.Add(
                $"Offset: 0x{candidate.Offset:X4}");

            lines.Add(
                $"Marker: 0x{candidate.MarkerOffset:X4}");

            lines.Add(
                $"Two-byte field: {candidate.CandidateValue}");

            lines.Add(
                $"16-bit value (big-endian): {candidate.FieldValue}");

            lines.Add(
                $"Byte following marker: {candidate.ValueAfterMarker:X2}");

            lines.Add(
                $"Context: {candidate.Context}");

            lines.Add("");
        }

        lines.Add("FIELD VARIATION");
        lines.Add("===============");

        var groups = candidates
            .GroupBy(x => x.CandidateValue)
            .OrderBy(x => x.Key);

        foreach (var group in groups)
        {
            lines.Add(
                $"{group.Key}: {group.Count()} occurrence(s)");
        }

        lines.Add("");
        lines.Add("IMPORTANT");
        lines.Add("=========");
        lines.Add(
            "The two-byte fields are extracted from the " +
            "position immediately before the recognised marker.");

        lines.Add(
            "Their semantic meaning and physical pad assignment " +
            "have not yet been established.");

        return string.Join(Environment.NewLine, lines);
    }

    private static bool MatchesMarker(
        byte[] data,
        int offset)
    {
        if (offset < 0 ||
            offset + Marker.Length > data.Length)
            return false;

        for (int i = 0; i < Marker.Length; i++)
        {
            if (data[offset + i] != Marker[i])
                return false;
        }

        return true;
    }

    private static string GetHex(
        byte[] data,
        int offset,
        int length)
    {
        int count = Math.Min(length, data.Length - offset);

        if (count <= 0)
            return "";

        return BitConverter.ToString(
            data, offset, count).Replace("-", " ");
    }
}
