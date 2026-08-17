using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using CoreRulesModern.Models;

namespace CoreRulesModern.Services;

public sealed record BookContentsLocation(
    string PagePath,
    string? SectionTitle = null,
    string? CoverPagePath = null);

public static partial class BookContentsResolver
{
    private static readonly string[] ManualCoverFileNames =
    {
        "cover.jpg",
        "cover.jpeg",
        "cover.png",
        "cover.webp"
    };

    private static readonly IReadOnlyDictionary<string, string> VanRichtenGuideTitles =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["vr01"] = "Vampires",
            ["vr02"] = "Werebeasts",
            ["vr03"] = "The Created",
            ["vr04"] = "Ghosts",
            ["vr05"] = "Liches",
            ["vr06"] = "The Ancient Dead",
            ["vr07"] = "Fiends",
            ["vr08"] = "The Vistani",
            ["vr09"] = "Witches"
        };

    public static BookContentsLocation Resolve(
        string startPage,
        string? currentPage,
        HtmlDocumentCollection collection = HtmlDocumentCollection.None)
    {
        var start = Path.GetFullPath(startPage);
        var fallback = collection switch
        {
            HtmlDocumentCollection.AdndSecondEdition => ResolveCoreRulesContents(start),
            HtmlDocumentCollection.Ravenloft when IsDomainsOfDread(start) => ResolveDomainsOfDread(start),
            _ => ResolveFirstTwoPages(start)
        };
        if (string.IsNullOrWhiteSpace(currentPage)) return fallback;

        var match = VanRichtenPageName().Match(Path.GetFileName(currentPage));
        if (!match.Success) return fallback;

        var prefix = match.Groups[1].Value.ToLowerInvariant();
        var folder = Path.GetDirectoryName(Path.GetFullPath(currentPage));
        if (folder is null) return fallback;

        string? coverPage = null;
        foreach (var extension in new[] { ".htm", ".html" })
        {
            var candidate = Path.Combine(folder, prefix + "_00" + extension);
            if (File.Exists(candidate))
            {
                coverPage = Path.GetFullPath(candidate);
                break;
            }
        }

        foreach (var extension in new[] { ".htm", ".html" })
        {
            var candidate = Path.Combine(folder, prefix + "_contents" + extension);
            if (File.Exists(candidate))
            {
                return new BookContentsLocation(
                    Path.GetFullPath(candidate),
                    VanRichtenGuideTitles[prefix],
                    coverPage);
            }
        }

        return new BookContentsLocation(
            start,
            VanRichtenGuideTitles[prefix],
            coverPage ?? fallback.CoverPagePath);
    }

    private static BookContentsLocation ResolveCoreRulesContents(string startPage)
    {
        var folder = Path.GetDirectoryName(startPage);
        return new BookContentsLocation(
            startPage,
            CoverPagePath: folder is not null && Directory.Exists(folder)
                ? FindManualCover(folder) ?? startPage
                : startPage);
    }

    private static BookContentsLocation ResolveDomainsOfDread(string startPage)
    {
        var folder = Path.GetDirectoryName(startPage);
        if (folder is null || !Directory.Exists(folder))
        {
            return new BookContentsLocation(startPage, CoverPagePath: startPage);
        }

        var linkedPages = ResolveLinkedPages(startPage, folder);
        var contentsPage = linkedPages.FirstOrDefault(IsContentsPage) ??
                           FindDomainsContentsAfterDedication(startPage, folder) ??
                           linkedPages.FirstOrDefault() ??
                           startPage;
        return new BookContentsLocation(
            contentsPage,
            CoverPagePath: FindManualCover(folder) ?? startPage);
    }

    private static string? FindDomainsContentsAfterDedication(string startPage, string folder)
    {
        var pages = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Where(path => path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var startIndex = Array.FindIndex(pages,
            page => page.Equals(startPage, StringComparison.OrdinalIgnoreCase));
        if (startIndex < 0) return null;

        // Domains of Dread begins with its landing page and dedication before
        // the actual table of contents. Prefer a positively identified page,
        // then use that known third-page layout as a compatibility fallback.
        var identified = pages.Skip(startIndex + 1).Take(4).FirstOrDefault(IsContentsPage);
        return identified ?? (startIndex + 2 < pages.Length ? pages[startIndex + 2] : null);
    }

    private static bool IsDomainsOfDread(string startPage)
    {
        var folder = Path.GetDirectoryName(startPage) ?? string.Empty;
        var normalised = new string(folder.Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
        return normalised.Contains("domainsofdread") || normalised.Contains("domainofdread");
    }

    private static bool IsContentsPage(string path)
    {
        if (Path.GetFileNameWithoutExtension(path)
            .Contains("contents", StringComparison.OrdinalIgnoreCase)) return true;

        try
        {
            using var reader = new StreamReader(path);
            var buffer = new char[32 * 1024];
            var length = reader.ReadBlock(buffer, 0, buffer.Length);
            var openingHtml = new string(buffer, 0, length);
            var title = PageTitle().Match(openingHtml);
            return title.Success && title.Groups[1].Value
                .Contains("contents", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static BookContentsLocation ResolveFirstTwoPages(string startPage)
    {
        var folder = Path.GetDirectoryName(startPage);
        if (folder is null || !Directory.Exists(folder))
        {
            return new BookContentsLocation(startPage, CoverPagePath: startPage);
        }

        var linkedPage = ResolveFirstLinkedPage(startPage, folder);
        var pages = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Where(path => path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var startIndex = Array.FindIndex(pages,
            page => page.Equals(startPage, StringComparison.OrdinalIgnoreCase));
        var secondPage = linkedPage ?? (startIndex >= 0 && startIndex + 1 < pages.Length
            ? pages[startIndex + 1]
            : startPage);

        return new BookContentsLocation(
            secondPage,
            CoverPagePath: FindManualCover(folder) ?? startPage);
    }

    private static string? FindManualCover(string folder)
    {
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .ToArray();
        foreach (var expectedName in ManualCoverFileNames)
        {
            var cover = files.FirstOrDefault(path => Path.GetFileName(path)
                .Equals(expectedName, StringComparison.OrdinalIgnoreCase));
            if (cover is not null) return Path.GetFullPath(cover);
        }

        return null;
    }

    private static string? ResolveFirstLinkedPage(string startPage, string folder)
    {
        return ResolveLinkedPages(startPage, folder).FirstOrDefault();
    }

    private static IReadOnlyList<string> ResolveLinkedPages(string startPage, string folder)
    {
        try
        {
            var html = File.ReadAllText(startPage);
            var pages = new List<string>();
            foreach (Match match in LocalPageLink().Matches(html))
            {
                var href = WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
                var fragmentIndex = href.IndexOf('#');
                if (fragmentIndex >= 0) href = href[..fragmentIndex];
                if (string.IsNullOrWhiteSpace(href)) continue;

                var candidate = Path.GetFullPath(Path.Combine(folder, href.Replace('/', Path.DirectorySeparatorChar)));
                if ((candidate.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) ||
                     candidate.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) &&
                    File.Exists(candidate) &&
                    !candidate.Equals(startPage, StringComparison.OrdinalIgnoreCase))
                {
                    if (!pages.Contains(candidate, StringComparer.OrdinalIgnoreCase)) pages.Add(candidate);
                }
            }
            return pages;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Fall back to the next filename when the start page cannot be read safely.
        }

        return [];
    }

    [GeneratedRegex(@"^(vr0[1-9])_\d+\.html?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VanRichtenPageName();

    [GeneratedRegex(@"href\s*=\s*[\""']([^\""']+\.html?(?:#[^\""']*)?)[\""']",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LocalPageLink();

    [GeneratedRegex(@"<title\b[^>]*>(.*?)</title\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex PageTitle();
}
