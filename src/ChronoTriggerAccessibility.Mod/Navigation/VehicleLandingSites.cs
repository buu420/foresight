using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Legal landing points that are walk-connected to an entrance. The search
/// walks the native collision lattice outward from the entrance's own contact points,
/// so a site is never on the far side of a wall or across water from the door.</summary>
public static class VehicleLandingSites
{
    public const int MaximumSites = 16;

    public static IReadOnlyList<NavigationPoint> Near(IReadOnlyList<NavigationPoint> entranceContacts,
        WorldNavigationGraph walking, Func<NavigationPoint, bool> canLand, NavigationPoint? latticeOrigin = null)
    {
        var phaseX = (latticeOrigin?.X ?? 0) % WorldNavigationGraph.Step;
        var phaseY = (latticeOrigin?.Y ?? 0) % WorldNavigationGraph.Step;
        // Each captured entrance contact represents one native eight-pixel chip.
        // Choose the point in that same chip on the vehicle's attainable lattice.
        var start = entranceContacts.Where(p => p.Layer == 1 && p.X % 16 == 0 && p.Y % 16 == 0)
            .Select(p => p with { X = p.X / WorldNavigationGraph.Step * WorldNavigationGraph.Step + phaseX,
                Y = p.Y / WorldNavigationGraph.Step * WorldNavigationGraph.Step + phaseY })
            .Where(walking.CanStand).Distinct().ToArray();
        var found = new List<NavigationPoint>();
        if (start.Length == 0) return found;
        var visited = new HashSet<NavigationPoint>();
        var queue = new Queue<NavigationPoint>();
        foreach (var point in start) { visited.Add(point); queue.Enqueue(point); }
        while (queue.TryDequeue(out var current))
        {
            if (canLand(current)) { found.Add(current); if (found.Count >= MaximumSites) break; }
            foreach (var (dx, dy) in new[] { (0, -WorldNavigationGraph.Step), (0, WorldNavigationGraph.Step),
                (-WorldNavigationGraph.Step, 0), (WorldNavigationGraph.Step, 0) })
            {
                var next = current with { X = current.X + dx, Y = current.Y + dy };
                if (!walking.CanStand(next) || !visited.Add(next)) continue;
                queue.Enqueue(next);
            }
        }
        return found;
    }

    public static Func<NavigationPoint, bool> Predicate(VehicleKind kind, int world, byte[] map, byte[] properties,
        WorldPixelPoint? otherVehicle, VehicleContactShape? movingShape = null, VehicleContactShape? parkedShape = null) =>
        point => VehicleLandingRules.CanLand(kind, world, map, properties, point, otherVehicle, movingShape, parkedShape);
}
