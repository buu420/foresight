using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class DeathPeakWindFeedbackTests
{
    [Fact]
    public void ManualSlopeAdviceTracksPushIndependentlyOfTheGust()
    {
        var speech = new List<string>();
        var feedback = new FieldFloorFeedback(speech.Add, _ => { });
        feedback.Observe(Floor(16, false));
        Assert.Contains("pushes", Assert.Single(speech));
        Assert.DoesNotContain("Run to climb", speech[0]);
        Assert.Contains("running", speech[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("downhill", feedback.Status);
        feedback.Observe(Floor(0, false));
        Assert.Contains("push stops", speech[1]);
        Assert.Contains("No downhill push", feedback.Status);
        Assert.DoesNotContain(speech, s => s.Contains("floor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SuspendingMotionDoesNotInterruptAdviceWithUnchangedWindFacts()
    {
        var speech = new List<string>();
        var feedback = new FieldFloorFeedback(speech.Add, _ => { });
        feedback.Observe(Floor(16, true));
        speech.Clear();
        feedback.Suspend();
        Assert.Null(feedback.Current);
        Assert.Null(feedback.Status);
        feedback.Observe(Floor(16, true));
        Assert.Empty(speech);
        feedback.Observe(Floor(16, false));
        Assert.Contains("Gust passed", Assert.Single(speech));
        feedback.Reset();
        feedback.Observe(Floor(16, false));
        Assert.Contains("pushes", speech[1]);
        Assert.DoesNotContain("Run to climb", speech[1]);
    }

    [Fact]
    public void SimultaneousChangesAreOneUtterance()
    {
        var speech = new List<string>();
        var feedback = new FieldFloorFeedback(speech.Add, _ => { });
        feedback.Observe(Floor(16, false));
        speech.Clear();
        feedback.Observe(Floor(0, true));
        var text = Assert.Single(speech);
        Assert.Contains("Strong wind", text);
        Assert.Contains("push stops", text);
    }

    [Fact]
    public void KnownPushStaysReadableWhenTheGustPhaseIsUnknown()
    {
        var speech = new List<string>();
        var feedback = new FieldFloorFeedback(speech.Add, _ => { });
        feedback.Observe(Floor(16, null));
        Assert.Contains("pushes", Assert.Single(speech));
        Assert.DoesNotContain("Run to climb", speech[0]);
        Assert.Contains("downhill", feedback.Status);
        Assert.DoesNotContain("gust", feedback.Status!, StringComparison.OrdinalIgnoreCase);
        feedback.Observe(null);
        Assert.Null(feedback.Status);
        feedback.Observe(Floor(16, null));
        Assert.Single(speech);
    }

    [Fact]
    public void AFirstCalmSampleDoesNotClaimTheWindIsEasing()
    {
        var speech = new List<string>();
        var feedback = new FieldFloorFeedback(speech.Add, _ => { });
        feedback.Observe(Floor(0, false));
        Assert.Empty(speech);
        Assert.Contains("No strong gust", feedback.Status);
        feedback.Observe(Floor(0, true));
        Assert.Contains("Strong wind", Assert.Single(speech));
        feedback.Observe(Floor(0, false));
        Assert.Contains("Gust passed", speech[1]);
        for (var i = 0; i < 60; i++) feedback.Observe(Floor(0, false));
        Assert.Equal(2, speech.Count);
    }

    [Fact]
    public void TheNativeVisibleRampHasItsOwnWarningBeforeTheStrongGust()
    {
        var speech = new List<string>();
        var feedback = new FieldFloorFeedback(speech.Add, _ => { });
        feedback.Observe(Floor(16, false) with { WindBuilding = false });
        speech.Clear();
        feedback.Observe(Floor(16, false) with { WindBuilding = true });
        Assert.Contains("Wind changing", Assert.Single(speech));
        Assert.Contains("Wind changing", feedback.Status);
        for (var i = 0; i < 60; i++) feedback.Observe(Floor(16, false) with { WindBuilding = true });
        feedback.Observe(Floor(16, true) with { WindBuilding = false });
        Assert.Contains("Strong wind", speech[1]);
        feedback.Observe(Floor(16, false) with { WindBuilding = false });
        Assert.Contains("Gust passed", speech[2]);
        Assert.Equal(3, speech.Count);
    }

    [Fact]
    public void AnUnknownRampDoesNotInventCalmOrRepeatTheWarningWhenItRecovers()
    {
        var speech = new List<string>();
        var feedback = new FieldFloorFeedback(speech.Add, _ => { });
        feedback.Observe(Floor(0, false) with { WindBuilding = true });
        Assert.Contains("Wind changing", Assert.Single(speech));
        feedback.Observe(Floor(0, false) with { WindBuilding = null });
        Assert.DoesNotContain("changing", feedback.Status!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("easing", feedback.Status!, StringComparison.OrdinalIgnoreCase);
        feedback.Suspend();
        feedback.Observe(Floor(0, false) with { WindBuilding = true });
        Assert.Single(speech);
    }

    private static FieldFloorSnapshot Floor(int force, bool? gust) =>
        new(0x1000, 0x2000, 244, 6, 0, force, 1, 2) { WindGusting = gust };
}
