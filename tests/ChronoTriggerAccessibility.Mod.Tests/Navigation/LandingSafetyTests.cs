using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class LandingSafetyTests
{
    private static readonly NavigationPoint Start = new(2176, 10368, 1);
    private static readonly NavigationPoint Goal = new(3200, 7296, 1);

    [Fact]
    public void AChangedNativeExitCannotBeUsedAsTheCatalogueStairFlight()
    {
        var map = LandingRouteTests.Stairwell() with
        {
            ExitDestinations = new Dictionary<int, int> { [0] = 497 },
        };
        var graph = FieldLandingGraph.Create(new FieldNavigationGraph(map), map,
            GameNavigationCatalog.ForScene(468));
        Assert.Null(NavigationPathfinder.Search(graph, Start, [Goal]).Route);
    }

    [Fact]
    public void AContactNeededBeforeTheStairsKeepsItsOwnStageIdentity()
    {
        var map = LandingRouteTests.Stairwell();
        var graph = FieldLandingGraph.Create(new PassageBeforeStairs(), map,
            GameNavigationCatalog.ForScene(468));
        var result = NavigationPathfinder.Search(graph, Start, [Goal]);
        Assert.NotNull(result.Route);
        Assert.Equal("landmark:30", result.IntermediateId);
    }

    private sealed class PassageBeforeStairs : IStagedNavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point) => [];
        public NavigationSearchResult FindStage(NavigationPoint start,
            IReadOnlyList<NavigationPoint> goals, int maximumVisited)
        {
            if (start == Start)
                return goals.Contains(Goal) ? new(null, false)
                    : new([start, new(2304, 10432, 1)], false, "landmark:30");
            return new([start, Goal], false);
        }
    }
}
