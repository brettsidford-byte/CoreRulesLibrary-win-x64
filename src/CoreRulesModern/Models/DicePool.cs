using System.Text.Json.Serialization;

namespace CoreRulesModern.Models;

public sealed class DicePool
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Pool";
    public string CharacterName { get; set; } = string.Empty;
    public DicePoolMode Mode { get; set; } = DicePoolMode.Generic;
    public int OpponentAc { get; set; } = 10;
    [JsonPropertyName("Thac0"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? LegacyThac0 { get; set; }
    [JsonPropertyName("AttackModifier"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? LegacyAttackModifier { get; set; }
    public List<DieSpec> Dice { get; set; } = [new()];
    [JsonIgnore] public string Summary => Dice.Count == 0 ? "No dice" : string.Join(" + ", Dice.Select(d => $"{Math.Max(1, d.Quantity)}d{d.Sides}{(d.Modifier > 0 ? $"+{d.Modifier}" : d.Modifier < 0 ? d.Modifier.ToString() : string.Empty)}"));
    [JsonIgnore] public string DetailLine => Mode switch
    {
        DicePoolMode.Attack => $"{Dice.Count} attack{(Dice.Count == 1 ? string.Empty : "s")}   vs AC {OpponentAc}",
        DicePoolMode.Damage => Dice.Select(d => d.Label).FirstOrDefault(label => !string.IsNullOrWhiteSpace(label)) ?? "Damage",
        _ => Dice.Select(d => d.Label).FirstOrDefault(label => !string.IsNullOrWhiteSpace(label)) ?? "Generic roll"
    };
    [JsonIgnore] public string ModeMarkerBrush => Mode switch
    {
        DicePoolMode.Attack => "#A72222",
        DicePoolMode.Damage => "#235DA8",
        _ => "#2C7A43"
    };
}

public enum DicePoolMode { Generic, Attack, Damage }

public sealed class DieSpec
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Sides { get; set; } = 20;
    public int Quantity { get; set; } = 1;
    public int Modifier { get; set; }
    public int Thac0 { get; set; } = 20;
    public int AttackBonus { get; set; }
    public string Label { get; set; } = string.Empty;
    public string ColourHex { get; set; } = "#A72222";
    public string SecondaryColourHex { get; set; } = "#292421";
    [JsonIgnore] public int LastResult { get; set; }
    [JsonIgnore] public int DisplayResult { get; set; }
    [JsonIgnore] public List<int> LastResults { get; set; } = [];
}

public sealed record DiceRollRecord(DateTime Timestamp, string PoolName, string CharacterName, string RequestText, string ResultText, int Total);
