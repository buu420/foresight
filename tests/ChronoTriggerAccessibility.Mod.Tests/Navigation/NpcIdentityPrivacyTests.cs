using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class NpcIdentityPrivacyTests
{
    [Theory]
    [InlineData(9,"Lucca's House, Upstairs Room")]
    [InlineData(151,"Denadoro Mountains, Inner Chamber")]
    [InlineData(169,"Magus's Keep, Chamber A")]
    [InlineData(173,"Magus's Keep, Chamber B")]
    [InlineData(174,"Magus's Keep, Chamber C")]
    [InlineData(470,"Magus's Keep, Passage C")]
    [InlineData(300,"Tyranno Lair, Chamber")]
    [InlineData(328,"Zeal Palace, Side Chamber A")]
    [InlineData(402,"Zeal Palace, Passage A")]
    [InlineData(403,"Zeal Palace, Side Chamber B")]
    [InlineData(465,"End of Time, Back Room")]
    public void InternalRoomTitlesDoNotIdentifyAnNpcOrBattleBeforeItsIntroduction(int scene,string label)
    {
        Assert.Equal(label,GameNavigationCatalog.AreaName(scene));
        Assert.Equal("To "+label,GameNavigationCatalog.DestinationLabel(scene));
    }

    [Theory]
    [InlineData("full:masa-mune","Explore the inner Denadoro Mountains")]
    [InlineData("full:forest-maze","Explore the Forest Maze")]
    [InlineData("full:keep-flea","Return to the east wing")]
    [InlineData("full:quest:rainbow:toma","Speak with the patron at the 600 AD Choras tavern")]
    [InlineData("full:time-egg","Speak with the old man at the End of Time")]
    [InlineData("full:quest:revival:egg","Speak with the old man at the End of Time")]
    [InlineData("bonus:eclipse:gaspar","Ask the old man about the completed Dimensional Vortices")]
    public void EarlierGuideTitlesCannotBypassTheNpcIdentityGate(string id,string label)
    {
        var objective=Assert.Single(FullStoryObjectives.All,o=>o.Id==id);
        Assert.Equal(label,objective.Label);
        if(id.EndsWith("time-egg",StringComparison.Ordinal)||id.EndsWith("revival:egg",StringComparison.Ordinal))
            Assert.Null(objective.Instruction);
    }

    [Fact]
    public void ThePillarRestrictionDoesNotNameTheUnintroducedBackRoomKeeper()
    {
        Assert.DoesNotContain("Spekkio",FutureAreaLabels.PillarInstruction(new FieldStoryState(75,false)));
        Assert.Equal("To Lucca's House, Upstairs Room",OptionalGuideAreas.ExitLabel(3,2));
        Assert.Equal("To Lucca's House, Upstairs Room",OptionalGuideAreas.ExitLabel(4,2));
    }
}
