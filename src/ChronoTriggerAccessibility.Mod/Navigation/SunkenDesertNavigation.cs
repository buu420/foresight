namespace ChronoTriggerAccessibility.Mod.Navigation;

internal static class SunkenDesertNavigation
{
    public static bool IsScene(int scene) => scene is 159 or 160 or 161;
    public static bool IsScene(string identity) => int.TryParse(identity.AsSpan(identity.LastIndexOf(':') + 1), out var scene) && IsScene(scene);
    public static bool ChestRoom(int scene) => scene is 160 or 161;
    public const string ChestInstruction = "Shifting sand can carry you away. Use Run to approach. Press or hold Confirm to open the chest; automatic walking keeps trying to stay in reach.";
    public const string ChestArrival = "Press Confirm, or keep it held, to open the chest.";
    public const string ReleaseDash = "Your Dash control selects walking in this Run setting. Release Dash, then start automatic walking again";
    public const string SelectRunning = "Running is required on the shifting sand. Briefly tap and release Dash to select running; holding it selects walking. Then start automatic walking again";
}
