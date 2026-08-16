using System.Text;
using System.IO;

namespace CoreRulesModern.Services;

public static class CrashLog
{
    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CoreRulesModern",
        "errors.log");

    public static void TryWrite(Exception exception, string source)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
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
}
