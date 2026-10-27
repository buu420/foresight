using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class GenoDomeNavigationTests
{
    [Theory]
    [InlineData(248,8,8,53,"Entrance terminal")]
    [InlineData(250,1,23,55,"Conveyor exit door")]
    [InlineData(256,10,28,43,"Southern pod switch")]
    [InlineData(256,11,26,17,"Northern pod switch")]
    [InlineData(256,12,32,9,"Conveyor direction switch")]
    [InlineData(256,13,40,17,"Laser barrier switch")]
    [InlineData(256,14,11,5,"Left wall switch")]
    [InlineData(256,15,13,5,"Middle wall switch")]
    [InlineData(256,16,15,5,"Right wall switch")]
    [InlineData(256,17,23,26,"Charging pod switch")]
    [InlineData(256,19,28,26,"Information terminal")]
    [InlineData(256,20,39,41,"Southern elevator door")]
    [InlineData(256,21,43,5,"Northern elevator door")]
    [InlineData(256,25,25,25,"Charging pod")]
    [InlineData(256,26,27,42,"Southern receiver pod")]
    [InlineData(256,27,9,13,"Western receiver pod")]
    [InlineData(256,28,25,16,"Northern receiver pod")]
    [InlineData(267,8,55,30,"Rear elevator door")]
    [InlineData(268,8,43,32,"Elevator door")]
    [InlineData(268,10,29,15,"Left figurine pedestal")]
    [InlineData(268,11,44,15,"Right figurine pedestal")]
    public void NativeControlsHaveDistinctUsefulNames(int scene,int slot,int x,int y,string label)
    {
        var target=Assert.Single(Frame(scene,Marker(slot,x,y)).Targets,t=>t.Id==$"landmark:{slot}");
        Assert.Equal(label,target.Label);
        Assert.Equal(NavigationCategory.Objects,target.Category);
        Assert.Equal(x*256+128,target.Position.X);
    }

    [Theory]
    [InlineData(8,170,"Mobile Sentinel",NavigationCategory.People)]
    [InlineData(29,170,"Guard Sentinel",NavigationCategory.People)]
    [InlineData(22,100,"Southern figurine",NavigationCategory.Objects)]
    [InlineData(23,100,"Northern figurine",NavigationCategory.Objects)]
    public void PuzzleSpritesKeepTheirOwnNativeIdentity(int slot,int visual,string label,NavigationCategory category)
    {
        var target=Assert.Single(Frame(256,Marker(slot,7,31) with{ClassTag=4,VisualIndex=visual,LoadedFlag=1}).Targets,
            t=>t.Id==$"actor:{slot}:4:{visual}");
        Assert.Equal(label,target.Label);Assert.Equal(category,target.Category);
    }

    [Fact]
    public void ParticleEffectsAreNotNavigationObjects()
    {
        foreach(var id in new[]{9,24})
            Assert.DoesNotContain(Frame(256,Marker(id,25,25) with{ClassTag=4,VisualIndex=86,LoadedFlag=1}).Targets,
                t=>t.Id==$"actor:{id}:4:86");
    }

    [Theory]
    [InlineData(248,9)] [InlineData(256,18)]
    public void TheTemporaryTerminalDisplayDoesNotBecomeAnotherSparkleTarget(int scene,int slot)=>
        Assert.DoesNotContain(Frame(scene,Marker(slot,27,25) with{ClassTag=4,VisualIndex=127,LoadedFlag=1}).Targets,
            t=>t.Id==$"actor:{slot}:4:127");

    [Fact]
    public void ForeignScenesAndWrongClassesDoNotReceiveGenoLabels()
    {
        Assert.DoesNotContain(Frame(659,Marker(14,11,5)).Targets,t=>t.Label=="Left wall switch");
        Assert.DoesNotContain(Frame(256,Marker(14,11,5),coherent:false).Targets,t=>t.Label=="Left wall switch");
        Assert.DoesNotContain(Frame(256,Marker(14,11,5) with{ClassTag=4,VisualIndex=127}).Targets,t=>t.Label=="Left wall switch");
        Assert.DoesNotContain(Frame(256,Marker(14,11,5) with{ScriptCallsEnabled=false}).Targets,t=>t.Label=="Left wall switch");
    }

    [Theory]
    [InlineData(8)]
    [InlineData(29)]
    public void AHiddenDroidDoesNotBecomeANamedTarget(int slot)=>Assert.DoesNotContain(
        Frame(256,Marker(slot,7,31) with{ClassTag=4,VisualIndex=170,LoadedFlag=1,DrawMode=0}).Targets,
        t=>t.Label is "Mobile Sentinel" or "Guard Sentinel");

    internal static FieldActorSnapshot Marker(int id,int x,int y)=>new(id,x,x*256+128,128,y,y*256+255,255,
        1,1,0,0,7,0x2020,0,128,false,true,true,true);

    internal static NavigationFrame Frame(int scene,FieldActorSnapshot actor,bool coherent=true)
    {
        var leader=Marker(4,25,28) with{ClassTag=0,VisualIndex=3,IsPartyMember=true,LoadedFlag=1};
        var f=new FieldNavigationSnapshot(0x1000,0x4000,0x2000,0x20000,51,scene,coherent,1,0,0,4,leader,[leader,actor]);
        var m=new FieldMapSnapshot(64,64,new byte[4096],new byte[4096],Enumerable.Repeat((byte)1,4096).ToArray(),1,false,64,64,
            Enumerable.Repeat((byte)128,4096).ToArray());
        var s=new FieldStoryState(214,true){Globals=Enumerable.Range(0,512).ToDictionary(i=>i,_=>0),Locals=Enumerable.Range(0,32).ToDictionary(i=>i,_=>0),
            Party=[3,5,0,130,4,131,2,6,1],Inventory=new Dictionary<int,int>()};
        return new FieldNavigationSource(new Empty(),_=>{}).Build(f,m,new(0,0,16384,16384),[],s);
    }
    private sealed class Empty:IReadableMemory{public bool TryRead(nuint address,Span<byte> bytes)=>false;}
}
