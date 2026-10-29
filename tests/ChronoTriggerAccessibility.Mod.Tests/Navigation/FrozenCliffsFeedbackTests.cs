using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class FrozenCliffsFeedbackTests
{
    [Fact]
    public void TheSlideExplainsStoppingAndTheOptInAssistOnce()
    {
        var spoken=new List<string>();var background=new List<string>();
        var feedback=new FieldFloorFeedback(spoken.Add,_=>{},background:background.Add);
        for(int i=0;i<20;i++)
        {
            feedback.Observe(Floor(32));feedback.Observe(Floor(0));feedback.Observe(null);
        }
        var cue=Assert.Single(background);
        Assert.Empty(spoken);Assert.Contains("hold Up",cue);Assert.Contains("automatic walking",cue);
        Assert.DoesNotContain("belt",cue);
    }

    [Fact]
    public void RepeatStatusIncludesTheSlideControlsAndRevokesOnUnavailableCapture()
    {
        var feedback=new FieldFloorFeedback(_=>{},_=>{});
        feedback.Observe(Floor(32));Assert.Contains("hold Up",feedback.Status);
        feedback.Suspend();Assert.Null(feedback.Status);
        feedback.Observe(Floor(0));Assert.Equal("Off the downhill slide.",feedback.Status);
    }

    [Fact]
    public void WalkingSettingNeedsAReadableRunningInstruction()
    {
        var spoken=new List<string>();var feedback=new FieldFloorFeedback(spoken.Add,_=>{});
        feedback.Observe(Floor(32) with{RunToggle=2});
        Assert.Contains("tap and release Dash",Assert.Single(spoken),StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HeldDashModeExplainsHowToRunDuringManualGuidance()
    {
        var feedback=new FieldFloorFeedback(_=>{},_=>{});
        feedback.Observe(Floor(32) with{RunMode=1,RunToggle=2});
        Assert.Contains("Hold Dash",feedback.Status);
    }

    [Fact]
    public void ReenteringTheSlopeCanRepeatItsInstructions()
    {
        var spoken=new List<string>();var feedback=new FieldFloorFeedback(spoken.Add,_=>{});
        feedback.Observe(Floor(32));feedback.Observe(Floor(0) with{Scene=603});feedback.Observe(Floor(32));
        Assert.Equal(2,spoken.Count);
    }
    private static FieldFloorSnapshot Floor(int y)=>new(0x1000,0x4000,604,3,0,y,0,1);
}
