using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CoreRulesModern.Models;

namespace CoreRulesModern.Services;

public sealed class DiceRollerStore
{
    private const long MaximumHistoryBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _folder;
    private string PoolsPath => Path.Combine(_folder, "dice-pools.json");
    private string PoolsBackupPath => PoolsPath + ".bak";
    private string InterfaceScalePath => Path.Combine(_folder, "interface-scale.txt");
    public string HistoryPath => Path.Combine(_folder, "DiceHistory.txt");
    public string? LoadWarning { get; private set; }
    public string? SaveWarning { get; private set; }
    public event Action<string>? PersistenceWarning;

    public DiceRollerStore(string? folder = null)
    {
        _folder = folder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CoreRulesModern",
            "DiceRoller");
    }

    public List<DicePool> LoadPools()
    {
        Directory.CreateDirectory(_folder);
        if (!File.Exists(PoolsPath)) return CreateDefaults();

        var pools = TryLoadPools(PoolsPath);
        if (pools is not null)
        {
            MigrateAttackSettings(pools);
            return pools;
        }

        PreserveCorruptPoolsFile();
        pools = TryLoadPools(PoolsBackupPath);
        if (pools is not null)
        {
            LoadWarning = "The saved dice pools were damaged. The previous backup was recovered and the damaged file was preserved for diagnosis.";
            MigrateAttackSettings(pools);
            return pools;
        }

        LoadWarning = "The saved dice pools and their backup could not be read. Default pools have been loaded; the damaged file was preserved for diagnosis.";
        return CreateDefaults();
    }

    public bool SavePools(IEnumerable<DicePool> pools)
    {
        SaveWarning = null;
        try
        {
            Directory.CreateDirectory(_folder);
            WriteAtomic(PoolsPath, JsonSerializer.Serialize(pools, JsonOptions), PoolsBackupPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SaveWarning = $"Dice pools could not be saved. Your previous saved copy has been retained.\n\n{exception.Message}";
            PersistenceWarning?.Invoke(SaveWarning);
            return false;
        }
    }

    public int Roll(int sides) => RandomNumberGenerator.GetInt32(1, Math.Max(2, sides) + 1);

    public string LoadInterfaceScale()
    {
        try
        {
            var value = File.Exists(InterfaceScalePath) ? File.ReadAllText(InterfaceScalePath).Trim() : "Auto";
            return value is "Auto" or "0.6" or "0.7" or "0.8" or "0.9" or "1" or "1.1" or "1.25" or "1.5" or "1.75" ? value : "Auto";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return "Auto"; }
    }

    public bool SaveInterfaceScale(string value)
    {
        try
        {
            Directory.CreateDirectory(_folder);
            WriteAtomic(InterfaceScalePath, value);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }

    public void AppendHistory(DiceRollRecord record)
    {
        try
        {
            Directory.CreateDirectory(_folder);
            var sb = new StringBuilder();
            sb.AppendLine($"[{record.Timestamp:dd MMMM yyyy HH:mm:ss}]");
            sb.AppendLine($"Pool: {record.PoolName}");
            if (!string.IsNullOrWhiteSpace(record.CharacterName)) sb.AppendLine($"PC/NPC: {record.CharacterName}");
            sb.AppendLine($"Request: {record.RequestText}");
            sb.AppendLine($"Result: {record.ResultText}");
            sb.AppendLine();
            File.AppendAllText(HistoryPath, sb.ToString());
            TrimHistoryIfNeeded();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static void MigrateAttackSettings(IEnumerable<DicePool> pools)
    {
        foreach (var pool in pools)
        {
            if (pool.Mode == DicePoolMode.Attack)
                foreach (var die in pool.Dice)
                {
                    if (pool.LegacyThac0.HasValue) die.Thac0 = pool.LegacyThac0.Value;
                    if (pool.LegacyAttackModifier.HasValue) die.AttackBonus = pool.LegacyAttackModifier.Value;
                }
            pool.LegacyThac0 = null;
            pool.LegacyAttackModifier = null;
        }
    }

    public string ReadHistory()
    {
        try { return File.Exists(HistoryPath) ? File.ReadAllText(HistoryPath) : "No rolls have been recorded yet."; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return "Roll history is currently unavailable."; }
    }

    public IReadOnlyList<string> ReadRecentHistory(int count)
    {
        if (!File.Exists(HistoryPath)) return [];
        try
        {
            var entries = File.ReadAllText(HistoryPath)
                .Split([Environment.NewLine + Environment.NewLine], StringSplitOptions.RemoveEmptyEntries)
                .Select(block => block.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries))
                .Where(lines => lines.Length > 0)
                .Select(lines => FormatRecentEntry(lines))
                .Where(entry => !string.IsNullOrWhiteSpace(entry))
                .ToList();
            entries.Reverse();
            return entries.Take(Math.Clamp(count, 1, 50)).ToList();
        }
        catch { return []; }
    }

    private static string FormatRecentEntry(string[] lines)
    {
        var timestamp = lines.FirstOrDefault(line => line.StartsWith('['))?.Trim('[', ']') ?? string.Empty;
        if (DateTime.TryParse(timestamp, out var date)) timestamp = date.ToString("HH:mm");
        var pool = lines.FirstOrDefault(line => line.StartsWith("Pool:"))?.Replace("Pool:", string.Empty).Trim() ?? "Roll";
        var result = lines.FirstOrDefault(line => line.StartsWith("Result:"))?.Replace("Result:", string.Empty).Trim() ?? string.Empty;
        if (result.Length > 72) result = result[..69] + "…";
        return $"{timestamp}  {pool}\n{result}".Trim();
    }

    private static List<DicePool>? TryLoadPools(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var pools = JsonSerializer.Deserialize<List<DicePool>>(File.ReadAllText(path));
            return pools is { Count: > 0 } ? pools : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void PreserveCorruptPoolsFile()
    {
        try
        {
            if (!File.Exists(PoolsPath)) return;
            var preservedPath = Path.Combine(_folder, $"dice-pools.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(PoolsPath, preservedPath, overwrite: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static void WriteAtomic(string path, string contents, string? backupPath = null)
    {
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, contents);
        if (File.Exists(path))
            File.Replace(temporaryPath, path, backupPath, ignoreMetadataErrors: true);
        else
            File.Move(temporaryPath, path);
    }

    private void TrimHistoryIfNeeded()
    {
        var file = new FileInfo(HistoryPath);
        if (!file.Exists || file.Length <= MaximumHistoryBytes) return;

        var text = File.ReadAllText(HistoryPath);
        var entries = text.Split([Environment.NewLine + Environment.NewLine], StringSplitOptions.RemoveEmptyEntries);
        var kept = new Stack<string>();
        var bytes = 0;
        for (var index = entries.Length - 1; index >= 0; index--)
        {
            var entryBytes = Encoding.UTF8.GetByteCount(entries[index]) + Encoding.UTF8.GetByteCount(Environment.NewLine) * 2;
            if (bytes + entryBytes > MaximumHistoryBytes / 2 && kept.Count > 0) break;
            kept.Push(entries[index]);
            bytes += entryBytes;
        }
        WriteAtomic(HistoryPath, string.Join(Environment.NewLine + Environment.NewLine, kept) + Environment.NewLine + Environment.NewLine);
    }

    private static List<DicePool> CreateDefaults() =>
    [
        new DicePool { Name = "Attack", Mode = DicePoolMode.Attack, Dice = [new DieSpec { Sides = 20 }] },
        new DicePool { Name = "Damage", Mode = DicePoolMode.Damage, Dice = [new DieSpec { Sides = 8, Label = "Slashing" }] }
    ];
}
