using ChronoTriggerAccessibility.Core.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.Navigation;

public sealed class NavigationTests
{
    private static NavigationPoint P(int x, int y, int layer = 1) => new(x, y, layer);
    private static NavigationTarget Target(string id, NavigationCategory category, NavigationPoint point,
        bool visible = true, bool discovered = false) => new(id, id, category, point, [point], visible, discovered);
    private static NavigationFrame Frame(params NavigationTarget[] targets) => new("room-a", true, P(0, 0), targets, new Grid());

    [Fact]
    public void CategoryAndTargetCyclingWrapAndExcludeHiddenUndiscoveredTargets()
    {
        var controller = new NavigationController();
        var frame = Frame(Target("Mother", NavigationCategory.People, P(16, 0)),
            Target("Hidden actor", NavigationCategory.People, P(0, 8), false),
            Target("Villager", NavigationCategory.People, P(0, 16), false, true),
            Target("Stairs", NavigationCategory.Exits, P(16, 16)),
            Target("Chest", NavigationCategory.Objects, P(24, 16)));

        Assert.Contains("Mother", Say(controller.Handle(NavigationCommand.Repeat, frame, 0)));
        Assert.Contains("Villager", Say(controller.Handle(NavigationCommand.NextTarget, frame, 1)));
        Assert.Contains("Mother", Say(controller.Handle(NavigationCommand.NextTarget, frame, 2)));
        Assert.Contains("Villager", Say(controller.Handle(NavigationCommand.PreviousTarget, frame, 3)));
        Assert.Contains("Interactable Objects", Say(controller.Handle(NavigationCommand.PreviousCategory, frame, 4)));
        Assert.Contains("People", Say(controller.Handle(NavigationCommand.NextCategory, frame, 5)));
        Assert.Contains("Stairs", Say(controller.Handle(NavigationCommand.NextCategory, frame, 6)));
    }

    [Fact]
    public void ExplicitRepeatAlwaysSpeaksAndEmptyCategoriesAreReported()
    {
        var controller = new NavigationController();
        var frame = Frame(Target("Mother", NavigationCategory.People, P(16, 0)));
        var first = Say(controller.Handle(NavigationCommand.Repeat, frame, 0));
        Assert.NotEmpty(first);
        Assert.Equal(first, Say(controller.Handle(NavigationCommand.Repeat, frame, 1)));
        Assert.Contains("No destinations", Say(controller.Handle(NavigationCommand.NextCategory, frame, 2)));
    }

    [Fact]
    public void GuidanceRoutesAroundWallAndDoesNotWalkUntilRequested()
    {
        var controller = new NavigationController();
        var frame = Frame(Target("Door", NavigationCategory.People, P(8, 0))) with { Graph = new Grid(P(4, 0)) };
        var guide = controller.Handle(NavigationCommand.Guide, frame, 0);
        Assert.True(guide.Guiding);
        Assert.False(guide.AutoWalking);
        Assert.Equal(NavigationDirection.None, guide.Direction);
        Assert.Contains("south", Say(guide));
        var walk = controller.Handle(NavigationCommand.ToggleWalk, frame, 1);
        Assert.True(walk.AutoWalking);
        Assert.Equal(NavigationDirection.South, walk.Direction);
        Assert.False(controller.Handle(NavigationCommand.ToggleWalk, frame, 2).AutoWalking);
    }

    [Fact]
    public void RouteUsesOnlyGraphEdgesAndDoesNotCrossLayersWithoutAnEdge()
    {
        var graph = new Grid(P(4, 0));
        var path = NavigationPathfinder.Find(graph, P(0, 0), [P(8, 0)]);
        Assert.NotNull(path);
        Assert.Equal(P(0, 0), path[0]);
        Assert.Equal(P(8, 0), path[^1]);
        Assert.DoesNotContain(P(4, 0), path);
        foreach (var (from, to) in path.Zip(path.Skip(1))) Assert.Contains(to, graph.Neighbours(from));
        Assert.Null(NavigationPathfinder.Find(graph, P(0, 0), [P(8, 0, 2)]));
        Assert.Null(NavigationPathfinder.Find(graph, P(0, 0), [P(8, 0)], 2));
    }

    [Theory]
    [InlineData("scene")]
    [InlineData("control")]
    [InlineData("target")]
    [InlineData("manual")]
    public void MovementStopsAndDoesNotResumeAfterLosingItsAuthority(string cause)
    {
        var controller = new NavigationController();
        var frame = Frame(Target("Mother", NavigationCategory.People, P(16, 0)));
        Assert.True(controller.Handle(NavigationCommand.ToggleWalk, frame, 0).AutoWalking);
        var changed = cause switch
        {
            "scene" => frame with { Scene = "room-b" },
            "control" => frame with { CanNavigate = false },
            "target" => frame with { Targets = [] },
            _ => frame,
        };
        var stopped = controller.Update(changed, 50, cause == "manual");
        Assert.False(stopped.AutoWalking);
        Assert.False(stopped.Guiding);
        Assert.Equal(NavigationDirection.None, stopped.Direction);
        Assert.Contains("stopped", Say(stopped));
        Assert.False(controller.Update(frame, 100).AutoWalking);
    }

