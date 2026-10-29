using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Current-room features of the native randomized vortex passages.
/// Names identify their local appearance without disclosing the next room.</summary>
internal static class VortexRoomTargets
{
    public static bool RandomRoom(int scene) => scene is >= 625 and <= 644 or >= 596 and <= 600 or 659 or 660;
    public static string? PassageLabel(int scene) => RandomRoom(scene) ? "Vortex passage" : null;
    public static bool Door(int scene, FieldActorSnapshot actor) => scene == 660 && actor.Index == 1 && actor.ClassTag == 7 &&
        actor.FineX == 6016 && actor.FineY == 14079;
    public const string DoorLabel = "Conveyor exit door";
    public const string DoorInstruction = "Press Confirm to open the conveyor exit door.";
    public static bool ConcealedPassage(int scene, FieldActorSnapshot actor, FieldStoryState? story) =>
        scene == 660 && actor.Index == 9 && actor.ClassTag == 7 && story?.Local(6) is not 1;

    public static IEnumerable<int[]> MarkerGroups(FieldNavigationSnapshot field)
    {
        // Atel0634 slots27..29 share fn2 at0825. They span the same east
        // opening at X31.5, Y11/12/13+FF; the last centre has no standing room.
        if (!field.SceneIdCoherent || field.SceneId != 634) yield break;
        var markers = field.Actors.Where(a => a.Index is >= 27 and <= 29 && a.ClassTag == 7 && a.IsUsable &&
            a.ScriptCallsEnabled && a.ScriptProcessingEnabled && a.FineX == 8064 &&
            a.FineY == (a.Index - 16) * 256 + 255).Select(a => a.Index).Order().ToArray();
        if (markers.Length >= 2) yield return markers;
    }
}
