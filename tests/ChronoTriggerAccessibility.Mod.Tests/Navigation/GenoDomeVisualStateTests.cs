using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class GenoDomeVisualStateTests
{
    [Theory]
    [InlineData(101,"Green lamp.")] [InlineData(102,"Red lamp.")]
    public void SwitchSelectionReadsTheDisplayedLamp(int tile,string state)
    {
        var target=Assert.Single(Build(Map(tile)).Targets,t=>t.Id=="landmark:14");
        Assert.Contains(state,target.Instruction!);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void UnknownOrCoveredTileGraphicsNeverAcquireAColour(int variant)
    {
        var map=Map(101);
        if(variant==0)map=map with{TileLayer1=null};
        if(variant==1)map.TileLayer1!.Tiles[4*64+11]=99;
        if(variant==2)map.TileLayer2!.Tiles[4*64+11]=17;
        if(variant==3)map.CollisionShapes[4*64+11]|=1; // upper graphic bank
        var target=Assert.Single(Build(map).Targets,t=>t.Id=="landmark:14");
        Assert.DoesNotContain("lamp",target.Instruction??"",StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0,0,32,32)]
    [InlineData(2816,1280,3072,1536)] // control visible, lamp above the viewport
    public void OffscreenLampIsNotReadFromASelectedGuideTarget(int left,int top,int right,int bottom)
    {
        var frame=new FieldNavigationSource(new Empty(),_=>{}).Build(
            Field(),Map(101),new(left,top,right,bottom),[],GenoDomeFeedbackTests.Story());
        var target=Assert.Single(frame.Targets,t=>t.Id=="landmark:14");
        Assert.DoesNotContain("lamp",target.Instruction??"",StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VisibleLampChangesSpeakOnceWithoutListingEverySwitchOnEntry()
    {
        var said=new List<string>();var feedback=new GenoDomeFeedback(said.Add);feedback.Enable();
        feedback.Observe(Field(),Map(101),GenoDomeFeedbackTests.Story(),View);Assert.Empty(said);
        feedback.Observe(Field(),Map(102),GenoDomeFeedbackTests.Story(),View);
        Assert.Equal("Left wall switch. Red lamp.",Assert.Single(said));
        feedback.Observe(Field(),Map(102),GenoDomeFeedbackTests.Story(),View);Assert.Single(said);
        feedback.Observe(Field(),Map(101),GenoDomeFeedbackTests.Story(),new(0,0,32,32));Assert.Single(said);
    }

    private static readonly FieldViewport View=new(0,0,16384,16384);
    internal static FieldNavigationSnapshot Field()
    {
        var f=GenoDomeFeedbackTests.Field();
        return f with{Actors=f.Actors.Append(GenoDomeNavigationTests.Marker(14,11,5)).ToArray()};
    }
    internal static FieldMapSnapshot Map(int tile)
    {
        var l1=new byte[4096];l1[58*64+57]=101;l1[58*64+58]=102;l1[4*64+11]=(byte)tile;
        return new(64,64,new byte[4096],new byte[4096],Enumerable.Repeat((byte)1,4096).ToArray(),1,false,64,64,
            Enumerable.Repeat((byte)128,4096).ToArray())
            {TileLayer1=new(64,64,l1),TileLayer2=new(64,64,new byte[4096])};
    }
    private static ChronoTriggerAccessibility.Core.Navigation.NavigationFrame Build(FieldMapSnapshot map)=>
        new FieldNavigationSource(new Empty(),_=>{}).Build(Field(),map,View,[],GenoDomeFeedbackTests.Story());
    private sealed class Empty:IReadableMemory{public bool TryRead(nuint address,Span<byte> bytes)=>false;}
}
