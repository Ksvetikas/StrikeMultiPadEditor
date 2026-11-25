
using System;
using System.Collections.Generic;
using System.Linq;

namespace StrikeMultiPadEditor;

public sealed class CandidateLayer
{
    public int RecordNumber { get; init; }
    public int RecordOffset { get; init; }
    public string LayerName { get; init; } = "";
    public string SampleName { get; set; } = "Not resolved";
    public string SampleNameSource { get; set; } = "Not resolved";
    public int DataOffset { get; init; }

    public byte[] FirstFourBytes { get; init; } = [];
    public byte AuxiliaryByte05 { get; init; }
    public List<StrikeParameter> PositionParameters { get; init; } = [];
    public List<StrikeParameter> LayerParameters { get; init; } = [];
    public string AuxiliaryBytesHex { get; init; } = "";
    public string RawBlockHex { get; init; } = "";

    public string FirstFourHex =>
        string.Join(" ", FirstFourBytes.Select(
            b => b.ToString("X2")));

    // Candidate decoded fields

    public string SampleReference =>
        FirstFourBytes.Length < 2
            ? "Unknown"
            : FirstFourBytes[0] == 0xFF &&
              FirstFourBytes[1] == 0xFF
                ? "Unassigned"
                : $"{FirstFourBytes[0]:X2} {FirstFourBytes[1]:X2}";

    public bool IsAssigned =>
        FirstFourBytes.Length >= 2 &&
        !(FirstFourBytes[0] == 0xFF &&
          FirstFourBytes[1] == 0xFF);

    public string AssignmentStatus =>
        IsAssigned ? "Assigned?" : "Unassigned?";

    public int VelocityLow =>
        FirstFourBytes.Length >= 4 ? FirstFourBytes[2] : 0;

    public int VelocityHigh =>
        FirstFourBytes.Length >= 4 ? FirstFourBytes[3] : 0;

    public string VelocityRangeText =>
        $"{VelocityLow}–{VelocityHigh}";

    public int AuxiliaryByte05Value => AuxiliaryByte05;
}

public static class CandidateLayerDecoder
{
    private const int RecordSize = 0x180;
    private const int LayerASubOffset = 0x80;
    private const int LayerBSubOffset = 0xE0;
    private const int LayerBlockSize = 0x60;

    public static List<CandidateLayer> Decode(KitFile kit)
    {
        var result = new List<CandidateLayer>();

        if (kit.Data == null || kit.Data.Length < RecordSize)
            return result;

        var records = KitDecoder.FindKitiStructures(kit.Data);

        int recordNumber = 0;

        foreach (var record in records)
        {
            recordNumber++;

            AddLayer(result, kit.Data, recordNumber,
                record.Offset, "Layer A", LayerASubOffset);

            AddLayer(result, kit.Data, recordNumber,
                record.Offset, "Layer B", LayerBSubOffset);
        }

        return result;
    }

    private static void AddLayer(
        List<CandidateLayer> result,
        byte[] data,
        int recordNumber,
        int recordOffset,
        string layerName,
        int subOffset)
    {
        int layerOffset = recordOffset + subOffset;

        if (layerOffset < 0 ||
            layerOffset + LayerBlockSize > data.Length)
            return;

        byte[] firstFour = data
            .Skip(layerOffset)
            .Take(4)
            .ToArray();

        result.Add(new CandidateLayer
        {
            RecordNumber = recordNumber,
            RecordOffset = recordOffset,
            LayerName = layerName,
            DataOffset = layerOffset,
            FirstFourBytes = firstFour,
            AuxiliaryByte05 = data[layerOffset + 0x05],
            AuxiliaryBytesHex = Convert.ToHexString(
                data.AsSpan(layerOffset + 0x04, 4)),
            PositionParameters = StrikeParameterDecoder.DecodeKitiTable(
                data, recordOffset),
            LayerParameters = StrikeParameterDecoder.DecodeLayerTable(
                data, layerOffset),
            RawBlockHex = Convert.ToHexString(
                data.AsSpan(layerOffset, LayerBlockSize))
        });
    }
}
