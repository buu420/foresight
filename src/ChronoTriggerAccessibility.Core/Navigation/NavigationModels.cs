namespace ChronoTriggerAccessibility.Core.Navigation;

public enum NavigationCategory { People, Exits, Objects, StoryEvents }
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
    IEnumerable<NavigationPoint> Neighbours(NavigationPoint point);
    bool IsTerminal(NavigationPoint point) => false;
    bool IsSameTerminal(NavigationPoint point, NavigationPoint goal) => point == goal;
}

public sealed record NavigationTarget(string Id, string Label, NavigationCategory Category,
    NavigationPoint Position, IReadOnlyList<NavigationPoint> ApproachPoints, bool Visible, bool Discovered)
{
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
