using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace StrikeMultiPadEditor;

public sealed class LayerSampleNameMatch
{
    public string Reference { get; init; } = "";
    public string SampleName { get; init; } = "";
    public int RecordOffset { get; init; }
    public int NameLength { get; init; }
}

public static class LayerSampleNameResolver
{
    public static Dictionary<string, LayerSampleNameMatch> BuildLookup(string backupPath)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Backup file not found.", backupPath);

        byte[] data = File.ReadAllBytes(backupPath);
        var lookup = new Dictionary<string, LayerSampleNameMatch>(StringComparer.OrdinalIgnoreCase);

        // Each verified record is:
        // [record byte] [2-byte reference] [00] [name length]
        // [10 reserved zero bytes] [ASCII sample name]
        for (int i = 1; i <= data.Length - 15; i++)
        {
            if (data[i + 2] != 0)
                continue;

            int nameLength = data[i + 3];
            if (nameLength < 3 || nameLength > 0x7F || i + 14 + nameLength > data.Length)
                continue;

            bool reservedBytesAreZero = true;
            for (int j = i + 4; j < i + 14; j++)
            {
                if (data[j] != 0)
                {
                    reservedBytesAreZero = false;
                    break;
                }
            }
            if (!reservedBytesAreZero)
                continue;

            // Some records store a fixed-width name field whose declared
            // length includes trailing NUL padding (for example TRAIMO~1).
            // Accept printable ASCII followed only by zero padding.
            int actualNameLength = nameLength;
            for (int j = 0; j < nameLength; j++)
            {
                byte value = data[i + 14 + j];
                if (value == 0)
                {
                    actualNameLength = j;
                    for (int k = j; k < nameLength; k++)
                    {
                        if (data[i + 14 + k] != 0)
                        {
                            actualNameLength = 0;
                            break;
                        }
                    }
                    break;
                }
                if (value < 32 || value > 126)
                {
                    actualNameLength = 0;
                    break;
                }
            }
            if (actualNameLength < 3)
                continue;

            string reference = $"{data[i]:X2} {data[i + 1]:X2}";
            string name = Encoding.ASCII.GetString(data, i + 14, actualNameLength).Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            // Keep the first valid record for a reference. The tested references
            // in Backup005.bkf each have exactly one matching record.
            lookup.TryAdd(reference, new LayerSampleNameMatch
            {
                Reference = reference,
                SampleName = name,
                RecordOffset = i - 1,
                NameLength = nameLength
            });
        }

        return lookup;
    }

    public static string Scan(string backupPath)
    {
        var lookup = BuildLookup(backupPath);
        var kits = BackupKitExtractor.ReadKits(backupPath);
        var references = kits
            .SelectMany(CandidateLayerDecoder.Decode)
            .Where(layer => layer.IsAssigned)
            .Select(layer => layer.SampleReference)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(reference => reference, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var output = new StringBuilder();
        output.AppendLine("STRIKE MULTIPAD LAYER REFERENCE TO SAMPLE NAME LOOKUP");
        output.AppendLine($"Backup: {backupPath}");
        output.AppendLine("Scanned assigned layer references from all kit records.");
        output.AppendLine("Name records accept printable ASCII with optional trailing NUL padding.");
        output.AppendLine();
        output.AppendLine($"DISTINCT ASSIGNED REFERENCES: {references.Count}");
        output.AppendLine($"REFERENCES WITH NAMES: {references.Count(reference => lookup.ContainsKey(reference))}");
        output.AppendLine($"UNRESOLVED REFERENCES: {references.Count(reference => !lookup.ContainsKey(reference))}");
        output.AppendLine();
        output.AppendLine("REFERENCE | SAMPLE NAME | RECORD OFFSET | STATUS");
        output.AppendLine("---------------------------------------------------------------");

        foreach (string reference in references)
        {
            if (lookup.TryGetValue(reference, out LayerSampleNameMatch? match))
                output.AppendLine($"{reference} | {match.SampleName} | 0x{match.RecordOffset:X8} | Matched");
            else
                output.AppendLine($"{reference} | [not found] | - | Unresolved");
        }
        output.AppendLine();
        output.AppendLine("END OF LAYER REFERENCE LOOKUP");
        return output.ToString();
    }


}
