using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Bounded look-ahead through the lab's same-room native warps. Only
/// the first real walking leg is returned. E1 owns the reload and landing.</summary>
internal sealed class TemporalLabWarpGraph(INavigationGraph inner, IReadOnlyList<TemporalLabWarpGraph.Entry> pads,
    FieldRegionIndex regions) : IStagedNavigationGraph
{
    internal sealed record Entry(NavigationTarget Target, NavigationPoint Landing);
    public static INavigationGraph Create(INavigationGraph inner, FieldNavigationSnapshot field, FieldMapSnapshot map,
        FieldStoryState? story, IReadOnlyList<NavigationTarget> targets)
    {
        if (!field.SceneIdCoherent || field.SceneId != 621 || map.TransitionPending) return inner;
        var floor = new FieldNavigationGraph(map);
        var entries = new List<Entry>();
        foreach (var actor in field.Actors)
            if (TemporalLabTargets.Pad(field.SceneId, actor, story, field.Actors) is { } pad &&
                targets.FirstOrDefault(t => t.ActorSlot == actor.Index && t.ContactPosition is not null && t.ApproachPoints.Count != 0) is { } target &&
                floor.TryPosition(pad.Arrival.X, pad.Arrival.Y, pad.Arrival.Layer, out var landing) && landing == pad.Arrival)
                entries.Add(new(target, landing));
        if (entries.Count == 0) return inner;
        var components = new FieldRegionIndex(); components.Update(map);
        return new TemporalLabWarpGraph(inner, entries, components);
    }

    public bool HasConnectedPad(NavigationPoint player) => pads.Any(p => p.Target.ApproachPoints.Any(g => regions.Connected(player, g) == true));
    public INavigationGraph ForGoals(IReadOnlyList<NavigationPoint> goals) => new TemporalLabWarpGraph(inner.ForGoals(goals), pads, regions);
    public INavigationGraph? WithoutCreatures => inner.WithoutCreatures is { } open ? new TemporalLabWarpGraph(open, pads, regions) : null;
    public int? BlockingActor(NavigationPoint from, NavigationPoint to) => inner.BlockingActor(from, to);
    public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point) => inner.Neighbours(point);
    public NavigationDirection InputDirection(NavigationPoint from, NavigationPoint to) => inner.InputDirection(from, to);
    public bool IsTerminal(NavigationPoint point) => inner.IsTerminal(point);
    public bool IsSameTerminal(NavigationPoint point, NavigationPoint goal) => inner.IsSameTerminal(point, goal);

    public NavigationSearchResult FindStage(NavigationPoint start, IReadOnlyList<NavigationPoint> goals, int maximumVisited)
    {
        if (inner is IStagedNavigationGraph staged && staged.FindStage(start, goals, maximumVisited) is { Route: not null } local) return local;
        // There are exactly15 audited sources,10 distinct landings. Each landing
        // is expanded once. Individual walking searches keep a finite budget.
        var budget = Math.Min(maximumVisited, 32768);
        var queue = new Queue<(Entry First, NavigationSearchResult Approach, NavigationPoint Landing)>();
        var visited = new HashSet<NavigationPoint>();
        var limited = false;
        foreach (var pad in pads.OrderBy(p => Distance(start, p.Target.Position)))
        {
            var approach = Walk(start, pad.Target.ApproachPoints, true);
            if (approach.Route is not null && visited.Add(pad.Landing)) queue.Enqueue((pad, approach, pad.Landing));
        }
        while (queue.TryDequeue(out var node))
        {
            if (Walk(node.Landing, goals, false).Route is not null)
                return node.Approach.IntermediateId is not null ? node.Approach : node.Approach with
                {
                    IntermediateId = node.First.Target.Id, IntermediateContact = node.First.Target.ContactPosition,
                    IntermediateLabel = node.First.Target.Label,
                };
            foreach (var pad in pads)
                if (!visited.Contains(pad.Landing) && Walk(node.Landing, pad.Target.ApproachPoints, true).Route is not null)
                {
                    visited.Add(pad.Landing); queue.Enqueue((node.First, node.Approach, pad.Landing));
                }
        }
        return new(null, limited);

        NavigationSearchResult Walk(NavigationPoint from, IReadOnlyList<NavigationPoint> to, bool rejectDisconnected)
        {
            if (rejectDisconnected && to.All(g => regions.Connected(from, g) == false)) return new(null, false);
            var found = NavigationPathfinder.Search(inner, from, to, budget);
            limited |= found.LimitReached;
            return found;
        }
    }

    private static long Distance(NavigationPoint a, NavigationPoint b) => Math.Abs((long)a.X - b.X) + Math.Abs((long)a.Y - b.Y);
}
