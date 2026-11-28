using System;
using System.IO;
using System.Linq;
using System.Text;

namespace StrikeMultiPadEditor;

public static class CombinedSampleNameResolver
{
    public static string Scan(string backupPath)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Backup file not found.", backupPath);

        var layerLookup = LayerSampleNameResolver.BuildLookup(backupPath);
        var catalogueLookup = WaveSampleNameDecoder.BuildCatalogueLookup(backupPath);
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
        int catalogueMatches = references.Count(r => !layerLookup.ContainsKey(r) && catalogueLookup.ContainsKey(r));
        int factoryFallbacks = references.Count(r => !layerLookup.ContainsKey(r) &&
            !catalogueLookup.ContainsKey(r) && factoryLookup.ContainsKey(r));
        int unresolved = references.Count(r => !layerLookup.ContainsKey(r) &&
            !catalogueLookup.ContainsKey(r) && !factoryLookup.ContainsKey(r));

        var output = new StringBuilder();
        output.AppendLine("STRIKE MULTIPAD COMBINED LAYER REFERENCE -> SAMPLE NAME LOOKUP");
        output.AppendLine($"Backup: {Path.GetFileName(backupPath)}");
        output.AppendLine("Priority: layer metadata, direct full-catalogue identifier, factory translation fallback.");
        output.AppendLine();
        output.AppendLine($"DISTINCT ASSIGNED REFERENCES: {references.Count}");
        output.AppendLine($"RESOLVED FROM LAYER METADATA: {layerMatches}");
        output.AppendLine($"RESOLVED FROM FULL CATALOGUE: {catalogueMatches}");
        output.AppendLine($"RESOLVED FROM FACTORY FALLBACK: {factoryFallbacks}");
        output.AppendLine($"UNRESOLVED: {unresolved}");
        output.AppendLine();
        output.AppendLine("REFERENCE | CHOSEN SAMPLE NAME | SOURCE | LAYER NAME | CATALOGUE NAME | FACTORY NAME");
        output.AppendLine("------------------------------------------------------------------------------------------------");

        foreach (string reference in references)
        {
            bool hasLayer = layerLookup.TryGetValue(reference, out var layerMatch);
            bool hasCatalogue = catalogueLookup.TryGetValue(reference, out var catalogueMatch);
            bool hasFactory = factoryLookup.TryGetValue(reference, out var factoryMatch);

            string chosenName;
            string source;
            if (hasLayer) { chosenName = layerMatch!.SampleName; source = "Layer metadata"; }
            else if (hasCatalogue) { chosenName = catalogueMatch!.SampleName; source = "Full catalogue"; }
            else if (hasFactory) { chosenName = factoryMatch!.SampleName; source = "Factory translation"; }
            else { chosenName = "[not found]"; source = "Unresolved"; }

            output.AppendLine($"{reference} | {chosenName} | {source} | " +
                $"{(hasLayer ? layerMatch!.SampleName : "—")} | " +
                $"{(hasCatalogue ? catalogueMatch!.SampleName : "—")} | " +
                $"{(hasFactory ? factoryMatch!.SampleName : "—")}");
        }

        output.AppendLine();
        output.Append(WaveSampleNameDecoder.Scan(backupPath));
        output.AppendLine();
        output.AppendLine("END OF COMBINED SAMPLE NAME LOOKUP");
        return output.ToString();
    }
}
