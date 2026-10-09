namespace ChronoTriggerAccessibility.Core.Navigation;

public sealed record NavigationSearchResult(IReadOnlyList<NavigationPoint>? Route, bool LimitReached, string? IntermediateId = null)
{
    /// <summary>A native touch target that must be approached with movement while
    /// waiting for the live terrain to change. Never authorizes Confirm.</summary>
    public NavigationPoint? IntermediateContact { get; init; }
    public NavigationTransition? Transition { get; init; }
}

public static class NavigationPathfinder
{
    /// <summary>Ceiling on distinct positions admitted by one search. Each can
    /// retain at most nine headings, including the initial zero heading.</summary>
    // Pixel boundaries needed by narrow field passages add distinct positions.
    // The full map audit includes reachable Mountain of Woe/Frozen Cliffs routes
    // exceeding 65,536 positions; retain a finite ceiling covering those maps.
    public const int DefaultMaximumVisited = 131072;

    private readonly record struct SearchNode(NavigationPoint Point, int XDirection, int YDirection);
    public static IReadOnlyList<NavigationPoint>? Find(INavigationGraph graph, NavigationPoint start,
        IReadOnlyList<NavigationPoint> goals, int maximumVisited = DefaultMaximumVisited) => Search(graph, start, goals, maximumVisited).Route;

    public static NavigationSearchResult Search(INavigationGraph graph, NavigationPoint start,
        IReadOnlyList<NavigationPoint> goals, int maximumVisited = DefaultMaximumVisited)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(goals);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumVisited, 1);
        if (goals.Count == 0) return new(null, false);
        if (goals.Count > 64) return new(null, true);
        graph = graph.ForGoals(goals);
        var destinations = goals.ToHashSet();
        var first = new SearchNode(start, 0, 0);
        var previous = new Dictionary<SearchNode, SearchNode> { [first] = first };
        var costs = new Dictionary<SearchNode, (double Distance, int Turns)> { [first] = (0, 0) };
        var distances = new Dictionary<NavigationPoint, double> { [start] = 0 };
        var closed = new HashSet<SearchNode>();
        // Retain heading as part of search state so equal-length paths can prefer
        // fewer turns without discarding a better approach to a later junction.
        var queue = new PriorityQueue<SearchNode, (double Total, int Turns, double Remaining, long Order)>();
        long order = 0;
        queue.Enqueue(first, (Estimate(start), 0, Estimate(start), order++));
        while (queue.TryDequeue(out var current, out _))
        {
            if (!closed.Add(current)) continue;
            if (costs[current].Distance > distances[current.Point]) continue;
            if (destinations.Contains(current.Point))
            {
                var path = new List<NavigationPoint> { current.Point };
                while (current != first) { current = previous[current]; path.Add(current.Point); }
                path.Reverse();
                return new(path.AsReadOnly(), false);
            }
            if (current != first && graph.IsTerminal(current.Point) &&
                !goals.Any(goal => graph.IsSameTerminal(current.Point, goal))) continue;
            // A local movement graph has a bounded number of neighbouring positions.
            var neighbours = 0;
            foreach (var point in graph.Neighbours(current.Point))
            {
                if (++neighbours > 16) return new(null, true);
                var dx = Math.Sign((long)point.X - current.Point.X);
                var dy = Math.Sign((long)point.Y - current.Point.Y);
                var next = new SearchNode(point, dx, dy);
                if (closed.Contains(next)) continue;
                var turn = current != first && (dx != current.XDirection || dy != current.YDirection) ? 1 : 0;
                var cost = (Distance: costs[current].Distance + Distance(current.Point, point), Turns: costs[current].Turns + turn);
                // Heading can improve the secondary turn count, but a longer
                // arrival at the same position cannot improve any continuation.
                // Charge the position budget once, rather than once per heading.
                if (distances.TryGetValue(point, out var bestDistance) && cost.Distance > bestDistance) continue;
                if (costs.TryGetValue(next, out var oldCost) && cost.CompareTo(oldCost) >= 0) continue;
                if (!distances.ContainsKey(point) && distances.Count >= maximumVisited) return new(null, true);
                distances[point] = cost.Distance;
                previous[next] = current;
                costs[next] = cost;
                var estimate = Estimate(point);
                queue.Enqueue(next, (cost.Distance + estimate, cost.Turns, estimate, order++));
            }
        }
        return graph is IStagedNavigationGraph staged ? staged.FindStage(start, goals, maximumVisited) : new(null, false);

        double Estimate(NavigationPoint point) => goals.Min(goal => Distance(point, goal));
        static double Distance(NavigationPoint from, NavigationPoint to) =>
            Math.Abs((double)from.X - to.X) + Math.Abs((double)from.Y - to.Y);
    }
}
