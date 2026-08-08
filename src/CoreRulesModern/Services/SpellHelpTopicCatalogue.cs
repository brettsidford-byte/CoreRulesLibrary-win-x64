using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using CoreRulesModern.Models;

namespace CoreRulesModern.Services;

public sealed partial class SpellHelpTopicCatalogue
{
    private static readonly Encoding Windows1252;
    private static readonly IReadOnlyDictionary<string, string> ReverseSpellNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["destroywater"] = "createwater"
        };
    private string? _webHelpFolder;
    private IReadOnlyDictionary<string, IReadOnlyList<TopicReference>> _topics =
        new Dictionary<string, IReadOnlyList<TopicReference>>(StringComparer.OrdinalIgnoreCase);

    static SpellHelpTopicCatalogue()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Windows1252 = Encoding.GetEncoding(1252);
    }

    public void Load(string installationRoot)
    {
        _webHelpFolder = Path.Combine(installationRoot, "WebHelp");
        var indexPath = Path.Combine(_webHelpFolder, "index.hhk");
        if (!File.Exists(indexPath))
        {
            _topics = new Dictionary<string, IReadOnlyList<TopicReference>>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        var topics = new Dictionary<string, List<TopicReference>>(StringComparer.OrdinalIgnoreCase);
        foreach (Match objectMatch in ObjectPattern().Matches(File.ReadAllText(indexPath, Windows1252)))
        {
            var parameters = ParameterPattern().Matches(objectMatch.Groups[1].Value)
                .Cast<Match>()
                .Select(match => (Name: match.Groups[1].Value, Value: WebUtility.HtmlDecode(match.Groups[2].Value)))
                .ToArray();
            string? currentTitle = null;
            foreach (var parameter in parameters)
            {
                if (parameter.Name.Equals("Name", StringComparison.OrdinalIgnoreCase))
                {
                    currentTitle = parameter.Value;
                    continue;
                }

                if (!parameter.Name.Equals("Local", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(currentTitle)) continue;
                AddTopic(topics, currentTitle, parameter.Value);
            }
        }

        _topics = topics.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<TopicReference>)pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    public SpellHelpTopic? Find(SpellRecord spell)
    {
        if (_webHelpFolder is null) return null;
        var requestedName = NormaliseSpellName(spell.Name);
        var lookupName = ReverseSpellNames.GetValueOrDefault(requestedName, requestedName);
        IReadOnlyList<TopicReference> candidates;
        if (_topics.TryGetValue(lookupName, out var exactCandidates))
        {
            candidates = exactCandidates;
        }
        else
        {
            candidates = _topics
                .Where(pair => IsCloseName(pair.Key, lookupName))
                .SelectMany(pair => pair.Value)
                .ToArray();
        }

        if (candidates.Count == 0) return null;
        foreach (var candidate in candidates.OrderByDescending(candidate => Score(candidate.Title, spell)))
        {
            var pagePath = Path.GetFullPath(Path.Combine(
                _webHelpFolder,
                candidate.LocalPath.Replace('\\', Path.DirectorySeparatorChar)));
            if (!pagePath.StartsWith(Path.GetFullPath(_webHelpFolder), StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(pagePath)) continue;

            var bodyMatch = BodyPattern().Match(File.ReadAllText(pagePath, Windows1252));
            if (!bodyMatch.Success) continue;
            var description = ExtractDescription(bodyMatch.Groups[1].Value);
            if (!string.IsNullOrWhiteSpace(description))
            {
                return new SpellHelpTopic(candidate.Title, pagePath, description);
            }
        }

        return null;
    }

    private static void AddTopic(
        IDictionary<string, List<TopicReference>> topics,
        string title,
        string localPath)
    {
        var separator = title.IndexOf("--", StringComparison.Ordinal);
        if (separator <= 0 || !title.Contains("Spell", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(localPath)) return;
        var spellName = NormaliseSpellName(title[..separator]);
        if (!topics.TryGetValue(spellName, out var entries)) topics[spellName] = entries = [];
        if (!entries.Any(entry => entry.LocalPath.Equals(localPath, StringComparison.OrdinalIgnoreCase)))
        {
            entries.Add(new TopicReference(title, localPath));
        }
    }

    private static bool IsCloseName(string indexedName, string requestedName) =>
        indexedName.Length >= requestedName.Length + 2 &&
        indexedName.StartsWith(requestedName, StringComparison.OrdinalIgnoreCase);

    private static int Score(string title, SpellRecord spell)
    {
        var score = 0;
        if (spell.Level > 0 && title.Contains($"{Ordinal(spell.Level)} Level", StringComparison.OrdinalIgnoreCase)) score += 20;
        if (spell.WizardSpell && title.Contains("Wizard Spell", StringComparison.OrdinalIgnoreCase)) score += 10;
        if (spell.PriestSpell && title.Contains("Priest Spell", StringComparison.OrdinalIgnoreCase)) score += 10;
        return score;
    }

    private static string ExtractDescription(string body)
    {
        var standardFields = SavingThrowPattern().Match(body);
        var description = standardFields.Success ? body[(standardFields.Index + standardFields.Length)..] : body;
        description = ContentsLinkPattern().Replace(description, string.Empty);
        description = FontTagPattern().Replace(description, string.Empty);
        return description.Trim();
    }

    private static string Ordinal(int value) => value % 100 is 11 or 12 or 13
        ? $"{value}th"
        : (value % 10) switch { 1 => $"{value}st", 2 => $"{value}nd", 3 => $"{value}rd", _ => $"{value}th" };

    private static string NormaliseSpellName(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private sealed record TopicReference(string Title, string LocalPath);

    [GeneratedRegex("<object\\b[^>]*>(.*?)</object>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ObjectPattern();

    [GeneratedRegex("<param\\s+name=[\"']([^\"']+)[\"']\\s+value=[\"']([^\"']*)[\"'][^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ParameterPattern();

    [GeneratedRegex("<body\\b[^>]*>(.*?)</body>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BodyPattern();

    [GeneratedRegex("Saving(?:\\s|<[^>]+>)*Throw(?:\\s|<[^>]+>)*:.*?(?:<p[^>]*>\\s*</p>\\s*){2}", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SavingThrowPattern();

    [GeneratedRegex("<a\\s+href=[\"'][^\"']+[\"'][^>]*>\\s*</a>.*?Table of Contents.*$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ContentsLinkPattern();

    [GeneratedRegex("</?font\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex FontTagPattern();
}
