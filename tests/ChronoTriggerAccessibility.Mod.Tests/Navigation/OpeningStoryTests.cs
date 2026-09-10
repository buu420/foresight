using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class OpeningStoryTests
{
    [Fact]
    public void BedroomObjectiveUsesOnlyAnAlreadyAvailableExit()
    {
        var state = new FieldStoryState(3, false);
        Assert.Empty(OpeningStoryTargets.Build(2, state, []));
        var exit = Target("exit:0", NavigationCategory.Exits);
        var story = Assert.Single(OpeningStoryTargets.Build(2, state, [exit]));
        Assert.Equal("Go downstairs", story.Label);
        Assert.Equal(NavigationCategory.StoryEvents, story.Category);
        Assert.Equal(exit.ApproachPoints, story.ApproachPoints);
        Assert.Empty(OpeningStoryTargets.Build(2, null, [exit]));
        Assert.Empty(OpeningStoryTargets.Build(2, state with { Point = 6 }, [exit]));
    }

    [Fact]
    public void KitchenObjectiveChangesAfterTheNativeIntroductionFlag()
    {
        var mother = Target("actor:15:4:37", NavigationCategory.People);
        var door = Target("exit:0", NavigationCategory.Exits);
        var before = Assert.Single(OpeningStoryTargets.Build(1, new(3, false), [mother, door]));
        Assert.Equal("Talk with Mother", before.Label);
        Assert.Equal(mother.ApproachPoints, before.ApproachPoints);
        var after = Assert.Single(OpeningStoryTargets.Build(1, new(3, true), [mother, door]));
        Assert.Equal("Leave for the Millennial Fair", after.Label);
        Assert.Equal(door.ApproachPoints, after.ApproachPoints);
        Assert.Empty(OpeningStoryTargets.Build(1, new(3, false), [door]));
        Assert.Empty(OpeningStoryTargets.Build(50, new(3, false), [mother, door]));
    }

    [Fact]
    public void StoryCategoryWrapsBothWaysAndStopsAnyPreviousRoute()
    {
        var controller = new NavigationController();
        var person = Target("person", NavigationCategory.People);
        var story = Target("story", NavigationCategory.StoryEvents);
        var frame = new NavigationFrame("room", true, new(0, 0, 1), [person, story], new Line());
        Assert.True(controller.Handle(NavigationCommand.ToggleWalk, frame, 0).AutoWalking);
        var previous = controller.Handle(NavigationCommand.PreviousCategory, frame, 16);
        Assert.Contains("Story Events.", previous.Speech);
        Assert.False(previous.AutoWalking);
        Assert.Contains("People.", controller.Handle(NavigationCommand.NextCategory, frame, 32).Speech);
        controller.Handle(NavigationCommand.NextCategory, frame, 48);
        controller.Handle(NavigationCommand.NextCategory, frame, 64);
        Assert.Contains("Story Events.", controller.Handle(NavigationCommand.NextCategory, frame, 80).Speech);
    }

    private static NavigationTarget Target(string id, NavigationCategory category) =>
        new(id, id, category, new(64, 0, 1), [new(64, 0, 1)], true, true);
    private sealed class Line : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point)
        { if (point.X < 64) yield return point with { X = point.X + 16 }; }
    }
}
