namespace CoreRulesModern.Models;

public enum HtmlDocumentKind
{
    Book,
    Character
}

public sealed record HtmlDocumentEntry(
    string Title,
    string StartPage,
    string SourceName,
    HtmlDocumentKind Kind);
