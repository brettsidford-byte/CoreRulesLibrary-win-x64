namespace CoreRulesModern.Services;

public static class BrowserSecurityPolicy
{
    private static readonly string[] AllowedOnlineHosts =
    [
        "completecompendium.com",
        "www.completecompendium.com"
    ];

    public static bool IsAllowedOnlineAddress(Uri? address) =>
        address is { Scheme: "https" } &&
        AllowedOnlineHosts.Contains(address.IdnHost, StringComparer.OrdinalIgnoreCase);

    public static bool IsLocalPageWithin(Uri? address, IEnumerable<string?> roots)
    {
        if (address is not { IsFile: true }) return false;
        var page = Path.GetFullPath(address.LocalPath);
        return roots.Where(root => !string.IsNullOrWhiteSpace(root)).Any(root =>
            IsWithin(page, Path.GetFullPath(root!)));
    }

    public static bool IsWithin(string candidatePath, string rootPath)
    {
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        return candidate.Equals(root, StringComparison.OrdinalIgnoreCase) ||
               candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
