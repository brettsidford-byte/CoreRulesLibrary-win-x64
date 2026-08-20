using System.IO;
using System.Net;
using System.Text;
using CoreRulesModern.Models;

namespace CoreRulesModern.Services;

public sealed class SpellHtmlRenderer(SpellHelpTopicCatalogue helpTopics)
{
    public string Render(SpellRecord spell)
    {
        var helpTopic = string.IsNullOrWhiteSpace(spell.Description) ? helpTopics.Find(spell) : null;
        var html = new StringBuilder();
        html.Append("<!doctype html><html><head><meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\">");
        if (helpTopic is not null)
            html.Append("<base href=\"").Append(Encode(new Uri(helpTopic.PagePath).AbsoluteUri)).Append("\">");

        html.Append("<style>").Append(DocumentAssetCss.PackagedFontCss).Append(DocumentAssetCss.ThemedScrollbarCss);
        html.Append("html,body,body *{font-family:'Core Rules Korinna','ITC Korinna','Korinna',Georgia,serif;box-sizing:border-box}");
        html.Append("body{margin:22px;color:#17212b;background-color:#f5e8c8;background-image:")
            .Append(DocumentAssetCss.ParchmentBackgroundImage)
            .Append(";background-repeat:repeat;background-size:768px 768px;font-size:16px;line-height:1.45}");
        html.Append(".badges{margin:0 0 18px}.badge{display:inline-block;background:#8d2f23;color:#fff;padding:4px 9px;margin:0 6px 6px 0;border-radius:3px}");
        html.Append("table{border-collapse:collapse;width:100%;max-width:980px;margin-bottom:24px}th,td{border-bottom:1px solid #d8d2c6;padding:9px 12px;text-align:left;vertical-align:top}");
        html.Append("th{width:190px;background:#f4f0e7}.description{max-width:980px;white-space:pre-wrap}.muted{color:#657078;font-style:italic}h2{color:#8d2f23;margin-top:22px}");
        html.Append("</style></head><body><div class=\"badges\">");
        if (spell.WizardSpell) AppendBadge(html, "Wizard");
        if (spell.PriestSpell) AppendBadge(html, "Priest");
        AppendBadge(html, $"Level {spell.Level}");
        AppendBadge(html, spell.DatabaseKind == SpellDatabaseKind.Core ? "Core Rules" : "User database");
        html.Append("</div><table>");
        AppendRow(html, "Schools", Join(spell.Schools));
        AppendRow(html, "Spheres", Join(spell.Spheres));
        AppendRow(html, "Range", spell.Range);
        AppendRow(html, "Duration", spell.Duration);
        AppendRow(html, "Area of effect", spell.AreaOfEffect);
        AppendRow(html, "Casting time", spell.CastingTime);
        AppendRow(html, "Components", spell.Components);
        AppendRow(html, "Saving throw", spell.SavingThrow);
        AppendRow(html, "Critical", spell.Critical);
        AppendRow(html, "Knockdown", spell.Knockdown);
        AppendRow(html, "Sensory", spell.Sensory);
        AppendRow(html, "Subtlety", spell.Subtlety);
        AppendRow(html, "Reversible", spell.Reversible ? "Yes" : "No");
        AppendRow(html, "Never Ban Cantrip", spell.NeverBanCantrip ? "Yes" : "No");
        if (spell.HelpTopicId > 0) AppendRow(html, "Help topic", spell.HelpTopicId.ToString());
        html.Append("</table><h2>Description</h2>");
        if (string.IsNullOrWhiteSpace(spell.Description))
        {
            if (helpTopic is null)
                html.Append("<p class=\"muted\">No description is stored in this database record and no matching WebHelp topic was found.</p>");
            else
            {
                html.Append("<div class=\"description\">").Append(helpTopic.DescriptionHtml).Append("</div>");
                html.Append("<p class=\"muted\">").Append(Encode(spell.Name)).Append(" — ")
                    .Append(Encode(GetHelpBookName(helpTopic.PagePath))).Append("</p>");
            }
        }
        else
            html.Append("<div class=\"description\">").Append(Encode(spell.Description)).Append("</div>");

        return html.Append("</body></html>").ToString();
    }

    private static string GetHelpBookName(string pagePath)
    {
        var folder = Path.GetFileName(Path.GetDirectoryName(pagePath)) ?? string.Empty;
        return folder.ToUpperInvariant() switch
        {
            "PHB" => "Player's Handbook", "DMG" => "Dungeon Master Guide", "MM" => "Monstrous Manual",
            "AEG" => "The Complete Book of Arms and Equipment", "CBH" => "The Complete Bard's Handbook",
            "HLC" => "Dungeon Master Option: High-Level Campaigns", "CDH" => "The Complete Druid's Handbook",
            "CBD" => "The Complete Book of Dwarves", "CBE" => "The Complete Book of Elves",
            "CFH" => "The Complete Fighter's Handbook", "CBGH" => "The Complete Book of Gnomes & Halflings",
            "CBN" => "The Complete Book of Necromancers", "CPAH" => "The Complete Paladin's Handbook",
            "CT" => "Player's Option: Combat & Tactics", "SM" => "Player's Option: Spells & Magic",
            "SP" => "Player's Option: Skills & Powers", "CPRH" => "The Complete Priest's Handbook",
            "CRH" => "The Complete Ranger's Handbook", "CTH" => "The Complete Thief's Handbook",
            "TM" => "Tome of Magic", "CWH" => "The Complete Wizard's Handbook",
            _ => string.IsNullOrWhiteSpace(folder) ? "Core Rules" : folder.Replace('_', ' ')
        };
    }

    private static void AppendBadge(StringBuilder html, string value) =>
        html.Append("<span class=\"badge\">").Append(Encode(value)).Append("</span>");

    private static void AppendRow(StringBuilder html, string label, string? value) =>
        html.Append("<tr><th>").Append(Encode(label)).Append("</th><td>")
            .Append(Encode(NormaliseValue(value))).Append("</td></tr>");

    private static string Join(IReadOnlyList<string> values) =>
        values.Count == 0 ? "N/A" : NormaliseValue(string.Join(", ", values));

    private static string NormaliseValue(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed is "—" or "â€”" ? "N/A" : trimmed;
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
