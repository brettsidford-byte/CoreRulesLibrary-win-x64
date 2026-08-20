namespace CoreRulesModern.Models;

public sealed record InstallationStatus(
    string RootPath,
    bool IsValid);
