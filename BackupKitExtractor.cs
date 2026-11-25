
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace StrikeMultiPadEditor;

public sealed class KitFile
{
    public string FileName { get; init; } = "";
    public int RecordOffset { get; init; }
    public byte[] Data { get; init; } = [];
    public string RecordType => KitType;

    public bool IsEmpty =>
        RecordType == "KITH" ||
        string.Equals(FileName, "Empty.kit",
            StringComparison.OrdinalIgnoreCase);

    public string KitType =>
        Data.Length >= 4
            ? Encoding.ASCII.GetString(Data, 0, 4)
            : "Unknown";
}

public static class BackupKitExtractor
{
    private const int RecordSize = 10240;
    private const int PayloadOffset = 0x180;
    private const int KitSize = 9824;

    public static List<KitFile> ReadKits(string backupPath)
    {
        byte[] backup = File.ReadAllBytes(backupPath);

        if (backup.Length < 8)
            throw new InvalidDataException("Backup file is too small.");

        string signature = Encoding.ASCII.GetString(backup, 0, 8);

        if (!signature.StartsWith("PACKBKF"))
            throw new InvalidDataException(
                $"Unexpected backup signature: {signature}");

        var kits = new List<KitFile>();
        int kitRecords = 0;
        int totalRecords = 0;

        for (int offset = 0x40; offset + PayloadOffset + KitSize <= backup.Length; offset += RecordSize)
        {
            totalRecords++;

            int dataOffset = offset + PayloadOffset;

            string type = Encoding.ASCII.GetString(
                backup, dataOffset, 4);

            if (type != "KITC" && type != "KITH")
                continue;

            kitRecords++;

            // Preserve the payload even while filename parsing is investigated.
            byte[] data = new byte[KitSize];
            Array.Copy(backup, dataOffset, data, 0, KitSize);

            string kitName = type == "KITH"
    		? $"Empty Slot {kitRecords:D3}"
    		: ReadKitName(data);

	    if (string.IsNullOrWhiteSpace(kitName))
    		kitName = $"Kit record {kitRecords:D3}";

	    kits.Add(new KitFile
	    {
    		FileName = $"{kitName}.kit",
    		RecordOffset = offset,
    		Data = data
	    });
        }

        if (kits.Count == 0)
        {
            throw new InvalidDataException(
                $"No KITC or KITH payloads found. " +
                $"File size: {backup.Length:N0} bytes. " +
                $"Records scanned: {totalRecords}. " +
                $"Signature: {signature}");
        }

        return kits;
    }

    private static string ReadKitName(byte[] data)
    {
        const int nameOffset = 0xC0;
        const int nameLength = 32;

        if (data.Length < nameOffset + nameLength)
            return "";

        string name = Encoding.ASCII.GetString(
            data, nameOffset, nameLength);

        int nullIndex = name.IndexOf('\0');

        if (nullIndex >= 0)
            name = name.Substring(0, nullIndex);

        return name.Trim();
    }

    public static void ExtractKit(KitFile kit, string outputFolder)
    {
        Directory.CreateDirectory(outputFolder);
        File.WriteAllBytes(
            Path.Combine(outputFolder, kit.FileName),
            kit.Data);
    }
}
