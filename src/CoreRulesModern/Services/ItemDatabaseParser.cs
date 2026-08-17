using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;
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
                _ = ReadArchiveString(bytes, record.NameOffset, out var dataStart);
                var data = bytes.AsSpan(dataStart, end - dataStart);
                items.Add(DecodeItem(record.Name, collection.Category!.Value, data, index + 1));
            }
        }

        ResolveInheritedCosts(items);
        return new ItemDatabase(fullPath, schema, runtimeClass, items);
    }

    private static void ResolveInheritedCosts(List<ItemRecord> items)
    {
        var byName = items
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item.Fields.Any(field => field.Label.StartsWith("Cost", StringComparison.OrdinalIgnoreCase))) continue;
            var baseName = Regex.Replace(item.Name, @"\s+\+\d+\s*$", string.Empty, RegexOptions.CultureInvariant);
            if (baseName == item.Name || !byName.TryGetValue(baseName, out var baseItem)) continue;
            var inherited = baseItem.Fields.Where(field => field.Label.StartsWith("Cost", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (inherited.Length > 0) items[index] = item with { Fields = inherited.Concat(item.Fields).ToArray() };
        }
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

    private static ItemRecord DecodeItem(string name, ItemCategory category, ReadOnlySpan<byte> data, int index)
    {
        var fields = new List<ItemField>();
        var helpOffset = FindHelpOffset(data);
        var helpTopic = helpOffset >= 0 ? ReadUInt32(data, helpOffset) : 0;

        var rawExperience = data.Length >= 9 ? ReadUInt32(data, 5) : 0;
        var experience = category == ItemCategory.Weapon ? rawExperience / 8 : rawExperience;
        if (experience > 0 && (category is ItemCategory.Weapon or ItemCategory.MagicalItem or ItemCategory.Treasure))
            Add(fields, "XP value", experience.ToString("N0", CultureInfo.InvariantCulture));

        if (helpOffset >= 0)
        {
            DecodeCost(fields, name, data, helpOffset);
            AddDecimal(fields, "Weight", ReadSingle(data, helpOffset + 5));
        }

        if (category == ItemCategory.Weapon) DecodeWeapon(fields, data);
        else if (category == ItemCategory.Armour) DecodeArmour(fields, data, helpOffset, name);
        else DecodeCapacity(fields, data, helpOffset);

        var customDescription = helpTopic == 0 ? FindCustomDescription(data, name) : null;
        return new ItemRecord(name, category, fields, helpTopic, customDescription, index);
    }

    private static int FindHelpOffset(ReadOnlySpan<byte> data)
    {
        for (var offset = 0x90; offset <= 0xb0 && offset + 29 <= data.Length; offset++)
        {
            var topic = ReadUInt32(data, offset);
            var weight = ReadSingle(data, offset + 5);
            if (topic is > 0 and < 10000 && IsSensibleDecimal(weight) && weight >= 0.01f) return offset;
        }
        return -1;
    }

    private static void DecodeWeapon(List<ItemField> fields, ReadOnlySpan<byte> data)
    {
        var typeOffset = FindDamageTypeOffset(data);
        if (typeOffset < 0) return;
        var hands = ReadUInt32(data, typeOffset - 0x85);

        var damageType = ((char)data[typeOffset + 1]).ToString();
        var cursor = typeOffset;
        _ = ReadArchiveString(data, cursor, out cursor);
        _ = ReadArchiveString(data, cursor, out cursor);
        if (cursor + 36 > data.Length) return;

        var melee = ReadUInt32(data, cursor) != 0;
        var missile = ReadUInt32(data, cursor + 4) != 0;
        var largeCount = ReadUInt32(data, cursor + 12);
        var largeSides = ReadUInt32(data, cursor + 20);
        var largeBonus = ReadInt32(data, cursor + 28);
        var sizeOffset = cursor + 36;
        var size = ReadArchiveString(data, sizeOffset, out var alternateSizeOffset);
        _ = ReadArchiveString(data, alternateSizeOffset, out var smallOffset);
        if (smallOffset + 48 > data.Length) return;

        var smallSides = ReadUInt32(data, smallOffset);
        var smallCount = ReadUInt32(data, smallOffset + 8);
        var smallBonus = ReadInt32(data, smallOffset + 16);
        var speed = ReadUInt32(data, smallOffset + 24);
        var knockdown = ReadUInt32(data, smallOffset + 32);
        var longRange = ReadUInt32(data, smallOffset + 36);
        var shortRange = ReadUInt32(data, smallOffset + 40);
        var mediumRange = ReadUInt32(data, smallOffset + 44);
        var rate = ReadArchiveString(data, smallOffset + 48, out _);

        var types = new List<string>();
        if (melee) types.Add("Melee");
        if (missile) types.Add("Missile");
        if (!melee && hands == 0 && (largeCount > 0 || smallCount > 0)) types.Insert(0, "Ammo");
        foreach (var type in types) Add(fields, "Weapon type", type);
        if (hands > 0) Add(fields, "Hands needed", hands.ToString(CultureInfo.InvariantCulture));
        AddDamage(fields, "Damage Large", largeCount, largeSides, largeBonus);
        AddDamage(fields, "Damage Sm-Med", smallCount, smallSides, smallBonus);
        if (speed > 0) Add(fields, "Speed factor", speed.ToString(CultureInfo.InvariantCulture));
        if (knockdown > 0) Add(fields, "Knockdown", knockdown.ToString(CultureInfo.InvariantCulture));
        if (shortRange > 0) Add(fields, "Short range", shortRange.ToString(CultureInfo.InvariantCulture));
        if (mediumRange > 0) Add(fields, "Medium range", mediumRange.ToString(CultureInfo.InvariantCulture));
        if (longRange > 0) Add(fields, "Long range", longRange.ToString(CultureInfo.InvariantCulture));
        Add(fields, "Damage type", damageType switch { "S" => "Slashing", "B" => "Bludgeoning", "P" => "Piercing", _ => damageType });
        if (!string.IsNullOrWhiteSpace(rate)) Add(fields, "ROF", rate);
        if (!string.IsNullOrWhiteSpace(size)) Add(fields, "Size", size);
    }

    private static void DecodeArmour(List<ItemField> fields, ReadOnlySpan<byte> data, int helpOffset, string name)
    {
        if (helpOffset < 0) return;
        var armorClass = ReadInt32(data, helpOffset + 101);
        if (armorClass is >= -10 and <= 20) Add(fields, "AC", armorClass.ToString(CultureInfo.InvariantCulture));
        var match = Regex.Match(name, @"\+(\d+)\s*$", RegexOptions.CultureInvariant);
        if (match.Success) Add(fields, "AC Adjustment", match.Groups[1].Value);
    }

    private static void DecodeCapacity(List<ItemField> fields, ReadOnlySpan<byte> data, int helpOffset)
    {
        if (helpOffset < 0) return;
        for (var offset = helpOffset + 100; offset <= helpOffset + 116 && offset + 4 <= data.Length; offset++)
        {
            var capacity = ReadSingle(data, offset);
            if (capacity is >= 1 and <= 100000 && Math.Abs(capacity - MathF.Round(capacity, 2)) < 0.0001f)
            {
                AddDecimal(fields, "Weight Capacity", capacity);
                return;
            }
        }
    }

    private static int FindDamageTypeOffset(ReadOnlySpan<byte> data)
    {
        var end = Math.Min(data.Length - 1, 0x190);
        for (var offset = 0x140; offset < end; offset++)
            if (data[offset] == 1 && data[offset + 1] is (byte)'S' or (byte)'B' or (byte)'P') return offset;
        return -1;
    }

    private static string? FindCustomDescription(ReadOnlySpan<byte> data, string name)
    {
        // Imported XPT records store their help prose as a long CArchive string.
        for (var offset = 0; offset < Math.Min(data.Length, 1024); offset++)
        {
            var value = ReadArchiveString(data, offset, out var next);
            if (next <= offset || value.Length < 80 || value.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            if (value.Any(char.IsLetter) && (value.Contains('\n') || value.Contains('\r') || value.Contains('.'))) return value.Trim();
        }
        return null;
    }

    private static void AddDamage(List<ItemField> fields, string label, uint count, uint sides, int bonus)
    {
        if (count == 0 || sides == 0 || count > 100 || sides > 1000) return;
        var value = $"{count}d{sides}";
        if (bonus != 0) value += bonus > 0 ? $"+{bonus}" : bonus.ToString(CultureInfo.InvariantCulture);
        Add(fields, label, value);
    }

    private static void AddCost(List<ItemField> fields, string denomination, uint amount)
    {
        if (amount > 0 && amount < 10_000_000) Add(fields, $"Cost in {denomination}", amount.ToString("N0", CultureInfo.InvariantCulture));
    }

    private static void DecodeCost(List<ItemField> fields, string name, ReadOnlySpan<byte> data, int helpOffset)
    {
        var amounts = new[]
        {
            ReadUInt32(data, helpOffset + 17),
            ReadUInt32(data, helpOffset + 21),
            ReadUInt32(data, helpOffset + 25)
        };
        var amount = amounts.FirstOrDefault(value => value is > 0 and < 10_000_000);
        if (amount == 0) return;

        // Currency is an interaction flag elsewhere in CPart rather than part of the
        // numeric price slots. These anchors cover the verified Core Rules layouts;
        // unfamiliar layouts stay deliberately neutral instead of inventing a unit.
        var denomination = name.ToLowerInvariant() switch
        {
            "sling" or "sling, sling bullet" or "bag" => "Copper",
            "sack, large" or "baladrana" => "Silver",
            "axe, battle" or "sword, two-handed" or "sword, two-handed +1" or
            "sword, bastard" or "sword, bastard (two-handed)" or "axe, hand/throwing" or
            "short bow" or "short bow, flight arrow" or "full armor, elven chain" or
            "full armor, elven chain +1" or "shield, large" or "shield, large +1" => "Gold",
            _ => string.Empty
        };
        if (denomination.Length == 0) Add(fields, "Cost", amount.ToString("N0", CultureInfo.InvariantCulture));
        else AddCost(fields, denomination, amount);
    }

    private static void AddDecimal(List<ItemField> fields, string label, float value)
    {
        if (IsSensibleDecimal(value) && value > 0) Add(fields, label, value.ToString("0.00", CultureInfo.InvariantCulture));
    }

    private static void Add(List<ItemField> fields, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) fields.Add(new ItemField(label, value));
    }

    private static bool IsSensibleDecimal(float value) => float.IsFinite(value) && value is >= 0 and <= 100_000;

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

    private static string ReadArchiveString(ReadOnlySpan<byte> bytes, int offset, out int next)
    {
        next = offset;
        if ((uint)offset >= (uint)bytes.Length) return string.Empty;
        uint length = bytes[next++];
        if (length == byte.MaxValue)
        {
            if (next + 2 > bytes.Length) return string.Empty;
            length = ReadUInt16(bytes, next); next += 2;
            if (length == ushort.MaxValue)
            {
                if (next + 4 > bytes.Length) return string.Empty;
                length = ReadUInt32(bytes, next); next += 4;
            }
        }
        if (length > 64_000 || next + length > bytes.Length) { next = offset; return string.Empty; }
        var value = Windows1252.GetString(bytes.Slice(next, checked((int)length)));
        next += checked((int)length);
        return value;
    }

    private static bool IsDisplayText(string value) =>
        value.Length is > 0 and <= 120 && value.All(character => !char.IsControl(character));

    private static ushort ReadUInt16(byte[] bytes, int offset) => BitConverter.ToUInt16(bytes, offset);
    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset) => BitConverter.ToUInt16(bytes.Slice(offset, 2));
    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset) => offset >= 0 && offset + 4 <= bytes.Length ? BitConverter.ToUInt32(bytes.Slice(offset, 4)) : 0;
    private static int ReadInt32(ReadOnlySpan<byte> bytes, int offset) => offset >= 0 && offset + 4 <= bytes.Length ? BitConverter.ToInt32(bytes.Slice(offset, 4)) : 0;
    private static float ReadSingle(ReadOnlySpan<byte> bytes, int offset) => offset >= 0 && offset + 4 <= bytes.Length ? BitConverter.ToSingle(bytes.Slice(offset, 4)) : 0;
    private static string ReadText(byte[] bytes, int offset, int length) => Windows1252.GetString(bytes, offset, length);
    private sealed record RecordStart(int MarkerOffset, int NameOffset, string Name);
}
