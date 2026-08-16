using System.Text;
using CoreRulesModern.Models;

namespace CoreRulesModern.Services;

/// <summary>
/// Reads the CPart collections in Core Rules Database/Parts.dat. Only collections
/// containing physical items are exposed; proficiencies, languages, abilities and
/// other character-builder parts in the same archive are deliberately skipped.
/// The file is always opened read-only.
/// </summary>
public sealed class ItemDatabaseParser
{
    private const ushort SupportedSchema = 93;
    private static readonly Encoding Windows1252;
    private static readonly (int Start, int Count, ItemCategory? Category)[] Collections =
    [
        (0, 733, ItemCategory.Weapon),
        (733, 573, ItemCategory.Armour),
        (1306, 122, null),
        (1428, 167, null),
        (1595, 61, null),
        (1656, 306, ItemCategory.Equipment),
        (1962, 689, ItemCategory.MagicalItem),
        (2651, 22, null),
        (2673, 28, null),
        (2701, 38, null),
        (2739, 1383, ItemCategory.Treasure)
    ];

    static ItemDatabaseParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Windows1252 = Encoding.GetEncoding(1252);
    }

    public ItemDatabase Parse(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        byte[] bytes;
        try
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
            if (stream.Length > int.MaxValue)
                throw new ItemDatabaseFormatException("The item database is too large to read safely.");
            bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
        }
        catch (IOException exception)
        {
            throw new ItemDatabaseFormatException("The item database could not be read.", exception);
        }

        if (bytes.Length < 16 || ReadUInt16(bytes, 0) != 733 || ReadUInt16(bytes, 2) != 0xffff)
            throw new ItemDatabaseFormatException("The file does not begin with the expected CPart collection.");
        var schema = ReadUInt16(bytes, 4);
        var classLength = ReadUInt16(bytes, 6);
        var runtimeClass = ReadText(bytes, 8, classLength);
        if (schema != SupportedSchema || runtimeClass != "CPart")
            throw new ItemDatabaseFormatException($"Unsupported item schema {schema} ({runtimeClass}). Expected CPart schema {SupportedSchema}.");

        var records = FindRecords(bytes, 8 + classLength);
        var requiredRecords = Collections.Max(collection => collection.Start + collection.Count);
        if (records.Count < requiredRecords)
            throw new ItemDatabaseFormatException($"Parts.dat ended after {records.Count:N0} CPart records; at least {requiredRecords:N0} were expected.");

        foreach (var collection in Collections.Skip(1))
        {
            var marker = records[collection.Start].MarkerOffset;
            if (marker < 2 || ReadUInt16(bytes, marker - 2) != collection.Count)
                throw new ItemDatabaseFormatException($"Unexpected CPart collection layout near record {collection.Start + 1:N0}.");
        }

        var items = new List<ItemRecord>();
        foreach (var collection in Collections.Where(collection => collection.Category is not null))
        {
            for (var index = collection.Start; index < collection.Start + collection.Count; index++)
            {
                var record = records[index];
                var end = index + 1 < records.Count ? records[index + 1].MarkerOffset : bytes.Length;
                _ = ReadArchiveString(bytes, record.NameOffset, out var textStart);
                var storedText = ExtractText(bytes, textStart, end, record.Name);
                items.Add(new ItemRecord(record.Name, collection.Category!.Value, storedText, index + 1));
            }
        }

        return new ItemDatabase(fullPath, schema, runtimeClass, items);
    }

    private static List<RecordStart> FindRecords(byte[] bytes, int firstNameOffset)
    {
        var records = new List<RecordStart>();
        var firstName = ReadArchiveString(bytes, firstNameOffset, out _);
        if (string.IsNullOrWhiteSpace(firstName)) throw new ItemDatabaseFormatException("The first CPart record has no name.");
        records.Add(new RecordStart(firstNameOffset - 8, firstNameOffset, firstName));

        for (var offset = firstNameOffset; offset < bytes.Length - 4; offset++)
        {
            if (bytes[offset] != 0x01 || bytes[offset + 1] != 0x80) continue;
            var nameOffset = offset + 2;
            var name = ReadArchiveString(bytes, nameOffset, out var next);
            if (next <= nameOffset || !IsDisplayText(name)) continue;
            records.Add(new RecordStart(offset, nameOffset, name));
        }
        return records;
    }

    private static IReadOnlyList<string> ExtractText(byte[] bytes, int nameOffset, int end, string name)
    {
        var values = new List<string>();
        var run = new List<byte>();
        for (var offset = nameOffset; offset < end; offset++)
        {
            var value = bytes[offset];
            if (value >= 32 && value != 127)
            {
                run.Add(value);
                continue;
            }
            AddRun(values, run, name);
            run.Clear();
        }
        AddRun(values, run, name);
        return values.Distinct(StringComparer.CurrentCultureIgnoreCase).Take(24).ToArray();
    }

    private static void AddRun(List<string> values, List<byte> run, string name)
    {
        if (run.Count < 2 || run.Count > 160) return;
        var value = Windows1252.GetString(run.ToArray()).Trim();
        if (value.Length >= 2 && !value.Equals(name, StringComparison.CurrentCultureIgnoreCase)) values.Add(value);
    }

    private static string ReadArchiveString(byte[] bytes, int offset, out int next)
    {
        next = offset;
        if (offset >= bytes.Length) return string.Empty;
        uint length = bytes[next++];
        if (length == byte.MaxValue)
        {
            if (next + 2 > bytes.Length) return string.Empty;
            length = ReadUInt16(bytes, next); next += 2;
            if (length == ushort.MaxValue)
            {
                if (next + 4 > bytes.Length) return string.Empty;
                length = BitConverter.ToUInt32(bytes, next); next += 4;
            }
        }
        if (length is 0 or > 512 || next + length > bytes.Length) return string.Empty;
        var value = ReadText(bytes, next, checked((int)length));
        next += checked((int)length);
        return value;
    }

    private static bool IsDisplayText(string value) =>
        value.Length is > 0 and <= 120 && value.All(character => !char.IsControl(character));

    private static ushort ReadUInt16(byte[] bytes, int offset) => BitConverter.ToUInt16(bytes, offset);
    private static string ReadText(byte[] bytes, int offset, int length) => Windows1252.GetString(bytes, offset, length);
    private sealed record RecordStart(int MarkerOffset, int NameOffset, string Name);
}
