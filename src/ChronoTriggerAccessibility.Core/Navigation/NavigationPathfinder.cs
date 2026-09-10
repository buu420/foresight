namespace ChronoTriggerAccessibility.Core.Navigation;

public sealed record NavigationSearchResult(IReadOnlyList<NavigationPoint>? Route, bool LimitReached);

public static class NavigationPathfinder
{
    public static IReadOnlyList<NavigationPoint>? Find(INavigationGraph graph, NavigationPoint start,
        IReadOnlyList<NavigationPoint> goals, int maximumVisited = 65536) => Search(graph, start, goals, maximumVisited).Route;

    public static NavigationSearchResult Search(INavigationGraph graph, NavigationPoint start,
        IReadOnlyList<NavigationPoint> goals, int maximumVisited = 65536)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(goals);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumVisited, 1);
        if (goals.Count == 0) return new(null, false);
        if (goals.Count > 64) return new(null, true);
        var destinations = goals.ToHashSet();
        var previous = new Dictionary<NavigationPoint, NavigationPoint> { [start] = start };
        var costs = new Dictionary<NavigationPoint, double> { [start] = 0 };
        var closed = new HashSet<NavigationPoint>();
        var queue = new PriorityQueue<NavigationPoint, double>();
        queue.Enqueue(start, Estimate(start));
        while (queue.TryDequeue(out var current, out _))
        {
            if (!closed.Add(current)) continue;
            if (destinations.Contains(current))
            {
                var path = new List<NavigationPoint> { current };
                while (current != start) { current = previous[current]; path.Add(current); }
                path.Reverse();
                return new(path.AsReadOnly(), false);
            }
            if (current != start && graph.IsTerminal(current)) continue;
            // A local movement graph has a bounded number of neighbouring positions.
            var neighbours = 0;
            foreach (var next in graph.Neighbours(current))
            {
                if (++neighbours > 16) return new(null, true);
                if (closed.Contains(next)) continue;
                var cost = costs[current] + Distance(current, next);
                if (costs.TryGetValue(next, out var oldCost) && cost >= oldCost) continue;
                if (!previous.ContainsKey(next) && previous.Count >= maximumVisited) return new(null, true);
                previous[next] = current;
                costs[next] = cost;
                queue.Enqueue(next, cost + Estimate(next));
            }
        }
        return new(null, false);

        double Estimate(NavigationPoint point) => goals.Min(goal => Distance(point, goal));
        static double Distance(NavigationPoint from, NavigationPoint to) =>
            Math.Abs((double)from.X - to.X) + Math.Abs((double)from.Y - to.Y);
    }
}
