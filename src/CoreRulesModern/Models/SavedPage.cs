namespace CoreRulesModern.Models;

public sealed record SavedPage(
    string DocumentTitle,
    string DocumentStartPage,
    string PagePath,
    string PageTitle,
    HtmlDocumentKind Kind,
    HtmlDocumentCollection Collection,
    DateTimeOffset LastVisited);

public sealed record SavedPageLink(SavedPage Page, bool IsBookmark);
