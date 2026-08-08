using CoreRulesModern.Models;
using System.IO;

namespace CoreRulesModern.Services;

public sealed class ManualCatalogue
{
    private static readonly string[] StartPageNames =
    {
        "index.html",
        "index.htm",
        "default.html",
        "default.htm"
    };

    private static readonly (string Title, string RelativePath)[] Books =
    {
        ("Player's Handbook", @"PHB\DD01405.htm"),
        ("Dungeon Master Guide", @"DMG\DD00183.htm"),
        ("Monstrous Manual", @"MM\DD03789.htm"),
        ("The Complete Book of Arms and Equipment", @"AEG\DD00001.htm"),
        ("The Complete Bard's Handbook", @"CBH\DD04937.htm"),
        ("Dungeon Master Option: High-Level Campaigns", @"HLC\DD01149.htm"),
        ("The Complete Druid's Handbook", @"CDH\DD05025.htm"),
        ("The Complete Book of Dwarves", @"CBD\DD04591.htm"),
        ("The Complete Book of Elves", @"CBE\DD04706.htm"),
        ("The Complete Fighter's Handbook", @"CFH\DD05104.htm"),
        ("The Complete Book of Gnomes & Halflings", @"CBGH\DD04825.htm"),
        ("The Complete Book of Necromancers", @"CBN\CBN00000.htm"),
        ("The Complete Paladin's Handbook", @"CPaH\DD05368.htm"),
        ("Player's Option: Combat & Tactics", @"CT\DD02368.htm"),
        ("Player's Option: Spells & Magic", @"SM\DD03364.htm"),
        ("Player's Option: Skills & Powers", @"SP\DD02860.htm"),
        ("The Complete Priest's Handbook", @"CPrH\DD05451.htm"),
        ("The Complete Ranger's Handbook", @"CRH\DD05676.htm"),
        ("The Complete Thief's Handbook", @"CTH\DD05762.htm"),
        ("Tome of Magic", @"TM\DD04121.htm"),
        ("The Complete Wizard's Handbook", @"CWH\DD06066.htm")
    };

    public IReadOnlyList<HtmlDocumentEntry> Read(string installationRoot)
    {
        var webHelpFolder = Path.Combine(installationRoot, "WebHelp");
        if (!Directory.Exists(webHelpFolder)) return Array.Empty<HtmlDocumentEntry>();

        var books = Books
            .Select(book => new HtmlDocumentEntry(
                book.Title,
                Path.Combine(webHelpFolder, book.RelativePath),
                book.RelativePath,
                HtmlDocumentKind.Book,
                HtmlDocumentCollection.AdndSecondEdition))
            .Where(book => File.Exists(book.StartPage))
            .ToList();

        var domainsOfDread = FindDomainsOfDread(webHelpFolder);
        if (domainsOfDread is not null)
        {
            books.Add(new HtmlDocumentEntry(
                "Domains of Dread",
                domainsOfDread,
                Path.GetRelativePath(webHelpFolder, domainsOfDread),
                HtmlDocumentKind.Book,
                HtmlDocumentCollection.Ravenloft));
        }

        return books.ToArray();
    }

    private static string? FindDomainsOfDread(string webHelpFolder)
    {
        foreach (var folder in Directory.EnumerateDirectories(webHelpFolder))
        {
            var normalisedName = new string(Path.GetFileName(folder)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());

            if (!normalisedName.Contains("domainsofdread") &&
                !normalisedName.Contains("domainofdread"))
            {
                continue;
            }

            foreach (var startPageName in StartPageNames)
            {
                var startPage = Path.Combine(folder, startPageName);
                if (File.Exists(startPage)) return startPage;
            }
        }

        return null;
    }
}
