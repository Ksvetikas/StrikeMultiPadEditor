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

public static class WaveSampleNameDecoder
{
    private const int FileHeaderSize = 0x180;
    private const int FileLengthOffset = 0x174;
    private const int TableHeaderSize = 0x20;
    private const int RecordSize = 0x90;
    private const int NameOffset = 0x10;
    private const int NameCapacity = 0x80;
    private const int TranslationChunk = 8000;
    private const int TranslationRecordSize = 8;
    private const int TranslationCount = 100;
    private const int FactoryIndexBase = 0x5000;

    private sealed record NameEntry(string Identifier, string Name, int Offset);

    // Direct lookup by the two-byte identifier stored at catalogue +0x02.
    // These identifiers are unique across the full factory catalogue.
    public static Dictionary<string, WaveSampleNameMatch> BuildCatalogueLookup(string backupPath)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Backup file not found.", backupPath);

        byte[] data = File.ReadAllBytes(backupPath);
        List<NameEntry> names = ReadFactoryNameTable(data);
        var lookup = new Dictionary<string, WaveSampleNameMatch>(StringComparer.OrdinalIgnoreCase);

        foreach (NameEntry entry in names)
        {
            if (string.IsNullOrWhiteSpace(entry.Identifier) ||
                lookup.ContainsKey(entry.Identifier))
                continue;

            lookup.Add(entry.Identifier, new WaveSampleNameMatch
            {
                Reference = entry.Identifier,
                SampleIndex = -1,
                SampleName = entry.Name,
                NameRecordOffset = entry.Offset
            });
        }
        return lookup;
    }

    public static Dictionary<string, WaveSampleNameMatch> BuildLookup(string backupPath)
    {
        var all = BuildLookupAll(backupPath);
        var result = new Dictionary<string, WaveSampleNameMatch>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in all)
            if (pair.Value.Count > 0)
                result[pair.Key] = pair.Value[0];
        return result;
    }

    public static Dictionary<string, List<WaveSampleNameMatch>> BuildLookupAll(string backupPath)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Backup file not found.", backupPath);

        byte[] data = File.ReadAllBytes(backupPath);
        List<NameEntry> names = ReadFactoryNameTable(data);
        (int wavePayload, int waveLength) = FindEmbeddedFile(data, "Wave_list.lst");

        int tableStart = checked(wavePayload + TableHeaderSize + TranslationChunk * TranslationRecordSize);
        int tableEnd = checked(tableStart + TranslationCount * TranslationRecordSize);
        if (tableEnd > wavePayload + waveLength)
            throw new InvalidDataException("The Wave_list.lst translation block is outside its payload.");

        var lookup = new Dictionary<string, List<WaveSampleNameMatch>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < TranslationCount; i++)
        {
            int offset = tableStart + i * TranslationRecordSize;
            int encodedIndex = ReadUInt16BE(data, offset + 2);
            if (encodedIndex < FactoryIndexBase ||
                encodedIndex >= FactoryIndexBase + TranslationCount ||
                data[offset + 4] != 0 || data[offset + 5] != 0)
                continue;

            int sampleIndex = encodedIndex - FactoryIndexBase;
            if (sampleIndex >= names.Count)
                continue;

            string reference = $"{data[offset + 6]:X2} {data[offset + 7]:X2}";
            var match = new WaveSampleNameMatch
            {
                Reference = reference,
                SampleIndex = sampleIndex,
                SampleName = names[sampleIndex].Name,
                TranslationOffset = offset,
                NameRecordOffset = names[sampleIndex].Offset,
                SecondaryReference = $"{data[offset]:X2} {data[offset + 1]:X2}"
            };
            if (!lookup.TryGetValue(reference, out var matches))
            {
                matches = new List<WaveSampleNameMatch>();
                lookup.Add(reference, matches);
            }
            matches.Add(match);
        }
        return lookup;
    }

    public static string Scan(string backupPath)
    {
        var all = BuildLookupAll(backupPath);
        var output = new StringBuilder();
        output.AppendLine("STRIKE MULTIPAD KITI REFERENCE -> FACTORY SAMPLE NAME");
        output.AppendLine($"Backup: {Path.GetFileName(backupPath)}");
        output.AppendLine("Lookup: Wave_list.lst translation rows 8000-8099 -> factory catalogue positions 0-99");
        output.AppendLine($"Distinct KITI references: {all.Count}");
        output.AppendLine($"Translation rows: {all.Values.Sum(x => x.Count)}");
        output.AppendLine($"References with multiple translation rows: {all.Count(x => x.Value.Count > 1)}");
        output.AppendLine();
        output.AppendLine("KITI REF | SAMPLE INDEX | FACTORY SAMPLE NAME | TRANSLATION OFFSET | NAME RECORD OFFSET");
        foreach (var pair in all.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            foreach (WaveSampleNameMatch match in pair.Value)
                output.AppendLine($"{match.Reference} | 0x{match.SampleIndex:X2} ({match.SampleIndex}) | {match.SampleName} | 0x{match.TranslationOffset:X8} | 0x{match.NameRecordOffset:X8}");
        output.AppendLine();
        output.AppendLine("END OF FACTORY SAMPLE NAME LOOKUP");
        return output.ToString();
    }

    private static List<NameEntry> ReadFactoryNameTable(byte[] data)
    {
        (int payloadStart, int payloadLength) = FindEmbeddedFile(data, "wave_list_sort_name.lst");
        if (payloadLength < TableHeaderSize)
            throw new InvalidDataException("The factory sample-name table is too short.");

        int recordsStart = payloadStart + TableHeaderSize;
        int recordCount = (payloadLength - TableHeaderSize) / RecordSize;
        var result = new List<NameEntry>(recordCount);

        for (int i = 0; i < recordCount; i++)
        {
            int offset = recordsStart + i * RecordSize;
            int declaredLength = ReadUInt16BE(data, offset + 4);
            if (data[offset] != 0 || declaredLength > NameCapacity)
                continue;

            int nameStart = offset + NameOffset;
            int length = 0;
            while (length < NameCapacity && data[nameStart + length] != 0)
            {
                byte value = data[nameStart + length];
                if (value < 32 || value > 126) break;
                length++;
            }
            if (length == 0) continue;

            string name = Encoding.ASCII.GetString(data, nameStart, length).Trim();
            if (name.Length > 0)
            {
                string identifier = $"{data[offset + 2]:X2} {data[offset + 3]:X2}";
                result.Add(new NameEntry(identifier, name, offset));
            }
        }

        if (result.Count < 100)
            throw new InvalidDataException($"Only {result.Count} factory names were decoded; expected at least 100.");
        return result;
    }

    private static (int PayloadStart, int PayloadLength) FindEmbeddedFile(byte[] data, string fileName)
    {
        int namePosition = FindBytes(data, Encoding.ASCII.GetBytes(fileName));
        if (namePosition < 0) throw new InvalidDataException($"{fileName} was not found in the backup.");

        int headerPosition = namePosition - 0xE0;
        if (headerPosition < 0 || !Matches(data, headerPosition, Encoding.ASCII.GetBytes("FILEH")))
            throw new InvalidDataException($"The FILEH header for {fileName} was not found.");

        int payloadLength = ReadUInt32LE(data, headerPosition + FileLengthOffset);
        int payloadStart = checked(headerPosition + FileHeaderSize);
        if (payloadLength <= 0 || payloadStart + (long)payloadLength > data.Length)
            throw new InvalidDataException($"The payload bounds for {fileName} are invalid.");
        return (payloadStart, payloadLength);
    }

    private static int ReadUInt16BE(byte[] d, int o) => (d[o] << 8) | d[o + 1];
    private static int ReadUInt32LE(byte[] d, int o) =>
        d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24);

    private static bool Matches(byte[] data, int offset, byte[] pattern)
    {
        if (offset < 0 || offset + pattern.Length > data.Length) return false;
        for (int i = 0; i < pattern.Length; i++)
            if (data[offset + i] != pattern[i]) return false;
        return true;
    }

    private static int FindBytes(byte[] data, byte[] pattern)
    {
        for (int i = 0; i <= data.Length - pattern.Length; i++)
            if (Matches(data, i, pattern)) return i;
        return -1;
    }
}
