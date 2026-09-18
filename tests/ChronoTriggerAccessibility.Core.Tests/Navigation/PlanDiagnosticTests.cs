using ChronoTriggerAccessibility.Core.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.Navigation;

/// <summary>A failed plan stops guidance, which clears the destination, so reading the
/// approach count off the destination afterwards always reported zero. That is the one
/// case the field exists to explain, and it made a target with goals look identical to a
/// target with none.</summary>
public sealed class PlanDiagnosticTests
{
    private static NavigationTarget Target(params NavigationPoint[] approaches) =>
        new("thing", "Thing", NavigationCategory.People, new(4096, 4096, 1), approaches, true, true);

    [Fact]
    public void AFailedPlanStillReportsHowManyApproachesTheSearchWasGiven()
    {
        var frame = new NavigationFrame("room", true, new(0, 0, 1),
            [Target(new(4096, 4352, 1), new(4352, 4096, 1), new(3840, 4096, 1))], new Walled());
        var controller = new NavigationController();
        var result = controller.Handle(NavigationCommand.ToggleWalk, frame, 0);
        Assert.Contains(result.Speech, s => s.Contains("No route"));
        Assert.Contains("approaches=3", controller.DiagnosticState);
    }

    [Fact]
    public void ATargetWithNoApproachesAtAllIsDistinguishable()
    {
        var frame = new NavigationFrame("room", true, new(0, 0, 1), [Target()], new Walled());
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.ToggleWalk, frame, 0);
        Assert.Contains("approaches=0", controller.DiagnosticState);
    }

    [Fact]
    public void AnIdleControllerReportsNoApproaches()
    {
        Assert.Contains("approaches=0", new NavigationController().DiagnosticState);
    }

    /// <summary>Every step is blocked, so the search exhausts and finds nothing.</summary>
    private sealed class Walled : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point) => [];
    }
}
