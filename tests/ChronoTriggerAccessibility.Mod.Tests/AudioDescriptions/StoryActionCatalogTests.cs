using ChronoTriggerAccessibility.Mod.AudioDescriptions;
using ChronoTriggerAccessibility.Native.Capture;
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

    [Theory]
    [InlineData(StorySceneAction.MuneSpinsAndVanishes, "Mune spins around and vanishes.")]
    [InlineData(StorySceneAction.MasaSpinsAndVanishes, "Masa spins around and vanishes.")]
    [InlineData(StorySceneAction.KnifeRises, "A glowing red knife rises.")]
    [InlineData(StorySceneAction.KnifeShootsIntoMachine, "The knife shoots up into the machine.")]
    [InlineData(StorySceneAction.SwordAppearsInMachine, "A sword appears in the machine.")]
    [InlineData(StorySceneAction.SchalaHoldsOutHands, "Schala holds out both hands.")]
    [InlineData(StorySceneAction.PartyGoneSchalaKneels, "Schala kneels alone, head bowed.")]
    [InlineData(StorySceneAction.MagusStandsWhereProphetWas, "The hooded Prophet is gone. Magus stands in his place.")]
    [InlineData(StorySceneAction.SchalaKnockedToKnees, "Schala drops to her knees.")]
    [InlineData(StorySceneAction.MagusThrownDown, "Magus is thrown backward, face down.")]
    public void OceanPalaceTextsAreTheApprovedPhysicalChanges(StorySceneAction action, string expected)
    {
        Assert.Equal(expected, StoryActionCatalog.DescribeScene(Scene(action, 0xC9), _ => "Cinder"));
        Assert.Equal(expected, StoryActionCatalog.DescribeScene(Scene(action, 0xC9), _ => null));
    }

    [Theory]
    [InlineData(StorySceneAction.CronoRaisesHandsToMachine, "raises both hands toward the machine.")]
    [InlineData(StorySceneAction.CronoRaisesArmsBeforeLavos, "raises both arms toward Lavos.")]
    [InlineData(StorySceneAction.CronoCrumbles, "turns into a dark silhouette and crumbles away.")]
    public void CronosOceanPalaceActionsUseTheCurrentNameOrTheVisibleAppearance(StorySceneAction action, string rest)
    {
        Assert.Equal("Cinder " + rest, StoryActionCatalog.DescribeScene(Scene(action, 0xC9), _ => "Cinder"));
        Assert.Equal("Crono " + rest, StoryActionCatalog.DescribeScene(Scene(action, 0xC9), _ => "Crono"));
        Assert.Equal("The red-haired boy " + rest, StoryActionCatalog.DescribeScene(Scene(action, 0xC9), _ => null));
        Assert.Equal("The red-haired boy " + rest, StoryActionCatalog.DescribeScene(Scene(action, 0xC9), _ => "  "));
    }

    [Fact]
    public void OceanPalaceTextsClaimNoUnprovenEffects()
    {
        var actions = Enum.GetValues<StorySceneAction>().Where(action => action >= StorySceneAction.MuneSpinsAndVanishes);
        Assert.Equal(13, actions.Count());
        foreach (var text in actions.Select(action => StoryActionCatalog.DescribeScene(Scene(action, 0xC9), _ => "Crono")))
            foreach (var word in new[] { "white", "blue", "light", "flash", "dies", "dead", "death", "Janus" })
                Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
    }

    private static StorySceneActionCandidate Scene(StorySceneAction action, int story)
    {
        var before = StoryActionRuntimeTests.Frame() with { StoryPoint = story };
        return new(action, before, before.ActorState, null, false);
    }
}
