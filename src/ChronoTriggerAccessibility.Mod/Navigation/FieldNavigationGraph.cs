using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Cardinal edges checked against native terrain and captured actor
/// contacts. Narrow passages retain pixel-sized boundary steps. A held direction
/// that meets a diagonal wall follows the native one-corner slide. The normal game
/// movement routine still performs movement.</summary>
public sealed class FieldNavigationGraph(FieldMapSnapshot map, FieldActorCollisionRules? actors = null,
    IReadOnlyList<(int Actor, IReadOnlyList<NavigationPoint> Goals)>? touchGoals = null,
    IReadOnlyList<NavigationPoint>? selectedGoals = null) : INavigationGraph
{
    private readonly HashSet<int> contactDestinations = selectedGoals is { Count: > 0 } && touchGoals is not null
        ? touchGoals.Where(t => selectedGoals!.Any(t.Goals.Contains)).Select(t => t.Actor).ToHashSet() : [];
    private readonly int[] goalXs = selectedGoals?.Select(g => g.X).Distinct().Order().ToArray() ?? [];
    private readonly int[] goalYs = selectedGoals?.Select(g => g.Y).Distinct().Order().ToArray() ?? [];

    public INavigationGraph ForGoals(IReadOnlyList<NavigationPoint> goals) =>
        new FieldNavigationGraph(map, actors, touchGoals, goals);

    public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point)
    {
        if (point.X < 0 || point.Y < 0) yield break;
        foreach (var candidate in Candidates(point))
        {
            NavigationPoint? reached = null;
            var whole = TryTraverse(point, candidate, out var next);
            if (whole) reached = next;
            else
            {
                // A seven-pixel body in an eight-pixel corridor can occupy only
                // the last pixel of its tile. A four-pixel grid has no node there.
                // Stop at the last traversable pixel boundary instead of throwing
                // away all progress toward a blocked coarse node. Keep coarse edges
                // in open space so map size does not multiply the search budget.
                var horizontal = candidate.X != point.X;
                var origin = horizontal ? point.X : point.Y;
                var end = horizontal ? candidate.X : candidate.Y;
                var direction = Math.Sign(end - origin);
                var pixel = direction < 0 ? (origin - 1) / 16 * 16 : (origin / 16 + 1) * 16;
                for (; direction * (end - pixel) > 0; pixel += direction * 16)
                {
                    var partial = horizontal ? point with { X = pixel } : point with { Y = pixel };
                    if (!TryTraverse(point, partial, out next)) break;
                    reached = next;
                }
            }
            if (reached is { } boundary) yield return boundary;
            // A blocked leading corner need not stop the held direction: the
            // native movers slide the body sideways along a diagonal wall.
            if (!whole && Slide(point, candidate) is { } slide) yield return slide;

            // Join precise native interaction/exit coordinates even when the
            // whole coarse edge is clear. One nearest alignment per direction
            // keeps the neighbour count bounded and retains further alignments.
            var horizontalGoal = candidate.X != point.X;
            var start = horizontalGoal ? point.X : point.Y;
            var finish = horizontalGoal ? candidate.X : candidate.Y;
            if (Alignment(start, finish, horizontalGoal ? goalXs : goalYs) is not { } alignment) continue;
            var aligned = horizontalGoal ? point with { X = alignment } : point with { Y = alignment };
            if (TryTraverse(point, aligned, out next) && next != reached) yield return next;
        }
    }

    private static int? Alignment(int start, int finish, int[] coordinates)
    {
        var index = Array.BinarySearch(coordinates, start);
        index = finish > start ? (index >= 0 ? index + 1 : ~index) : (index >= 0 ? index - 1 : ~index - 1);
        if (index < 0 || index >= coordinates.Length) return null;
        var value = coordinates[index];
        return finish > start ? value < finish ? value : null : value > finish ? value : null;
    }

    private static NavigationPoint[] Candidates(NavigationPoint point) =>
    [
        point with { Y = point.Y == 0 ? -1 : (point.Y - 1) / 64 * 64 },
        point with { X = (point.X / 64 + 1) * 64 },
        point with { Y = (point.Y / 64 + 1) * 64 },
        point with { X = point.X == 0 ? -1 : (point.X - 1) / 64 * 64 },
    ];

    /// <summary>The held command that produces an edge. A slide drifts sideways, so
    /// its displacement does not name its input. When two commands slide to the same
    /// point, the larger commanded advance wins, then the horizontal command.</summary>
    public NavigationDirection InputDirection(NavigationPoint from, NavigationPoint to)
    {
        var input = NavigationDirection.None;
        var advance = 0;
        if (from.X >= 0 && from.Y >= 0 && from.X != to.X && from.Y != to.Y)
            foreach (var candidate in Candidates(from))
            {
                var horizontal = candidate.X != from.X;
                if (TryTraverse(from, candidate, out _) || Slide(from, candidate) != to) continue;
                var distance = Math.Abs(horizontal ? to.X - from.X : to.Y - from.Y);
                if (distance < advance || distance == advance && !horizontal) continue;
                advance = distance;
                input = horizontal ? candidate.X < from.X ? NavigationDirection.West : NavigationDirection.East
                    : candidate.Y < from.Y ? NavigationDirection.North : NavigationDirection.South;
            }
        if (input != NavigationDirection.None) return input;
        return to.X < from.X ? NavigationDirection.West : to.X > from.X ? NavigationDirection.East :
            to.Y < from.Y ? NavigationDirection.North : to.Y > from.Y ? NavigationDirection.South : NavigationDirection.None;
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

    /// <summary>Hold the command toward a coarse candidate for native walking frames.
    /// The edge ends at the furthest slid frame with forward progress and no larger
    /// sideways drift, every frame within a pixel of the straight segment that the
    /// controller follows. Frames stop at the candidate line, a refusal, a new exit
    /// tile or a reversed drift.</summary>
    private NavigationPoint? Slide(NavigationPoint from, NavigationPoint candidate)
    {
        var dx = Math.Sign(candidate.X - from.X);
        var dy = Math.Sign(candidate.Y - from.Y);
        var exit = ExitAt(from.X, from.Y);
        Span<NavigationPoint> path = stackalloc NavigationPoint[16];
        var frames = 0;
        var drift = 0;
        for (var current = from; frames < path.Length && TryFrame(current, dx, dy, out var next);)
        {
            var side = dx != 0 ? next.Y - current.Y : next.X - current.X;
            if (side != 0 && drift != 0 && Math.Sign(side) != Math.Sign(drift)) break;
            drift += side;
            path[frames++] = current = next;
            if (ExitAt(next.X, next.Y) is var id && id >= 0 && id != exit) break;
            if ((dx != 0 ? dx * (next.X - candidate.X) : dy * (next.Y - candidate.Y)) >= 0) break;
        }
        for (var end = frames - 1; end >= 0; end--)
        {
            var to = path[end];
            var along = Math.Abs(dx != 0 ? to.X - from.X : to.Y - from.Y);
            var across = Math.Abs(dx != 0 ? to.Y - from.Y : to.X - from.X);
            if (along != 0 && across != 0 && across <= along && Follows(from, path[..end], to)) return to;
        }
        return null;
    }

    private static bool Follows(NavigationPoint from, ReadOnlySpan<NavigationPoint> frames, NavigationPoint to)
    {
        long dx = to.X - from.X, dy = to.Y - from.Y;
        foreach (var frame in frames)
        {
            var cross = dx * (frame.Y - from.Y) - dy * (frame.X - from.X);
            if (cross * cross > 16L * 16 * (dx * dx + dy * dy)) return false;
        }
        return true;
    }

    /// <summary>One held walking frame of 16 units. 178980 tests actors at the
    /// unslid probe before any nudge; 178FF0 then commits the foot with the stored
    /// layer or refuses the whole frame.</summary>
    private bool TryFrame(NavigationPoint from, int dx, int dy, out NavigationPoint next)
    {
        next = from;
        if (OpposesStrongFloor(from.X, from.Y, dx, dy) ||
            actors?.BlocksMove(from.X, from.Y, from.X + dx * 16, from.Y + dy * 16, contactDestinations) == true ||
            Displacement(from, dx, dy) is not { } move) return false;
        var x = from.X + move.X; var y = from.Y + move.Y;
        if (OpposesStrongFloor(x, y, dx, dy)) return false;
        // A floor push would change the slide; only slides on plain floor are claimed.
        if (move != (dx * 16, dy * 16) && (MovingFloor(from.X, from.Y) || MovingFloor(x, y))) return false;
        return TryPosition(x, y, from.Layer, out next);
    }

    /// <summary>175E90 sends a held frame to 1761C0 (left/right), 176AE0 (down) or
    /// 176810 (up). Each probes the two leading corners of the moved body, 0x70 from
    /// its foot, with the stored layer (175EE0). When exactly one is blocked, the body
    /// is nudged 16 toward the clear side and probed again; when the nudged blocked
    /// corner still fails, the forward step is given up for the nudge alone.</summary>
    private (int X, int Y)? Displacement(NavigationPoint from, int dx, int dy)
    {
        int x = from.X, y = from.Y, layer = from.Layer;
        Probe At(int px, int py) => Corner(px, py, layer);
        if (dx != 0)
        {
            int ahead = x + dx * 128, beside = x + dx * 112;
            var foot = At(ahead, y);
            if (foot == Probe.Clear)
            {
                var head = At(ahead, y - 112);
                if (head == Probe.Clear) return (dx * 16, 0);
                // States 5, 7 and 10: the head corner pushes the body down.
                if (head != Probe.Terrain || At(ahead, y + 16) != Probe.Clear) return null;
                if (At(ahead, y - 96) == Probe.Clear) return (dx * 16, 16);
                return At(beside, y + 16) == Probe.Clear ? (0, 16) : null;
            }
            // States 2, 3 and 8: the foot corner pushes the body up.
            if (foot != Probe.Terrain || At(ahead, y - 112) != Probe.Clear) return null;
            if (At(ahead, y - 16) == Probe.Clear) return At(ahead, y - 128) == Probe.Clear ? (dx * 16, -16) : null;
            return At(beside, y - 128) == Probe.Clear ? (0, -16) : null;
        }
        int row = dy > 0 ? y + 16 : y - 128, still = dy > 0 ? y : y - 112;
        var left = At(x - 112, row);
        var right = At(x + 112, row);
        if (left == Probe.Clear && right == Probe.Clear) return (0, dy * 16);
        // 176810 shortens the head to 0x60 for scene 0x163 inside this box. The scene
        // is not known here, so no slide is claimed there.
        if (dy < 0 && x > 0xC8F && x < 0xD80 && y > 0x14DF && y < 0x1590) return null;
        if (left == Probe.Terrain && right == Probe.Clear)
            return At(x - 96, row) == Probe.Clear
                ? At(x + 128, row) == Probe.Clear ? (16, dy * 16) : null
                : At(x + 128, still) == Probe.Clear ? (16, 0) : null;
        if (left != Probe.Clear || right != Probe.Terrain) return null;
        // 176AE0 adds 0xFFF0 to X, then ADC #0x70 with that carry: X + 0x61.
        return At(x + (dy > 0 ? 97 : 96), row) == Probe.Clear
            ? At(x - 128, row) == Probe.Clear ? (-16, dy * 16) : null
            : At(x - 128, still) == Probe.Clear ? (-16, 0) : null;
    }

    private enum Probe { Clear, Terrain, Unknown }

    // 175EE0 sets bit 2 for a terrain or layer rejection, which the movers slide
    // along. Off the map the native tile index wraps, so no result is claimed there.
    private Probe Corner(int x, int y, int layer) =>
        x < 0 || y < 0 || x / 256 >= map.Width || y / 256 >= map.Height ? Probe.Unknown :
        TryPosition(x, y, layer, out _) ? Probe.Clear : Probe.Terrain;

    private bool MovingFloor(int x, int y) => x >= 0 && y >= 0 && x / 256 < map.Width &&
        y / 256 < map.Height && (map.TerrainFlags[y / 256 * map.Width + x / 256] & 12) != 0;

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
