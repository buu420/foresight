using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class AlternativeDestinationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EitherOfTwoTouchDestinationsCanBeReachedWithoutIgnoringAnotherActor(bool firstBlocked)
    {
        var first = new NavigationPoint(1664, 1664, 1);
        var second = new NavigationPoint(2432, 1664, 1);
        var actors = new List<FieldActorSnapshot>
        {
            FullGameNavigationTests.Actor(9, first.X, first.Y),
            FullGameNavigationTests.Actor(10, second.X, second.Y)
        };
        if (firstBlocked) actors.Add(FullGameNavigationTests.Actor(35, first.X, first.Y));
        var field = FullGameNavigationTests.Field(412) with
        {
            Actors = actors
        };
        var graph = new FieldNavigationGraph(OpenMap(), new FieldActorCollisionRules(field),
            [(9, new[] { first }), (10, new[] { second })]);
        var route = NavigationPathfinder.Search(graph, new(896, 1664, 1), [first, second]).Route;
        Assert.NotNull(route);
        Assert.Equal(firstBlocked ? second : first, route[^1]);
        // Selecting both does not make either actor permeable on another route.
        Assert.DoesNotContain(first, graph.Neighbours(first with { X = first.X - 64 }));
    }

    [Fact]
    public void OffscreenAlternativeRemainsRoutableWhenVisibleGoalIsOnAnotherComponent()
    {
        var map = OpenMap();
        for (var y = 0; y < map.Height; y++) map.CollisionShapes[y * map.Width + 6] = 4;
        var player = new NavigationPoint(512, 1024, 1);
        var visible = Target("exit:0", new(2048, 1024, 1), true);
        var offscreen = Target("exit:1", new(512, 2048, 1), false);
        var objective = StoryTarget.BindAny("onward", "Continue onward", [visible, offscreen],
            [visible.Id, offscreen.Id], player);
        var route = NavigationPathfinder.Search(new FieldNavigationGraph(map), player, objective.ApproachPoints).Route;
        Assert.NotNull(route);
        Assert.Equal(offscreen.Position, route[^1]);
    }

    private static NavigationTarget Target(string id, NavigationPoint point, bool visible) =>
        new(id, "Passage", NavigationCategory.Exits, point, [point], visible, true);

    private static FieldMapSnapshot OpenMap() => new(13, 13, new byte[169], new byte[169],
        Enumerable.Repeat((byte)1, 169).ToArray(), 1, false, 13, 13,
        Enumerable.Repeat((byte)128, 169).ToArray());
}
