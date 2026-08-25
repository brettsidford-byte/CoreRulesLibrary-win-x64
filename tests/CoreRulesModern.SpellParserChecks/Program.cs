using System.Text;
using System.Reflection;
using CoreRulesModern.Models;
using CoreRulesModern.Services;
using CoreRulesModern.Views;

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

    var partsPath = Path.Combine(temporaryFolder, "PartsU.dat");
    WritePartFixture(partsPath);
    var collections = new SpellPartDatabaseParser().Parse(partsPath);
    Require(collections.TryGetValue("Chronomancy", out var collectionSpells),
        "The embedded spell collection was not discovered.");
    Require(collectionSpells.SequenceEqual(["Test Spell"]),
        "The embedded spell collection membership was not read.");

    var suppliedPartsPath = Environment.GetEnvironmentVariable("CORE_RULES_PARTS_TEST_PATH");
    if (!string.IsNullOrWhiteSpace(suppliedPartsPath))
    {
        var suppliedCollections = new SpellPartDatabaseParser().Parse(suppliedPartsPath);
        Require(suppliedCollections.ContainsKey("Amethyts (Death)"),
            "The supplied PartsU.dat did not expose Amethyts (Death).");
        Require(suppliedCollections.ContainsKey("Chronomancy"),
            "The supplied PartsU.dat did not expose Chronomancy.");
    }

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
    CheckDiceRollerStore(temporaryFolder);
    CheckBrowserFontIsolation();
    CheckBrowserSecurityPolicy(temporaryFolder);
    CheckBookContentsResolver(temporaryFolder);

    Console.WriteLine("Parser, settings, dice persistence and browser security checks passed.");
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

void WritePartFixture(string path)
{
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream, encoding);
    writer.Write((ushort)0x8001);  // CPart class reference
    WriteArchiveString(writer, "Chronomancy");
    writer.Write(new byte[64]);
    WriteArchiveString(writer, "Chronomancy");
    writer.Write(new byte[64]);
    writer.Write((ushort)1);       // one embedded spell
    writer.Write((ushort)0xffff);  // new runtime class
    writer.Write((ushort)88);      // CSpellsOb schema
    writer.Write((ushort)9);
    writer.Write(encoding.GetBytes("CSpellsOb"));

    WriteArchiveString(writer, "Test Spell");
    writer.Write(0u);
    writer.Write(1u);
    writer.Write(1u);
    writer.Write(0u);
    writer.Write(3u);
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
    writer.Write(0u);
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
    var first = new UserSettingsStore.UserSettings(
        Scale: 150, RecentPageLimit: 30, BookReferenceCoverHeight: 360, BookContentsWidth: 420);
    store.Save(first);
    Require(store.Load().Scale == 150, "Settings did not survive a save/load round trip.");
    Require(store.Load().BookReferenceCoverHeight == 360,
        "The cover/contents splitter position did not survive a save/load round trip.");
    Require(store.Load().BookContentsWidth == 420,
        "The centre viewer width did not survive a save/load round trip.");

    store.Save(first with
    {
        Scale = 999,
        RecentPageLimit = 999,
        BookReferenceCoverHeight = 20,
        BookContentsWidth = 20
    });
    var normalised = store.Load();
    Require(normalised.Scale == 125, "Invalid document scale was not normalised.");
    Require(normalised.RecentPageLimit == 20, "Invalid recent-page limit was not normalised.");
    Require(normalised.BookReferenceCoverHeight == 240,
        "Invalid cover/contents splitter position was not normalised.");
    Require(normalised.BookContentsWidth == 300,
        "Invalid centre viewer width was not normalised.");
    Require(File.Exists(settingsPath + ".bak"), "Atomic settings replacement did not retain a backup.");

    File.WriteAllText(settingsPath, "{not valid JSON");
    var recovered = store.Load();
    Require(recovered.Scale == 150 && recovered.RecentPageLimit == 30,
        "Corrupt settings did not recover from the last known-good backup.");
}

static void CheckDiceRollerStore(string temporaryFolder)
{
    var diceFolder = Path.Combine(temporaryFolder, "dice");
    var store = new DiceRollerStore(diceFolder);
    var pools = new List<DicePool>
    {
        new() { Name = "Saved pool", Mode = DicePoolMode.Generic, Dice = [new DieSpec { Sides = 12 }] }
    };

    Require(store.SavePools(pools), "Dice pools could not be saved.");
    Require(store.LoadPools().Single().Name == "Saved pool", "Dice pools did not survive a save/load round trip.");

    pools[0].Name = "Backup pool";
    Require(store.SavePools(pools), "The second dice-pool save failed.");
    File.WriteAllText(Path.Combine(diceFolder, "dice-pools.json"), "not valid json");
    var recovered = new DiceRollerStore(diceFolder);
    Require(recovered.LoadPools().Single().Name == "Saved pool", "The dice-pool backup was not recovered.");
    Require(!string.IsNullOrWhiteSpace(recovered.LoadWarning), "Dice-pool recovery did not report a warning.");
    Require(Directory.EnumerateFiles(diceFolder, "dice-pools.corrupt-*.json").Any(),
        "The damaged dice-pool file was not preserved.");
}

