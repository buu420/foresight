using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Cardinal four-pixel edges, checked at each native pixel step. The normal
/// game movement routine still owns movement and enforces dynamic actor collisions.</summary>
public sealed class FieldNavigationGraph(FieldMapSnapshot map) : INavigationGraph
{
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
        for (var done = 0; done < distance;)
        {
            done = Math.Min(distance, done + 16);
            if (!TryPosition(from.X + dx * done, from.Y + dy * done, next.Layer, out next)) return false;
        }
        return distance > 0;
    }
}
