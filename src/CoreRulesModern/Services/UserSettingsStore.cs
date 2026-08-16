using System.Text.Json;
using System.IO;
using CoreRulesModern.Models;

namespace CoreRulesModern.Services;

public sealed class UserSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath;

    public UserSettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CoreRulesModern",
            "settings.json");
    }

    public UserSettings Load()
    {
        var settings = TryLoad(_settingsPath) ?? TryLoad(_settingsPath + ".bak");
        return Normalise(settings);
    }

    public void Save(UserSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _settingsPath + ".tmp";
        var backupPath = _settingsPath + ".bak";
        var json = JsonSerializer.Serialize(Normalise(settings), JsonOptions);

        File.WriteAllText(temporaryPath, json);
        if (File.Exists(_settingsPath))
        {
            File.Replace(temporaryPath, _settingsPath, backupPath, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temporaryPath, _settingsPath);
        }
    }

    private static UserSettings Normalise(UserSettings? settings)
    {
        settings ??= new UserSettings();
        return settings with
        {
            Scale = NormaliseScale(settings.Scale, 125),
            SpellScale = NormaliseScale(settings.SpellScale, 175),
            RecentPageLimit = settings.RecentPageLimit is 10 or 20 or 30 or 50
                ? settings.RecentPageLimit
                : 20,
            BookReferenceCoverHeight = settings.BookReferenceCoverHeight is >= 120 and <= 1000
                ? settings.BookReferenceCoverHeight
                : 240,
            BookContentsWidth = settings.BookContentsWidth is >= 220 and <= 1000
                ? settings.BookContentsWidth
                : 300,
            Bookmarks = settings.Bookmarks ?? [],
            RecentPages = settings.RecentPages ?? []
        };
    }

    private static UserSettings? TryLoad(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path))
                : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static int NormaliseScale(int value, int fallback) =>
        value is >= 100 and <= 300 && value % 25 == 0 ? value : fallback;

    public sealed record UserSettings(
        string? LibraryPath = null,
        string? CharacterSheetsPath = null,
        int Scale = 125,
        int SpellScale = 175,
        bool ReopenLastPage = true,
        int RecentPageLimit = 20,
        IReadOnlyList<SavedPage>? Bookmarks = null,
        IReadOnlyList<SavedPage>? RecentPages = null,
        string? LastDocumentStartPage = null,
        string? LastPagePath = null,
        string SpellCasterFilter = "All",
        int SpellLevelFilter = -1,
        string SpellSchoolSphereFilter = "All schools and spheres",
        string SpellComponentFilter = "All",
        string SpellSourceFilter = "All",
        bool BookContentsVisible = true,
        double BookReferenceCoverHeight = 240,
        double BookContentsWidth = 300);
}
