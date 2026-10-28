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
