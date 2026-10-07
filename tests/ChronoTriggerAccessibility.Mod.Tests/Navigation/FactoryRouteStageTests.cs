using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class FactoryRouteStageTests
{
    [Theory]
    [InlineData(7154)]
    [InlineData(6802)]
    public void InspectionReturnUsesTheSouthernPassageBeforeOfferingTheCrane(int y)
    {
        var exits = Enumerable.Range(0, 6).Select(i => Target($"exit:{i}")).ToArray();
        var goals = FutureStoryTargets.Build(231, State(), [.. exits, Target("landmark:9")], new(1153, y, 1));
        var walkways = Assert.Single(goals, t => t.Label == "Reach the warehouse walkways");
        Assert.False(walkways.IsStoryNote);
        Assert.All(walkways.ApproachPoints, p => Assert.True(exits[4].ApproachPoints.Contains(p) || exits[5].ApproachPoints.Contains(p)));
        Assert.DoesNotContain(goals, t => t.Label == "Operate the crane" || t.Label == "Find the crane instructions");
    }

    [Fact]
    public void ConveyorPassageUsesTheDoorThatReturnsToTheUpperWalkway()
    {
        var west = Target("exit:0"); var upper = Target("exit:1");
        var goal = Assert.Single(FutureStoryTargets.Build(232, State(), [west, upper], new(5 * 256, 42 * 256, 1)));
        Assert.Equal(upper.ApproachPoints, goal.ApproachPoints);
    }

    [Fact]
    public void CraneControlAlcoveIsReachedThroughItsOwnDoorway()
    {
        var panel = Target("landmark:9"); var passage = Target("exit:1");
        var outside = FutureStoryTargets.Build(231, State(), [panel, passage], new(24 * 256, 29 * 256, 2));
        Assert.DoesNotContain(outside, t => t.Label == "Operate the crane");
        Assert.Contains(outside, t => t.Label == "Reach the passage to the crane" && t.ApproachPoints.SequenceEqual(passage.ApproachPoints));
        var inside = FutureStoryTargets.Build(231, State(), [panel, Target("exit:0")], new(13 * 256 + 128, 8 * 256 + 255, 1));
        Assert.Contains(inside, t => t.Label == "Operate the crane" && t.ApproachPoints.SequenceEqual(panel.ApproachPoints));
        var west = Target("exit:0"); var east = Target("exit:1");
        var beforeGoals = FutureStoryTargets.Build(233, State(), [west, east], default);
        var before = Assert.Single(beforeGoals, t => t.Label == "Enter the crane-control alcove");
        Assert.Equal(west.ApproachPoints, before.ApproachPoints);
        Assert.Contains(beforeGoals, t => t.Label == "Return to the warehouse walkway" && t.ApproachPoints.SequenceEqual(east.ApproachPoints));
        var after = Assert.Single(FutureStoryTargets.Build(233, State(0x64), [west, east], default));
        Assert.Equal(east.ApproachPoints, after.ApproachPoints);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(0x64)]
    public void CraneAlcoveOffersItsLiveReturnDoorBeforeDisconnectedDestinations(int flags)
    {
        var exit = Target("exit:0");
        var goals = FutureStoryTargets.Build(231, State(flags),
            [exit, Target("landmark:9"), Target("exit:2"), Target("exit:3"), Target("landmark:8")],
            new(13 * 256 + 128, 8 * 256 + 255, 1));
        var back = Assert.Single(goals, t => t.Label == "Return through the room before the crane");
        Assert.Equal(exit.ApproachPoints, back.ApproachPoints);
        Assert.DoesNotContain(goals, t => t.Label is "Find the crane instructions" or "Read the security code" or "Return to the factory entrance");
        if ((flags & 0x60) == 0x60) Assert.DoesNotContain(goals, t => t.Label == "Operate the crane");
    }

    private static FieldStoryState State(int flags = 4) => new(60, false) { Globals = new Dictionary<int, int> { [0x58] = flags } };
    private static NavigationTarget Target(string id) => new(id, id, NavigationCategory.Exits,
        default, [new(id[^1] * 256, 512, 1)], true, true);
}
