using ChronoTriggerAccessibility.Core.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.Navigation;

public sealed class CountedGuidanceTests
{
    [Fact]
    public void GivesCountedLegsAndRepeatsRemainingDistanceWithoutFrameByFrameChatter()
    {
        var controller = new NavigationController();
        var frame = Frame();
        var initial = controller.Handle(NavigationCommand.Guide, frame, 0);
        Assert.Contains("Left 3 steps, then down 2 steps.", Say(initial));
        Assert.Equal(NavigationDirection.None, initial.Direction);

        for (var x = 752; x >= 512; x -= 16)
            Assert.Empty(controller.Update(frame with { Player = new(x, 0, 1) }, 768 - x).Speech);
        Assert.Contains("Left 2 steps, then down 2 steps.", Say(controller.Handle(
            NavigationCommand.Repeat, frame with { Player = new(512, 0, 1) }, 300)));

        var turn = controller.Update(frame with { Player = new(0, 0, 1) }, 800);
        Assert.Contains("Down 2 steps.", Say(turn));
        Assert.Empty(controller.Update(frame with { Player = new(0, 32, 1) }, 832).Speech);
        Assert.Contains("Arrived", Say(controller.Update(frame with { Player = new(0, 512, 1) }, 1500)));
    }

    [Fact]
    public void PassingSeveralWaypointsAdvancesInsteadOfSendingThePlayerBackwards()
    {
        var controller = new NavigationController();
        var frame = Frame();
        Assert.Equal(NavigationDirection.West, controller.Handle(NavigationCommand.ToggleWalk, frame, 0).Direction);
        var progressed = controller.Update(frame with { Player = new(448, 0, 1) }, 300);
        Assert.Equal(NavigationDirection.West, progressed.Direction);
        Assert.Empty(progressed.Speech);
        Assert.Equal(NavigationDirection.South, controller.Update(frame with { Player = new(0, 0, 1) }, 750).Direction);
    }

    [Fact]
    public void EqualLengthOpenRoutesPreferOneTurnOverAStaircase()
    {
        var graph = new OpenGrid();
        var route = NavigationPathfinder.Find(graph, new(256, 256, 1), [new(1792, 1280, 1)])!;
        var directions = route.Zip(route.Skip(1)).Select(pair =>
            (Math.Sign(pair.Second.X - pair.First.X), Math.Sign(pair.Second.Y - pair.First.Y))).ToArray();
        Assert.Equal(40, directions.Length);
        Assert.Equal(1, directions.Zip(directions.Skip(1)).Count(pair => pair.First != pair.Second));
        foreach (var edge in route.Zip(route.Skip(1))) Assert.Contains(edge.Second, graph.Neighbours(edge.First));
    }

    [Fact]
    public void ReorderingOrAddingAlternativeGoalsDoesNotRestartTheInstruction()
    {
        var controller = new NavigationController();
        var frame = Frame();
        controller.Handle(NavigationCommand.Guide, frame, 0);
        var target = frame.Targets[0];
        var changed = frame with { Targets = [target with { ApproachPoints = [new(0, 448, 1), target.Position] }] };
        Assert.Empty(controller.Update(changed, 16).Speech);
    }

    private static string Say(NavigationResult result) => string.Join(" ", result.Speech);
    private static NavigationFrame Frame() => new("room", true, new(768, 0, 1),
        [new("stairs", "Stairs", NavigationCategory.People, new(0, 512, 1), [new(0, 512, 1)], true, true)],
        new Corridor(), 256);

    private sealed class Corridor : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point)
        {
            foreach (var next in Adjacent(point))
                if ((next.Y == 0 && next.X >= 0 && next.X <= 768) ||
                    (next.X == 0 && next.Y >= 0 && next.Y <= 512)) yield return next;
        }
    }

    private sealed class OpenGrid : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point) => Adjacent(point)
            .Where(p => p.X >= 0 && p.Y >= 0 && p.X <= 2048 && p.Y <= 2048);
    }

    private static IEnumerable<NavigationPoint> Adjacent(NavigationPoint point)
    {
        yield return point with { Y = point.Y - 64 };
        yield return point with { X = point.X + 64 };
        yield return point with { Y = point.Y + 64 };
        yield return point with { X = point.X - 64 };
    }
}
