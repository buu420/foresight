using ChronoTriggerAccessibility.Core.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.Navigation;

public sealed class StagedNavigationTests
{
    [Theory]
    [InlineData(NavigationCommand.Guide)]
    [InlineData(NavigationCommand.ToggleWalk)]
    public void ReachingOpeningTriggerContinuesToDestinationOnlyAfterLiveFloorChanges(NavigationCommand command)
    {
        var controller = new NavigationController();
        var graph = new DoorGraph();
        var target = new NavigationTarget("queen", "Queen's chamber", NavigationCategory.People,
            new(48, 0, 1), [new(48, 0, 1)], true, true);
        var frame = new NavigationFrame("castle", true, new(0, 0, 1), [target], graph);
        Assert.True(controller.Handle(command, frame, 0).Guiding);
        frame = frame with { Player = new(16, 0, 1) };
        var waiting = controller.Update(frame, 300);
        Assert.True(waiting.Guiding);
        Assert.Equal(NavigationDirection.None, waiting.Direction);
        Assert.DoesNotContain(waiting.Speech, s => s.StartsWith("Arrived"));
        Assert.Empty(controller.Update(frame, 400).Speech);
        graph.Open = true;
        var continuing = controller.Update(frame, 800);
        Assert.True(continuing.Guiding);
        Assert.Equal(command == NavigationCommand.Guide ? NavigationDirection.None : NavigationDirection.East, continuing.Direction);
        Assert.DoesNotContain(continuing.Speech, s => s.StartsWith("Arrived"));
        var arrived = controller.Update(frame with { Player = new(48, 0, 1) }, 900);
        Assert.False(arrived.Guiding);
        Assert.Contains("Arrived at Queen's chamber.", arrived.Speech);
    }

    [Fact]
    public void LeavingAndReturningToAContactStartsAFreshWait()
    {
        var controller = new NavigationController();
        var frame = Frame(new DoorGraph());
        controller.Handle(NavigationCommand.Guide, frame, 0);
        var contact = frame with { Player = new(16, 0, 1) };
        controller.Update(contact, 300);
        var away = controller.Update(frame with { Player = new(8, 0, 1) }, 4000);
        Assert.True(away.Guiding);
        Assert.NotNull(away.ManualLeg);
        var returned = controller.Update(contact, 5000);
        Assert.True(returned.Guiding);
        Assert.Contains(returned.Speech, s => s.StartsWith("Waiting"));
        Assert.DoesNotContain(returned.Speech, s => s.Contains("did not open"));
    }

    [Fact]
    public void StagedRouteSurvivesReorderedAndAdditionalDestinationApproaches()
    {
        var controller = new NavigationController();
        var graph = new DoorGraph();
        var frame = Frame(graph);
        controller.Handle(NavigationCommand.Guide, frame, 0);
        Assert.Equal(1, graph.StageSearches);
        controller.Update(frame with { Player = new(4, 0, 1),
            Targets = [frame.Targets[0] with { ApproachPoints = [new(44, 0, 1), new(48, 0, 1)] }] }, 100);
        Assert.Equal(1, graph.StageSearches);
    }

    private static NavigationFrame Frame(INavigationGraph graph) => new("castle", true, new(0, 0, 1),
        [new("queen", "Queen's chamber", NavigationCategory.People, new(48, 0, 1), [new(48, 0, 1)], true, true)], graph);

    [Fact]
    public void ATriggerThatDoesNotOpenCannotBeClaimedAsTheDestination()
    {
        var controller = new NavigationController();
        var frame = new NavigationFrame("castle", true, new(0, 0, 1),
            [new("queen", "Queen's chamber", NavigationCategory.People, new(48, 0, 1), [new(48, 0, 1)], true, true)], new DoorGraph());
        controller.Handle(NavigationCommand.ToggleWalk, frame, 0);
        frame = frame with { Player = new(16, 0, 1) };
        controller.Update(frame, 300);
        var stopped = controller.Update(frame, 3400);
        Assert.False(stopped.Guiding);
        Assert.Contains(stopped.Speech, s => s.Contains("passage did not open"));
        Assert.DoesNotContain(stopped.Speech, s => s.StartsWith("Arrived"));
    }

    private sealed class DoorGraph : IStagedNavigationGraph
    {
        public bool Open { get; set; }
        public int StageSearches { get; private set; }
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint p)
        {
            if (p.X > 0) yield return p with { X = p.X - 4 };
            if (p.X < (Open ? 48 : 16)) yield return p with { X = p.X + 4 };
        }
        public NavigationSearchResult FindStage(NavigationPoint start, IReadOnlyList<NavigationPoint> goals, int maximumVisited)
        {
            StageSearches++;
            return new(Enumerable.Range(start.X / 4, (16 - start.X) / 4 + 1).Select(x => new NavigationPoint(x * 4, 0, 1)).ToArray(), false, "stairs");
        }
    }
}
