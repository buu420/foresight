using ChronoTriggerAccessibility.Core.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.Navigation;

/// <summary>An intermediate leg is either a passage that has to open where the player is
/// standing, or a way out they have to take. Telling someone to wait for a passage when
/// what they need to do is walk through a door leaves them stuck with no idea why.</summary>
public sealed class StagedLegTests
{
    private static readonly NavigationPoint Leg = new(256, 0, 1);
    private static readonly NavigationPoint Goal = new(4096, 0, 1);

    private static NavigationFrame Frame(string stage, NavigationPoint player) =>
        new("room", true, player,
            [new("thing", "Thing", NavigationCategory.People, Goal, [Goal], true, true)],
            new Staged(stage));

    [Fact]
    public void AnExitLegAsksThePlayerToTakeItRatherThanToWait()
    {
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.ToggleWalk, Frame("exit:0", new(0, 0, 1)), 0);
        var arrived = controller.Update(Frame("exit:0", Leg), 100);
        Assert.Contains(arrived.Speech, s => s.Contains("Take") && s.Contains("continue"));
        Assert.DoesNotContain(arrived.Speech, s => s.Contains("Waiting for the passage"));
    }

    [Fact]
    public void APassageLegStillAsksThePlayerToWait()
    {
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.ToggleWalk, Frame("terrain:9", new(0, 0, 1)), 0);
        var arrived = controller.Update(Frame("terrain:9", Leg), 100);
        Assert.Contains(arrived.Speech, s => s.Contains("Waiting for the passage"));
    }

    [Fact]
    public void SteppingAwayFromTheLegAndBackStartsTheWaitAgain()
    {
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.ToggleWalk, Frame("terrain:9", new(0, 0, 1)), 0);
        controller.Update(Frame("terrain:9", Leg), 100);
        // Walk off the contact, take longer than the give-up window getting back.
        controller.Update(Frame("terrain:9", new(0, 0, 1)), 200);
        var back = controller.Update(Frame("terrain:9", Leg), 9000);
        Assert.Contains(back.Speech, s => s.Contains("Waiting for the passage"));
        Assert.DoesNotContain(back.Speech, s => s.Contains("did not open"));
        Assert.True(controller.IsActive);
    }

    /// <summary>Walking is possible along the X axis, but the goal is never reached, so
    /// every search falls through to the stage.</summary>
    private sealed class Staged(string id) : IStagedNavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point) =>
            point.X < Leg.X ? [new(point.X + 256, point.Y, point.Layer)] : [];
        public NavigationSearchResult FindStage(NavigationPoint start, IReadOnlyList<NavigationPoint> goals,
            int maximumVisited) =>
            NavigationPathfinder.Search(new Plain(), start, [Leg], maximumVisited) with { IntermediateId = id };
        private sealed class Plain : INavigationGraph
        {
            public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point) =>
                point.X < Leg.X ? [new(point.X + 256, point.Y, point.Layer)] : [];
        }
    }
}
