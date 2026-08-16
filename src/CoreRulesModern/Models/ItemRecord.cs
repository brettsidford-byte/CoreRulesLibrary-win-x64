namespace CoreRulesModern.Models;

public enum ItemCategory
{
    Weapon,
    Armour,
    Equipment,
    MagicalItem,
    Treasure
}

public sealed record ItemRecord(
    string Name,
    ItemCategory Category,
    IReadOnlyList<string> StoredText,
    int DatabaseIndex);

public sealed record ItemDatabase(
    string Path,
    ushort Schema,
    string RuntimeClass,
    IReadOnlyList<ItemRecord> Items);
