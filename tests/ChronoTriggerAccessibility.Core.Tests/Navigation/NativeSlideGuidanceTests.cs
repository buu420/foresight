using ChronoTriggerAccessibility.Core.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.Navigation;

public sealed class NativeSlideGuidanceTests
{
    [Fact]
    public void SlideInstructionsAndCountedFootstepsUsePhysicalTravel()
    {
        var points = new List<NavigationPoint> { new(2048, 2048, 1) };
        for (var i = 1; i <= 16; i++) points.Add(new(2048 + i * 64, 2048 - i * 64, 1));
        for (var i = 1; i <= 9; i++) points.Add(new(3072, 1024 - i * 64, 1));
        points.Add(new(3072, 436, 1));
        var frame = new NavigationFrame("counted-slide", true, points[0],
            [new("passage", "Passage", NavigationCategory.Exits, points[^1], [points[^1]], true, true)],
            new CountedSlide(points), 256);
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        var result = controller.Handle(NavigationCommand.Guide, frame, 1);
        // 1024*sqrt(2) + 588 = 2036 physical units, rounded to eight steps.
        Assert.Equal(["Guidance to Passage.", "Up 8 steps."], result.Speech);
        var tracker = new FootstepTracker();
        tracker.Update(new(1, 1, 1, 2048, 2048, 256), 0x100, 1, result.ManualLeg);
        var beats = 0;
        for (var i = 1; i < points.Count && result.Guiding; i++)
        {
            result = controller.Update(frame with { Player = points[i] }, i * 20, true);
            tracker.Update(new(1, 1, 1, points[i].X, points[i].Y, 256), 0x100, i * 20, result.ManualLeg);
            beats += tracker.Steps;
        }
        Assert.False(result.Guiding);
        Assert.Equal(8, beats);
    }

    [Fact]
    public void AutoWalkKeepsTheCommandThatProducesVerifiedSidewaysDrift()
    {
        var controller = new NavigationController();
        var frame = Frame();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        var result = controller.Handle(NavigationCommand.ToggleWalk, frame, 1);
        Assert.Equal(NavigationDirection.North, result.Direction);
        Assert.Equal(["Walking to Passage.", "Up 1.25 steps."], result.Speech);

        var midway = controller.Update(frame with { Player = new(416, 416, 1) }, 100);
        Assert.True(midway.AutoWalking);
        Assert.Equal(NavigationDirection.North, midway.Direction);
        Assert.Empty(midway.Speech);
    }

    [Fact]
    public void ManualGuidanceAcceptsProgressAlongTheVerifiedSlide()
    {
        var controller = new NavigationController();
        var frame = Frame();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        var result = controller.Handle(NavigationCommand.Guide, frame, 1);
        Assert.Equal(["Guidance to Passage.", "Up 1.25 steps."], result.Speech);
        Assert.Equal(NavigationDirection.North, result.ManualLeg!.Direction);

        var midway = controller.Update(frame with { Player = new(416, 416, 1) }, 100, true);
        Assert.True(midway.Guiding);
        Assert.Empty(midway.Speech);
        Assert.Equal(NavigationDirection.North, midway.ManualLeg!.Direction);
        var arrived = controller.Update(frame with { Player = new(384, 256, 1) }, 200, true);
        Assert.Equal(["Arrived at Passage."], arrived.Speech);
        Assert.False(arrived.Guiding);
    }

    [Fact]
    public void ManualMovementAwayFromTheSlideStillRequiresNewDirections()
    {
        var controller = new NavigationController();
        var frame = Frame();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        controller.Handle(NavigationCommand.Guide, frame, 1);
        var result = controller.Update(frame with { Player = new(576, 480, 1) }, 100, true);
        Assert.Equal(["Off route. Stop moving for new directions."], result.Speech);
        Assert.Null(result.ManualLeg);
    }

    [Fact]
    public void CancellingSlideWalkingReleasesItsMovement()
    {
        var controller = new NavigationController();
        var frame = Frame();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        controller.Handle(NavigationCommand.ToggleWalk, frame, 1);
        var result = controller.Update(frame, 100, true);
        Assert.False(result.AutoWalking);
        Assert.Equal(NavigationDirection.None, result.Direction);
    }

    private static NavigationFrame Frame() => new("native-slide", true, new(512, 512, 1),
        [new("passage", "Passage", NavigationCategory.Exits, new(384, 256, 1),
            [new(384, 256, 1)], true, true)], new Slide(), 256);

    // The graph owns the native movement proof. Core guidance must preserve its
    // command instead of guessing a button from the displacement's first axis.
    private sealed class Slide : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point) => point switch
        {
            (512, 512, 1) => [new(448, 448, 1)],
            (448, 448, 1) => [new(384, 384, 1)],
            (384, 384, 1) => [new(384, 256, 1)],
            _ => [],
        };
        public NavigationDirection InputDirection(NavigationPoint from, NavigationPoint to) => NavigationDirection.North;
    }

    private sealed class CountedSlide(IReadOnlyList<NavigationPoint> points) : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point)
        {
            for (var i = 0; i + 1 < points.Count; i++)
                if (points[i] == point) yield return points[i + 1];
        }
        public NavigationDirection InputDirection(NavigationPoint from, NavigationPoint to) => NavigationDirection.North;
    }
}
