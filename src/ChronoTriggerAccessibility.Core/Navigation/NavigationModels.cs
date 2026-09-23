namespace ChronoTriggerAccessibility.Core.Navigation;

// Enemies is appended so the existing indexes, and any saved selection that uses
// them, keep meaning what they did before.
public enum NavigationCategory { People, Exits, Objects, StoryEvents, Enemies }
public enum NavigationCommand { PreviousCategory, NextCategory, PreviousTarget, NextTarget, Repeat, Guide, ToggleWalk }
public enum NavigationDirection { None, North, South, West, East, NorthWest, NorthEast, SouthWest, SouthEast }

public readonly record struct NavigationPoint(int X, int Y, int Layer);

public static class NavigationUnits
{
    public const int LocalStep = 256;
    public const int WorldStep = 128;
}

public interface INavigationGraph
{
    /// <summary>Bind movement rules to the selected destination, when reaching
    /// it requires a native contact that would block ordinary travel.</summary>
    INavigationGraph ForGoals(IReadOnlyList<NavigationPoint> goals) => this;
    IEnumerable<NavigationPoint> Neighbours(NavigationPoint point);
    bool IsTerminal(NavigationPoint point) => false;
    bool IsSameTerminal(NavigationPoint point, NavigationPoint goal) => point == goal;
}

/// <summary>A live passage that must open before the final route can be walked.</summary>
public interface IStagedNavigationGraph : INavigationGraph
{
    NavigationSearchResult FindStage(NavigationPoint start, IReadOnlyList<NavigationPoint> goals, int maximumVisited);
}

public sealed record NavigationTarget(string Id, string Label, NavigationCategory Category,
    NavigationPoint Position, IReadOnlyList<NavigationPoint> ApproachPoints, bool Visible, bool Discovered)
{
    /// <summary>A guide entry bound to a currently active native destination. It
    /// can be selected before discovery without claiming it has been seen.</summary>
    public bool GuideAvailable { get; init; }
    /// <summary>A current story reminder with no spatial claim. Its Position is unused.</summary>
    public bool IsStoryNote { get; init; }
    public string? Instruction { get; init; }
    public string? ArrivalInstruction { get; init; }
}

public sealed record NavigationFrame(string Scene, bool CanNavigate, NavigationPoint Player,
    IReadOnlyList<NavigationTarget> Targets, INavigationGraph Graph, int UnitsPerTile = 16)
{
    public string? AreaName { get; init; }
}

public sealed record NavigationLeg(NavigationPoint End, NavigationDirection Direction, int UnitsPerStep, long Revision);

public sealed record NavigationResult(IReadOnlyList<string> Speech, NavigationDirection Direction,
    bool Guiding, bool AutoWalking)
{
    public NavigationLeg? ManualLeg { get; init; }
}
