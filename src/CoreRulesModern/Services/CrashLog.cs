using System.Text;
using System.IO;

namespace CoreRulesModern.Services;

public static class CrashLog
{
    private const long MaximumLogBytes = 1024 * 1024;
    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CoreRulesModern",
        "errors.log");

    public static void TryWrite(Exception exception, string source)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            RotateIfNeeded();
            var entry = new StringBuilder()
                .AppendLine($"[{DateTimeOffset.Now:O}] {source}")
                .AppendLine(exception.ToString())
                .AppendLine();
            File.AppendAllText(LogPath, entry.ToString());
        }
        catch
        {
            // Diagnostics must never cause a second application failure.
        }
    }

    private static void RotateIfNeeded()
    {
        var file = new FileInfo(LogPath);
        if (!file.Exists || file.Length < MaximumLogBytes) return;
        var previousPath = LogPath + ".1";
        File.Move(LogPath, previousPath, overwrite: true);
    }
}
