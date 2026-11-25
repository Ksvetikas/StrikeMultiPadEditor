
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StrikeMultiPadEditor;

public sealed class SampleReferenceResolution
{
    public string Reference { get; init; } = "";
    public string SampleName { get; init; } = "";
    public string MetadataFile { get; init; } = "";
    public int MetadataMatchCount { get; init; }

    public bool IsResolved =>
        MetadataMatchCount == 1 &&
        !string.IsNullOrWhiteSpace(SampleName);

    public string Status =>
        IsResolved ? "Matched" :
        MetadataMatchCount > 1 ? "Ambiguous" :
        "Unresolved";
}

public static class SampleReferenceResolver
{
    public static Dictionary<string, SampleReferenceResolution>
        ResolveFolder(string folderPath)
    {
        var result =
            new Dictionary<string, SampleReferenceResolution>(
                StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException(
                $"Metadata folder not found: {folderPath}");

        List<HadMetadataEntry> entries =
            HadMetadataDecoder.ScanFolder(folderPath);

        var references = entries
            .SelectMany(entry => entry.ReferenceMatches
                .Select(match => new
                {
                    Reference = Normalize(match.ReferenceHex),
                    Entry = entry
                }))
            .Where(x => !string.IsNullOrWhiteSpace(x.Reference))
            .GroupBy(x => x.Reference);

        foreach (var group in references)
        {
            var matchingEntries = group
                .Select(x => x.Entry)
                .GroupBy(GetEntryIdentity)
                .Select(x => x.First())
                .ToList();

            string sampleName = "";
            string metadataFile = "";

            if (matchingEntries.Count == 1)
            {
                var entry = matchingEntries[0];

                sampleName = GetSampleName(entry);
                metadataFile = entry.FileName;
            }

            result[group.Key] = new SampleReferenceResolution
            {
                Reference = group.Key,
                SampleName = sampleName,
                MetadataFile = metadataFile,
                MetadataMatchCount = matchingEntries.Count
            };
        }

        return result;
    }

    private static string Normalize(string value)
    {
        return string.Join(
            " ",
            (value ?? "")
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();
    }

    private static string GetEntryIdentity(
        HadMetadataEntry entry)
    {
        return !string.IsNullOrWhiteSpace(entry.FullPath)
            ? entry.FullPath
            : entry.FileName;
    }

    private static string GetSampleName(
        HadMetadataEntry entry)
    {
        // Prefer a WAV filename found in the metadata.
        if (!string.IsNullOrWhiteSpace(entry.FullPath) &&
            entry.FullPath.EndsWith(
                ".wav",
                StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileNameWithoutExtension(
                entry.FullPath);
        }

        foreach (string fragment in entry.TextFragments)
        {
            if (fragment.Contains(
                ".wav",
                StringComparison.OrdinalIgnoreCase))
            {
                string cleaned = fragment.Trim()
                    .Trim('"', '\'', '\0');

                string name =
                    Path.GetFileNameWithoutExtension(cleaned);

                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
        }

        // Do not assume the .had filename is the sample name.
        return "";
    }
}
