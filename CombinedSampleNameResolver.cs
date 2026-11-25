using System;
using System.IO;
using System.Linq;
using System.Text;

namespace StrikeMultiPadEditor;

/// <summary>
/// Produces one consolidated report for references actually assigned in
/// the kit layer blocks. Layer metadata wins; the factory translation table
/// is used only when no layer-metadata name exists.
/// </summary>
public static class CombinedSampleNameResolver
{
    public static string Scan(string backupPath)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Backup file not found.", backupPath);

        var layerLookup = LayerSampleNameResolver.BuildLookup(backupPath);
        var factoryLookup = WaveSampleNameDecoder.BuildLookup(backupPath);
        var kits = BackupKitExtractor.ReadKits(backupPath);

        var references = kits
            .SelectMany(CandidateLayerDecoder.Decode)
            .Where(layer => layer.IsAssigned)
            .Select(layer => layer.SampleReference)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(reference => reference, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int layerMatches = references.Count(r => layerLookup.ContainsKey(r));
        int factoryFallbacks = references.Count(r =>
            !layerLookup.ContainsKey(r) && factoryLookup.ContainsKey(r));
        int unresolved = references.Count(r =>
            !layerLookup.ContainsKey(r) && !factoryLookup.ContainsKey(r));
        int overlaps = references.Count(r =>
            layerLookup.ContainsKey(r) && factoryLookup.ContainsKey(r) &&
            !string.Equals(
                layerLookup[r].SampleName,
                factoryLookup[r].SampleName,
                StringComparison.OrdinalIgnoreCase));

        var output = new StringBuilder();
        output.AppendLine("STRIKE MULTIPAD COMBINED LAYER REFERENCE -> SAMPLE NAME LOOKUP");
        output.AppendLine($"Backup: {Path.GetFileName(backupPath)}");
        output.AppendLine("References are collected from assigned candidate layer blocks in all kit records.");
        output.AppendLine("Priority: layer metadata first; factory translation table only as fallback.");
        output.AppendLine();
        output.AppendLine($"DISTINCT ASSIGNED REFERENCES: {references.Count}");
        output.AppendLine($"RESOLVED FROM LAYER METADATA: {layerMatches}");
        output.AppendLine($"RESOLVED FROM FACTORY FALLBACK: {factoryFallbacks}");
        output.AppendLine($"UNRESOLVED: {unresolved}");
        output.AppendLine($"CROSS-TABLE NAME CONFLICTS: {overlaps}");
        output.AppendLine();
        output.AppendLine("REFERENCE | CHOSEN SAMPLE NAME | SOURCE | LAYER METADATA NAME | FACTORY TABLE NAME");
        output.AppendLine("------------------------------------------------------------------------------------------------");

        foreach (string reference in references)
        {
            bool hasLayer = layerLookup.TryGetValue(reference, out var layerMatch);
            bool hasFactory = factoryLookup.TryGetValue(reference, out var factoryMatch);

            string chosenName;
            string source;
            if (hasLayer)
            {
                chosenName = layerMatch!.SampleName;
                source = "Layer metadata";
            }
            else if (hasFactory)
            {
                chosenName = factoryMatch!.SampleName;
                source = "Factory fallback";
            }
            else
            {
                chosenName = "[not found]";
                source = "Unresolved";
            }

            string layerName = hasLayer ? layerMatch!.SampleName : "—";
            string factoryName = hasFactory ? factoryMatch!.SampleName : "—";

            output.AppendLine(
                $"{reference} | {chosenName} | {source} | {layerName} | {factoryName}");

            if (hasLayer)
            {
                output.AppendLine(
                    $"  Layer metadata record offset: 0x{layerMatch!.RecordOffset:X8}");
            }

            if (hasFactory)
            {
                output.AppendLine(
                    $"  Factory index: 0x{factoryMatch!.SampleIndex:X2} ({factoryMatch.SampleIndex}); " +
                    $"translation: 0x{factoryMatch.TranslationOffset:X8}; " +
                    $"name record: 0x{factoryMatch.NameRecordOffset:X8}");
            }

            if (hasLayer && hasFactory &&
                !string.Equals(
                    layerMatch!.SampleName,
                    factoryMatch!.SampleName,
                    StringComparison.OrdinalIgnoreCase))
            {
                output.AppendLine(
                    "  NOTE: Both tables contain this two-byte value but give different names. " +
                    "Layer metadata is retained as the selected name; the factory result is shown for diagnosis.");
            }
        }

        output.AppendLine();
        output.AppendLine("FACTORY TRANSLATION TABLE");
        output.AppendLine("-------------------------");
        output.Append(WaveSampleNameDecoder.Scan(backupPath));
        output.AppendLine();
        output.AppendLine("END OF COMBINED SAMPLE NAME LOOKUP");

        return output.ToString();
    }
}
