using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CoreRulesModern.Models;

namespace CoreRulesModern.Services;

/// <summary>
/// Converts the installation's own WinHelp equipment file into a temporary RTF
/// catalogue and reads topics from it. No Core Rules prose is shipped with the app.
/// </summary>
public sealed class ItemHelpCatalogue
{
    private readonly Dictionary<string, Task<IReadOnlyDictionary<string, string>>> _loads =
        new(StringComparer.OrdinalIgnoreCase);

    public string? LastError { get; private set; }

    public async Task<string?> FindAsync(string? installationRoot, ItemRecord item)
    {
        LastError = null;
        if (!string.IsNullOrWhiteSpace(item.CustomDescription)) return item.CustomDescription;
        var helpPath = FindEquipmentHelpPath(installationRoot);
        if (helpPath is null)
        {
            LastError = "Help\\EQUIP.HLP was not found beneath the selected Core Rules folder.";
            return null;
        }

        if (!_loads.TryGetValue(helpPath, out var load))
        {
            load = LoadAsync(helpPath);
            _loads[helpPath] = load;
        }

        IReadOnlyDictionary<string, string> topics;
        try
        {
            topics = await load;
        }
        catch (Exception exception)
        {
            _loads.Remove(helpPath);
            LastError = $"The WinHelp decoder failed: {exception.Message}";
            return null;
        }
        foreach (var candidate in TopicCandidates(item.Name))
            if (topics.TryGetValue(Normalise(candidate), out var description)) return description;
        LastError = $"The help file was decoded, but no topic matching “{item.Name}” was found.";
        return null;
    }

    private static string? FindEquipmentHelpPath(string? installationRoot)
    {
        if (string.IsNullOrWhiteSpace(installationRoot)) return null;
        var folder = Path.Combine(installationRoot, "Help");
        return Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "Equip.hlp", SearchOption.TopDirectoryOnly).FirstOrDefault()
            : null;
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadAsync(string helpPath)
    {
        var converter = Path.Combine(AppContext.BaseDirectory, "Tools", "helpdeco.exe");
        if (!File.Exists(converter))
        {
            LastError = $"The packaged WinHelp decoder is missing: {converter}";
            return new Dictionary<string, string>();
        }

        var stamp = File.GetLastWriteTimeUtc(helpPath).Ticks.ToString("x", CultureInfo.InvariantCulture);
        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CoreRulesLibrary", "HelpCache", stamp);
        Directory.CreateDirectory(cache);
        var rtfPath = Path.Combine(cache, "EQUIP.rtf");
        if (!File.Exists(rtfPath) || new FileInfo(rtfPath).Length < 1000)
        {
            var start = new ProcessStartInfo
            {
                FileName = converter,
                WorkingDirectory = cache,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add(helpPath);
            start.ArgumentList.Add("-y");
            start.ArgumentList.Add("-g");
            using var process = Process.Start(start);
            if (process is null)
            {
                LastError = "Windows could not start the packaged WinHelp decoder.";
                return new Dictionary<string, string>();
            }
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                LastError = $"The WinHelp decoder exited with code {process.ExitCode}.";
                return new Dictionary<string, string>();
            }
            rtfPath = Directory.EnumerateFiles(cache, "*.rtf", SearchOption.TopDirectoryOnly)
                .FirstOrDefault() ?? rtfPath;
            if (!File.Exists(rtfPath))
            {
                LastError = $"The WinHelp decoder did not create an RTF catalogue in {cache}.";
                return new Dictionary<string, string>();
            }
        }

        var rtf = await File.ReadAllTextAsync(rtfPath, Encoding.Latin1);
        var topics = ParseTopics(rtf);
        if (topics.Count == 0) LastError = "The converted equipment help catalogue contained no readable topics.";
        return topics;
    }

    private static IReadOnlyDictionary<string, string> ParseTopics(string rtf)
    {
        var topics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawTopic in Regex.Split(rtf, @"\\page\b", RegexOptions.CultureInvariant))
        {
            var text = RtfToPlainText(rawTopic);
            var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => Regex.Replace(line, @"\s+", " ").Trim())
                .Where(line => line.Length > 0 && line != "#")
                .ToArray();
            if (lines.Length < 2) continue;

            // Context tokens are stored in footnote groups, which the converter
            // deliberately suppresses, leaving the visible heading first.
            const int headingIndex = 0;
            if (headingIndex >= lines.Length - 1) continue;
            var heading = lines[headingIndex];
            var body = string.Join(Environment.NewLine + Environment.NewLine, lines.Skip(headingIndex + 1)).Trim();
            if (heading.Length is > 0 and <= 160 && body.Length > 0)
                topics.TryAdd(Normalise(heading), body);
        }
        return topics;
    }

    private static string RtfToPlainText(string rtf)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var windows1252 = Encoding.GetEncoding(1252);
        var output = new StringBuilder(rtf.Length / 2);
        var skip = new Stack<bool>();
        var skipping = false;
        for (var index = 0; index < rtf.Length; index++)
        {
            var character = rtf[index];
            if (character == '{') { skip.Push(skipping); continue; }
            if (character == '}') { skipping = skip.Count > 0 && skip.Pop(); continue; }
            if (character != '\\') { if (!skipping) output.Append(character); continue; }
            if (++index >= rtf.Length) break;
            character = rtf[index];
            if (character is '\\' or '{' or '}') { if (!skipping) output.Append(character); continue; }
            if (character == '\'')
            {
                if (index + 2 < rtf.Length && byte.TryParse(rtf.AsSpan(index + 1, 2),
                        NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) && !skipping)
                    output.Append(windows1252.GetString([value]));
                index += 2;
                continue;
            }
            if (character == '*') { skipping = true; continue; }

            var start = index;
            while (index < rtf.Length && char.IsLetter(rtf[index])) index++;
            var word = rtf[start..index];
            if (word is "fonttbl" or "colortbl" or "stylesheet" or "footnote" or "info" or "pict") skipping = true;
            var negative = index < rtf.Length && rtf[index] == '-';
            if (negative) index++;
            var numberStart = index;
            while (index < rtf.Length && char.IsDigit(rtf[index])) index++;
            if (index < rtf.Length && rtf[index] != ' ') index--;
            if (skipping) continue;
            switch (word)
            {
                case "par": case "line": output.AppendLine(); break;
                case "tab": output.Append('\t'); break;
                case "emdash": output.Append('—'); break;
                case "endash": output.Append('–'); break;
                case "lquote": case "rquote": output.Append('’'); break;
                case "ldblquote": case "rdblquote": output.Append('”'); break;
                case "u" when numberStart < index:
                    if (int.TryParse(rtf[numberStart..index], out var unicode))
                        output.Append((char)(negative ? -unicode : unicode));
                    break;
            }
        }
        return output.ToString();
    }

    private static IEnumerable<string> TopicCandidates(string name)
    {
        yield return name;
        var baseName = Regex.Replace(name, @"\s+\+\d+\s*$", string.Empty, RegexOptions.CultureInvariant);
        if (baseName != name) yield return baseName;
        if (baseName.StartsWith("Potion of ", StringComparison.OrdinalIgnoreCase)) yield return baseName[10..];
        if (baseName.StartsWith("Full armor, ", StringComparison.OrdinalIgnoreCase)) yield return baseName[12..];
        if (baseName.StartsWith("Full armour, ", StringComparison.OrdinalIgnoreCase)) yield return baseName[13..];
    }

    private static string Normalise(string value) => Regex.Replace(value, @"[^a-z0-9]+", " ",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim().ToLowerInvariant();
}
