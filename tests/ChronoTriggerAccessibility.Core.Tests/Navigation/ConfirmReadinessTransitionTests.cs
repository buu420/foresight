using ChronoTriggerAccessibility.Core.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.Navigation;

public sealed class ConfirmReadinessTransitionTests
{
    [Fact]
    public void LosingReadinessDuringTheFinalTurnReleasesThePadAndResumesWithANewTurnDeadline()
    {
        var controller = new NavigationController();
        var turning = controller.Handle(NavigationCommand.ToggleWalk, Frame(ready: true), 0);
        Assert.Equal(NavigationDirection.North, turning.Direction);

        // A newly touched actor or an unreadable native word can revoke readiness
        // after turning has already started. Waiting must release that old input.
        var waiting = controller.Update(Frame(ready: false), 100);
        Assert.True(waiting.AutoWalking);
        Assert.Equal(NavigationDirection.None, waiting.Direction);
        Assert.DoesNotContain(waiting.Speech, s => s.StartsWith("Arrived"));
        Assert.Equal(NavigationDirection.None, controller.Update(Frame(ready: false), 1200).Direction);

        // Readiness returns before the pending timeout, but after the old turn's
        // deadline. The resumed turn gets its own deadline instead of failing.
        var resumed = controller.Update(Frame(ready: true), 1590);
        Assert.True(resumed.AutoWalking);
        Assert.Equal(NavigationDirection.North, resumed.Direction);
        var arrived = controller.Update(Frame(ready: true) with { PlayerFacing = NavigationDirection.North }, 1640);
        Assert.False(arrived.AutoWalking);
        Assert.Equal(NavigationDirection.None, arrived.Direction);
        Assert.Contains("Arrived at Seller.", arrived.Speech);
    }

    private static NavigationFrame Frame(bool ready)
    {
        var goal = new NavigationPoint(512, 256, 1);
        var target = new NavigationTarget("seller", "Seller", NavigationCategory.People,
            new(512, 0, 1), [goal], true, true)
        {
            ConfirmFacings = _ => ready ? [NavigationDirection.North] : [],
            ConfirmPending = _ => !ready,
        };
        return new("fair", true, goal, [target], new StandingPoint(), 256) { PlayerFacing = NavigationDirection.East };
    }

    private sealed class StandingPoint : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point) => [];
    }
}
