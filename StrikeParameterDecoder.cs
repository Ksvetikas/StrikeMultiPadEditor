using System;
using System.Collections.Generic;
using System.Linq;

namespace StrikeMultiPadEditor;

/// <summary>
/// A four-byte parameter entry found in a KITI position or layer table.
/// The last two bytes are retained as metadata; they are not assumed to
/// mean minimum and maximum for every parameter.
/// </summary>
public sealed class StrikeParameter
{
    public byte Id { get; init; }
    public byte Value { get; init; }
    public byte Metadata1 { get; init; }
    public byte Metadata2 { get; init; }
    public int RelativeOffset { get; init; }
    public string RawHex => $"{Id:X2} {Value:X2} {Metadata1:X2} {Metadata2:X2}";
    public string Meaning { get; init; } = "Unidentified parameter";
    public string Confidence { get; init; } = "Unknown";

    public string DisplayValue
    {
        get
        {
            if (Id == 0x08)
                return $"{Value} (MIDI note)";
            if (Id == 0x09)
                return $"{Value} ({StrikeParameterDecoder.GetVelocityCurveName(Value)})";
            return Value.ToString();
        }
    }
}

public static class StrikeParameterDecoder
{
    public const int KitiParameterOffset = 0x28;
    public const int LayerParameterOffset = 0x08;
    public const int ParameterSlotSize = 4;
    public const int ParameterSlotCount = 22;

    public static List<StrikeParameter> DecodeKitiTable(byte[] data, int kitiOffset)
    {
        return DecodeTable(data, kitiOffset + KitiParameterOffset, true);
    }

    public static List<StrikeParameter> DecodeLayerTable(byte[] data, int layerOffset)
    {
        return DecodeTable(data, layerOffset + LayerParameterOffset, false);
    }

    private static List<StrikeParameter> DecodeTable(
        byte[] data, int tableOffset, bool isKiti)
    {
        var result = new List<StrikeParameter>();

        if (data == null || tableOffset < 0 ||
            tableOffset + ParameterSlotCount * ParameterSlotSize > data.Length)
            return result;

        for (int slot = 0; slot < ParameterSlotCount; slot++)
        {
            int offset = tableOffset + slot * ParameterSlotSize;
            byte id = data[offset];

            // FF marks an unused parameter slot in the examined records.
            if (id == 0xFF)
                continue;

            result.Add(new StrikeParameter
            {
                Id = id,
                Value = data[offset + 1],
                Metadata1 = data[offset + 2],
                Metadata2 = data[offset + 3],
                RelativeOffset = offset - (isKiti ? tableOffset - KitiParameterOffset : tableOffset - LayerParameterOffset),
                Meaning = GetMeaning(id, isKiti),
                Confidence = GetConfidence(id, isKiti)
            });
        }

        return result;
    }

    public static string GetMeaning(byte id, bool isKiti)
    {
        if (isKiti)
        {
            return id switch
            {
                0x00 => "Kit volume (candidate)",
                0x02 => "Pan (candidate)",
                0x08 => "MIDI note",
                0x10 => "Fixed Level (candidate)",
                0x11 => "Sensitivity (candidate)",
                0x12 => "Threshold (candidate)",
                0x16 => "Kit FX Level (candidate)",
                _ => "Unidentified KITI parameter"
            };
        }

        return id switch
        {
            0x00 => "Layer volume (candidate)",
            0x02 => "Pan (candidate)",
            0x09 => "Velocity curve (candidate)",
            _ => "Unidentified layer parameter"
        };
    }

    private static string GetConfidence(byte id, bool isKiti)
    {
        if (isKiti && id == 0x08)
            return "Confirmed structural field; MIDI-note interpretation";
        if (isKiti && id is 0x11 or 0x12)
            return "Strong match to documented control range; verify against hardware";
        if (isKiti && id is 0x10 or 0x16)
            return "Range/name match; candidate";
        if (isKiti && id is 0x00 or 0x02)
            return "Strong candidate from observed range/default";
        if (!isKiti && id == 0x09)
            return "Curve-code candidate; six code labels need device confirmation";
        if (!isKiti && id is 0x00 or 0x02)
            return "Strong candidate from observed range/default";
        return "Unknown";
    }

    public static string GetVelocityCurveName(byte value)
    {
        return value switch
        {
            0 => "Normal",
            1 => "Exp1",
            2 => "Exp2",
            3 => "Log1",
            4 => "Log2",
            5 => "Loud",
            _ => "Unknown curve code"
        };
    }

    public static string FormatTable(
        IEnumerable<StrikeParameter> parameters,
        string tableName)
    {
        var lines = new List<string> { tableName };

        foreach (StrikeParameter parameter in parameters.OrderBy(p => p.RelativeOffset))
        {
            lines.Add(
                $"  ID {parameter.Id:X2} | {parameter.Meaning} | " +
                $"value={parameter.DisplayValue} | " +
                $"metadata={parameter.Metadata1:X2} {parameter.Metadata2:X2} | " +
                $"raw={parameter.RawHex} | confidence={parameter.Confidence}");
        }

        if (lines.Count == 1)
            lines.Add("  No populated parameter entries detected.");

        return string.Join(Environment.NewLine, lines);
    }
}
