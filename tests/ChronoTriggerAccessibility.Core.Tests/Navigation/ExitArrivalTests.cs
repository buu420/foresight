using ChronoTriggerAccessibility.Core.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.Navigation;

/// <summary>Offering the boundary node of an exit cell is what makes a one-tile doorway
/// routable, but the arrival box is a one eighth of a tile on each axis, so "close enough" can sit
/// on the wrong side of the cell edge. The game warps on the cell, not on the distance, so
/// arrival at a terminal goal has to mean the graph agrees the player is in it.</summary>
public sealed class ExitArrivalTests
{
    private const int Step = 256;
    private static readonly NavigationPoint Outside = new(Step - 32, 0, 1);   // inside the arrival box, wrong cell
    private static readonly NavigationPoint Inside = new(Step, 0, 1);         // the cell's boundary node

    private static NavigationFrame Frame(NavigationPoint player) =>
        new("room", true, player,
            [new("door", "Door", NavigationCategory.Exits, Inside, [Inside], true, true)],
            new Doorway(), Step);

    [Fact]
    public void StoppingAEighthTileShortOfTheCellIsNotArrival()
    {
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, Frame(new(0, 0, 1)), 0);   // People -> Exits
        controller.Handle(NavigationCommand.ToggleWalk, Frame(new(0, 0, 1)), 0);
        var result = controller.Update(Frame(Outside), 100);
        Assert.DoesNotContain(result.Speech, s => s.Contains("Arrived") || s.Contains("You are at"));
        Assert.True(controller.IsActive);
    }

    [Fact]
    public void GuidanceKeepsDrivingInsteadOfRunningOutOfWaypoints()
    {
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, Frame(new(0, 0, 1)), 0);   // People -> Exits
        controller.Handle(NavigationCommand.ToggleWalk, Frame(new(0, 0, 1)), 0);
        var result = controller.Update(Frame(Outside), 100);
        // The route has been consumed but the cell is not entered. The next tick must still
        // be steering at the final node rather than sitting on an exhausted cursor.
        var diagnostic = controller.DiagnosticState;
        Assert.DoesNotContain("next=none", diagnostic);
        Assert.True(controller.IsActive);
        Assert.Equal(NavigationDirection.East, result.Direction);
        Assert.Equal(NavigationDirection.East, controller.Update(Frame(Outside), 200).Direction);
    }

    [Fact]
    public void ReachingTheCellIsArrival()
    {
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, Frame(new(0, 0, 1)), 0);   // People -> Exits
        controller.Handle(NavigationCommand.ToggleWalk, Frame(new(0, 0, 1)), 0);
        var result = controller.Update(Frame(Inside), 100);
        Assert.False(controller.IsActive);
        Assert.NotEmpty(result.Speech);
    }

    [Fact]
    public void AnOrdinaryTargetKeepsTheEighthTileTolerance()
    {
        // Nothing is a terminal on a plain graph, so the old distance rule still applies.
        var goal = new NavigationPoint(Step, 0, 1);
        NavigationFrame Plain(NavigationPoint player) =>
            new("room", true, player,
                [new("thing", "Thing", NavigationCategory.Objects, goal, [goal], true, true)],
                new OpenFloor(), Step);
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, Plain(new(0, 0, 1)), 0);   // People -> Exits
        controller.Handle(NavigationCommand.NextCategory, Plain(new(0, 0, 1)), 0);   // Exits -> Objects
        controller.Handle(NavigationCommand.ToggleWalk, Plain(new(0, 0, 1)), 0);
        Assert.True(controller.IsActive, "the plain target must be guiding before the tolerance check");
        controller.Update(Plain(Outside), 100);
        Assert.False(controller.IsActive);
    }

    /// <summary>One axis of floor whose far node is an exit cell.</summary>
    private sealed class Doorway : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point) =>
            point.X < Step ? [new(point.X + 64, point.Y, point.Layer)] : [];
        public bool IsTerminal(NavigationPoint point) => point.X >= Step;
        public bool IsSameTerminal(NavigationPoint point, NavigationPoint goal) =>
            IsTerminal(point) && IsTerminal(goal);
    }

    private sealed class OpenFloor : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point) =>
            point.X < Step ? [new(point.X + 64, point.Y, point.Layer)] : [];
    }
}
