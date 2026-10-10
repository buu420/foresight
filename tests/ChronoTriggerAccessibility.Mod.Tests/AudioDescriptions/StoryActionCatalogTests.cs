using ChronoTriggerAccessibility.Mod.AudioDescriptions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.AudioDescriptions;

public sealed class StoryActionCatalogTests
{
    [Fact]
    public void TheOpeningBlanketOccludesTheBoysHeadShake()
    {
        var frame = StoryActionRuntimeTests.Frame();
        var sleeping = frame with { Location = frame.Location with
            { Scene = 2, ScriptId = 324, Actor = 1, Address = 0x40F } };
        Assert.Null(StoryActionCatalog.Describe(sleeping, 0x17, _ => "Cinder"));
        Assert.Equal("Cinder shakes their head.", StoryActionCatalog.Describe(frame, 0x17, _ => "Cinder"));
    }

    [Fact]
    public void TheRobotsEmptySlotDoesNotBorrowTheBoysRaisedArmsMeaning()
    {
        var frame = StoryActionRuntimeTests.Frame();
        Assert.Null(StoryActionCatalog.Describe(frame with
        { ActorState = frame.ActorState with { Visual = 3 } }, 0x11, _ => "Robo"));
        Assert.Equal("Cinder holds their arms up.", StoryActionCatalog.Describe(frame, 0x11, _ => "Cinder"));
    }

    [Theory]
    [InlineData(1, 0x07, "The blonde girl nods.")]
    [InlineData(1, 0x08, "Current name nods.")]
    [InlineData(3, 0x3B, "The robot nods.")]
    [InlineData(3, 0x3C, "Current name nods.")]
    [InlineData(5, 0x6E, "The blonde woman in furs nods.")]
    [InlineData(5, 0x6F, "Current name nods.")]
    public void CurrentNamesAreUsedOnlyAfterTheVerifiedIntroduction(int sprite, int story, string expected)
    {
        var frame = StoryActionRuntimeTests.Frame();
        Assert.Equal(expected, StoryActionCatalog.Describe(frame with
        { StoryPoint = story, ActorState = frame.ActorState with { Visual = sprite } }, 0x16, _ => "Current name"));
    }

    [Fact]
    public void AnNpcGraphicWithTheSameNumericVisualIdDoesNotBecomeAPartyMember()
    {
        var frame = StoryActionRuntimeTests.Frame();
        Assert.Null(StoryActionCatalog.Describe(frame with
        { ActorState = frame.ActorState with { ClassTag = 4 } }, 0x16, _ => "Cinder"));
    }
}
