using ChronoTriggerAccessibility.Core.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.Navigation;

public sealed class ManualLegGuidanceTests
{
    [Theory]
    [InlineData(128)]
    [InlineData(256)]
    public void ManualInstructionsAdvanceOneLegAtATimeAtTheActualTurn(int units)
    {
        var controller = new NavigationController();
        var frame = Frame(units);
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        var start = controller.Handle(NavigationCommand.Guide, frame, 0);
        Assert.Equal(["Guidance to Door.", "Right 5 steps."], start.Speech);
        AssertManual(start);

        for (var step = 1; step < 5; step++)
        {
            var moving = controller.Update(At(frame, step, 1), step * 100, true);
            Assert.Empty(moving.Speech);
            AssertManual(moving);
        }
        var turn = controller.Update(At(frame, 5, 1), 500, true);
        Assert.Equal(["Up 1 step."], turn.Speech);
        AssertManual(turn);
        var nextTurn = controller.Update(At(frame, 5, 0), 600, true);
        Assert.Equal(["Right 2 steps."], nextTurn.Speech);
        AssertManual(nextTurn);
        var arrived = controller.Update(At(frame, 7, 0), 800, true);
        Assert.Equal(["Arrived at Door. Press Confirm to enter."], arrived.Speech);
        Assert.False(arrived.Guiding);
        Assert.Equal(NavigationDirection.None, arrived.Direction);
    }

    [Theory]
    [InlineData(128)]
    [InlineData(256)]
    public void RepeatGivesOnlyTheRemainingCurrentLeg(int units)
    {
        var controller = new NavigationController();
        var frame = Frame(units);
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        controller.Handle(NavigationCommand.Guide, frame, 0);
        var midway = At(frame, 2, 1);
        controller.Update(midway, 200, true);
        var repeat = controller.Handle(NavigationCommand.Repeat, midway, 250);
        Assert.Equal(["Door, 1 of 1. Right 3 steps."], repeat.Speech);
        AssertManual(repeat);

        var turned = At(frame, 5, 1);
        controller.Update(turned, 500, true);
        Assert.Equal(["Door, 1 of 1. Up 1 step."],
            controller.Handle(NavigationCommand.Repeat, turned, 550).Speech);
    }

    [Theory]
    [InlineData(128)]
    [InlineData(256)]
    public void DeviationRecalculatesOneCurrentLegFromTheStoppedPosition(int units)
    {
        var controller = new NavigationController();
        var frame = Frame(units);
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        controller.Handle(NavigationCommand.Guide, frame, 0);
        controller.Update(At(frame, 2, 1), 200, true);
        var deviated = At(frame, 2, 2);
        Assert.Equal(["Off route. Stop moving for new directions."],
            controller.Update(deviated, 300, true).Speech);
        Assert.Empty(controller.Update(deviated, 450).Speech);

        var corrected = controller.Update(deviated, 550);
        Assert.Equal(["Route updated.", "Right 3 steps."], corrected.Speech);
        AssertManual(corrected);
        Assert.Equal(["Door, 1 of 1. Right 3 steps."],
            controller.Handle(NavigationCommand.Repeat, deviated, 600).Speech);
        Assert.Empty(controller.Update(At(frame, 3, 2), 700, true).Speech);
        Assert.Equal(["Up 2 steps."], controller.Update(At(frame, 5, 2), 900, true).Speech);
    }

    [Fact]
    public void SwitchingFromAutoWalkToManualDropsTheOverviewAndMovementOutput()
    {
        var controller = new NavigationController();
        var frame = Frame(256);
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        var walk = controller.Handle(NavigationCommand.ToggleWalk, frame, 0);
        Assert.Equal(["Walking to Door.", "Right 5 steps, then up 1 step, then right 2 steps."], walk.Speech);
        Assert.True(walk.AutoWalking);
        Assert.Equal(NavigationDirection.East, walk.Direction);

        var guide = controller.Handle(NavigationCommand.Guide, frame, 100);
        Assert.Equal(["Guidance to Door.", "Right 5 steps."], guide.Speech);
        AssertManual(guide);
    }

    private static void AssertManual(NavigationResult result)
    {
        Assert.True(result.Guiding);
        Assert.False(result.AutoWalking);
        Assert.Equal(NavigationDirection.None, result.Direction);
    }

    private static NavigationFrame At(NavigationFrame frame, int x, int y) =>
        frame with { Player = new(x * frame.UnitsPerTile, y * frame.UnitsPerTile, 1) };

    private static NavigationFrame Frame(int units) => new("manual-legs", true, new(0, units, 1),
        [new("door", "Door", NavigationCategory.Exits, new(7 * units, 0, 1),
            [new(7 * units, 0, 1)], true, true) { ArrivalInstruction = "Press Confirm to enter." }],
        new Corridor(units), units);

    // The direct corridor is right 5, up 1, right 2. A lower passage permits
    // a real route correction after deviating downward part way through it.
    private sealed class Corridor(int units) : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point)
        {
            foreach (var next in new[] { point with { X = point.X + units }, point with { Y = point.Y - units },
                point with { X = point.X - units }, point with { Y = point.Y + units } })
                if (next.Layer == 1 && next.X % units == 0 && next.Y % units == 0 &&
                    ((next.Y == units && next.X >= 0 && next.X <= 5 * units) ||
                     (next.Y == 0 && next.X >= 5 * units && next.X <= 7 * units) ||
                     (next.Y == 2 * units && next.X >= 2 * units && next.X <= 5 * units))) yield return next;
        }
    }
}
