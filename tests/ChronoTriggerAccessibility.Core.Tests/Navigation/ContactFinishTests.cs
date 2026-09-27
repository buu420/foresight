using ChronoTriggerAccessibility.Core.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.Navigation;

public sealed class ContactFinishTests
{
    [Theory]
    [InlineData(543, 320, NavigationDirection.West)]
    [InlineData(225, 320, NavigationDirection.East)]
    [InlineData(384, 415, NavigationDirection.North)]
    [InlineData(384, 161, NavigationDirection.South)]
    [InlineData(384, 256, NavigationDirection.North)]
    [InlineData(416, 288, NavigationDirection.North)]
    public void NearbyEncounterNeedsNativeContactBeforeWalkingStops(int x, int y, NavigationDirection direction)
    {
        var player = new NavigationPoint(x, y, 1);
        var target = new NavigationTarget("battle", "Encounter", NavigationCategory.People,
            new(384, 256, 1), [player], true, true) { ContactPosition = new(384, 256, 1) };
        var frame = new NavigationFrame("forest", true, player, [target], new Line(), 256);
        var controller = new NavigationController();
        var start = controller.Handle(NavigationCommand.ToggleWalk, frame, 0);
        Assert.True(start.AutoWalking);
        Assert.Equal(direction, start.Direction);
        Assert.DoesNotContain(start.Speech, s => s.StartsWith("Arrived"));
        Assert.False(controller.Update(frame with { CanNavigate = false }, 100).AutoWalking);
    }

    [Fact]
    public void AutomaticContactWaitsForNativeAdvanceAndStopsIfItDoesNotHappen()
    {
        var controller = new NavigationController();
        var frame = Frame();
        var start = controller.Handle(NavigationCommand.ToggleWalk, frame, 0);
        Assert.True(start.AutoWalking);
        Assert.Equal(NavigationDirection.West, start.Direction);
        Assert.DoesNotContain(start.Speech, s => s.StartsWith("Arrived"));
        var stopped = controller.Update(frame, 1600);
        Assert.False(stopped.AutoWalking);
        Assert.Equal(NavigationDirection.None, stopped.Direction);
        Assert.Contains(stopped.Speech, s => s.Contains("did not register"));
    }

    [Fact]
    public void ChangedNativeCheckpointReplansAndClearsTheContactDirection()
    {
        var controller = new NavigationController();
        var frame = Frame();
        controller.Handle(NavigationCommand.ToggleWalk, frame, 0);
        var target = frame.Targets[0] with { Position = new(768, 256, 1), ApproachPoints = [new(768, 256, 1)],
            ContactDirection = NavigationDirection.None, Label = "Next checkpoint" };
        var next = controller.Update(frame with { Targets = [target] }, 100);
        Assert.True(next.AutoWalking);
        Assert.Equal(NavigationDirection.East, next.Direction);
    }

    [Fact]
    public void ManualContactSpeaksItsDirectionWithoutMovingAndAllowsTimeToRespond()
    {
        var controller = new NavigationController();
        var first = controller.Handle(NavigationCommand.Guide, Frame(), 0);
        Assert.True(first.Guiding);
        Assert.False(first.AutoWalking);
        Assert.Equal(NavigationDirection.None, first.Direction);
        Assert.Contains(first.Speech, s => s.Contains("left"));
        Assert.Null(first.ManualLeg);
        Assert.True(controller.Update(Frame(), 9000).Guiding);
        var repeated = controller.Handle(NavigationCommand.Repeat, Frame(), 9100);
        Assert.Contains(repeated.Speech, s => s.Contains("Continue left") && s.Contains("registers"));
        Assert.Equal(NavigationDirection.None, repeated.Direction);
    }

    [Fact]
    public void ContactTravelIsBoundedEvenWhenNoWallStopsIt()
    {
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.ToggleWalk, Frame(), 0);
        var result = controller.Update(Frame() with { Player = new(352, 256, 1) }, 100);
        Assert.False(result.AutoWalking);
        Assert.DoesNotContain(result.Speech, s => s.StartsWith("Arrived"));
    }

    private static NavigationFrame Frame() => new("lesson", true, new(512, 256, 1),
        [new("checkpoint", "Checkpoint", NavigationCategory.People, new(512, 256, 1), [new(512, 256, 1)], true, true)
            { ContactDirection = NavigationDirection.West }], new Line(), 256);
    private sealed class Line : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point)
        {
            if (point.X < 1024) yield return point with { X = point.X + 64 };
            if (point.X > 0) yield return point with { X = point.X - 64 };
        }
    }
}
