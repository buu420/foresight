namespace ChronoTriggerAccessibility.Mod.Battle;

internal static class BattleIdentity
{
    public static string Unnamed(int slot) => slot < 3
        ? $"Party member {slot + 1}" : $"Enemy {(char)('A' + slot - 3)}";
}
