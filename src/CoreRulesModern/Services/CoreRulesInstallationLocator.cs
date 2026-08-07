using Microsoft.Win32;
using System.IO;

namespace CoreRulesModern.Services;

public sealed class CoreRulesInstallationLocator
{
    public IEnumerable<string> FindCandidates()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddIfPresent(candidates, AppContext.BaseDirectory);
        AddIfPresent(candidates, Environment.CurrentDirectory);

        foreach (var registryPath in ReadRegistryCandidates())
        {
            AddIfPresent(candidates, registryPath);
        }

        var conventionalRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        foreach (var conventionalRoot in conventionalRoots.Where(Directory.Exists))
        {
            AddIfPresent(candidates, Path.Combine(conventionalRoot, "TSR", "Core Rules 2.0"));
            AddIfPresent(candidates, Path.Combine(conventionalRoot, "Wizards of the Coast", "Core Rules 2.0"));
        }

        return candidates;
    }

    private static IEnumerable<string> ReadRegistryCandidates()
    {
        var keys = new[]
        {
            @"SOFTWARE\WOW6432Node\TSR\Core Rules 2.0",
            @"SOFTWARE\TSR\Core Rules 2.0",
            @"SOFTWARE\WOW6432Node\Wizards of the Coast\Core Rules 2.0"
        };

        foreach (var keyPath in keys)
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            foreach (var valueName in new[] { "Path", "InstallPath", "Directory" })
            {
                if (key?.GetValue(valueName) is string value && !string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }
            }
        }
    }

    private static void AddIfPresent(ISet<string> candidates, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            candidates.Add(Path.GetFullPath(path));
        }
    }
}
