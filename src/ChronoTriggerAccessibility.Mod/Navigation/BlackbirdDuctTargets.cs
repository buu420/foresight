using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>The Blackbird cell's air duct (scene371, Atel_0269) and the ventilation shaft
/// it opens into (scene376, Atel_0274, Map169). The shaft is one-tile ducts: its dead
/// ends are native exit cells that drop into the rooms below, and its junctions carry
/// grates. Openings name the rooms their native records lead to; grates are named by place.</summary>
public static class BlackbirdDuctTargets
{
    public const int Cell = 371;
    public const int Shaft = 376;

    public const string GrateInstruction = "Stand on it and press Confirm to look into the room below. Press Confirm again to come back.";
    public const string GrateArrival = "Press Confirm to look down through the grate, and Confirm again to return to the shaft.";

    // The rooms whose actor0 loops while AF bit 80 is set (the shaft's fn3 sets it): E3 00,
    // then opcode 31 (Confirm) and E0 back to the grate it was opened from.
    private static bool View(int scene) => BlackbirdGrateViewCapture.IsViewRoom(scene);

    /// <summary>Atel_0274 actors 8..36 each compare 22 00 08 09 (the leader's tile) with one
    /// tile, play E8 82 on arriving there, and only after opcode 31 (166360 tests the Confirm
    /// latch engine+BC1 that 189110 sets) run actor0 fn3 (E3 00) and E0 into a room. That
    /// room waits for Confirm again and returns to the same grate. Walking over a grate never
    /// moves the party, so a grate is neither a passage nor a stop on a route.</summary>
    public static bool IsGrate(int scene, GameNavigationCatalog.Region region) =>
        scene == Shaft && region.Kind == "Warp" && region.Actor is >= 8 and <= 36 &&
        region.Left == region.Right && region.Top == region.Bottom && View(region.Destination);

    /// <summary>A grate looks like every other grate until its view opens, so it is named by its
    /// place: numbered from the top of the shaft, then from the left, over the grates whose tile
    /// can be stood on (actor25's (54,33) cannot). BlackbirdGrateViewRuntime names the room
    /// when the native view actually shows it.</summary>
    public static string GrateLabel(int scene, GameNavigationCatalog.Region grate, FieldMapSnapshot map)
    {
        var grates = (GameNavigationCatalog.ForScene(scene)?.Regions ?? [])
            .Where(r => IsGrate(scene, r) && Standable(map, r.Left, r.Top))
            .OrderBy(r => r.Top).ThenBy(r => r.Left).Select(r => r.Id).Distinct().ToList();
        var number = grates.IndexOf(grate.Id);
        return number >= 0 ? $"Viewing grate {number + 1}" : "Viewing grate";
    }

    /// <summary>The shaft's dead ends are MapJump records into the rooms below. Two that reach
    /// the same room are told apart by their height in the shaft.</summary>
    public static string? ExitLabel(int scene, int exit, FieldMapSnapshot map)
    {
        if (scene != Shaft || map.ExitDestinations is not { } destinations ||
            !destinations.TryGetValue(exit, out var destination) || RoomName(destination) is not { } room) return null;
        if (destination == Cell) return $"Down to {room}, the cell";
        var shared = destinations.Where(p => p.Value == destination).Select(p => (Id: p.Key, Row: TopRow(map, p.Key)))
            .Where(p => p.Row >= 0).OrderBy(p => p.Row).ThenBy(p => p.Id).ToList();
        var index = shared.FindIndex(p => p.Id == exit);
        if (shared.Count < 2 || index < 0) return $"Down to {Article(room)}";
        var place = shared.Count == 2 && shared[0].Row != shared[1].Row ? index == 0 ? "upper" : "lower" : $"number {index + 1}";
        return $"Down to {Article(room)}, {place} opening";
    }

    /// <summary>Atel_0269 actor10 (25,7) is the touch that has the party notice the duct
    /// (mest3 057..060) while AF20 is set and B340 clear, and sets B340. Actor11 (22,6) then
    /// runs the climb on Confirm and E0 enters the shaft at (54,39). Only one is live at once.</summary>
    public static string? MarkerLabel(int scene, int actor) => (scene, actor) switch
    {
        (Cell, 10) => "Air duct",
        (Cell, 11) => "Air duct entrance",
        _ => null,
    };

    public static string? ArrivalInstruction(int scene, int actor) =>
        (scene, actor) == (Cell, 11) ? "Press Confirm to climb into the air duct." : null;

    /// <summary>Atel_0274 actor6 (54,37) is a touch at the end of the stub beyond exit5 (54,38).
    /// Both return to the cell at (22,4); while the live grid holds that exit cell it is the one
    /// opening a route can reach, so the touch is not a second row for the same opening.</summary>
    public static bool ConcealedLandmark(int scene, int actor, FieldMapSnapshot map) =>
        scene == Shaft && actor == 6 && map.ExitDestinations?.TryGetValue(5, out var destination) == true &&
        destination == Cell && TopRow(map, 5) >= 0;

    private static string? RoomName(int scene) => GameNavigationCatalog.AreaName(scene) is { } name
        ? name.StartsWith("Blackbird, ", StringComparison.Ordinal) ? name["Blackbird, ".Length..] : name : null;

    private static string Article(string room) => room.StartsWith("Room ", StringComparison.Ordinal) ? room : "the " + room;

    // FieldNavigationGraph.TryPosition at the tile centre, the point RegionPoints offers.
    private static bool Standable(FieldMapSnapshot map, int tileX, int tileY)
    {
        if (tileX < 0 || tileY < 0 || tileX >= map.Width || tileY >= map.Height) return false;
        var index = tileY * map.Width + tileX;
        var region = FieldCollisionRules.Region(map.CollisionShapes[index], map.CollisionLayers[index],
            tileX * 256 + 128, tileY * 256 + 128);
        return Enumerable.Range(1, 3).Any(layer => FieldCollisionRules.TryEnter(layer, region, out _));
    }

    private static int TopRow(FieldMapSnapshot map, int exit)
    {
        for (var index = 0; index < map.ExitCells.Length; index++)
            if (map.ExitCells[index] == exit) return index / map.ExitWidth;
        return -1;
    }
}
