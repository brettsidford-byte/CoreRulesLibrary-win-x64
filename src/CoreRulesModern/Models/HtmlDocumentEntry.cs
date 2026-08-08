namespace CoreRulesModern.Models;

public enum HtmlDocumentKind
{
    Book,
    Character
}

public enum HtmlDocumentCollection
{
    None,
    AdndSecondEdition,
    Ravenloft
}

public sealed record HtmlDocumentEntry(
    string Title,
    string StartPage,
    string SourceName,
    HtmlDocumentKind Kind,
    HtmlDocumentCollection Collection = HtmlDocumentCollection.None);

public sealed record OnlineResourceEntry(string Title, string Address);
