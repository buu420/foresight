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
    /// <summary>The input that produces a verified edge. Native corner slides
    /// can move sideways during a cardinal command; displacement is not input.</summary>
    NavigationDirection InputDirection(NavigationPoint from, NavigationPoint to) =>
        to.X < from.X ? NavigationDirection.West : to.X > from.X ? NavigationDirection.East :
        to.Y < from.Y ? NavigationDirection.North : to.Y > from.Y ? NavigationDirection.South : NavigationDirection.None;
    bool IsTerminal(NavigationPoint point) => false;
    bool IsSameTerminal(NavigationPoint point, NavigationPoint goal) => point == goal;
}

/// <summary>A live passage that must open before the final route can be walked.</summary>
public interface IStagedNavigationGraph : INavigationGraph
{
    NavigationSearchResult FindStage(NavigationPoint start, IReadOnlyList<NavigationPoint> goals, int maximumVisited);
}

/// <summary>An audited one-way native movement script. A0 finishes on entering
/// the landing tile, rather than at its centre.</summary>
public sealed record NavigationTransition(string Id, string Label, NavigationPoint Approach,
    NavigationPoint Landing, NavigationDirection Direction, IReadOnlyList<int> LandingLayers)
{
    public bool HasLanded(NavigationPoint point) => LandingLayers.Contains(point.Layer) &&
        point.X / NavigationUnits.LocalStep == Landing.X / NavigationUnits.LocalStep &&
        point.Y / NavigationUnits.LocalStep == Landing.Y / NavigationUnits.LocalStep;
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
    /// <summary>A visible moving hazard remains readable by position, but is
    /// not a destination for the mod's guidance or automatic walking.</summary>
    public string? GuidanceRestriction { get; init; }
    public string? ArrivalInstruction { get; init; }
    public NavigationTransition? Transition { get; init; }
    /// <summary>An audited moving interaction, such as the Arris Dome rat: stay active
    /// in Confirm range and follow again when it moves. Only the player's own Confirm
    /// input is permitted alongside automatic movement; never synthesize the catch.</summary>
    public bool FollowUntilInteraction { get; init; }
    /// <summary>Audited contact from a reachable standing point. Arrival must
    /// wait for the native objective to advance; proximity alone is insufficient.</summary>
    public NavigationDirection ContactDirection { get; init; }
    /// <summary>For contacts approachable from several sides, finish toward
    /// this live native contact centre instead of reporting proximity as arrival.</summary>
    public NavigationPoint? ContactPosition { get; init; }
    /// <summary>Alternative story destinations can require different contact
    /// centres, or no contact. Bind the finish to the selected route goal.</summary>
    public IReadOnlyDictionary<NavigationPoint, NavigationPoint>? ApproachContacts { get; init; }
    public NavigationPoint? ContactAt(NavigationPoint goal) => ApproachContacts is null ? ContactPosition :
        ApproachContacts.TryGetValue(goal, out var centre) ? centre : null;
    /// <summary>For a destination the game activates with Confirm: the facings from which
    /// Confirm, pressed with the player standing at a point, reaches it (preferred first; empty
    /// when none does). Standing in range is not enough; the native handler only tests the
    /// actor on the side the player faces. Evaluated on the live frame, so a moved actor is
    /// judged where it is now. Null for touch contacts, exits and anything not confirmed.</summary>
    public Func<NavigationPoint, IReadOnlyList<NavigationDirection>>? ConfirmFacings { get; init; }
    /// <summary>Alternative destinations may mix Confirm targets with exits or touch contacts.
    /// When set, only a goal listed here finishes by facing, with that alternative's rule;
    /// any other goal is an ordinary arrival. Mirrors <see cref="ApproachContacts"/>.</summary>
    public IReadOnlyDictionary<NavigationPoint, Func<NavigationPoint, IReadOnlyList<NavigationDirection>>>? ApproachConfirms { get; init; }
    /// <summary>True where the destination is geometrically in reach but the game would not give
    /// it Confirm right now (its native scan gate is off, another touched actor overrides, or the
    /// native state is unreadable). Such a point is neither ready nor unreachable: wait there.</summary>
    public Func<NavigationPoint, bool>? ConfirmPending { get; init; }
    public Func<NavigationPoint, IReadOnlyList<NavigationDirection>>? ConfirmAt(NavigationPoint goal) =>
        ApproachConfirms is null ? ConfirmFacings : ApproachConfirms.TryGetValue(goal, out var rule) ? rule : null;
    /// <summary>Facings that would work from here for any of this destination's Confirm rules.</summary>
    public IReadOnlyList<NavigationDirection> AnyConfirmFacings(NavigationPoint point)
    {
        IEnumerable<Func<NavigationPoint, IReadOnlyList<NavigationDirection>>> rules = ApproachConfirms is not null
            ? ApproachConfirms.Values.Distinct() : ConfirmFacings is not null ? [ConfirmFacings] : [];
        return rules.SelectMany(rule => rule(point)).Distinct().ToArray();
    }
}

public readonly record struct NavigationFloor(NavigationDirection Direction, int Speed);

public sealed record NavigationFrame(string Scene, bool CanNavigate, NavigationPoint Player,
    IReadOnlyList<NavigationTarget> Targets, INavigationGraph Graph, int UnitsPerTile = 16)
{
    public string? AreaName { get; init; }
    /// <summary>The leader's live facing; None when it could not be read.</summary>
    public NavigationDirection PlayerFacing { get; init; }
    /// <summary>Native moving terrain under the leader's foot, in field units per tick.</summary>
    public NavigationFloor? MovingFloor { get; init; }
    /// <summary>Fresh native script identity, not merely loss of player control.</summary>
    public IReadOnlySet<string> ActiveTransitions { get; init; } = new HashSet<string>();
}

public sealed record NavigationLeg(NavigationPoint End, NavigationDirection Direction, int UnitsPerStep, long Revision)
{
    /// <summary>Whole audible steps promised by this instruction. Includes native
    /// slide travel; stable until the instruction revision changes.</summary>
    public int? ExpectedSteps { get; init; }
}

public sealed record NavigationResult(IReadOnlyList<string> Speech, NavigationDirection Direction,
    bool Guiding, bool AutoWalking)
{
    public NavigationLeg? ManualLeg { get; init; }
}
