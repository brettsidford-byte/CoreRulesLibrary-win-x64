using System.Text;
using CoreRulesModern.Models;
using CoreRulesModern.Services;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var encoding = Encoding.GetEncoding(1252);
var temporaryFolder = Path.Combine(Path.GetTempPath(), $"CoreRulesModern-{Guid.NewGuid():N}");
Directory.CreateDirectory(temporaryFolder);

try
{
    var validPath = Path.Combine(temporaryFolder, "SpellsU.dat");
    WriteFixture(validPath, invalidBoolean: false);

    var database = new SpellDatabaseParser().Parse(validPath, SpellDatabaseKind.User);
    Require(database.Schema == 88, "Schema was not read.");
    Require(database.RuntimeClass == "CSpellsOb", "Runtime class was not read.");
    Require(database.Spells.Count == 1, "Unexpected spell count.");

    var spell = database.Spells[0];
    Require(spell.Name == "Test Spell", "Name was not read.");
    Require(spell.Reversible && spell.WizardSpell && !spell.PriestSpell, "Flags were not read.");
    Require(spell.Level == 3, "Level was not read.");
    Require(spell.Description == "A test description.", "Description was not read.");
    Require(spell.Schools.SequenceEqual(["Alteration", "Shadow"]), "Schools were not read.");
    Require(spell.Spheres.SequenceEqual(["All"]), "Spheres were not read.");

    var invalidPath = Path.Combine(temporaryFolder, "Invalid.dat");
    WriteFixture(invalidPath, invalidBoolean: true);
    try
    {
        _ = new SpellDatabaseParser().Parse(invalidPath, SpellDatabaseKind.User);
        throw new InvalidOperationException("An invalid Boolean value was accepted.");
    }
    catch (SpellDatabaseFormatException)
    {
        // Expected: malformed records must fail safely.
    }

    CheckSettingsStore(temporaryFolder);
    CheckBrowserSecurityPolicy(temporaryFolder);

    Console.WriteLine("Parser, settings reliability and browser security checks passed.");
}
finally
{
    Directory.Delete(temporaryFolder, recursive: true);
}

return;

void WriteFixture(string path, bool invalidBoolean)
{
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream, encoding);

    writer.Write((ushort)1);       // one spell-array entry
    writer.Write((ushort)0xffff);  // new runtime class
    writer.Write((ushort)88);      // schema
    writer.Write((ushort)9);
    writer.Write(encoding.GetBytes("CSpellsOb"));

    WriteArchiveString(writer, "Test Spell");
    writer.Write(invalidBoolean ? 2u : 0u); // Never Ban Cantrip
    writer.Write(1u);                       // Reversible
    writer.Write(1u);                       // Wizard Spell
    writer.Write(0u);                       // Priest Spell
    writer.Write(3u);                       // Level
    WriteArchiveString(writer, "1 creature");
    WriteArchiveString(writer, "3");
    WriteArchiveString(writer, "V, S, M");
    WriteArchiveString(writer, "");
    WriteArchiveString(writer, "1 round/level");
    WriteArchiveString(writer, "None");
    WriteArchiveString(writer, "30 yards");
    WriteArchiveString(writer, "Neg.");
    WriteArchiveString(writer, "Moderate visual");
    WriteArchiveString(writer, "+3");
    writer.Write(0u); // no original help-topic identifier
    WriteArchiveString(writer, "A test description.");
    writer.Write((ushort)2);
    WriteArchiveString(writer, "Alteration");
    WriteArchiveString(writer, "Shadow");
    writer.Write((ushort)1);
    WriteArchiveString(writer, "All");
}

void WriteArchiveString(BinaryWriter writer, string value)
{
    var bytes = encoding.GetBytes(value);
    if (bytes.Length >= byte.MaxValue) throw new InvalidOperationException("Fixture string is too long.");
    writer.Write((byte)bytes.Length);
    writer.Write(bytes);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void CheckSettingsStore(string temporaryFolder)
{
    var settingsPath = Path.Combine(temporaryFolder, "settings", "settings.json");
    var store = new UserSettingsStore(settingsPath);
    var first = new UserSettingsStore.UserSettings(Scale: 150, RecentPageLimit: 30);
    store.Save(first);
    Require(store.Load().Scale == 150, "Settings did not survive a save/load round trip.");

    store.Save(first with { Scale = 999, RecentPageLimit = 999 });
    var normalised = store.Load();
    Require(normalised.Scale == 125, "Invalid document scale was not normalised.");
    Require(normalised.RecentPageLimit == 20, "Invalid recent-page limit was not normalised.");
    Require(File.Exists(settingsPath + ".bak"), "Atomic settings replacement did not retain a backup.");

    File.WriteAllText(settingsPath, "{not valid JSON");
    var recovered = store.Load();
    Require(recovered.Scale == 150 && recovered.RecentPageLimit == 30,
        "Corrupt settings did not recover from the last known-good backup.");
}

static void CheckBrowserSecurityPolicy(string temporaryFolder)
{
    var library = Path.Combine(temporaryFolder, "library");
    var sibling = Path.Combine(temporaryFolder, "library-elsewhere");
    Directory.CreateDirectory(library);
    Directory.CreateDirectory(sibling);

    Require(BrowserSecurityPolicy.IsLocalPageWithin(
        new Uri(Path.Combine(library, "index.htm")), [library]),
        "A page inside the selected library was blocked.");
    Require(!BrowserSecurityPolicy.IsLocalPageWithin(
        new Uri(Path.Combine(sibling, "index.htm")), [library]),
        "A sibling path escaped the selected library boundary.");
    Require(BrowserSecurityPolicy.IsAllowedOnlineAddress(
        new Uri("https://www.completecompendium.com/page")),
        "The permitted online resource was blocked.");
    Require(!BrowserSecurityPolicy.IsAllowedOnlineAddress(
        new Uri("http://www.completecompendium.com/page")),
        "Unencrypted online navigation was permitted.");
    Require(!BrowserSecurityPolicy.IsAllowedOnlineAddress(
        new Uri("https://completecompendium.com.example/page")),
        "A lookalike host was permitted.");
}
