using System.Text.Json;
using System.IO;
using CoreRulesModern.Models;

namespace CoreRulesModern.Services;

public sealed class UserSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CoreRulesModern",
        "settings.json");

    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new UserSettings();
            return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(SettingsPath)) ?? new UserSettings();
        }
        catch (JsonException)
        {
            return new UserSettings();
        }
    }

    public void Save(UserSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

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
        bool BookContentsVisible = true);
}
