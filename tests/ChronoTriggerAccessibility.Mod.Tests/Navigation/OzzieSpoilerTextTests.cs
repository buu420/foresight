using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class OzzieSpoilerTextTests
{
    [Theory]
    [InlineData(177,"Entrance")]
    [InlineData(183,"Room A")]
    [InlineData(178,"Room B")]
    [InlineData(184,"Room C")]
    [InlineData(179,"Room D")]
    [InlineData(180,"Room E")]
    [InlineData(181,"Room F")]
    public void AreaAndPassageLabelsDoNotExposePrivateDebugMapOutcomes(int scene,string room)
    {
        var expected="Ozzie's Fort, "+room;
        Assert.Equal(expected,GameNavigationCatalog.AreaName(scene));
        Assert.Equal("To "+expected,GameNavigationCatalog.DestinationLabel(scene));
    }

    [Fact]
    public void CurrentRoomUsesItsNeutralNameEvenWhenNativeDebugTextIsLoaded()
    {
        var lead=new FieldActorSnapshot(0,8,2176,128,11,2944,128,1,1,1,0,0,0x2020,1,128,true,true,true,true);
        var field=new FieldNavigationSnapshot(0x1000,0x4000,0x2000,0x20000,32,181,true,1,0,0,0,lead,[lead]);
        var map=new FieldMapSnapshot(32,32,new byte[1024],new byte[1024],Enumerable.Repeat((byte)1,1024).ToArray(),1,false,
            32,32,Enumerable.Repeat((byte)128,1024).ToArray());
        var source=new FieldNavigationSource(new NoMemory(),_=>{},_=>"Ozzie's Fort, The End of Ozzie");
        var frame=source.Build(field,map,new(0,0,8192,8192),[],null);
        Assert.Equal("Ozzie's Fort, Room F",frame.AreaName);
    }

    [Theory]
    [InlineData("flea",183,9)]
    [InlineData("slash",184,9)]
    [InlineData("guillotine",179,0)]
    [InlineData("three",180,8)]
    [InlineData("switch",181,8)]
    [InlineData("return",181,8)]
    public void FortSelectionsDoNotRevealFutureBattlesSolutionsOrOutcomes(string step,int scene,int actor)
    {
        var objective=ClassicOptionalObjectives.All.Single(o=>o.Id=="full:quest:ozzie:"+step);
        Assert.Equal("Continue through Ozzie's Fort",objective.Label);
        Assert.Null(objective.Instruction);
        // Text changes preserve the existing native goal and category contracts.
        Assert.Equal(NavigationCategory.Objects,objective.Category);
        var goal=Assert.Single(objective.Goals);
        Assert.Equal(scene,goal.Scene);Assert.Equal([actor],goal.Actors);Assert.Empty(goal.Targets);
    }

    [Theory]
    [InlineData(1,0,"flea")]
    [InlineData(4,0,"slash")]
    [InlineData(8,0,"guillotine")]
    [InlineData(0x10,0,"three")]
    [InlineData(0x40,0,"switch")]
    [InlineData(0x40,0x80,"return")]
    public void NativeQuestProgressStillChoosesTheSameGoalWithNeutralSpeech(int fort,int floor,string step)
    {
        var globals=Enumerable.Range(0,512).ToDictionary(i=>i,_=>0);
        globals[0x1A1]=fort;globals[0x1A0]=floor;
        var state=new FieldStoryState(0xD2,false){Globals=globals};
        var objective=Assert.Single(ClassicOptionalObjectives.Build(177,state),o=>o.Id.StartsWith("full:quest:ozzie:",StringComparison.Ordinal));
        Assert.Equal("full:quest:ozzie:"+step,objective.Id);
        Assert.Equal("Continue through Ozzie's Fort",objective.Label);Assert.Null(objective.Instruction);
    }

    private sealed class NoMemory:IReadableMemory
    {public bool TryRead(nuint address,Span<byte> destination)=>false;}
}
