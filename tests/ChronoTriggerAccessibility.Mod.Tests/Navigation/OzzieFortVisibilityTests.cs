using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class OzzieFortVisibilityTests
{
    [Fact]
    public void UnmarkedFalseWallIsNotAnEntityTarget()
    {
        var frame=Build(0,false);
        Assert.DoesNotContain(frame.Targets,t=>t.Id=="landmark:1");
        Assert.Contains(frame.Targets,t=>t.Id=="chest:84");
    }

    [Theory]
    [InlineData(null,false)]
    [InlineData(0,false)]
    [InlineData(1,false)]
    [InlineData(0,true)]
    [InlineData(null,true)]
    public void CoveredLootStaysHiddenEvenWhenInsideTheViewport(int? local,bool revealed)
    {
        var frame=Build(local,revealed);
        Assert.DoesNotContain(frame.Targets,t=>t.Id is "chest:85" or "chest:86" or "chest:87" or "actor:13:4:112");
    }

    [Fact]
    public void NativeOpeningAndAppliedCopyRevealTheThreeChestsAndFloorGlint()
    {
        var frame=Build(1,true);
        Assert.All(new[]{"chest:85","chest:86","chest:87","actor:13:4:112"},id=>Assert.Contains(frame.Targets,t=>t.Id==id));
        Assert.DoesNotContain(frame.Targets,t=>t.Id=="landmark:1");
    }

    [Fact]
    public void VisibleBladeHasAnAppearanceDescriptionWithNoInteractionOrPuzzleAdvice()
    {
        var frame=Build(0,false,blade:true);
        var target=Assert.Single(frame.Targets,t=>t.Label=="Hanging blade");
        Assert.DoesNotContain(frame.Targets,t=>t.Id is "actor:10:4:90" or "actor:11:4:91");
        Assert.False(target.IsStoryNote);Assert.Empty(target.ApproachPoints);
        Assert.Equal("A metal blade hangs on a chain above a chest.",target.Instruction);
        Assert.DoesNotContain("avoid",target.Instruction,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("trap",target.Instruction,StringComparison.OrdinalIgnoreCase);
        foreach(var command in new[]{NavigationCommand.Guide,NavigationCommand.ToggleWalk})
        {
            var controller=new NavigationController();
            var onlyBlade=frame with{Targets=[target]};
            controller.Handle(NavigationCommand.NextCategory,onlyBlade,0);
            var selection=controller.Handle(NavigationCommand.NextCategory,onlyBlade,1);
            Assert.Contains("steps away",string.Join(" ",selection.Speech));
            Assert.DoesNotContain("on another level",string.Join(" ",selection.Speech));
            var result=controller.Handle(command,onlyBlade,2);
            Assert.Contains(target.GuidanceRestriction!,result.Speech);
            Assert.False(result.Guiding);Assert.False(result.AutoWalking);
            Assert.Equal(NavigationDirection.None,result.Direction);
        }
    }

    [Fact]
    public void HiddenBladeDoesNotCreateASceneryDescription()=>
        Assert.DoesNotContain(Build(0,false,blade:true,drawn:false).Targets,t=>t.Label=="Hanging blade");

    internal static NavigationFrame Build(int? local,bool revealed,bool blade=false,bool drawn=true)
    {
        var lead=Actor(0,0,0,2,40) with{IsPartyMember=true};
        var actors=new List<FieldActorSnapshot>{lead,Actor(1,7,0,12,43),Actor(13,4,112,13,44)};
        if(blade)
        {
            actors.Add(Actor(10,4,90,2,34) with{FineX=0x250,FractionX=0x50,FineY=0x22F0,FractionY=0xF0,LoadedFlag=0,ActivationEnabled=1,DrawMode=drawn?1:0});
            actors.Add(Actor(11,4,91,2,34) with{FineX=0x250,FractionX=0x50,FineY=0x22F0,FractionY=0xF0,LoadedFlag=0,ActivationEnabled=1,DrawMode=drawn?1:0});
        }
        var field=new FieldNavigationSnapshot(0x1000,0x4000,0x2000,0x20000,64,179,true,1,0,0,0,lead,actors);
        var shapes=new byte[64*48];foreach(var x in new[]{7,8,9,13})shapes[44*64+x]=(byte)(revealed?1:3);
        var map=new FieldMapSnapshot(64,48,shapes,new byte[64*48],Enumerable.Repeat((byte)1,64*48).ToArray(),1,false,
            64,48,Enumerable.Repeat((byte)128,64*48).ToArray());
        var treasures=new[]{new FieldTreasure(84,2*256+128,34*256+128),new(85,7*256+128,44*256+128),
            new(86,8*256+128,44*256+128),new(87,9*256+128,44*256+128)};
        var state=RecurringNpcLabelTests.State(0xD2) with{Locals=local is{} value?new Dictionary<int,int>{{11,value}}:new Dictionary<int,int>()};
        return new FieldNavigationSource(new NoMemory(),_=>{}).Build(field,map,new(0,0,64*256,48*256),treasures,state);
    }
    private static FieldActorSnapshot Actor(int slot,int type,int visual,int x,int y)=>
        new(slot,x,x*256+128,128,y,y*256+255,255,1,1,1,visual,type,0x2020,1,128,false,true,true,true);
    private sealed class NoMemory:IReadableMemory{public bool TryRead(nuint address,Span<byte> destination)=>false;}
}
