namespace CoreRulesModern.Services;

public static class SettingsNormaliser
{
    public static int Scale(int value, int fallback) =>
        value is >= 100 and <= 300 && value % 25 == 0 ? value : fallback;

    public static int RecentPageLimit(int value) => value switch
    {
        10 or 20 or 30 or 50 => value,
        _ => 20
    };
}
