using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class TemporalLabFloorFeedbackTests
{
    [Fact]
    public void BeltBoundaryNoiseDoesNotRepeatTheSameWarningOrInterruptGuidance()
    {
        long now = 0; var speech = new List<string>(); var background = new List<string>();
        var feedback = new FieldFloorFeedback(speech.Add, _ => { }, () => now, background.Add);
        feedback.Observe(Floor(0, 0)); feedback.Observe(Floor(32, 0));
        for (var i = 1; i <= 100; i++)
        {
            now = i * 16; feedback.Observe(Floor(0, 0)); now += 1; feedback.Observe(Floor(32, 0));
        }
        Assert.Empty(speech);
        var entry = Assert.Single(background);
        Assert.Contains("Conveyor moving east", entry);
        Assert.Contains("cannot move against", entry);
        Assert.Contains("east", feedback.Status);
        Assert.DoesNotContain("Run to", feedback.Status);
    }

    [Fact]
    public void StableDirectionAndDepartureAreReportedWhileStatusStaysCurrent()
    {
        long now = 0; var speech = new List<string>();
        var feedback = new FieldFloorFeedback(speech.Add, _ => { }, () => now);
        feedback.Observe(Floor(32, 0));
        now = 2000; feedback.Observe(Floor(0, -32));
        Assert.Contains("north", feedback.Status);
        now = 2300; feedback.Observe(Floor(0, -32));
        Assert.Equal(2, speech.Count);
        Assert.Contains("north", speech[1]);
        now = 2400; feedback.Observe(Floor(0, 0));
        Assert.Null(feedback.Status);
        Assert.Equal(2, speech.Count);
        now = 4100; feedback.Observe(Floor(0, 0));
        Assert.Equal("Off the conveyor.", speech[2]);
        now = 9000; feedback.Observe(Floor(0, 0));
        Assert.Equal(3, speech.Count);
    }

    [Fact]
    public void MenuPauseDoesNotRepeatTheBeltButANewOwnerGetsItsOwnCue()
    {
        long now = 0; var speech = new List<string>();
        var feedback = new FieldFloorFeedback(speech.Add, _ => { }, () => now);
        feedback.Observe(Floor(-32, 0));
        feedback.Suspend(); Assert.Null(feedback.Status);
        now = 2000; feedback.Observe(Floor(-32, 0)); Assert.Single(speech);
        feedback.Observe(Floor(-32, 0) with { ActorBase = 0x8000 });
        Assert.Equal(2, speech.Count);
    }

    private static FieldFloorSnapshot Floor(int x, int y) => new(0x1000, 0x4000, 617, 1, x, y, 0, 1);
}
