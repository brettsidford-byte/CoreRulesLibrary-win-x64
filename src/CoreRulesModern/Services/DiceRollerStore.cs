using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CoreRulesModern.Models;

namespace CoreRulesModern.Services;

public sealed class DiceRollerStore
{
    private readonly string _folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoreRulesModern", "DiceRoller");
    private string PoolsPath => Path.Combine(_folder, "dice-pools.json");
    public string HistoryPath => Path.Combine(_folder, "DiceHistory.txt");

    public List<DicePool> LoadPools()
    {
        Directory.CreateDirectory(_folder);
        try
        {
            if (File.Exists(PoolsPath))
            {
                var pools = JsonSerializer.Deserialize<List<DicePool>>(File.ReadAllText(PoolsPath)) ?? CreateDefaults();
                MigrateAttackSettings(pools);
                return pools;
            }
        }
        catch { }
        return CreateDefaults();
    }

    public void SavePools(IEnumerable<DicePool> pools)
    {
        try
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(PoolsPath, JsonSerializer.Serialize(pools, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    public int Roll(int sides) => RandomNumberGenerator.GetInt32(1, Math.Max(2, sides) + 1);

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

    private static List<DicePool> CreateDefaults() =>
    [
        new DicePool { Name = "Attack", Mode = DicePoolMode.Attack, Dice = [new DieSpec { Sides = 20 }] },
        new DicePool { Name = "Damage", Mode = DicePoolMode.Damage, Dice = [new DieSpec { Sides = 8, Label = "Slashing" }] }
    ];
}
