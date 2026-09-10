namespace ChronoTriggerAccessibility.Core.Navigation;

public enum NavigationCategory { People, Exits, Objects, StoryEvents }
public enum NavigationCommand { PreviousCategory, NextCategory, PreviousTarget, NextTarget, Repeat, Guide, ToggleWalk }
public enum NavigationDirection { None, North, South, West, East, NorthWest, NorthEast, SouthWest, SouthEast }

public readonly record struct NavigationPoint(int X, int Y, int Layer);

public interface INavigationGraph
{
    IEnumerable<NavigationPoint> Neighbours(NavigationPoint point);
    bool IsTerminal(NavigationPoint point) => false;
    bool IsSameTerminal(NavigationPoint point, NavigationPoint goal) => point == goal;
}

public sealed record NavigationTarget(string Id, string Label, NavigationCategory Category,
    NavigationPoint Position, IReadOnlyList<NavigationPoint> ApproachPoints, bool Visible, bool Discovered);

public sealed record NavigationFrame(string Scene, bool CanNavigate, NavigationPoint Player,
    IReadOnlyList<NavigationTarget> Targets, INavigationGraph Graph, int UnitsPerTile = 16);

public sealed record NavigationResult(IReadOnlyList<string> Speech, NavigationDirection Direction,
    bool Guiding, bool AutoWalking);
