using ChronoTriggerAccessibility.Core.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.Navigation;

public sealed class ManualTurnRecoveryTests
{
    [Fact]
    public void WarnsOnceBeforeALongManualLegEndsWithoutDrivingThePlayer()
    {
        var controller = new NavigationController();
        var frame = Frame();
        Assert.Contains("Right 12 steps, then up 38 steps, then right 19 steps.",
            controller.Handle(NavigationCommand.Guide, frame, 0).Speech);
        Assert.Empty(controller.Update(At(frame, 3328, 9856), 900, true).Speech);
        var warning = controller.Update(At(frame, 3456, 9856), 1000, true);
        Assert.Contains("Turn up in 4 steps.", warning.Speech);
        Assert.Equal(NavigationDirection.None, warning.Direction);
        Assert.Empty(controller.Update(At(frame, 3584, 9856), 1100, true).Speech);
        Assert.Contains("Up 38 steps.", controller.Update(At(frame, 3968, 9856), 1500, true).Speech);
    }

    [Fact]
    public void MissedTurnWaitsForReleaseAndSettledPositionInsteadOfReversingEveryFrame()
    {
        // The user's 2026-09-13 10:58:29 trace passed the right-to-up turn at
        // (3968,9856), then continued right in 16-fine-unit world animation ticks.
        var controller = new NavigationController();
        var frame = Frame();
        controller.Handle(NavigationCommand.Guide, frame, 0);
        controller.Update(At(frame, 3952, 9856), 1500, true);
        var missed = controller.Update(At(frame, 4000, 9856), 1550, true);
        Assert.Contains("Off route. Stop moving for new directions.", missed.Speech);
        Assert.True(missed.Guiding);
        Assert.Equal(NavigationDirection.None, missed.Direction);
        for (var x = 4016; x <= 4480; x += 16)
        {
            var moving = controller.Update(At(frame, x, 9856), 1550 + x - 4000, true);
            Assert.Empty(moving.Speech);
        }
        var repeat = controller.Handle(NavigationCommand.Repeat, At(frame, 4480, 9856), 2050);
        Assert.Contains("Stop moving for new directions.", string.Join(" ", repeat.Speech));

        // Releasing the key can leave the native eight-pixel step in flight.
        Assert.Empty(controller.Update(At(frame, 4480, 9840), 2100).Speech);
        Assert.Empty(controller.Update(At(frame, 4480, 9728), 2200).Speech);
        Assert.Empty(controller.Update(At(frame, 4480, 9728), 2350).Speech);
        var recovered = controller.Update(At(frame, 4480, 9728), 2450);
        Assert.Contains("Route updated.", string.Join(" ", recovered.Speech));
        Assert.Contains("steps", string.Join(" ", recovered.Speech));
        Assert.DoesNotContain("quarter", string.Join(" ", recovered.Speech));
        Assert.Equal(NavigationDirection.None, recovered.Direction);
        Assert.Empty(controller.Update(At(frame, 4480, 9728), 2700).Speech);
    }

    [Fact]
    public void TerrainChangeWhileAlreadyStoppedDoesNotAskThePlayerToStopAgain()
    {
        var controller = new NavigationController();
        var frame = Frame();
        controller.Handle(NavigationCommand.Guide, frame, 0);
        controller.Update(frame, 500);
        var changed = controller.Update(frame with { Graph = new ClosedPassage() }, 1000);
        Assert.DoesNotContain("Stop moving", string.Join(" ", changed.Speech));
        Assert.Contains("No route", string.Join(" ", changed.Speech));
        Assert.False(changed.Guiding);
    }

    [Fact]
    public void ReturningToTheExistingRouteResumesGuidanceWithoutReplanning()
    {
        var controller = new NavigationController();
        var frame = Frame();
        controller.Handle(NavigationCommand.Guide, frame, 0);
        controller.Update(At(frame, 4000, 9856), 1000, true);
        var returned = controller.Update(At(frame, 3968, 9600), 1200, true);
        Assert.Contains("Up 36 steps.", returned.Speech);
        Assert.DoesNotContain("Route updated.", returned.Speech);
        Assert.Empty(controller.Update(At(frame, 3968, 9472), 1400, true).Speech);
    }

    private static NavigationFrame At(NavigationFrame frame, int x, int y) =>
        frame with { Player = new(x, y, 1) };

    private static NavigationFrame Frame() => new("world-test", true, new(2432, 9856, 1),
        [new("inn", "Truce Inn", NavigationCategory.People, new(6400, 4992, 1),
            [new(6400, 4992, 1)], true, true)], new Passage(), 128);

    private sealed class ClosedPassage : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point) => [];
    }

    // This is an intentionally small walkable fixture, not a claim about world
    // collision data: right 12, up 38, right 19 is the logged route under test.
    private sealed class Passage : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point)
        {
            foreach (var next in new[] {
                point with { X = (point.X / 128 + 1) * 128 },
                point with { X = (point.X - 1) / 128 * 128 },
                point with { Y = (point.Y / 128 + 1) * 128 },
                point with { Y = (point.Y - 1) / 128 * 128 } })
                if ((next.Y == 9856 && next.X >= 2432 && next.X <= 4608) ||
                    (next.X == 3968 && next.Y >= 4992 && next.Y <= 9856) ||
                    (next.Y == 9728 && next.X >= 3968 && next.X <= 4608) ||
                    (next.Y == 4992 && next.X >= 3968 && next.X <= 6400)) yield return next;
        }
    }
}
