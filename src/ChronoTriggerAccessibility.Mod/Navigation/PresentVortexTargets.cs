using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Native Volcano wall switches. Their script tests the standing tile
/// and Confirm, then opens a choice; its accepted result is not a walk-on exit.</summary>
internal static class PresentVortexTargets
{
    public const string FirstSwitch = "script-switch:8:Extra:35:1:True:22:14:22:14";
    public const string LadderSwitch = "script-switch:8:Extra:35:2:True:7:8:7:8";
    public const string Instruction = "Press Confirm to examine the switch.";
    private static (int X, int Y, int Mask, int Start, int End)? Control(int scene) => scene switch
    {
        608 => (22, 14, 1, 0x17A, 0x1FA),
        613 => (7, 8, 2, 0x185, 0x1E9),
        _ => null,
    };
    public static bool IsSwitch(int scene, GameNavigationCatalog.Region region) =>
        Control(scene) is { } c && region is { Actor: 8, Kind: "Switch", Source: "Extra", Index: 35, Set: true } &&
        region.Value == c.Mask && At(c.X, c.Y, region);
    public static bool IsEffect(int scene, GameNavigationCatalog.Region region) =>
        Control(scene) is { } c && region.Actor == 8 && At(c.X, c.Y, region) &&
        (region.Kind == "Terrain" || region.Kind == "Warp" && region.Destination == (scene == 608 ? 609 : 613));
    private static bool At(int x, int y, GameNavigationCatalog.Region region) =>
        region.Left == x && region.Right == x && region.Top == y && region.Bottom == y;
    public static bool WatcherAvailable(FieldNavigationSnapshot field, GameNavigationCatalog.Region region, FieldStoryState? state)
    {
        if (!IsSwitch(field.SceneId, region)) return true;
        var c = Control(field.SceneId)!.Value;
        return field.SceneIdCoherent && state?.Extra(35) is { } flags && (flags & c.Mask) == 0 &&
            field.Actors.Any(a => a.Index == 8 && a.ClassTag == 7 && a.IsUsable && !a.IsPartyMember &&
                a.ScriptProcessingEnabled && a.ScriptAddress >= c.Start && a.ScriptAddress < c.End);
    }
    public static IReadOnlyList<NavigationDirection> ConfirmFacings(GameNavigationCatalog.Region region, NavigationPoint point) =>
        point.X / 256 == region.Left && point.Y / 256 == region.Top
            // Opcode24 writes facing into Local3; the native script tests
            // Local0 instead. Do not impose a facing restriction it never checks.
            ? [NavigationDirection.North, NavigationDirection.East, NavigationDirection.South, NavigationDirection.West] : [];
}
