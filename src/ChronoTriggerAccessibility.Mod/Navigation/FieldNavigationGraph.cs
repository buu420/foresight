using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Cardinal four-pixel edges checked against native terrain and captured
/// actor contacts. The normal game movement routine still performs movement.</summary>
public sealed class FieldNavigationGraph(FieldMapSnapshot map, FieldActorCollisionRules? actors = null,
    IReadOnlyList<(int Actor, IReadOnlyList<NavigationPoint> Goals)>? touchGoals = null,
    IReadOnlyList<NavigationPoint>? selectedGoals = null) : INavigationGraph
{
    private readonly HashSet<int> contactDestinations = selectedGoals is { Count: > 0 } && touchGoals is not null
        ? touchGoals.Where(t => selectedGoals.Any(t.Goals.Contains)).Select(t => t.Actor).ToHashSet() : [];

    public INavigationGraph ForGoals(IReadOnlyList<NavigationPoint> goals) =>
        touchGoals is { Count: > 0 } ? new FieldNavigationGraph(map, actors, touchGoals, goals) : this;

    public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point)
    {
        if (point.X < 0 || point.Y < 0) yield break;
        var candidates = new[]
        {
            point with { Y = point.Y == 0 ? -1 : (point.Y - 1) / 64 * 64 },
            point with { X = (point.X / 64 + 1) * 64 },
            point with { Y = (point.Y / 64 + 1) * 64 },
            point with { X = point.X == 0 ? -1 : (point.X - 1) / 64 * 64 },
        };
        foreach (var candidate in candidates)
            if (TryTraverse(point, candidate, out var next)) yield return next;
    }

    public bool IsTerminal(NavigationPoint point) => ExitAt(point.X, point.Y) >= 0;
    public bool IsSameTerminal(NavigationPoint point, NavigationPoint goal) =>
        ExitAt(point.X, point.Y) is var id && id >= 0 && id == ExitAt(goal.X, goal.Y);

    public int ExitAt(int x, int y)
    {
        if (x < 0 || y < 0 || x / 256 >= map.ExitWidth || y / 256 >= map.ExitHeight) return -1;
        var id = map.ExitCells[y / 256 * map.ExitWidth + x / 256];
        return id < 128 ? id : -1;
    }

    public bool TryPosition(int x, int y, int layer, out NavigationPoint point)
    {
        point = new(x, y, layer);
        if (x < 0 || y < 0 || x / 256 >= map.Width || y / 256 >= map.Height) return false;
        var index = y / 256 * map.Width + x / 256;
        if (!FieldCollisionRules.TryEnter(layer,
            FieldCollisionRules.Region(map.CollisionShapes[index], map.CollisionLayers[index], x, y), out var nextLayer)) return false;
        point = point with { Layer = nextLayer };
        return true;
    }

    private bool TryTraverse(NavigationPoint from, NavigationPoint target, out NavigationPoint next)
    {
        next = from;
        if (target.X < 0 || target.Y < 0) return false;
        var distance = Math.Abs(target.X - from.X) + Math.Abs(target.Y - from.Y);
        var dx = Math.Sign(target.X - from.X);
        var dy = Math.Sign(target.Y - from.Y);
        if (OpposesStrongFloor(from.X, from.Y, dx, dy)) return false;
        for (var done = 0; done < distance;)
        {
            done = Math.Min(distance, done + 16);
            var x = from.X + dx * done; var y = from.Y + dy * done;
            if (OpposesStrongFloor(x, y, dx, dy)) return false;
            if (actors?.BlocksMove(from.X, from.Y, x, y, contactDestinations) == true) return false;
            // 175E90 dispatches cardinal movement to 175F70/176780 (horizontal)
            // and 176810/176AE0 (vertical). Before committing the foot's layer,
            // 175EE0 checks the two leading corners, seven pixels from the foot.
            // Probe results must not themselves change the physical player layer.
            var probeY = dy < 0 ? y - 112 : y;
            if (dx != 0)
            {
                if (!TryPosition(x + dx * 112, y, next.Layer, out _) ||
                    !TryPosition(x + dx * 112, y - 112, next.Layer, out _)) return false;
            }
            else if (!TryPosition(x - 112, probeY, next.Layer, out _) ||
                     !TryPosition(x + 112, probeY, next.Layer, out _)) return false;
            if (!TryPosition(x, y, next.Layer, out next)) return false;
        }
        return distance > 0;
    }

    private bool OpposesStrongFloor(int x, int y, int dx, int dy)
    {
        if (x < 0 || y < 0 || x / 256 >= map.Width || y / 256 >= map.Height) return false;
        var flags = map.TerrainFlags[y / 256 * map.Width + x / 256];
        // 178FF0 decodes bits 2..3 as floor speed 0/8/16/32 and bits
        // 0..1 as north/south/west/east. Input at 175A94 tops out at 32.
        // Opposing a speed-32 floor cannot make progress even while running.
        // Check both ends: entering it may succeed for one frame, then bounce
        // back. Occupancy alone therefore does not establish a walkable edge.
        return (flags & 12) == 12 && (flags & 3) switch
        {
            0 => dy > 0,
            1 => dy < 0,
            2 => dx > 0,
            _ => dx < 0,
        };
    }
}
