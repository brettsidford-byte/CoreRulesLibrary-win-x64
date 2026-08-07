using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using CoreRulesModern.Models;

namespace CoreRulesModern.Services;

public sealed partial class CharacterSheetCatalogue
{
    public IReadOnlyList<HtmlDocumentEntry> Read(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return [];

        return Directory
            .EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            .Select(CreateEntry)
            .OrderBy(entry => entry.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static HtmlDocumentEntry CreateEntry(string path)
    {
        var title = ReadTitle(path);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = Path.GetFileNameWithoutExtension(path).Replace('_', ' ');
        }

        return new HtmlDocumentEntry(
            title.Trim(),
            Path.GetFullPath(path),
            Path.GetFileName(path),
            HtmlDocumentKind.Character);
    }

    private static string ReadTitle(string path)
    {
        try
        {
            using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
            var buffer = new char[65536];
            var length = reader.ReadBlock(buffer, 0, buffer.Length);
            var match = TitleElement().Match(new string(buffer, 0, length));
            return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    [GeneratedRegex(@"<title\b[^>]*>(.*?)</title\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex TitleElement();
}
