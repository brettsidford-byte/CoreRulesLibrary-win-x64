namespace CoreRulesModern.Models;

public sealed record InstallationStatus(
    string RootPath,
    bool IsValid,
    string Summary,
    IReadOnlyList<ValidationItem> Items,
    int CharacterFileCount,
    int UserDatabaseCount);

public sealed record ValidationItem(string Name, string Path, bool Exists, long? SizeBytes = null);

