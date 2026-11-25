
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace StrikeMultiPadEditor;

public sealed class BackupSampleMetadata
{
    public string HadFileName { get; init; } = "";
    public string SampleName { get; init; } = "";
    public string SamplePath { get; init; } = "";
    public long HadMetadataOffset { get; init; }
    public long SampleMetadataOffset { get; init; }

    public string Status =>
        string.IsNullOrWhiteSpace(SampleName) ||
        string.IsNullOrWhiteSpace(SamplePath)
            ? "No WAV metadata match"
            : "WAV metadata found";
}

public static class BackupSampleMetadataScanner
{
    private static readonly byte[] NameMarker =
        Encoding.ASCII.GetBytes("NAME");

    private static readonly byte[] PathMarker =
        Encoding.ASCII.GetBytes("PATH");

    private sealed class HadRecord
    {
        public string FileName { get; init; } = "";
        public long Offset { get; init; }
    }

    private sealed class WavRecord
    {
        public string SampleName { get; init; } = "";
        public string SamplePath { get; init; } = "";
        public long Offset { get; init; }
    }

    public static List<BackupSampleMetadata> Scan(string backupPath)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException(
                "Backup file not found.", backupPath);

        byte[] data = File.ReadAllBytes(backupPath);

        var hadRecords = FindHadRecords(data);
        var wavRecords = FindWavRecords(data);

        var wavByNumber = wavRecords
            .GroupBy(x => GetFileNumber(x.SamplePath))
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .ToDictionary(
                x => x.Key,
                x => x.First(),
                StringComparer.OrdinalIgnoreCase);

        var results = new List<BackupSampleMetadata>();

        foreach (var had in hadRecords.OrderBy(x => x.Offset))
        {
            string number = GetFileNumber(had.FileName);

            wavByNumber.TryGetValue(number, out var wav);

            results.Add(new BackupSampleMetadata
            {
                HadFileName = had.FileName,
                HadMetadataOffset = had.Offset,
                SampleName = wav?.SampleName ?? "",
                SamplePath = wav?.SamplePath ?? "",
                SampleMetadataOffset = wav?.Offset ?? 0
            });
        }

        return results;
    }

    private static List<HadRecord> FindHadRecords(byte[] data)
    {
        var results = new List<HadRecord>();
        var seen = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i <= data.Length - 4; i++)
        {
            if (!Matches(data, i, ".had"))
                continue;

            // The filename must belong to a nearby NAME metadata record.
            int searchStart = Math.Max(0, i - 64);

            if (FindSequence(data, NameMarker, searchStart, i) < 0)
                continue;

            string fileName = ReadPreviousAsciiString(data, i);

            if (!fileName.EndsWith(
                ".had",
                StringComparison.OrdinalIgnoreCase))
                continue;

            string key = fileName + "|" + i;

            if (!seen.Add(key))
                continue;

            results.Add(new HadRecord
            {
                FileName = fileName,
                Offset = FindSequence(data, NameMarker, searchStart, i)
            });
        }

        return results;
    }

    private static List<WavRecord> FindWavRecords(byte[] data)
    {
        var results = new List<WavRecord>();

        for (int i = 0; i <= data.Length - 4; i++)
        {
            if (!Matches(data, i, PathMarker))
                continue;

            string samplePath = ReadPathAfterMarker(data, i);

            if (!samplePath.EndsWith(
                ".wav",
                StringComparison.OrdinalIgnoreCase))
                continue;

            string sampleName = ReadPreviousPrintableRun(data, i);

            results.Add(new WavRecord
            {
                SampleName = sampleName,
                SamplePath = samplePath,
                Offset = i
            });
        }

        return results
            .GroupBy(x => x.SamplePath, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
    }

    private static string ReadPathAfterMarker(byte[] data, int markerOffset)
    {
        int start = -1;
        int searchEnd = Math.Min(data.Length, markerOffset + 32);

        for (int i = markerOffset + 4; i < searchEnd; i++)
        {
            if (data[i] == (byte)'\\' || data[i] == (byte)'/')
            {
                start = i;
                break;
            }
        }

        if (start < 0)
            return "";

        int end = start;

        while (end < data.Length &&
               data[end] >= 32 &&
               data[end] <= 126)
        {
            end++;
        }

        return Encoding.ASCII.GetString(
            data, start, end - start);
    }

    private static string ReadPreviousPrintableRun(
        byte[] data,
        int position)
    {
        int end = position - 1;

        while (end >= 0 &&
               (data[end] < 32 || data[end] > 126))
        {
            end--;
        }

        if (end < 0)
            return "";

        int start = end;

        while (start >= 0 &&
               data[start] >= 32 &&
               data[start] <= 126)
        {
            start--;
        }

        return Encoding.ASCII.GetString(
            data, start + 1, end - start);
    }

    private static string ReadPreviousAsciiString(
        byte[] data,
        int position)
    {
        int end = position + 3;

        while (end >= 0 &&
               (data[end] < 32 || data[end] > 126))
        {
            end--;
        }

        if (end < 0)
            return "";

        int start = end;

        while (start >= 0 &&
               data[start] >= 32 &&
               data[start] <= 126)
        {
            start--;
        }

        return Encoding.ASCII.GetString(
            data, start + 1, end - start);
    }

    private static string GetFileNumber(string path)
    {
        string name = Path.GetFileNameWithoutExtension(
            path.Replace('\\', Path.DirectorySeparatorChar));

        return name.Trim();
    }

    private static bool Matches(
        byte[] data,
        int offset,
        string value)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(value);
        return Matches(data, offset, bytes);
    }

    private static bool Matches(
        byte[] data,
        int offset,
        byte[] pattern)
    {
        if (offset < 0 || offset + pattern.Length > data.Length)
            return false;

        for (int i = 0; i < pattern.Length; i++)
        {
            if (data[offset + i] != pattern[i])
                return false;
        }

        return true;
    }

    private static int FindSequence(
        byte[] data,
        byte[] pattern,
        int start,
        int end)
    {
        int limit = Math.Min(end, data.Length - pattern.Length);

        for (int i = Math.Max(0, start); i <= limit; i++)
        {
            if (Matches(data, i, pattern))
                return i;
        }

        return -1;
    }
}
