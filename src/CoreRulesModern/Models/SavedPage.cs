namespace CoreRulesModern.Models;

public enum SavedLocationKind
{
    Document,
    Spell,
    Online
}

public sealed record SavedPage(
    string DocumentTitle,
    string DocumentStartPage,
    string PagePath,
    string PageTitle,
    HtmlDocumentKind Kind,
    HtmlDocumentCollection Collection,
    DateTimeOffset LastVisited,
    SavedLocationKind LocationKind = SavedLocationKind.Document,
    string? ResourceKey = null);

public sealed record SavedPageLink(SavedPage Page, bool IsBookmark);
