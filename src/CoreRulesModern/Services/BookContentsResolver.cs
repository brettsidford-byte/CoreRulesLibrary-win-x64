using System.IO;
using System.Text.RegularExpressions;

namespace CoreRulesModern.Services;

public sealed record BookContentsLocation(string PagePath, string? SectionTitle = null);

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
        var fallback = new BookContentsLocation(Path.GetFullPath(startPage));
        if (string.IsNullOrWhiteSpace(currentPage)) return fallback;

        var match = VanRichtenPageName().Match(Path.GetFileName(currentPage));
        if (!match.Success) return fallback;

        var prefix = match.Groups[1].Value.ToLowerInvariant();
        var folder = Path.GetDirectoryName(Path.GetFullPath(currentPage));
        if (folder is null) return fallback;

        foreach (var extension in new[] { ".htm", ".html" })
        {
            var candidate = Path.Combine(folder, prefix + "_contents" + extension);
            if (File.Exists(candidate))
            {
                return new BookContentsLocation(
                    Path.GetFullPath(candidate),
                    VanRichtenGuideTitles[prefix]);
            }
        }

        return fallback;
    }

    [GeneratedRegex(@"^(vr0[1-9])_\d+\.html?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VanRichtenPageName();
}
