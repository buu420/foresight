using ChronoTriggerAccessibility.Core.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.Navigation;

public sealed class StoryNoteTests
{
    private static NavigationTarget Note() => new("story:door", "Leave for the fair",
        NavigationCategory.StoryEvents, default, [], false, false)
    { IsStoryNote = true, Instruction = "The scene is still in progress. Listen to the conversation." };

    [Fact]
    public void CurrentObjectiveCanBeRepeatedWithoutInventingABearingOrARoute()
    {
        var frame = new NavigationFrame("room", true, new(2000, 2000, 1), [Note()], new NoSearch());
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.PreviousCategory, frame, 0);
        var selection = controller.Handle(NavigationCommand.PreviousCategory, frame, 0);
        Assert.Contains(selection.Speech, s => s.Contains("Leave for the fair") && s.Contains("still in progress"));
        Assert.DoesNotContain(selection.Speech, s => s.Contains("steps away") || s.Contains("another level"));
        foreach (var command in new[] { NavigationCommand.Repeat, NavigationCommand.Guide, NavigationCommand.ToggleWalk })
        {
            var result = controller.Handle(command, frame, 16);
            Assert.False(result.Guiding);
            Assert.False(result.AutoWalking);
            Assert.Equal(NavigationDirection.None, result.Direction);
            Assert.Contains(result.Speech, s => s.Contains("still in progress"));
        }
    }

    [Fact]
    public void NoteCannotRevealAnUndiscoveredPersonInAnotherCategory()
    {
        var frame = new NavigationFrame("room", true, default,
            [Note() with { Category = NavigationCategory.People }], new NoSearch());
        var result = new NavigationController().Handle(NavigationCommand.Repeat, frame, 0);
        Assert.DoesNotContain(result.Speech, s => s.Contains("Leave for the fair"));
    }

    private sealed class NoSearch : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point) =>
            throw new InvalidOperationException("A narrative objective must never search a route.");
    }
}
