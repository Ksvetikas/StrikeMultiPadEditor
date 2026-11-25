
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace StrikeMultiPadEditor;

public sealed class BackupMetadataLocation
{
    public string FileName { get; init; } = "";
    public long NameOffset { get; init; }
    public string Extension { get; init; } = "";
    public string ContextHex { get; init; } = "";
    public string ContextText { get; init; } = "";
}

public static class BackupMetadataLocator
{
    private static readonly string[] Extensions =
    [
        ".had", ".wav", ".lst"
    ];

    public static List<BackupMetadataLocation> Scan(
        string backupPath)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException(
                "Backup file not found.", backupPath);

        byte[] data = File.ReadAllBytes(backupPath);

        var results = new List<BackupMetadataLocation>();

        for (int i = 0; i < data.Length; i++)
        {
            foreach (string extension in Extensions)
            {
                byte[] pattern = Encoding.ASCII.GetBytes(extension);

                if (i + pattern.Length > data.Length)
                    continue;

                bool found = true;

                for (int j = 0; j < pattern.Length; j++)
                {
                    byte value = data[i + j];

                    if (char.ToLowerInvariant((char)value) !=
                        char.ToLowerInvariant((char)pattern[j]))
                    {
                        found = false;
                        break;
                    }
                }

                if (!found)
                    continue;

                int nameStart = i;

                while (nameStart > 0 &&
                       IsFilenameCharacter(data[nameStart - 1]) &&
                       i - nameStart < 240)
                {
                    nameStart--;
                }

                int nameEnd = i + pattern.Length;

                while (nameEnd < data.Length &&
                       IsFilenameCharacter(data[nameEnd]) &&
                       nameEnd - nameStart < 260)
                {
                    nameEnd++;
                }

                string fileName = Encoding.ASCII.GetString(
                    data, nameStart, nameEnd - nameStart);

                int contextStart = Math.Max(0, nameStart - 32);
                int contextLength = Math.Min(
                    96, data.Length - contextStart);

                byte[] context = new byte[contextLength];

                Array.Copy(
                    data, contextStart,
                    context, 0, contextLength);

                results.Add(new BackupMetadataLocation
                {
                    FileName = fileName,
                    NameOffset = nameStart,
                    Extension = extension,
                    ContextHex = BitConverter.ToString(context)
                        .Replace("-", " "),
                    ContextText = ToReadableText(context)
                });

                i = nameEnd - 1;
                break;
            }
        }

        return results
            .GroupBy(x => $"{x.NameOffset:X}:{x.FileName}")
            .Select(x => x.First())
            .OrderBy(x => x.NameOffset)
            .ToList();
    }

    private static bool IsFilenameCharacter(byte value)
    {
        return value >= 32 &&
               value <= 126 &&
               value != (byte)'<' &&
               value != (byte)'>' &&
               value != (byte)'"' &&
               value != (byte)'|' &&
               value != (byte)'?' &&
               value != (byte)'*';
    }

    private static string ToReadableText(byte[] data)
    {
        var result = new StringBuilder();

        foreach (byte value in data)
        {
            result.Append(
                value >= 32 && value <= 126
                    ? (char)value
                    : '.');
        }

        return result.ToString();
    }
}
