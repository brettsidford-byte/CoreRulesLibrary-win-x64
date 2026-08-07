namespace CoreRulesModern.Models;

public enum SpellDatabaseKind
{
    Core,
    User
}

public sealed record SpellRecord(
    string Name,
    bool NeverBanCantrip,
    bool Reversible,
    bool WizardSpell,
    bool PriestSpell,
    int Level,
    string AreaOfEffect,
    string CastingTime,
    string Components,
    string Critical,
    string Duration,
    string Knockdown,
    string Range,
    string SavingThrow,
    string Sensory,
    string Subtlety,
    uint HelpTopicId,
    string Description,
    IReadOnlyList<string> Schools,
    IReadOnlyList<string> Spheres,
    SpellDatabaseKind DatabaseKind);

public sealed record SpellDatabase(
    string Path,
    ushort Schema,
    string RuntimeClass,
    IReadOnlyList<SpellRecord> Spells);
