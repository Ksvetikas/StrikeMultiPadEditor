
using System;
using System.Collections.Generic;
using System.Linq;

namespace StrikeMultiPadEditor;

public sealed class KitByteDifference
{
    public int RelativeOffset { get; init; }
    public byte ValueA { get; init; }
    public byte ValueB { get; init; }
}

public sealed class KitRecordComparison
{
    public int RecordIndex { get; init; }
    public int OffsetA { get; init; }
    public int OffsetB { get; init; }
    public int LengthA { get; init; }
    public int LengthB { get; init; }
    public List<KitByteDifference> Differences { get; init; } = new();
    public int DifferenceCount => Differences.Count;
    public bool HasDifferences => Differences.Count > 0;
}

public sealed class KitComparisonResult
{
    public string KitNameA { get; init; } = "";
    public string KitNameB { get; init; } = "";

    public int RecordCountA { get; init; }
    public int RecordCountB { get; init; }

    public List<KitRecordComparison> Records { get; init; } = new();

    // Whole-file comparison
    public int FileLengthA { get; init; }
    public int FileLengthB { get; init; }
    public List<KitByteDifference> FullFileDifferences { get; init; } = new();

    public int FullFileDifferenceCount =>
        FullFileDifferences.Count + Math.Abs(FileLengthA - FileLengthB);

    public int TotalDifferences =>
        Records.Sum(record => record.DifferenceCount);

    public int MatchingRecordCount =>
        Records.Count(record => !record.HasDifferences);

    public int DifferentRecordCount =>
        Records.Count(record => record.HasDifferences);
}

public static class KitComparison
{
    public static KitComparisonResult Compare(KitFile kitA, KitFile kitB)
    {
        if (kitA == null)
            throw new ArgumentNullException(nameof(kitA));

        if (kitB == null)
            throw new ArgumentNullException(nameof(kitB));

        // Compare every byte in the complete kit files.
        var fullFileDifferences = new List<KitByteDifference>();

        int commonFileLength = Math.Min(
            kitA.Data.Length,
            kitB.Data.Length);

        for (int i = 0; i < commonFileLength; i++)
        {
            byte valueA = kitA.Data[i];
            byte valueB = kitB.Data[i];

            if (valueA != valueB)
            {
                fullFileDifferences.Add(new KitByteDifference
                {
                    RelativeOffset = i,
                    ValueA = valueA,
                    ValueB = valueB
                });
            }
        }

        // Keep the existing KITI-record comparison.
        var structuresA = KitDecoder.FindKitiStructures(kitA.Data);
        var structuresB = KitDecoder.FindKitiStructures(kitB.Data);

        var comparisons = new List<KitRecordComparison>();

        int commonCount = Math.Min(
            structuresA.Count,
            structuresB.Count);

        for (int recordIndex = 0; recordIndex < commonCount; recordIndex++)
        {
            var recordA = structuresA[recordIndex];
            var recordB = structuresB[recordIndex];

            int compareLength = Math.Min(
                recordA.Length,
                recordB.Length);

            var differences = new List<KitByteDifference>();

            for (int i = 0; i < compareLength; i++)
            {
                byte valueA = kitA.Data[recordA.Offset + i];
                byte valueB = kitB.Data[recordB.Offset + i];

                if (valueA != valueB)
                {
                    differences.Add(new KitByteDifference
                    {
                        RelativeOffset = i,
                        ValueA = valueA,
                        ValueB = valueB
                    });
                }
            }

            comparisons.Add(new KitRecordComparison
            {
                RecordIndex = recordIndex + 1,
                OffsetA = recordA.Offset,
                OffsetB = recordB.Offset,
                LengthA = recordA.Length,
                LengthB = recordB.Length,
                Differences = differences
            });
        }

        return new KitComparisonResult
        {
            KitNameA = kitA.FileName,
            KitNameB = kitB.FileName,

            RecordCountA = structuresA.Count,
            RecordCountB = structuresB.Count,

            Records = comparisons,

            FileLengthA = kitA.Data.Length,
            FileLengthB = kitB.Data.Length,
            FullFileDifferences = fullFileDifferences
        };
    }
}
