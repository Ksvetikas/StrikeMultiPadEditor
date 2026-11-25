using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace StrikeMultiPadEditor;

public sealed class WaveSampleNameMatch
{
    public string Reference { get; init; } = "";
    public int SampleIndex { get; init; }
    public string SampleName { get; init; } = "";
    public int TranslationOffset { get; init; }
    public int NameRecordOffset { get; init; }
    public string SecondaryReference { get; init; } = "";
}

/// <summary>
/// Resolves the two-byte KITI reference through the backup's factory
/// translation table and then through wave_list_sort_name.lst.
///
/// Verified translation entry layout (8 bytes):
///   +0..1 secondary reference
///   +2..3 50xx sample index
///   +4..5 zero bytes
///   +6..7 KITI reference
///
/// The 50xx low byte selects the big-endian record number in the
/// wave_list_sort_name.lst table.
/// </summary>
public static class WaveSampleNameDecoder
{
    private const int TranslationRecordSize = 8;
    private const int NameRecordSize = 0x90;
    private const int NameOffset = 0x10;
    private const int NameCapacity = 0x80;
    private const int MinimumTranslationRun = 32;

    public static Dictionary<string, WaveSampleNameMatch> BuildLookup(
        string backupPath)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Backup file not found.", backupPath);

        byte[] data = File.ReadAllBytes(backupPath);
        Dictionary<int, (string Name, int Offset)> names =
            ReadFactoryNameTable(data);

        int tableStart = FindTranslationTable(data);
        var lookup = new Dictionary<string, WaveSampleNameMatch>(
            StringComparer.OrdinalIgnoreCase);

        for (int offset = tableStart;
             offset + TranslationRecordSize <= data.Length;
             offset += TranslationRecordSize)
        {
            if (!IsTranslationRecord(data, offset))
                break;

            int sampleIndex = data[offset + 3];
            string reference =
                $"{data[offset + 6]:X2} {data[offset + 7]:X2}";

            if (!names.TryGetValue(sampleIndex, out var nameEntry))
                continue;

            lookup.TryAdd(reference, new WaveSampleNameMatch
            {
                Reference = reference,
                SampleIndex = sampleIndex,
                SampleName = nameEntry.Name,
                TranslationOffset = offset,
                NameRecordOffset = nameEntry.Offset,
                SecondaryReference =
                    $"{data[offset]:X2} {data[offset + 1]:X2}"
            });
        }

