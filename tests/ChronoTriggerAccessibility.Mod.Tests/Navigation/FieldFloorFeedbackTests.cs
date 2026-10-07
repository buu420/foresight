using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class FieldFloorFeedbackTests
{
    [Fact]
    public void EntryDirectionChangeAndExitAreAnnouncedOnce()
    {
        var speech = new List<string>();
        var feedback = new FieldFloorFeedback(speech.Add, _ => { });
        feedback.Observe(Floor(16, 0));
        for (var i = 0; i < 100; i++) feedback.Observe(Floor(16, 0));
        Assert.Single(speech);
        Assert.Contains("Conveyor moving east", speech[0]);
        feedback.Observe(Floor(-32, 0));
        Assert.Contains("west", speech[1]);
        Assert.Contains("cannot move against", speech[1]);
        feedback.Observe(Floor(0, 0));
        feedback.Observe(Floor(0, 0));
        Assert.Equal("Off the conveyor.", speech[2]);
        Assert.Equal(3, speech.Count);
        Assert.Null(feedback.Status);
    }

    [Fact]
    public void AnUnreadableTickDoesNotInventAnExitOrRepeatTheSameEntry()
    {
        var speech = new List<string>();
        var feedback = new FieldFloorFeedback(speech.Add, _ => { });
        feedback.Observe(Floor(0, -16));
        feedback.Observe(null);
        Assert.Null(feedback.Current);
        Assert.Null(feedback.Status);
        feedback.Observe(Floor(0, -16));
        Assert.Single(speech);
        Assert.Contains("north", feedback.Status);
        feedback.Reset();
        feedback.Observe(Floor(0, -16));
        Assert.Equal(2, speech.Count);
    }

    [Fact]
    public void SceneChangesDoNotClaimThePreviousConveyorWasWalkedOff()
    {
        var speech = new List<string>();
        var feedback = new FieldFloorFeedback(speech.Add, _ => { });
        feedback.Observe(Floor(-32, 0));
        feedback.Observe(Floor(0, 0) with { Scene = 226 });
        Assert.Single(speech);
        feedback.Observe(Floor(0, 8) with { Scene = 228 });
        Assert.Contains("Moving floor", speech[1]);
        Assert.DoesNotContain("Conveyor", speech[1]);
    }

    private static FieldFloorSnapshot Floor(int x, int y) => new(0x1000, 0x4000, 231, 1, x, y, 1, 2);
}
