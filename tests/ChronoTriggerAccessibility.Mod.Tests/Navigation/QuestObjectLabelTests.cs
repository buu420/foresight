using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class QuestObjectLabelTests
{
    [Theory]
    [InlineData(197,10,149,"Rainbow Shell")]
    [InlineData(440,9,149,"Rainbow Shell")]
    [InlineData(251,17,110,"Moon Stone")]
    [InlineData(255,8,110,"Sun Stone")]
    [InlineData(327,8,112,"Sunlit spot")]
    public void VerifiedObjectsKeepTheirNativeTargetsAndGainUsefulLabels(int scene,int index,int visual,string name)
    {
        var target=Assert.Single(Frame(scene,index,visual).Targets,t=>t.Id==$"actor:{index}:4:{visual}");
        Assert.Equal(name,target.Label);Assert.Equal(NavigationCategory.Objects,target.Category);
        Assert.NotEmpty(target.ApproachPoints);
        // Treasury actor9 has an empty Confirm and native touch handler.
        if(scene==440)Assert.Null(target.ConfirmFacings);else Assert.NotNull(target.ConfirmFacings);
    }

    [Fact]
    public void IncoherentOrHiddenObjectsCannotAcquireTheQuestName()
    {
        Assert.DoesNotContain(Frame(197,10,149,coherent:false).Targets,t=>t.Label=="Rainbow Shell");
        Assert.DoesNotContain(Frame(197,10,149,drawn:false).Targets,t=>t.Id=="actor:10:4:149");
        Assert.DoesNotContain(Frame(197,9,149).Targets,t=>t.Label=="Rainbow Shell");
    }

    [Fact]
    public void TheTempleGlintAndOrdinaryPickupsDoNotBecomeTheMoonStone()
    {
        Assert.DoesNotContain(Frame(251,8,112).Targets,t=>t.Label.Contains("Stone"));
        Assert.Equal("Sparkle",FieldVisualLabels.Describe(Actor(10,112)).Label);
        Assert.Equal("Round stone",FieldVisualLabels.Describe(Actor(17,110)).Label);
        Assert.Equal("Multicolored shell",FieldVisualLabels.Describe(Actor(10,149)).Label);
    }

    [Fact]
    public void ThePresentShrineControlDescribesTheSpotRatherThanAnUnseenStone()
    {
        var target=Assert.Single(Frame(64,9,0,classTag:7).Targets,t=>t.Id=="landmark:9");
        Assert.Equal("Sunlit spot",target.Label);Assert.Equal(NavigationCategory.Objects,target.Category);
    }

    [Fact]
    public void TheFutureShrineCheckControlUsesTheSameVerifiedPlaceLabel()
    {
        var target=Assert.Single(Frame(255,9,0,classTag:7,shrineState:7).Targets,t=>t.Id=="landmark:9");
        Assert.Equal("Sunlit spot",target.Label);Assert.NotEmpty(target.ApproachPoints);
    }

    internal static NavigationFrame Frame(int scene,int index,int visual,int classTag=4,bool coherent=true,bool drawn=true,int? shrineState=null)
    {
        var lead=Actor(0,0) with{ClassTag=0,IsPartyMember=true,FineX=2048,FineY=3840,TileX=8,TileY=15,FractionX=0,FractionY=0};
        var actor=Actor(index,visual) with{ClassTag=classTag,DrawMode=drawn?1:0};
        var field=new FieldNavigationSnapshot(0x1000,0x4000,0x2000,0x20000,32,scene,coherent,1,0,0,0,lead,[lead,actor]);
        var map=new FieldMapSnapshot(32,32,new byte[1024],new byte[1024],Enumerable.Repeat((byte)1,1024).ToArray(),1,false,32,32,Enumerable.Repeat((byte)128,1024).ToArray());
        var globals=Enumerable.Range(0,512).ToDictionary(i=>i,_=>0);globals[0x13A]=shrineState??(scene==255?0x3F:scene==64?7:3);
        var state=new FieldStoryState(0xD4,false){Globals=globals,Inventory=new Dictionary<int,int>(),Party=[0,1,2,3,4,5,6,255,255]};
        return new FieldNavigationSource(new NoMemory(),_=>{}).Build(field,map,new(0,0,8192,8192),[],state);
    }
    private static FieldActorSnapshot Actor(int id,int visual)=>new(id,8,2176,128,11,2944,128,1,1,1,visual,4,0x2020,1,128,false,true,true,true);
    private sealed class NoMemory:IReadableMemory{public bool TryRead(nuint a,Span<byte>b)=>false;}
}
