using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>A flight of stairs drawn as one scene the player re-enters.
///
/// Guardia Castle's towers, and every stairwell built the same way, hold several landings
/// in a single map that share no floor at all. Climbing is an exit whose destination is
/// the scene it already is, arriving on the next landing. A goal on another landing is
/// perfectly reachable in play and completely unreachable on the map, which is why asking
/// for the chest on the middle landing of scene 468 answered that there was no route.
///
/// So when the ordinary search fails, look for a flight whose arrival does reach the goal
/// and route to that flight instead, reporting it as an intermediate leg. The player takes
/// the stairs, the scene rebuilds, and the next plan continues from the new landing. A
/// goal that no flight reaches still gets no route, because there is none.</summary>
public sealed class FieldLandingGraph(INavigationGraph inner, FieldMapSnapshot map,
    GameNavigationCatalog.Scene scene) : IStagedNavigationGraph
{
    private readonly FieldNavigationGraph positions = new(map);

    public static INavigationGraph Create(INavigationGraph inner, FieldMapSnapshot map,
        GameNavigationCatalog.Scene? scene) =>
        scene is not null && scene.Exits.Any(e => IsSameScene(e, map, scene.Id))
            ? new FieldLandingGraph(inner, map, scene) : inner;

    public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point) => inner.Neighbours(point);
    public bool IsTerminal(NavigationPoint point) => inner.IsTerminal(point);
    public bool IsSameTerminal(NavigationPoint point, NavigationPoint goal) => inner.IsSameTerminal(point, goal);

    public NavigationSearchResult FindStage(NavigationPoint start, IReadOnlyList<NavigationPoint> goals,
        int maximumVisited)
    {
        // Anything the wrapped graph can stage for itself takes precedence: opening a
        // passage on this floor is always better than sending the player up a flight.
        if (inner is IStagedNavigationGraph staged)
        {
            var opened = staged.FindStage(start, goals, maximumVisited);
            if (opened.Route is not null) return opened;
        }
        foreach (var exit in scene.Exits.Where(e => IsSameScene(e, map, scene.Id))
                     .OrderBy(e => Distance(start, Arrival(e))))
        {
            if (Arrival(exit) is not { } arrival) continue;
            // The flight is only worth taking if the landing it lands on can reach the
            // goal. Nothing here assumes which way a flight runs.
            if (NavigationPathfinder.Search(inner, arrival, goals, maximumVisited).Route is null) continue;
            var boarding = Cells(exit.Id);
            if (boarding.Count == 0) continue;
            var approach = NavigationPathfinder.Search(inner, start, boarding, maximumVisited);
            if (approach.Route is not null)
                return approach.IntermediateId is null ? approach with { IntermediateId = $"exit:{exit.Id}" } : approach;
        }
        return new(null, false);
    }

    // Scripts can change a loaded exit's destination. A stale catalogue link must
    // not promise another landing when the live doorway now leaves this scene.
    private static bool IsSameScene(GameNavigationCatalog.Exit exit, FieldMapSnapshot map, int sceneId) =>
        (map.ExitDestinations?.TryGetValue(exit.Id, out var current) == true ? current : exit.Destination) == sceneId;

    /// <summary>Where this flight puts the player. The catalogue's exit X and Y are the
    /// arrival tile in the destination scene, not the doorway in this one.</summary>
    private NavigationPoint? Arrival(GameNavigationCatalog.Exit exit)
    {
        var x = exit.X * 256 + 128; var y = exit.Y * 256 + 128;
        for (var layer = 1; layer <= 3; layer++)
            if (positions.TryPosition(x, y, layer, out var point)) return point;
        return null;
    }

    /// <summary>Standing room on the flight's own doorway cells, which is where the
    /// player has to end up for the game to take them up.</summary>
    private IReadOnlyList<NavigationPoint> Cells(int id)
    {
        var found = new List<NavigationPoint>();
        for (var y = 0; y < map.ExitHeight; y++)
        for (var x = 0; x < map.ExitWidth; x++)
        {
            if (map.ExitCells[y * map.ExitWidth + x] != id) continue;
            for (var layer = 1; layer <= 3; layer++)
                if (positions.TryPosition(x * 256 + 128, y * 256 + 128, layer, out var point)) found.Add(point);
        }
        return found.Distinct().Take(64).ToArray();
    }

    private static long Distance(NavigationPoint from, NavigationPoint? to) =>
        to is { } point ? Math.Abs((long)point.X - from.X) + Math.Abs((long)point.Y - from.Y) : long.MaxValue;
}
