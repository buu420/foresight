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
        DreamscapePassage(scene, region) || DreamscapeEncounter(scene, region);
    public static bool DreamscapePassage(int scene, GameNavigationCatalog.Region region) =>
        scene == 645 && region is { Actor: 8, Kind: "Terrain", Left: 41, Top: 57, Right: 43, Bottom: 57 };
    public static bool DreamscapeEncounter(int scene, GameNavigationCatalog.Region region) =>
        scene == 645 && region is { Actor: 8, Kind: "Encounter", Value: 49280, Left: 35, Top: 51, Right: 46, Bottom: 51 };
    public static bool RegionAvailable(int scene, GameNavigationCatalog.Region region, FieldMapSnapshot map, FieldStoryState? story)
    {
        // The hidden sprite's battle call is not a visible enemy destination.
        // The same local passage row owns the opening and onward walking legs.
        if (DreamscapeEncounter(scene, region)) return false;
        if (!DreamscapePassage(scene, region)) return FrozenCliffsTargets.RegionAvailable(scene, region, story);
        return story?.Local(20) is 0 or 1 && DreamscapeCopyApplied(map) is not null && region.Guards.All(g =>
            g is { Source: "Local", Index: 20, Operation: 0, Value: 0, Expected: true } || g.Allows(story));
    }
    public static bool DreamscapeOpened(int scene, FieldMapSnapshot map, FieldStoryState? story) =>
        scene == 645 && story?.Local(20) == 1 && DreamscapeCopyApplied(map) == true;
    public static bool DreamscapeOpening(int scene, FieldMapSnapshot map, FieldStoryState? story) =>
        scene == 645 && story?.Local(20) == 1 && DreamscapeCopyApplied(map) == false;
    private static bool? DreamscapeCopyApplied(FieldMapSnapshot map)
    {
        if (map.Width != 64 || map.Height != 64 || new[] { map.CollisionShapes, map.TerrainFlags, map.CollisionLayers }
            .Any(p => p.Length != 4096)) return null;
        // Atel0645:019B E5 03 20 05 22 29 36 3B. Compare only observed
        // planes; Local20 is set BEFORE E8 and E5 yield and queue this copy.
        foreach (var plane in new[] { map.CollisionShapes, map.TerrainFlags, map.CollisionLayers })
        for (var y = 0; y < 3; y++)
        for (var x = 0; x < 3; x++)
            if (plane[(54 + y) * 64 + 41 + x] != plane[(32 + y) * 64 + 3 + x]) return false;
        return true;
    }
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