    [Fact]
    public void StuckTimeoutAndArrivalStopMovementAndAnnounce()
    {
        var controller = new NavigationController();
        var frame = Frame(Target("Mother", NavigationCategory.People, P(16, 0)));
        controller.Handle(NavigationCommand.ToggleWalk, frame, 0);
        Assert.True(controller.Update(frame, 500).AutoWalking);
        var stuck = controller.Update(frame, 1600);
        Assert.False(stuck.AutoWalking);
        Assert.Contains("blocked", Say(stuck));
        controller.Handle(NavigationCommand.ToggleWalk, frame, 2000);
        var arrived = controller.Update(frame with { Player = P(16, 0) }, 2100);
        Assert.False(arrived.AutoWalking);
        Assert.Equal(NavigationDirection.None, arrived.Direction);
        Assert.Contains("Arrived", Say(arrived));
    }

    [Fact]
    public void BrowsingDestinationsStopsExistingWalkAndSelectionSurvivesReordering()
    {
        var controller = new NavigationController();
        var mother = Target("Mother", NavigationCategory.People, P(16, 0));
        var cat = Target("Cat", NavigationCategory.People, P(16, 16));
        var frame = Frame(mother, cat);
        controller.Handle(NavigationCommand.ToggleWalk, frame, 0);
        var selection = controller.Handle(NavigationCommand.NextTarget, frame, 1);
        Assert.False(selection.AutoWalking);
        Assert.Contains("Cat", Say(selection));
        Assert.Contains("Cat", Say(controller.Handle(NavigationCommand.Repeat, frame with { Targets = [cat, mother] }, 2)));
    }

    [Fact]
    public void UnreachableTargetNeverStartsWalking()
    {
        var controller = new NavigationController();
        var frame = Frame(Target("Door", NavigationCategory.People, P(16, 0, 2)));
        var result = controller.Handle(NavigationCommand.ToggleWalk, frame, 0);
        Assert.False(result.AutoWalking);
        Assert.Equal(NavigationDirection.None, result.Direction);
        Assert.Contains("No route", Say(result));
    }

    private static string Say(NavigationResult result) => string.Join(" ", result.Speech);

    [Fact]
    public void NavigationSpeechReachesTheSharedNarratorAndExplicitRepeatsAreNotDeduplicated()
    {
        var state = new ChronoTriggerAccessibility.Core.State.AccessibilityState();
        var value = new NavigationAnnouncement("Person, north.");
        Assert.Equal(value.Text, Assert.Single(state.Apply(value)).Text);
        Assert.Equal(value.Text, Assert.Single(state.Apply(value)).Text);
    }

    [Fact]
    public void NativeFixedPointUsesTileDistanceAndOnePixelArrivalTolerance()
    {
        var controller = new NavigationController();
        var frame = new NavigationFrame("room", true, P(0, 0),
            [Target("Exit", NavigationCategory.People, P(256, 0))], new NativeLine(), 256);
        Assert.Contains("1 tiles away", Say(controller.Handle(NavigationCommand.Repeat, frame, 0)));
        Assert.True(controller.Handle(NavigationCommand.ToggleWalk, frame, 1).AutoWalking);
        Assert.True(controller.Update(frame with { Player = P(224, 0) }, 2).AutoWalking);
        Assert.Contains("Arrived", Say(controller.Update(frame with { Player = P(248, 0) }, 3)));
    }

    [Fact]
    public void ExplicitCancellationDisarmsUntilAnotherStartCommand()
    {
        var controller = new NavigationController();
        var frame = Frame(Target("Door", NavigationCategory.People, P(16, 0)));
        controller.Handle(NavigationCommand.ToggleWalk, frame, 0);
        Assert.Contains("dialogue", Say(controller.Cancel("dialogue")));
        Assert.False(controller.Update(frame, 1).AutoWalking);
        Assert.Empty(controller.Cancel("dialogue").Speech);
    }

    private sealed class NativeLine : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point)
        {
            if (point.X < 256) yield return point with { X = (point.X / 64 + 1) * 64 };
            if (point.X > 0) yield return point with { X = ((point.X - 1) / 64) * 64 };
        }
    }

    [Fact]
    public void ArrivalToleranceAgreesWithTheDirectionDeadzoneOnBothAxes()
    {
        var controller = new NavigationController();
        var goal = P(256, 256);
        var frame = new NavigationFrame("room", true, P(0, 256),
            [Target("Door", NavigationCategory.People, goal)], new NativeLine(), 256);
        Assert.True(controller.Handle(NavigationCommand.ToggleWalk, frame, 0).AutoWalking);
        var arrived = controller.Update(frame with { Player = P(248, 248) }, 16);
        Assert.False(arrived.AutoWalking);
        Assert.Contains("Arrived", Say(arrived));
    }

    [Fact]
    public void SearchLimitIsDistinguishedFromAnUnreachableDestination()
    {
        Assert.True(NavigationPathfinder.Search(new Grid(), P(0, 0), [P(32, 32)], 2).LimitReached);
        var unreachable = NavigationPathfinder.Search(new Grid(), P(0, 0), [P(32, 32, 2)]);
        Assert.False(unreachable.LimitReached);
        Assert.Null(unreachable.Route);
    }

    private sealed class Grid(params NavigationPoint[] walls) : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point)
        {
            foreach (var next in new[] { point with { Y = point.Y - 4 }, point with { X = point.X + 4 },
                point with { Y = point.Y + 4 }, point with { X = point.X - 4 } })
                if (next.X >= 0 && next.Y >= 0 && next.X <= 32 && next.Y <= 32 && !walls.Contains(next)) yield return next;
        }
    }
}
