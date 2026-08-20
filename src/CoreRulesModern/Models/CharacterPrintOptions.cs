namespace CoreRulesModern.Models;

public sealed record CharacterPrintOptions(
    string PaperSize,
    bool Landscape,
    int MarginMm,
    int SectionsPerPage,
    bool KeepSectionsTogether,
    bool PrintBackgrounds,
    IReadOnlyList<string> BreakAfterSections);
