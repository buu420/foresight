using ChronoTriggerAccessibility.Core.Navigation;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Plan up to the native contact, then recapture the landing before
/// continuing. Preview landings prove connectivity; they never become walking edges.</summary>
public sealed class FieldTransitionGraph(INavigationGraph inner, IReadOnlyList<FieldTransitions.Entry> transitions)
    : IStagedNavigationGraph
{
    public static INavigationGraph Create(INavigationGraph inner, IReadOnlyList<FieldTransitions.Entry> transitions) =>
        transitions.Count == 0 ? inner : new FieldTransitionGraph(inner, transitions);
    public INavigationGraph ForGoals(IReadOnlyList<NavigationPoint> goals) =>
        new FieldTransitionGraph(inner.ForGoals(goals), transitions);
    public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point) => inner.Neighbours(point);
    public bool IsTerminal(NavigationPoint point) => inner.IsTerminal(point);
    public bool IsSameTerminal(NavigationPoint point, NavigationPoint goal) => inner.IsSameTerminal(point, goal);

    public NavigationSearchResult FindStage(NavigationPoint start, IReadOnlyList<NavigationPoint> goals, int maximumVisited)
    {
        if (inner is IStagedNavigationGraph staged && staged.FindStage(start, goals, maximumVisited) is { Route: not null } opened)
            return opened;
        foreach (var entry in transitions.Where(t => t.Available).OrderBy(t => Distance(start, t.Transition.Approach)))
        {
            var t = entry.Transition;
            var approach = NavigationPathfinder.Search(inner, start, [t.Approach], maximumVisited);
            if (approach.Route is null) continue;
            if (!Reaches(t.Landing, goals, [t.Id], entry.SharedLock, 1, maximumVisited)) continue;
            return approach.IntermediateId is null
                ? approach with { IntermediateId = t.Id, Transition = t }
                : approach;
        }
        return new(null, false);
    }

    private bool Reaches(NavigationPoint start, IReadOnlyList<NavigationPoint> goals,
        HashSet<string> taken, int consumedLock, int depth, int maximumVisited)
    {
        if (NavigationPathfinder.Search(inner, start, goals, maximumVisited).Route is not null) return true;
        // Atel_0145 has exactly two consecutive drops. Do not invent general
        // traversal of arbitrary scene scripts, or reuse the shared one-use slide.
        if (depth >= 2) return false;
        foreach (var next in transitions.Where(t => t.Available && !taken.Contains(t.Transition.Id) &&
                     (consumedLock < 0 || t.SharedLock != consumedLock)))
        {
            if (NavigationPathfinder.Search(inner, start, [next.Transition.Approach], maximumVisited).Route is null) continue;
            var visited = new HashSet<string>(taken) { next.Transition.Id };
            if (Reaches(next.Transition.Landing, goals, visited, next.SharedLock >= 0 ? next.SharedLock : consumedLock,
                    depth + 1, maximumVisited)) return true;
        }
        return false;
    }

    private static long Distance(NavigationPoint a, NavigationPoint b) => Math.Abs((long)a.X - b.X) + Math.Abs((long)a.Y - b.Y);
}
