
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace StrikeMultiPadEditor;

public sealed class HadMetadataEntry
{
    public string FileName { get; init; } = "";
    public string FullPath { get; init; } = "";
    public int FileLength { get; init; }

    public List<string> TextFragments { get; init; } = [];
    public List<HadReferenceMatch> ReferenceMatches { get; init; } = [];
}

public sealed class HadReferenceMatch
{
    public string ReferenceHex { get; init; } = "";
    public string ByteOrder { get; init; } = "";
    public int Offset { get; init; }

    public string OffsetHex => $"0x{Offset:X4}";
}

public static class HadMetadataDecoder
{
    private static readonly string[] KnownReferences =
    [
        "1A D1",
        "1A CD",
        "19 34",
        "1B E0",
        "1B DE",
        "1B E2",
        "01 F4",
        "01 BF",
        "03 73",
        "03 74",
        "01 B3"
    ];

    public static List<HadMetadataEntry> ScanFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException(
                $"HAD folder not found: {folderPath}");
        }

        var results = new List<HadMetadataEntry>();

        string[] files = Directory.GetFiles(
            folderPath,
            "*.had",
            SearchOption.AllDirectories);

        foreach (string file in files)
        {
            byte[] data = File.ReadAllBytes(file);

            results.Add(new HadMetadataEntry
            {
                FileName = Path.GetFileName(file),
                FullPath = file,
                FileLength = data.Length,
                TextFragments = ExtractText(data),
                ReferenceMatches = FindReferences(data)
            });
        }

        return results
            .OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<HadReferenceMatch> FindReferences(byte[] data)
    {
        var matches = new List<HadReferenceMatch>();

        foreach (string reference in KnownReferences)
        {
            byte[] bytes = reference
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => Convert.ToByte(x, 16))
                .ToArray();

            byte[] reversed = bytes.Reverse().ToArray();

            FindSequence(data, bytes, reference, "Forward", matches);

            FindSequence(data, reversed, reference, "Reversed", matches);
        }

        return matches
            .OrderBy(x => x.Offset)
            .ThenBy(x => x.ReferenceHex)
            .ToList();
    }

    private static void FindSequence(
        byte[] data,
        byte[] pattern,
        string reference,
        string byteOrder,
        List<HadReferenceMatch> matches)
    {
        if (pattern.Length == 0 || data.Length < pattern.Length)
            return;

        for (int offset = 0;
             offset <= data.Length - pattern.Length;
             offset++)
        {
            bool found = true;

            for (int i = 0; i < pattern.Length; i++)
            {
                if (data[offset + i] != pattern[i])
                {
                    found = false;
                    break;
                }
            }

            if (!found)
                continue;

            matches.Add(new HadReferenceMatch
            {
                ReferenceHex = reference,
                ByteOrder = byteOrder,
                Offset = offset
            });
        }
    }

    private static List<string> ExtractText(byte[] data)
    {
        var fragments = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        ExtractAscii(data, fragments);
        ExtractUtf16LittleEndian(data, fragments);

        return fragments
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ExtractAscii(
        byte[] data,
        HashSet<string> fragments)
    {
        var current = new StringBuilder();

        foreach (byte value in data)
        {
            if (value >= 32 && value <= 126)
            {
                current.Append((char)value);
            }
            else
            {
                AddFragment(current, fragments);
            }
        }

        AddFragment(current, fragments);
    }

    private static void ExtractUtf16LittleEndian(
        byte[] data,
        HashSet<string> fragments)
    {
        for (int start = 0; start + 1 < data.Length; start++)
        {
            var current = new StringBuilder();

            int offset = start;

            while (offset + 1 < data.Length)
            {
                ushort value = (ushort)(
                    data[offset] | (data[offset + 1] << 8));

                if (value < 32 || value > 126)
                    break;

                current.Append((char)value);
                offset += 2;
            }

            AddFragment(current, fragments);
        }
    }

    private static void AddFragment(
        StringBuilder current,
        HashSet<string> fragments)
    {
        if (current.Length >= 3)
        {
            string value = current.ToString().Trim();

            if (value.Length >= 3)
                fragments.Add(value);
        }

        current.Clear();
    }
}
