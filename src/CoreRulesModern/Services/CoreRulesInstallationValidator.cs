using CoreRulesModern.Models;
using System.IO;

namespace CoreRulesModern.Services;

public sealed class CoreRulesInstallationValidator
{
    public InstallationStatus Validate(string rootPath)
    {
        var root = Path.GetFullPath(rootPath);
        var webHelpPath = Path.Combine(root, "WebHelp");
        var htmlCount = Directory.Exists(webHelpPath)
            ? Directory.EnumerateFiles(webHelpPath, "*", SearchOption.AllDirectories)
                .Count(path => path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) ||
                               path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            : 0;
        var valid = htmlCount > 0;

        return new InstallationStatus(root, valid);
    }
}
