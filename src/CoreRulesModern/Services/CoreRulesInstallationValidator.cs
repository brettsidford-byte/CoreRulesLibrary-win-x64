using CoreRulesModern.Models;
using System.IO;

namespace CoreRulesModern.Services;

public sealed class CoreRulesInstallationValidator
{
    public InstallationStatus Validate(string rootPath)
    {
        var root = Path.GetFullPath(rootPath);
        var items = new List<ValidationItem>();
        var webHelpPath = Path.Combine(root, "WebHelp");
        AddDirectory(items, "HTML book library", webHelpPath);
        var htmlCount = Directory.Exists(webHelpPath)
            ? Directory.EnumerateFiles(webHelpPath, "*.htm", SearchOption.AllDirectories).Count()
            : 0;
        var valid = htmlCount > 0;

        var summary = valid
            ? $"HTML book library ready: {htmlCount:N0} help pages found."
            : "This folder does not contain the Core Rules WebHelp library.";

        return new InstallationStatus(root, valid, summary, items, 0, 0);
    }

    private static void AddDirectory(ICollection<ValidationItem> items, string name, string path) =>
        items.Add(new ValidationItem(name, path, Directory.Exists(path)));

}
