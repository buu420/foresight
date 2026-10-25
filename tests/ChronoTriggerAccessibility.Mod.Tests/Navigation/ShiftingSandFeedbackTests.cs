using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class ShiftingSandFeedbackTests
{
    [Fact]
    public void RapidCellChangesKeepCurrentStatusWithoutFloodingSpeech()
    {
        var speech = new List<string>();
        var feedback = new FieldFloorFeedback(speech.Add,_=>{},()=>0);
        for(var n=0;n<100;n++) feedback.Observe(Floor(n%2==0?16:-16));
        Assert.InRange(speech.Count,1,2);
        Assert.Contains("west",feedback.Status);
        Assert.Contains("sand",feedback.Status!,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("conveyor",string.Join(" ",speech),StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PendingChangesPublishOnlyTheLatestDirectionAfterTheInterval()
    {
        long now=0;var queued=new List<string>();var urgent=new List<string>();
        var feedback=new FieldFloorFeedback(urgent.Add,_=>{},()=>now,queued.Add);
        feedback.Observe(Floor(16));now=100;feedback.Observe(Floor(-16));
        now=300;feedback.Observe(Floor(0) with{ForceY=16});
        Assert.Single(queued);Assert.Contains("south",feedback.Status!);
        now=1500;feedback.Observe(Floor(0) with{ForceY=16});
        Assert.Equal(2,queued.Count);Assert.Contains("south",queued[1]);Assert.DoesNotContain("west",queued[1]);
        Assert.Empty(urgent);
    }

    [Fact]
    public void AMenuPauseRevokesCurrentFactsWithoutInventingAnOffSandMessage()
    {
        long now=0;var speech=new List<string>();var feedback=new FieldFloorFeedback(speech.Add,_=>{},()=>now);
        feedback.Observe(Floor(16));feedback.Suspend();Assert.Null(feedback.Current);Assert.Null(feedback.Status);
        now=100;feedback.Observe(Floor(16));Assert.Single(speech);
        feedback.Observe(null);Assert.Null(feedback.Current);
        now=1500;feedback.Observe(Floor(0));Assert.Equal("Off the shifting sand.",speech.Last());
    }

    [Fact]
    public void WalkingToggleAdviceNamesTheActualTapAndReleaseRequirement()
    {
        var feedback = new FieldFloorFeedback(_=>{},_=>{});
        feedback.Observe(Floor(16) with { RunToggle=2 });
        Assert.Contains("tap",feedback.Status!,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("release",feedback.Status!,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectedTerrainProbeDescribesOnlyTheCommittedPush()
    {
        var speech=new List<string>();var feedback=new FieldFloorFeedback(speech.Add,_=>{});
        var floor=Floor(16) with{TerrainSampleRejected=true};feedback.Observe(floor);
        Assert.Equal(floor,feedback.Current);Assert.Contains("pushes you east",feedback.Status!);
        Assert.Contains("pushes you east",Assert.Single(speech));
        Assert.DoesNotContain("flows",feedback.Status!);
    }

    [Fact]
    public void ChangingRunOnSolidGroundNamesTheSettingWithoutClaimingThePlayerLeftSand()
    {
        var speech=new List<string>();long now=0;var feedback=new FieldFloorFeedback(speech.Add,_=>{},()=>now);
        feedback.Observe(Floor(0));now=100;feedback.Observe(Floor(0) with{RunToggle=2});
        Assert.Single(speech);Assert.Contains("Walking",speech[0]);Assert.DoesNotContain("Off",speech[0]);
        now=200;feedback.Observe(Floor(0));Assert.Contains("Release Dash",speech[1]);
    }

    [Fact]
    public void RunModeOneRequestsRunningForPerpendicularSandTravelToo()
    {
        var frame = new ChronoTriggerAccessibility.Core.Navigation.NavigationFrame("00001000:160",true,new(0,0,1),[],new Empty(),256)
        { MovingFloor=new(ChronoTriggerAccessibility.Core.Navigation.NavigationDirection.East,16) };
        var input=FieldFloorMovement.Adjust(frame,new([],ChronoTriggerAccessibility.Core.Navigation.NavigationDirection.North,true,true),Floor(16) with {RunMode=1,RunToggle=2});
        Assert.Null(input.StopReason);Assert.Equal(0x808u,input.Pad);
    }
    private static FieldFloorSnapshot Floor(int x)=>new(0x1000,0x4000,160,6,x,0,0,1);
    private sealed class Empty:ChronoTriggerAccessibility.Core.Navigation.INavigationGraph
    {public IEnumerable<ChronoTriggerAccessibility.Core.Navigation.NavigationPoint> Neighbours(ChronoTriggerAccessibility.Core.Navigation.NavigationPoint p)=>[];}
}
