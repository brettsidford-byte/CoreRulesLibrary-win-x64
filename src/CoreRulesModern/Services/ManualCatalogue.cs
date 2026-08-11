using CoreRulesModern.Models;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace CoreRulesModern.Services;

public sealed class ManualCatalogue
{
    private static readonly string[] StartPageNames =
    {
        "index.html",
        "index.htm",
        "default.html",
        "default.htm",
        "reader.html",
        "reader.htm",
        "book.html",
        "book.htm"
    };

    private static readonly IReadOnlyDictionary<string, string> KnownRavenloftBooks =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["domainsofdread"] = "Domains of Dread",
            ["domainofdread"] = "Domains of Dread",
            ["feastofgoblyns"] = "Feast of Goblyns",
            ["rootsofevil"] = "Roots of Evil",
            ["fromtheshadows"] = "From the Shadows",
            ["shipofhorror"] = "Ship of Horror",
            ["touchofdeath"] = "Touch of Death"
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

        books.AddRange(FindRavenloftBooks(webHelpFolder));

        return books.ToArray();
    }

    private static IEnumerable<HtmlDocumentEntry> FindRavenloftBooks(string webHelpFolder)
    {
        var discoveredPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in Directory.EnumerateDirectories(webHelpFolder, "*", SearchOption.AllDirectories))
        {
            var normalisedName = NormaliseName(Path.GetFileName(folder));
            var relativeParts = Path.GetRelativePath(webHelpFolder, folder)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var isInsideRavenloft = relativeParts
                .Take(Math.Max(0, relativeParts.Length - 1))
                .Select(NormaliseName)
                .Any(part => part is "ravenloft" or "ravenloftbooks");
            var isKnownBook = KnownRavenloftBooks.TryGetValue(normalisedName, out var knownTitle);

            if (!isInsideRavenloft && !isKnownBook)
            {
                continue;
            }

            var startPage = FindStartPage(folder);
            if (startPage is null || !discoveredPages.Add(Path.GetFullPath(startPage))) continue;
            var title = knownTitle ?? ReadHtmlTitle(startPage) ?? MakeReadableTitle(Path.GetFileName(folder));
            yield return new HtmlDocumentEntry(
                title,
                startPage,
                Path.GetRelativePath(webHelpFolder, startPage),
                HtmlDocumentKind.Book,
                HtmlDocumentCollection.Ravenloft);
        }

        foreach (var ravenloftRoot in Directory.EnumerateDirectories(webHelpFolder, "*", SearchOption.AllDirectories)
                     .Where(folder => NormaliseName(Path.GetFileName(folder)) is "ravenloft" or "ravenloftbooks"))
        {
            foreach (var page in Directory.EnumerateFiles(ravenloftRoot, "*.htm*", SearchOption.TopDirectoryOnly))
            {
                if (!KnownRavenloftBooks.TryGetValue(NormaliseName(Path.GetFileNameWithoutExtension(page)), out var title) ||
                    !discoveredPages.Add(Path.GetFullPath(page))) continue;
                yield return new HtmlDocumentEntry(
                    title,
                    page,
                    Path.GetRelativePath(webHelpFolder, page),
                    HtmlDocumentKind.Book,
                    HtmlDocumentCollection.Ravenloft);
            }
        }
    }

    private static string? FindStartPage(string folder)
    {
        foreach (var name in StartPageNames)
        {
            var path = Path.Combine(folder, name);
            if (File.Exists(path)) return path;
        }

        return null;
    }

    private static string? ReadHtmlTitle(string path)
    {
        try
        {
            using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
            var buffer = new char[65536];
            var length = reader.ReadBlock(buffer, 0, buffer.Length);
            var match = Regex.Match(new string(buffer, 0, length), @"<title\b[^>]*>(.*?)</title\s*>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
            if (!match.Success) return null;
            var title = WebUtility.HtmlDecode(Regex.Replace(match.Groups[1].Value, "<[^>]+>", string.Empty)).Trim();
            return title.Length == 0 ? null : title;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string NormaliseName(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string MakeReadableTitle(string value) =>
        Regex.Replace(value.Replace('_', ' ').Replace('-', ' '), "(?<=[a-z])(?=[A-Z])", " ").Trim();
}
