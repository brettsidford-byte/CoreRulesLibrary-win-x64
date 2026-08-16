using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace CoreRulesModern.Services;

public sealed record BookContentsLocation(
    string PagePath,
    string? SectionTitle = null,
    string? CoverPagePath = null);

public static partial class BookContentsResolver
{
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

    public static BookContentsLocation Resolve(string startPage, string? currentPage)
    {
        var start = Path.GetFullPath(startPage);
        var fallback = ResolveFirstTwoPages(start);
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

        return new BookContentsLocation(secondPage, CoverPagePath: startPage);
    }

    private static string? ResolveFirstLinkedPage(string startPage, string folder)
    {
        try
        {
            var html = File.ReadAllText(startPage);
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
                    return candidate;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Fall back to the next filename when the start page cannot be read safely.
        }

        return null;
    }

    [GeneratedRegex(@"^(vr0[1-9])_\d+\.html?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VanRichtenPageName();

    [GeneratedRegex(@"href\s*=\s*[\""']([^\""']+\.html?(?:#[^\""']*)?)[\""']",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LocalPageLink();
}
