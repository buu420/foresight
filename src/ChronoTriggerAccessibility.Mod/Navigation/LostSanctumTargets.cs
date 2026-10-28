using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Installed Atel0538/0556 characters and the unfinished bridge.
/// All movement coordinates still come from live actors and collision planes.</summary>
internal static class LostSanctumTargets
{
    public static bool NarrowCave(int scene) => scene is 578 or 581;
    public static bool NarrowCave(string scene) => scene.EndsWith(":578", StringComparison.Ordinal) ||
        scene.EndsWith(":581", StringComparison.Ordinal);
    public static string? ActorLabel(int scene, FieldActorSnapshot actor, FieldStoryState? state) =>
        (scene, actor.Index, actor.ClassTag, actor.VisualIndex) switch
        {
            (538, 9, 5 or 6, 288) or (556, 8, 5 or 6, 289) =>
                state?.Extra(0x15) is { } n && (n & 0x80) != 0 ? "Nu Master" : "Nu",
            (556, 9, 4, 76) => "Reptite bridge builder",
            _ => null,
        };

    // The captured 0556 bridge has a native standing row at fineY2559;
    // rounding an actor's live foot to a64-unit lattice loses that row.
    public static bool PreciseRow(int scene, FieldActorSnapshot actor) =>
        (scene, actor.Index, actor.ClassTag, actor.VisualIndex) is
            (556, 8, 5 or 6, 289) or (556, 9, 4, 76);

    public static IEnumerable<NavigationPoint> PassagePoints(int scene, GameNavigationCatalog.Region region,
        FieldNavigationGraph graph)
    {
        if (scene != 578 || region.Kind != "Warp" || region.Destination is not (579 or 580) ||
            (region.Left, region.Top, region.Right, region.Bottom) != (0, 2, 4, 9)) yield break;
        // The installed floor and logged poses need the last pixel at2559.
        // A tile centre can be valid in isolation while no native body step
        // can stand there. Every proposed point still passes the live graph.
        foreach (var y in Enumerable.Range(region.Top, region.Bottom - region.Top + 1))
        foreach (var x in Enumerable.Range(region.Left, region.Right - region.Left + 1))
        foreach (var oy in new[] { 0, 64, 112, 128, 192, 240, 255 })
        foreach (var ox in new[] { 0, 64, 112, 128, 192, 240, 255 })
        for (var layer = 1; layer <= 3; layer++)
            if (graph.TryPosition(x * 256 + ox, y * 256 + oy, layer, out var point)) yield return point;
    }

    // Atel0556 actor0 opens the completed span only on E1A&40. The visible
    // builder and Nu do not make that unfinished floor a traversable exit.
    public static string? ExitRestriction(int scene, int exit, FieldStoryState? state) =>
        scene == 556 && exit == 1 && state?.Extra(0x1A) is { } n && (n & 0x40) == 0
            ? "The bridge is not complete." : null;

    public static int? DoorExit(int scene, FieldActorSnapshot actor) => actor.ClassTag != 7 ? null :
        (scene, actor.Index) switch { (545, >= 9 and <= 11) => actor.Index - 7, (554, 9) => 1, _ => null };

    public static string? DoorLabel(int scene, FieldActorSnapshot actor) => DoorExit(scene, actor) is not null
        ? scene == 545 ? "Door " + (char)('A' + actor.Index - 9) : "Door" : null;
}