        return lookup;
    }

    public static string Scan(string backupPath)
    {
        Dictionary<string, WaveSampleNameMatch> lookup =
            BuildLookup(backupPath);

        var output = new StringBuilder();
        output.AppendLine("STRIKE MULTIPAD KITI REFERENCE -> FACTORY SAMPLE NAME");
        output.AppendLine($"Backup: {Path.GetFileName(backupPath)}");
        output.AppendLine("Lookup chain:");
        output.AppendLine("KITI reference -> 8-byte translation entry -> 50xx index -> wave_list_sort_name.lst record");
        output.AppendLine();
        output.AppendLine($"Decoded translation entries with names: {lookup.Count}");
        output.AppendLine();
        output.AppendLine("KITI REF | SAMPLE INDEX | FACTORY SAMPLE NAME | TRANSLATION OFFSET | NAME RECORD OFFSET");
        output.AppendLine("------------------------------------------------------------------------------------------");

        foreach (WaveSampleNameMatch match in lookup.Values
                     .OrderBy(x => x.SampleIndex))
        {
            output.AppendLine(
                $"{match.Reference} | 0x{match.SampleIndex:X2} ({match.SampleIndex}) | " +
                $"{match.SampleName} | 0x{match.TranslationOffset:X8} | " +
                $"0x{match.NameRecordOffset:X8}");
        }

        output.AppendLine();
        output.AppendLine("Verified examples from this backup:");
        AddExample(output, lookup, "1F 83");
        AddExample(output, lookup, "1F 9C");
        output.AppendLine();
        output.AppendLine("END OF FACTORY SAMPLE NAME LOOKUP");
        return output.ToString();
    }

    private static void AddExample(
        StringBuilder output,
        Dictionary<string, WaveSampleNameMatch> lookup,
        string reference)
    {
        if (lookup.TryGetValue(reference, out WaveSampleNameMatch? match))
        {
            output.AppendLine(
                $"{reference} -> {match.SampleName} " +
                $"(index 0x{match.SampleIndex:X2}, " +
                $"translation 0x{match.TranslationOffset:X8}, " +
                $"name record 0x{match.NameRecordOffset:X8})");
        }
        else
        {
            output.AppendLine($"{reference} -> no mapping in translation table");
        }
    }

    private static Dictionary<int, (string Name, int Offset)> ReadFactoryNameTable(
        byte[] data)
    {
        int fileNamePosition = FindBytes(
            data, Encoding.ASCII.GetBytes("wave_list_sort_name.lst"));

        if (fileNamePosition < 0)
            throw new InvalidDataException(
                "wave_list_sort_name.lst was not found in the backup.");

        int headerPosition = FindBytes(
            data, Encoding.ASCII.GetBytes("SLTH"), fileNamePosition);

        if (headerPosition < 0 ||
            headerPosition + 0x20 > data.Length)
        {
            throw new InvalidDataException(
                "The sample-name table header (SLTH) was not found.");
        }

        int count = data[headerPosition + 8] |
                    (data[headerPosition + 9] << 8);
        int recordsStart = headerPosition + 0x20;

        if (count <= 0 || count > 10000 ||
            recordsStart + (long)count * NameRecordSize > data.Length)
        {
            throw new InvalidDataException(
                $"Invalid factory sample-name table size: {count} entries.");
        }

        var result = new Dictionary<int, (string Name, int Offset)>();

        for (int i = 0; i < count; i++)
        {
            int offset = recordsStart + i * NameRecordSize;
            int recordNumber = (data[offset] << 8) | data[offset + 1];

            int nameStart = offset + NameOffset;
            int end = nameStart;
            int limit = Math.Min(data.Length, nameStart + NameCapacity);

            while (end < limit && data[end] != 0)
                end++;

            string name = Encoding.ASCII.GetString(
                data, nameStart, end - nameStart).Trim();

            if (!string.IsNullOrWhiteSpace(name))
                result[recordNumber] = (name, offset);
        }

        return result;
    }

    private static int FindTranslationTable(byte[] data)
    {
        int bestStart = -1;
        int bestCount = 0;

        // The translation list begins with index 0 encoded as 50 00.
        // Require a long consecutive run of 8-byte records to avoid
        // mistaking unrelated 50xx values elsewhere in the backup.
        for (int start = 0; start <= data.Length - TranslationRecordSize; start++)
        {
            if (data[start + 2] != 0x50 ||
                data[start + 3] != 0x00 ||
                data[start + 4] != 0x00 ||
                data[start + 5] != 0x00)
            {
                continue;
            }

            int count = 0;
            int expectedIndex = 0;
            int offset = start;

            while (offset + TranslationRecordSize <= data.Length &&
                   IsTranslationRecord(data, offset) &&
                   data[offset + 3] == (byte)expectedIndex)
            {
                count++;
                expectedIndex++;
                offset += TranslationRecordSize;

                if (expectedIndex > 255)
                    break;
            }

            if (count > bestCount)
            {
                bestCount = count;
                bestStart = start;
            }
        }

        if (bestStart < 0 || bestCount < MinimumTranslationRun)
        {
            throw new InvalidDataException(
                "The KITI-to-50xx sample translation table could not be identified.");
        }

        return bestStart;
    }

    private static bool IsTranslationRecord(byte[] data, int offset)
    {
        return offset >= 0 &&
               offset + TranslationRecordSize <= data.Length &&
               data[offset + 2] == 0x50 &&
               data[offset + 4] == 0x00 &&
               data[offset + 5] == 0x00;
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
}
