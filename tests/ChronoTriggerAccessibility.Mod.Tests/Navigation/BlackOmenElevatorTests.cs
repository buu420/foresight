using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using System.Text.Json;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class BlackOmenElevatorTests
{
    [Theory]
    [InlineData(1,3)]
    [InlineData(2,1)]
    [InlineData(4,2)]
    [InlineData(8,1)]
    [InlineData(5,3)]
    [InlineData(6,1)]
    [InlineData(12,1)]
    [InlineData(15,3)]
    public void CurrentNativeStopSelectsOnlyItsForwardControl(int flags,int actor)
    {
        var frame=Frame(flags);
        var story=Story(frame);
        var control=Control(frame,actor);
        Assert.Equal(control.ApproachPoints,story.ApproachPoints);
        Assert.Equal("Continue through Black Omen",story.Label);
        Assert.False(story.IsStoryNote);
        Assert.DoesNotContain("Next passage:",story.Instruction??"");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(0x10)]
    [InlineData(0x80)]
    public void UnknownStopCannotRouteBackThroughAnotherControl(int? flags)
    {
        var frame=Frame(flags);
        var story=Story(frame);
        Assert.Equal("Continue through Black Omen",story.Label);
        Assert.True(story.IsStoryNote);
        Assert.Empty(story.ApproachPoints);
        Assert.Contains("stop",story.Instruction!);
        foreach(var command in new[]{NavigationCommand.Guide,NavigationCommand.ToggleWalk})
        {
            var result=Select(frame,story,command);
            Assert.False(result.Guiding);Assert.False(result.AutoWalking);
            Assert.Equal(NavigationDirection.None,result.Direction);
            Assert.DoesNotContain(result.Speech,s=>s.Contains("End of Time")||s.Contains("Next passage:"));
        }
    }

    [Theory]
    [InlineData(1,3)]
    [InlineData(2,1)]
    [InlineData(4,2)]
    [InlineData(8,1)]
    public void UnavailableForwardControlDoesNotChooseAnAvailableReturn(int flags,int actor)
    {
        var frame=Frame(flags,missing:actor);
        var story=Story(frame);
        Assert.True(story.IsStoryNote);
        Assert.Empty(story.ApproachPoints);
        Assert.DoesNotContain("Next passage:",story.Instruction??"");
    }

    [Theory]
    [InlineData(1,2048,3583)]
    [InlineData(2,2048,3583)]
    [InlineData(4,2176,5119)]
    [InlineData(8,2176,5119)]
    [InlineData(1,1105,6624)]
    [InlineData(2,1105,6624)]
    [InlineData(4,1105,6624)]
    [InlineData(8,1105,6624)]
    public void BothGuidanceModesUseTheSameNativeControlFromReportedPositions(int flags,int x,int y)
    {
        // Native actor coordinates and logged player poses, explicit open-floor
        // geometry: this proves binding/controller integration, not live traversal.
        var frame=Frame(flags,new(x,y,1));var story=Story(frame);
        foreach(var command in new[]{NavigationCommand.Guide,NavigationCommand.ToggleWalk})
        {
            var result=Select(frame,story,command);
            Assert.True(result.Guiding,string.Join(" | ",result.Speech));
            Assert.Equal(command==NavigationCommand.ToggleWalk,result.AutoWalking);
            Assert.DoesNotContain(result.Speech,s=>s.Contains("No route")||s.Contains("End of Time"));
        }
    }

    [Fact]
    public void TheWarpLandingAndElevatorStoryRouteDoNotSendEachOtherBack()
    {
        var landing=BlackOmenNavigationTests.Frame(98,flags:4);
        Assert.Equal(landing.Targets.Single(t=>t.Id=="exit:0").ApproachPoints,Story(landing).ApproachPoints);
        var elevator=Frame(4);
        Assert.Equal(Control(elevator,2).ApproachPoints,Story(elevator).ApproachPoints);
        Assert.NotEqual(Control(elevator,1).ApproachPoints,Story(elevator).ApproachPoints);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void TheRecordedPersistentStateActuallyPermitsTheOldBackwardFallback(int flags)
    {
        var frame=Frame(flags);
        var backward=new SceneConnectionRouter().Next(99,State(flags),[FullStoryObjectives.EndOfTime],frame.Targets);
        Assert.NotEmpty(backward);
        var story=Story(frame);
        Assert.DoesNotContain(backward,t=>t.ApproachPoints.SequenceEqual(story.ApproachPoints));
    }

    private static NavigationResult Select(NavigationFrame frame,NavigationTarget target,NavigationCommand command)
    {
        frame=frame with{Targets=[target]};
        var controller=new NavigationController();
        while(controller.CurrentCategoryLabel!="Story Events")controller.Handle(NavigationCommand.NextCategory,frame,0);
        return controller.Handle(command,frame,16);
    }

    private static NavigationTarget Story(NavigationFrame frame)=>frame.Targets.Single(t=>t.Id=="story:full:lavos");
    private static NavigationTarget Control(NavigationFrame frame,int actor)=>frame.Targets.Single(t=>
        t.Id==(actor==1?"landmark:1":$"actor:{actor}:4:113"));

    internal static NavigationFrame Frame(int? flags,NavigationPoint? position=null,int missing=-1)
    {
        var p=position??new NavigationPoint(2048,3583,1);
        var player=Actor(0,p.X,p.Y,0) with{IsPartyMember=true};
        // Atel0455's native marker and sparkle anchors. Draw state is synthetic;
        // the fixture deliberately offers alternatives to catch a wrong binding.
        var actors=new[]{player,Actor(1,1920,3071,7),Actor(2,3520,5216,4),Actor(3,592,5216,4)}
            .Where(a=>a.Index!=missing).ToArray();
        var field=new FieldNavigationSnapshot(0x1000,0x4000,0x2000,0x20000,11,99,true,181,0,0,0,player,actors);
        var map=new FieldMapSnapshot(16,48,new byte[768],new byte[768],Enumerable.Repeat((byte)1,768).ToArray(),1,false,
            16,48,Enumerable.Repeat((byte)128,768).ToArray());
        var state=State(flags);
        return new FieldNavigationSource(new NoMemory(),_=>{}).Build(field,map,new(0,0,4096,12288),[],state);
    }

    private static FieldStoryState State(int? flags)
    {
        using var stream=typeof(BlackOmenElevatorTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.black-omen-persistent-state-0377.json")!;
        using var document=JsonDocument.Parse(stream);var source=document.RootElement;
        var globals=source.GetProperty("globals").Deserialize<Dictionary<int,int>>()!;
        if(flags is{} known)globals[0x1A7]=known;else globals.Remove(0x1A7);
        return new FieldStoryState(source.GetProperty("point").GetInt32(),true)
        {
            Globals=globals,Extended=source.GetProperty("extended").Deserialize<Dictionary<int,int>>()!,
            Locals=Enumerable.Range(0,32).ToDictionary(i=>i,_=>0),
        };
    }

    private static FieldActorSnapshot Actor(int slot,int x,int y,int kind)=>
        new(slot,x/256,x,x%256,y/256,y,y%256,0,1,1,113,kind,0x2020,1,1,false,true,true,true);
    private sealed class NoMemory:IReadableMemory{public bool TryRead(nuint address,Span<byte> destination)=>false;}
}
