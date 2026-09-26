using ChronoTriggerAccessibility.Core.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.Navigation;

public sealed class LargeRouteTests
{
    [Fact]
    public void LargeDetourFitsTheFieldPositionBudgetDespiteMultipleApproachHeadings()
    {
        var graph = new DividedField();
        var start = new NavigationPoint(64, 0, 1);
        var goal = new NavigationPoint(192, 0, 1);
        var result = NavigationPathfinder.Search(graph, start, [goal]);
        Assert.False(result.LimitReached);
        Assert.NotNull(result.Route);
        Assert.Equal(635, result.Route.Count);
        foreach (var (from, to) in result.Route.Zip(result.Route.Skip(1)))
            Assert.Contains(to, graph.Neighbours(from));
    }

    [Fact]
    public void SmallCallerBudgetStillStopsTheSameDetour()
    {
        var result = NavigationPathfinder.Search(new DividedField(), new(64, 0, 1), [new(192, 0, 1)], 100);
        Assert.True(result.LimitReached);
        Assert.Null(result.Route);
    }

    private sealed class DividedField : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point)
        {
            foreach (var next in new[] { point with { X = point.X + 1 }, point with { X = point.X - 1 },
                         point with { Y = point.Y + 1 }, point with { Y = point.Y - 1 } })
                if (next.X is >= 0 and < 256 && next.Y is >= 0 and < 256 &&
                    (next.X != 128 || next.Y >= 253)) yield return next;
        }
    }
}
