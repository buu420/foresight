using ChronoTriggerAccessibility.Core.Navigation;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Stop at a scripted passage just as at a native exit cell. A route to
/// an unrelated object must not silently take a Gate or leave the current room.</summary>
public sealed class ScriptPassageGraph(INavigationGraph inner,
    IReadOnlyList<(string Id, int Left, int Top, int Right, int Bottom)> passages) : INavigationGraph
{
    public INavigationGraph ForGoals(IReadOnlyList<NavigationPoint> goals) =>
        new ScriptPassageGraph(inner.ForGoals(goals), passages);
    public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point) => inner.Neighbours(point);
    public bool IsTerminal(NavigationPoint point) => inner.IsTerminal(point) || At(point).Any();
    public bool IsSameTerminal(NavigationPoint point, NavigationPoint goal) =>
        inner.IsSameTerminal(point, goal) || At(point).Intersect(At(goal)).Any();
    private IEnumerable<string> At(NavigationPoint p) => passages.Where(r =>
        p.X >= 0 && p.Y >= 0 && p.X / 256 >= r.Left && p.X / 256 <= r.Right &&
        p.Y / 256 >= r.Top && p.Y / 256 <= r.Bottom).Select(r => r.Id);
}
