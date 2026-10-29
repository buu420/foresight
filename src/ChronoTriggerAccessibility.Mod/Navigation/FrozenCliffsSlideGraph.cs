using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Coarse, directed waypoints for the native southward slide. Each
/// sample retains its ordinary input heading; Up+Right moves down and right.
/// Samples conservatively sweep the body and actors, never create uphill motion,
/// and stop at current script terminals. The game still performs every frame.</summary>
internal sealed class FrozenCliffsSlideGraph(INavigationGraph inner, FieldMapSnapshot map,
    Func<IReadOnlyList<NavigationPoint>, FieldNavigationGraph> bodyFactory,
    IReadOnlyList<NavigationTarget> caves,
    IReadOnlyList<NavigationPoint>? goals = null, bool withoutCreatures = false, bool allowReturn = true) : IStagedNavigationGraph
{
    private readonly FieldNavigationGraph body = Body(bodyFactory, goals ?? [], withoutCreatures);
    private static readonly NavigationDirection[] inputs = [NavigationDirection.North, NavigationDirection.NorthWest,
        NavigationDirection.NorthEast, NavigationDirection.West, NavigationDirection.East,
        NavigationDirection.SouthWest, NavigationDirection.SouthEast, NavigationDirection.South];
    private static readonly (int X, int Y)[] bodyCorners = [(-112, 0), (112, 0), (-112, -112), (112, -112)];

    private static FieldNavigationGraph Body(Func<IReadOnlyList<NavigationPoint>, FieldNavigationGraph> factory,
        IReadOnlyList<NavigationPoint> selected, bool without) =>
        without ? factory(selected).WithoutCreatures as FieldNavigationGraph ?? factory(selected) : factory(selected);

    public INavigationGraph ForGoals(IReadOnlyList<NavigationPoint> selected) =>
        new FrozenCliffsSlideGraph(inner.ForGoals(selected), map, bodyFactory, caves, selected, withoutCreatures, allowReturn);
    public INavigationGraph? WithoutCreatures => inner.WithoutCreatures is { } open
        ? new FrozenCliffsSlideGraph(open, map, bodyFactory, caves, goals, true, allowReturn) : null;
    public bool IsTerminal(NavigationPoint point) => inner.IsTerminal(point);
    public bool IsSameTerminal(NavigationPoint point, NavigationPoint goal) => inner.IsSameTerminal(point, goal);
    public int? BlockingActor(NavigationPoint from, NavigationPoint to) => inner.BlockingActor(from, to);
    public NavigationSearchResult FindStage(NavigationPoint start, IReadOnlyList<NavigationPoint> selected, int maximumVisited)
    {
        var stage = inner is IStagedNavigationGraph staged ? staged.FindStage(start, selected, maximumVisited) : new(null, false);
        if (stage.Route is not null || stage.LimitReached || !allowReturn || selected.Count == 0 ||
            selected.Any(p => !caves.Any(c => c.ApproachPoints.Contains(p)))) return stage;
        // Atel0603's eastern exit enters 0604 at (16,1), facing down.
        // A return is offered only for these native cave goals, after a direct
        // route has failed and the same current terrain proves the goal from above.
        var direct = new FrozenCliffsSlideGraph(inner, map, bodyFactory, caves, selected, withoutCreatures, false);
        var fresh = NavigationPathfinder.Search(direct, new(4224, 511, 1), selected, maximumVisited);
        if (fresh.Route is null) return new(null, fresh.LimitReached);
        foreach (var cave in caves.Where(c => c.Id.StartsWith("script-region:9:602:", StringComparison.Ordinal))
            .OrderBy(c => Math.Abs((long)c.Position.X - start.X) + Math.Abs((long)c.Position.Y - start.Y)))
        {
            var route = NavigationPathfinder.Search(direct, start, cave.ApproachPoints, maximumVisited);
            if (route.Route is null) continue;
            return route with
                {
                    IntermediateId = cave.Id, IntermediateLabel = cave.Label,
                    IntermediateDirection = cave.ContactDirection,
                    IntermediateInstruction = "Approach the selected cave from above. Use this return cave to return to the entrance, then follow Story Events back to the top.",
                };
        }
        return new(null, false);
    }

    public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point)
    {
        var seen = new HashSet<NavigationPoint>();
        if (Floor(point) == 0)
            foreach (var next in inner.Neighbours(point))
                if (!CrossesSlide(point, next) && seen.Add(next)) yield return next;
        foreach (var input in inputs)
            if (Advance(point, input, out var next, out var crossed) && (Floor(point) != 0 || crossed) && seen.Add(next))
                yield return next;
    }

    public NavigationDirection InputDirection(NavigationPoint from, NavigationPoint to)
    {
        if (Floor(from) == 0 && !CrossesSlide(from, to)) return inner.InputDirection(from, to);
        foreach (var input in inputs)
            if (Advance(from, input, out var next, out _) && next == to) return input;
        return inner.InputDirection(from, to);
    }

    private bool Advance(NavigationPoint from, NavigationDirection input, out NavigationPoint next, out bool crossed)
    {
        next = from; crossed = false;
        var wallContact = false;
        var steps = 0;
        for (; steps < 4; steps++)
        {
            var force = Floor(next); crossed |= force != 0;
            var (dx, dy) = Delta(input, force);
            if ((dx, dy) == (0, 0)) break;
            if (!ClearStep(next, dx, dy, out var moved))
            {
                // 175E90 can reject the southern axis while retaining ordinary
                // sideways input at a solid wall. Diagonal-wall nudges and actor
                // rejections do not establish this proof.
                if (force != 32 || dx == 0 || dy <= 0) break;
                if ((SolidBlock(next.X + dx - 112, next.Y + dy, next.Layer) ||
                     SolidBlock(next.X + dx + 112, next.Y + dy, next.Layer)) && ClearStep(next, dx, 0, out moved)) { }
                // 178C50 shape1E sets vertical movement to16 on entry. At
                // this narrow chute the native axes can clear in sequence.
                else if (Climb(next.X + dx, next.Y + dy) && AxisStep(next, dx, 16, out moved)) { }
                else break;
                wallContact = true;
            }
            next = moved;
            if (goals is not null)
                foreach (var goal in goals)
                    if (goal.Layer == next.Layer && Math.Abs((long)goal.X - next.X) <= 24 &&
                        Math.Abs((long)goal.Y - next.Y) <= 24 && ClearStep(next, goal.X - next.X, goal.Y - next.Y, out var at) && at == goal)
                    { next = goal; return next != from; }
            if (inner.IsTerminal(next)) return next != from;
        }
        if (next == from) return false;
        // Keep the search finite in open snow. Subpixel/native-frame endpoints
        // remain unrounded at walls, terminals and changes of floor/layer.
        // A rounded waypoint is accepted only through the same body sweep.
        if (steps == 4)
        {
            var x = (next.X + 16) / 32 * 32; var y = (next.Y + 16) / 32 * 32;
            if (Floor(from) == 32) y = Math.Max(from.Y, y);
            if (ClearStep(next, x - next.X, y - next.Y, out var rounded) && rounded.Layer == next.Layer &&
                Floor(rounded) == Floor(next) && !inner.IsTerminal(rounded)) next = rounded;
            else if (wallContact && ClearStep(next, x - next.X, 0, out rounded) && rounded.Layer == next.Layer &&
                Floor(rounded) == Floor(next) && !inner.IsTerminal(rounded)) next = rounded;
        }
        return true;
    }

    private bool ClearStep(NavigationPoint from, int dx, int dy, out NavigationPoint next)
    {
        next = from;
        if (dx == 0 && dy == 0) return true;
        if (!body.CanTraverseFloorStep(from, dx, dy)) return false;
        return Envelope(from, dx, dy) && body.TryPosition(from.X + dx, from.Y + dy, from.Layer, out next);
    }

    private bool AxisStep(NavigationPoint from, int dx, int dy, out NavigationPoint next)
    {
        next = from;
        var horizontal = new NavigationPoint(from.X + dx, from.Y, from.Layer);
        return body.CanTraverseFloorStep(from, dx, dy) && Envelope(from, dx, 0) && Envelope(horizontal, 0, dy) &&
            body.TryPosition(from.X + dx, from.Y + dy, from.Layer, out next);
    }

    private bool Envelope(NavigationPoint from, int dx, int dy)
    {
        var distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (distance == 0) return true;
        for (var amount = Math.Min(8, distance); ; amount = Math.Min(distance, amount + 8))
        {
            var x = from.X + dx * amount / distance; var y = from.Y + dy * amount / distance;
            // A full body envelope is conservative for the diagonal mover's
            // leading-corner tests. No claimed corner-slide on moving terrain.
            foreach (var (ox, oy) in bodyCorners)
                if (!body.TryPosition(x + ox, y + oy, from.Layer, out _)) return false;
            if (amount == distance) break;
        }
        return true;
    }

    private bool Climb(int x, int y) => x >= 0 && y >= 0 && x / 256 < map.Width && y / 256 < map.Height &&
        map.CollisionShapes[y / 256 * map.Width + x / 256] >> 2 == 0x1E;
    private bool SolidBlock(int x, int y, int layer) => x >= 0 && y >= 0 && x / 256 < map.Width && y / 256 < map.Height &&
        map.CollisionShapes[y / 256 * map.Width + x / 256] >> 2 is 0 or 1 && !body.TryPosition(x, y, layer, out _);

    private int Floor(NavigationPoint point)
    {
        if (point.X < 0 || point.Y < 0 || point.X / 256 >= map.Width || point.Y / 256 >= map.Height) return 0;
        return (map.TerrainFlags[point.Y / 256 * map.Width + point.X / 256] & 15) == 13 ? 32 : 0;
    }

    private bool CrossesSlide(NavigationPoint from, NavigationPoint to)
    {
        var count = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));
        for (var i = 0; i <= count; i += 8)
            if (Floor(new(from.X + (to.X - from.X) * i / Math.Max(1, count),
                    from.Y + (to.Y - from.Y) * i / Math.Max(1, count), from.Layer)) != 0) return true;
        return Floor(to) != 0;
    }

    private static (int X, int Y) Delta(NavigationDirection input, int force)
    {
        var bits = FieldNavigationRuntime.DirectionBits(input);
        var x = (bits & 0x100) != 0 ? 32 : (bits & 0x200) != 0 ? -32 : 0;
        var y = (bits & 0x400) != 0 ? 32 : (bits & 0x800) != 0 ? -32 : 0;
        return x != 0 && y != 0 ? ((int)(x / 1.39f), (int)(force + y / 1.39f)) : (x, force + y);
    }
}
