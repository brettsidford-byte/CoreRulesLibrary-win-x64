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
    IReadOnlyList<ItemField> Fields,
    uint HelpTopicId,
    string? CustomDescription,
    int DatabaseIndex);

public sealed record ItemField(string Label, string Value);

public sealed record ItemDatabase(
    string Path,
    ushort Schema,
    string RuntimeClass,
    IReadOnlyList<ItemRecord> Items);
