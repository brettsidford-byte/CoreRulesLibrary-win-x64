using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using CoreRulesModern.Models;

namespace CoreRulesModern.Services;

/// <summary>Resolves the prose behind an original Core Rules item help link.</summary>
public sealed class ItemDescriptionCatalogue
{
    private readonly Dictionary<string, string> _plainText = new(StringComparer.OrdinalIgnoreCase);

    public string? Find(string? installationRoot, ItemRecord item)
    {
        if (!string.IsNullOrWhiteSpace(item.CustomDescription)) return item.CustomDescription;
        if (string.IsNullOrWhiteSpace(installationRoot) || item.HelpTopicId == 0) return null;
        var books = Path.Combine(installationRoot, "Books");
        if (!Directory.Exists(books)) return null;

        var headings = HeadingCandidates(item.Name);
        foreach (var path in Directory.EnumerateFiles(books, "*.rtf", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => Path.GetFileName(path).Equals("DMGBK.RTF", StringComparison.OrdinalIgnoreCase) ? 0 : 1))
        {
            var text = ReadPlainText(path);
            foreach (var heading in headings)
            {
                var description = ExtractSection(text, heading);
                if (!string.IsNullOrWhiteSpace(description)) return description;
            }
        }
        return null;
    }

    private string ReadPlainText(string path)
    {
        if (_plainText.TryGetValue(path, out var cached)) return cached;
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var box = new RichTextBox();
            var range = new TextRange(box.Document.ContentStart, box.Document.ContentEnd);
            range.Load(stream, DataFormats.Rtf);
            cached = range.Text;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
            cached = string.Empty;
        }
        _plainText[path] = cached;
        return cached;
    }

    private static IEnumerable<string> HeadingCandidates(string name)
    {
        yield return name;
        var comma = name.IndexOf(',');
        if (comma > 0 && comma + 1 < name.Length)
            yield return $"{name[(comma + 1)..].Trim()} {name[..comma].Trim()}";
    }

    private static string? ExtractSection(string text, string heading)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var pattern = $@"(?im)^\s*{Regex.Escape(heading)}\s*:?[ \t]*";
        foreach (Match match in Regex.Matches(text, pattern, RegexOptions.CultureInvariant))
        {
            var start = match.Index;
            var searchStart = Math.Min(text.Length, match.Index + match.Length + 100);
            var searchLength = Math.Min(10_000, text.Length - searchStart);
            var headingPattern = new Regex(@"(?m)^\s*[A-Z][^\r\n]{2,70}:\s*",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
            var next = headingPattern.Match(text, searchStart, searchLength);
            var end = Math.Min(text.Length, start + 8_000);
            if (next.Success) end = next.Index;
            var value = text[start..end].Trim();
            if (value.Length >= heading.Length + 40) return value;
        }
        return null;
    }
}
