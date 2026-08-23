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
                return JsonSerializer.Deserialize<List<DicePool>>(File.ReadAllText(PoolsPath)) ?? CreateDefaults();
        }
        catch { }
        return CreateDefaults();
    }

    public void SavePools(IEnumerable<DicePool> pools)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(PoolsPath, JsonSerializer.Serialize(pools, new JsonSerializerOptions { WriteIndented = true }));
    }

    public int Roll(int sides) => RandomNumberGenerator.GetInt32(1, Math.Max(2, sides) + 1);

    public void AppendHistory(DiceRollRecord record)
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

    public string ReadHistory() => File.Exists(HistoryPath) ? File.ReadAllText(HistoryPath) : "No rolls have been recorded yet.";

    public IReadOnlyList<string> ReadRecentHistory(int count)
    {
        if (!File.Exists(HistoryPath)) return [];
        try
        {
            var entries = File.ReadAllText(HistoryPath)
                .Split([Environment.NewLine + Environment.NewLine], StringSplitOptions.RemoveEmptyEntries)
                .Select(block => block.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries))
                .Where(lines => lines.Length > 0)
                .Select(lines => string.Join("  ", lines.Where(line => line.StartsWith('[') || line.StartsWith("Pool:") || line.StartsWith("Result:"))))
                .Where(entry => !string.IsNullOrWhiteSpace(entry))
                .ToList();
            entries.Reverse();
            return entries.Take(Math.Clamp(count, 1, 50)).ToList();
        }
        catch { return []; }
    }

    private static List<DicePool> CreateDefaults() =>
    [
        new DicePool { Name = "Attack", Mode = DicePoolMode.Attack, Dice = [new DieSpec { Sides = 20 }] },
        new DicePool { Name = "Damage", Mode = DicePoolMode.Damage, Dice = [new DieSpec { Sides = 8, Label = "Slashing" }] }
    ];
}
