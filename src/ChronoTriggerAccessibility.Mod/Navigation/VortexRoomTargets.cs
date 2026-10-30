using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Current-room features of the native randomized vortex passages.
/// Names identify their local appearance without disclosing the next room.</summary>
internal static class VortexRoomTargets
{
    public static bool RandomRoom(int scene) => scene is >= 625 and <= 644 or >= 596 and <= 600 or 659 or 660;
    public static string? PassageLabel(int scene) => RandomRoom(scene) ? "Vortex passage" : null;
    public static bool WatcherAvailable(FieldNavigationSnapshot field, GameNavigationCatalog.Region region)
    {
        if (DreamscapeRegion(field.SceneId, region))
            // The catalog follows actor8's request back into actor0fn3.
            // Actor0 owns these coordinate tests; actor8 is a hidden sprite.
            return field.SceneIdCoherent && field.Actors.Any(a => a.Index == 0 && a.ClassTag == 7 &&
                a.IsUsable && !a.IsPartyMember && a.ScriptProcessingEnabled && a.ScriptAddress is >= 0x17E and <= 0x21E);
        if (field.SceneId != 638 || region.Kind != "Warp" || region.Actor != 26) return true;
        // Atel0638 actor0 refreshes Local6/7; actor26 polls those coordinates.
        // Both loops must run. Neither marker is drawn or camera-activated.
        return field.SceneIdCoherent && new[] { 0, 26 }.All(index => field.Actors.Any(a =>
            a.Index == index && a.ClassTag == 7 && a.IsUsable && !a.IsPartyMember && a.ScriptProcessingEnabled));
    }
    private static bool DreamscapeRegion(int scene, GameNavigationCatalog.Region region) =>
        scene == 645 && region.Actor == 8 && (DreamscapePassage(scene, region) ||
            region is { Kind: "Encounter", Value: 49280, Left: 35, Top: 51, Right: 46, Bottom: 51 });
    public static bool DreamscapePassage(int scene, GameNavigationCatalog.Region region) =>
        scene == 645 && region is { Actor: 8, Kind: "Terrain", Left: 41, Top: 57, Right: 43, Bottom: 57 };
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
