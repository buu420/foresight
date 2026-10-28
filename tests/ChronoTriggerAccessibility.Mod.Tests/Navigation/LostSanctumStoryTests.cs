using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class LostSanctumStoryTests
{
    [Theory]
    [InlineData(false, false, 580)] [InlineData(true, false, 580)]
    [InlineData(false, true, 579)] [InlineData(true, true, 579)]
    public void CaveWarpDoesNotInheritTheUnrelatedVillageAmbushBranch(bool underAttack, bool lit, int destination)
    {
        var state = State((0x11, underAttack ? 9 : 8), (0x12, 0x78), (0x13, lit ? 1 : 0));
        var regions = GameNavigationCatalog.ForScene(578)!.Regions
            .Where(r => r.Kind == "Warp" && r.Available(state)).ToArray();
        var region = Assert.Single(regions);
        Assert.Equal(destination, region.Destination);
        Assert.Equal((0, 2, 4, 9), (region.Left, region.Top, region.Right, region.Bottom));
        Assert.All(region.Guards, g => Assert.Equal(0x13, g.Index));
    }

    [Fact]
    public void DefenseRouteRemainsConnectedAfterTheNativeVillageMeeting()
    {
        var state = State((0x11, 9), (0x12, 0x78), (0x13, 1), (0x14, 1));
        Assert.NotNull(new SceneConnectionRouter().DistanceWithinFields(583, state, 555));
    }

    [Theory]
    [InlineData(578)] [InlineData(579)] [InlineData(540)] [InlineData(545)]
    public void PrimaryStoryEventsFollowTheAcceptedLocalSanctumQuest(int scene)
    {
        var state = State((0x10, 0x7F), (0x11, 9), (0x12, 0x78), (0x13, 1), (0x14, 1));
        var objective = Assert.Single(FullStoryObjectives.Build(scene, state), o => o.Category == NavigationCategory.StoryEvents);
        Assert.Equal("Confront the enemy leaders in the Primeval Fortress", objective.Label);
        Assert.Contains(objective.Goals, g => g.Scene == 555);
        Assert.DoesNotContain("Lavos", objective.Instruction ?? "");
    }

    [Theory]
    [InlineData(556)] [InlineData(668)] [InlineData(538)]
    public void BridgeWorkRetainsItsOwnAcceptedObjective(int scene)
    {
        var state = State((0x10, 0x3F), (0x19, 0x1A), (0x16, 2), (0x1D, 2), (0x15, 8), (0x1B, 0x80));
        var objective = Assert.Single(FullStoryObjectives.Build(scene, state), o => o.Category == NavigationCategory.StoryEvents);
        Assert.Contains("Nu's sweet fruit", objective.Label);
        Assert.DoesNotContain("Lavos", objective.Label);
    }

    [Theory]
    [InlineData(578, "Primeval Fortress")]
    [InlineData(545, "Primeval Fortress")]
    [InlineData(556, "Great Bridge")]
    [InlineData(668, "Great Bridge")]
    public void IndependentAcceptedErrandsPreferTheCurrentAreaWithoutRemovingChoices(int scene, string area)
    {
        var state = State((0x10, 0x7F), (0x11, 9), (0x12, 0x78), (0x13, 1), (0x14, 1),
            (0x19, 0x1A), (0x16, 2), (0x1D, 2), (0x15, 8), (0x1B, 0x80)) with
        { Inventory = new Dictionary<int, int> { [0x401E] = 1 } };
        var choices = FullStoryObjectives.Build(scene, state);
        Assert.Contains(area, Assert.Single(choices, o => o.Category == NavigationCategory.StoryEvents).Label);
        Assert.Contains(choices, o => o.Id == "bonus:sanctum:bridge:feed");
        Assert.Contains(choices, o => o.Id == "bonus:sanctum:defend:leaders");
    }

    [Theory]
    [InlineData(578)] [InlineData(556)] [InlineData(585)] [InlineData(668)]
    public void UnknownSanctumStateDoesNotSendThePlayerToTheFinalBattle(int scene)
    {
        var objective = Assert.Single(FullStoryObjectives.Build(scene, new(214, false)), o => o.Category == NavigationCategory.StoryEvents);
        Assert.Empty(objective.Goals);
        Assert.DoesNotContain("Lavos", objective.Label);
        Assert.Contains("Exits", objective.Instruction!);
    }

    internal static FieldStoryState State(params (int Cell, int Value)[] changes)
    {
        var extra = Enumerable.Range(0, 80).ToDictionary(i => i, _ => 0);
        extra[0x1F] = 1; extra[0x12] = 8; extra[0x11] = 8; extra[0x0A] = 255;
        extra[0x18] = 4;
        foreach (var (cell, value) in changes) extra[cell] = value;
        return new(214, false)
        {
            Extended = extra, Globals = Enumerable.Range(0, 512).ToDictionary(i => i, _ => 0),
            Locals = Enumerable.Range(0, 256).ToDictionary(i => i, _ => 0),
            Inventory = new Dictionary<int, int>(), Party = [0, 5, 6, 130, 4, 131, 2, 3, 1],
        };
    }
}