static void CheckBrowserFontIsolation()
{
    var windowType = typeof(MainWindow);
    var profileType = windowType.GetNestedType("WebView2FontProfile", BindingFlags.NonPublic)!;
    var packagedMethod = windowType.GetMethod("CreatePackagedFontCss", BindingFlags.Static | BindingFlags.NonPublic)!;
    var legacyMethod = windowType.GetMethod("CreateLegacyFontCss", BindingFlags.Static | BindingFlags.NonPublic)!;
    var ravenloftProfile = Enum.Parse(profileType, "Ravenloft");
    var ravenloftCss = (string)packagedMethod.Invoke(null, [ravenloftProfile])!;
    var legacyCss = (string)legacyMethod.Invoke(null, null)!;

    Require(!ravenloftCss.Contains("Core Rules Quadrat Serial XBold", StringComparison.OrdinalIgnoreCase),
        "The WebView2 Ravenloft font profile contains the WebView1-only Quadrat font.");
    Require(legacyCss.Contains("Core Rules Quadrat Serial XBold", StringComparison.OrdinalIgnoreCase),
        "The WebView1 legacy font profile lost Quadrat support.");
    Require(legacyCss.Contains("Core Rules Friz Quadrata Bold", StringComparison.OrdinalIgnoreCase),
        "The WebView1 legacy profile misclassified Friz Quadrata as Quadrat Serial.");
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

static void CheckBookContentsResolver(string temporaryFolder)
{
    var ordinaryFolder = Path.Combine(temporaryFolder, "ordinary-book");
    Directory.CreateDirectory(ordinaryFolder);
    var ordinaryStart = Path.Combine(ordinaryFolder, "DD00100.htm");
    var ordinarySecond = Path.Combine(ordinaryFolder, "DD00101.htm");
    var ordinaryCover = Path.Combine(ordinaryFolder, "Cover.PNG");
    File.WriteAllText(ordinaryStart, "<title>Page 1</title>");
    File.WriteAllText(ordinarySecond, "<title>Page 2</title>");
    File.WriteAllBytes(ordinaryCover, [0x89, 0x50, 0x4E, 0x47]);
    var ordinary = BookContentsResolver.Resolve(
        ordinaryStart,
        ordinaryStart,
        HtmlDocumentCollection.AdndSecondEdition);
    Require(ordinary.CoverPagePath == Path.GetFullPath(ordinaryCover),
        "An ordinary book did not prefer its manually supplied cover image.");
    Require(ordinary.PagePath == Path.GetFullPath(ordinaryStart),
        "A Core Rules book did not retain its landing/contents page in the lower pane.");

    var domainsFolder = Path.Combine(temporaryFolder, "Domains_of_Dread");
    Directory.CreateDirectory(domainsFolder);
    var domainsStart = Path.Combine(domainsFolder, "index.htm");
    var domainsCredits = Path.Combine(domainsFolder, "dod01.htm");
    var domainsContents = Path.Combine(domainsFolder, "dod02.htm");
    File.WriteAllText(domainsStart,
        "<a href='dod01.htm'>Credits</a><a href='dod02.htm'>Contents</a>");
    File.WriteAllText(domainsCredits, "<title>Credits</title>");
    File.WriteAllText(domainsContents, "<title>Table of Contents</title>");
    var domains = BookContentsResolver.Resolve(
        domainsStart,
        domainsStart,
        HtmlDocumentCollection.Ravenloft);
    Require(domains.PagePath == Path.GetFullPath(domainsContents),
        "Domains of Dread did not skip credits and select its contents page.");

    var domainsSequentialFolder = Path.Combine(temporaryFolder, "Domains of Dread sequential");
    Directory.CreateDirectory(domainsSequentialFolder);
    var domainsLanding = Path.Combine(domainsSequentialFolder, "dod00.htm");
    var domainsDedication = Path.Combine(domainsSequentialFolder, "dod01.htm");
    var domainsSequentialContents = Path.Combine(domainsSequentialFolder, "dod02.htm");
    File.WriteAllText(domainsLanding, "<title>Domains of Dread</title><a href='dod01.htm'>Next</a>");
    File.WriteAllText(domainsDedication, "<title>Dedication</title>");
    File.WriteAllText(domainsSequentialContents, "<title>Table of Contents</title>");
    var sequentialDomains = BookContentsResolver.Resolve(
        domainsLanding, domainsLanding, HtmlDocumentCollection.Ravenloft);
    Require(sequentialDomains.PagePath == Path.GetFullPath(domainsSequentialContents),
        "Domains of Dread selected its dedication instead of the following contents page.");

    var domainsActualFolder = Path.Combine(temporaryFolder, "Domains of Dread actual");
    Directory.CreateDirectory(domainsActualFolder);
    var domainsActualStart = Path.Combine(domainsActualFolder, "index.htm");
    var domainsActualCredits = Path.Combine(domainsActualFolder, "dod00010.htm");
    var domainsContentsOne = Path.Combine(domainsActualFolder, "dod00000.htm");
    var domainsContentsTwo = Path.Combine(domainsActualFolder, "dod00001.htm");
    var domainsContentsThree = Path.Combine(domainsActualFolder, "dod00002.htm");
    File.WriteAllText(domainsActualStart, "<title>Domains of Dread</title><a href='dod00010.htm'>Enter</a>");
    File.WriteAllText(domainsActualCredits, "<title>Domains of Dread - Credits</title>");
    File.WriteAllText(domainsContentsOne, "<title>Domains of Dread - Table of Contents</title>");
    File.WriteAllText(domainsContentsTwo, "<title>Domains of Dread - Table of Contents</title>");
    File.WriteAllText(domainsContentsThree, "<title>Domains of Dread - Table of Contents</title>");
    var actualDomains = BookContentsResolver.Resolve(
        domainsActualStart, domainsActualStart, HtmlDocumentCollection.Ravenloft);
    Require(actualDomains.PagePath == Path.GetFullPath(domainsContentsOne),
        "Domains of Dread did not open the first page of its real multi-page contents.");
    Require(actualDomains.ContentsPagePaths?.SequenceEqual(
                new[] { domainsContentsOne, domainsContentsTwo, domainsContentsThree }
                    .Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase) == true,
        "Domains of Dread did not retain all three contents pages in the centre pane.");

    var folder = Path.Combine(temporaryFolder, "Van_Richten_Guides_V2");
    Directory.CreateDirectory(folder);
    var startPage = Path.Combine(folder, "index.htm");
    var guidePage = Path.Combine(folder, "vr05_03.htm");
    var guideContents = Path.Combine(folder, "vr05_contents.htm");
    var guideCover = Path.Combine(folder, "vr05_00.htm");
    var guideImageCover = Path.Combine(folder, "vr05_cover.png");
    var collectionCover = Path.Combine(folder, "cover.jpg");
    var landingImageCover = Path.Combine(folder, "van_richtens_cover.png");
    File.WriteAllText(startPage, "<title>Van Richten Guides</title>");
    File.WriteAllText(guidePage, "<title>Liches</title>");
    File.WriteAllText(guideContents, "<title>Liches contents</title>");
    File.WriteAllText(guideCover, "<title>Liches cover</title>");
    File.WriteAllBytes(guideImageCover, [0x89, 0x50, 0x4E, 0x47]);
    File.WriteAllBytes(collectionCover, [0xFF, 0xD8, 0xFF]);
    File.WriteAllBytes(landingImageCover, [0x89, 0x50, 0x4E, 0x47]);

    var resolved = BookContentsResolver.Resolve(
        startPage, guidePage, HtmlDocumentCollection.Ravenloft);
    Require(resolved.PagePath == Path.GetFullPath(guideContents),
        "The active Van Richten guide did not select its own contents page.");
    Require(resolved.SectionTitle == "Liches", "The Van Richten guide title was not resolved.");
    Require(resolved.CoverPagePath == Path.GetFullPath(guideImageCover),
        "The active Van Richten guide did not prefer its supplied image cover.");

    var guideWithoutContents = Path.Combine(folder, "vr09_03.htm");
    var guideWithoutContentsCover = Path.Combine(folder, "vr09_00.htm");
    File.WriteAllText(guideWithoutContents, "<title>Witches</title>");
    File.WriteAllText(guideWithoutContentsCover, "<title>Witches cover</title>");
    var fallbackContents = BookContentsResolver.Resolve(
        startPage, guideWithoutContents, HtmlDocumentCollection.Ravenloft);
    Require(fallbackContents.PagePath == Path.GetFullPath(startPage),
        "A guide without a dedicated contents page did not use the collection contents.");
    Require(fallbackContents.CoverPagePath == Path.GetFullPath(guideWithoutContentsCover),
        "A guide without a dedicated contents page lost its own cover page.");
    Require(fallbackContents.SectionTitle == "Witches",
        "A guide without a dedicated contents page lost its guide title.");

    var unrelated = BookContentsResolver.Resolve(
        startPage, Path.Combine(folder, "index.htm"), HtmlDocumentCollection.Ravenloft);
    Require(unrelated.CoverPagePath == Path.GetFullPath(landingImageCover),
        "The Van Richten landing page did not select its dedicated collection cover.");
    Require(unrelated.PagePath != Path.GetFullPath(startPage),
        "A non-guide book did not select a distinct second page for the lower pane.");
}
