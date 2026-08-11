using CoreRulesModern.Models;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

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
            AddRavenloftBook(books, webHelpFolder, domainsOfDread, "Domains of Dread");
        }

        var ravenloftFolder = Path.Combine(webHelpFolder, "Ravenloft");
        if (Directory.Exists(ravenloftFolder))
        {
            foreach (var bookFolder in Directory.EnumerateDirectories(ravenloftFolder)
                         .OrderBy(folder => Path.GetFileName(folder), StringComparer.CurrentCultureIgnoreCase))
            {
                var startPage = FindStartPage(bookFolder);
                if (startPage is not null)
                {
                    AddRavenloftBook(books, webHelpFolder, startPage, ReadBookTitle(startPage, bookFolder));
                }
            }
        }

        return books.ToArray();
    }

    private static void AddRavenloftBook(
        ICollection<HtmlDocumentEntry> books,
        string webHelpFolder,
        string startPage,
        string title)
    {
        var fullPath = Path.GetFullPath(startPage);
        if (books.Any(book => Path.GetFullPath(book.StartPage)
                .Equals(fullPath, StringComparison.OrdinalIgnoreCase))) return;

        books.Add(new HtmlDocumentEntry(
            title,
            fullPath,
            Path.GetRelativePath(webHelpFolder, fullPath),
            HtmlDocumentKind.Book,
            HtmlDocumentCollection.Ravenloft));
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

            var startPage = FindStartPage(folder);
            if (startPage is not null) return startPage;
        }

        return null;
    }

    private static string? FindStartPage(string folder)
    {
        foreach (var startPageName in StartPageNames)
        {
            var startPage = Path.Combine(folder, startPageName);
            if (File.Exists(startPage)) return startPage;
        }

        return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(path => StartPageNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(path => RelativeDepth(folder, path))
            .ThenBy(path => Array.FindIndex(StartPageNames,
                name => name.Equals(Path.GetFileName(path), StringComparison.OrdinalIgnoreCase)))
            .ThenBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault();
    }

    private static int RelativeDepth(string folder, string path) =>
        Path.GetRelativePath(folder, path).Count(character =>
            character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar);

    private static string ReadBookTitle(string startPage, string bookFolder)
    {
        try
        {
            using var stream = new FileStream(startPage, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[(int)Math.Min(64 * 1024, stream.Length)];
            var length = stream.Read(buffer, 0, buffer.Length);
            string openingHtml;
            try
            {
                openingHtml = new UTF8Encoding(false, true).GetString(buffer, 0, length);
            }
            catch (DecoderFallbackException)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                openingHtml = Encoding.GetEncoding(1252).GetString(buffer, 0, length);
            }

            var match = Regex.Match(openingHtml, "<title\\b[^>]*>(.*?)</title\\s*>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
            if (match.Success)
            {
                var title = Regex.Replace(WebUtility.HtmlDecode(match.Groups[1].Value), "<[^>]+>", string.Empty)
                    .Trim();
                if (!string.IsNullOrWhiteSpace(title)) return title;
            }
        }
        catch (IOException)
        {
            // Use the folder name when a legacy start page cannot be read.
        }

        return (Path.GetFileName(bookFolder) ?? "Ravenloft Book").Replace('_', ' ').Trim();
    }
}
