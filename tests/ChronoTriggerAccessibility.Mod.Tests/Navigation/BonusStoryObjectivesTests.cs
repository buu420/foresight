using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class BonusStoryObjectivesTests
{
    private static FieldStoryState State(params (int Cell, int Value)[] values)
    {
        var extra = Enumerable.Range(0, 80).ToDictionary(i => i, _ => 0);
        extra[0x1F] = 1; extra[0x12] = 8; extra[0x11] = 8; extra[0x0A] = 255;
        foreach (var (cell, value) in values) extra[cell] = value;
        var globals = Enumerable.Range(0, 512).ToDictionary(i => i, _ => 0);
        globals[0x1FB] = 0x45;
        return new(0xD2, false)
        {
            Extended = extra, Globals = globals,
            Locals = Enumerable.Range(0, 256).ToDictionary(i => i, _ => 0),
            Inventory = new Dictionary<int, int>(), Party = [0, 1, 2, 3, 4, 5, 6, 255, 255],
        };
    }
    private static FieldStoryState Items(FieldStoryState s, params int[] items) =>
        s with { Inventory = items.ToDictionary(i => i, _ => 1) };
    private static FullStoryObjective? Quest(string family, FieldStoryState s, int scene = 496) =>
        BonusStoryObjectives.Build(scene, s).SingleOrDefault(o => o.Id.StartsWith("bonus:sanctum:" + family + ":"));
    private static string[] Vortex(FieldStoryState s, int scene = 496) =>
        BonusStoryObjectives.Build(scene, s).Where(o => o.Id.StartsWith("bonus:vortex:") || o.Id.StartsWith("bonus:eclipse:"))
            .Select(o => o.Id).ToArray();
    private static void Is(string expected, FullStoryObjective? objective) =>
        Assert.Equal("bonus:sanctum:" + expected, objective?.Id);

    [Fact]
    public void UnreadableStateAndUnrelatedRoomsDoNotInventQuests()
    {
        Assert.Empty(BonusStoryObjectives.Build(496, null));
        Assert.Empty(BonusStoryObjectives.Build(496, new(0xD4, false)));
        Assert.Empty(BonusStoryObjectives.Build(226, State()));
        Assert.Empty(BonusStoryObjectives.Build(496, State() with { Point = 0xD1 }));
        Assert.NotEmpty(BonusStoryObjectives.Build(496, State()));
        Assert.NotEmpty(BonusStoryObjectives.Build(583, State() with { Point = 0xD1 }));
    }

    [Fact]
    public void ForestUsesPersistentClearsInsteadOfResetRoomFlags()
    {
        Is("forest:group-1", Quest("forest", State((0x12, 0), (0x11, 0), (0x17, 0x3F))));
        Is("forest:group-3", Quest("forest", State((0x12, 0), (0x11, 0), (0x16, 0x0C))));
        Is("forest:group-6", Quest("forest", State((0x12, 0), (0x11, 0), (0x16, 0x7C))));
        Is("forest:report", Quest("forest", State((0x11, 0))));
        Assert.Null(Quest("forest", State()));
    }

    [Fact]
    public void GoldenHammerHasSandSaplingChaseAndHandInSteps()
    {
        Is("hammer:request", Quest("hammer", State()));
        Is("hammer:sand", Quest("hammer", State((0x10, 1))));
        Is("hammer:sapling", Quest("hammer", Items(State((0x10, 1), (0x12, 0x48)), 0x5019)));
        Is("hammer:tracks", Quest("hammer", State((0x10, 1), (0x12, 0x18))));
        Assert.Null(Quest("hammer", State((0x10, 1), (0x12, 0x18), (0x1B, 8), (0x18, 1))));
        Is("hammer:goldhammer", Quest("hammer", State((0x10, 1), (0x12, 0x18), (0x1B, 8), (0x18, 2))));
        Is("hammer:deliver", Quest("hammer", Items(State((0x10, 1)), 0x501A)));
        Assert.Null(Quest("hammer", State((0x10, 3))));
    }

    [Fact]
    public void UnknownInventoryIsDifferentFromKnownEmptyInventory()
    {
        var s = State((0x10, 1));
        Is("hammer:sand", Quest("hammer", s));
        Assert.Null(Quest("hammer", s with { Inventory = null }));
        Assert.Null(Quest("prism", State((0x10, 7)) with { Inventory = null }));
    }

    [Fact]
    public void PrismRequiresTheHammerAndUsesTheActualNuAndSummit()
    {
        Assert.Null(Quest("prism", State()));
        Is("prism:request", Quest("prism", State((0x10, 3))));
        Is("prism:guardian", Quest("prism", State((0x10, 7))));
        Is("prism:collect", Quest("prism", State((0x10, 7), (0x1F, 0x21))));
        Is("prism:deliver", Quest("prism", Items(State((0x10, 7)), 0x501B)));
        Assert.Null(Quest("prism", State((0x10, 0x0F))));
    }

    [Fact]
    public void SaintstoneTracksTheTwoEraAltarOccupancy()
    {
        Is("saint:first-altar", Quest("saint", Items(State((0x10, 0x1F)), 0x501B)));
        Is("saint:elder", Quest("saint", State((0x10, 0x1F), (0x1F, 0x41), (0x18, 3))));
        Is("saint:mark", Quest("saint", State((0x10, 0x1F), (0x1F, 0x41), (0x18, 4))));
        Is("saint:mark-return", Quest("saint", Items(State((0x10, 0x1F), (0x1F, 0x41)), 0x501D)));
        Is("saint:future-left", Quest("saint", State((0x10, 0x1F), (0x1F, 0x41), (0x1A, 0x80), (0x1B, 2))));
        Is("saint:future-right", Quest("saint", State((0x10, 0x1F), (0x1F, 0x81), (0x1A, 0x80), (0x1B, 4))));
        Is("saint:past-right", Quest("saint", Items(State((0x10, 0x1F), (0x1F, 0x41)), 0x501B)));
        Is("saint:past-left", Quest("saint", Items(State((0x10, 0x1F), (0x1F, 0x81)), 0x501B)));
        Is("saint:deliver", Quest("saint", Items(State((0x10, 0x1F)), 0x501C)));
        Assert.Null(Quest("saint", State((0x10, 0x3F), (0x12, 0x28))));
    }

    [Fact]
    public void SaintstoneOfferRequiresRecoveringAPrematurelyPlacedPrismastone()
    {
        Is("saint:request", Quest("saint", Items(State((0x10, 0x0F)), 0x501B)));
        Is("saint:recover-left", Quest("saint", State((0x10, 0x0F), (0x1F, 0x41))));
        Is("saint:recover-right", Quest("saint", State((0x10, 0x0F), (0x1F, 0x81))));
        Assert.Null(Quest("saint", State((0x10, 0x0F), (0x1F, 0x41)) with { Inventory = null }));
        Assert.Null(Quest("saint", State((0x10, 0x0F))));
    }

    [Fact]
    public void MiddleForestCompletionSurvivesLaterMarkQuestAndRespawnFlags()
    {
        Is("middle-forest:request", Quest("middle-forest", State()));
        Is("middle-forest:group-4", Quest("middle-forest", State((0x18, 1), (0x1C, 7))));
        Is("middle-forest:report", Quest("middle-forest", State((0x18, 2))));
        Assert.Null(Quest("middle-forest", State((0x18, 4), (0x1C, 0))));
    }

    [Fact]
    public void NuQuestRepairsTheLadderAcrossErasAndNeverRefetchesConsumedVines()
    {
        var basis = State((0x10, 0x0F), (0x18, 3), (0x19, 4), (0x1D, 1));
        Is("nu:ladder", Quest("nu", basis));
        Is("nu:vines", Quest("nu", State((0x10, 0x0F), (0x18, 3), (0x19, 4), (0x1D, 1), (0x15, 0x40))));
        Is("nu:try-vines", Quest("nu", Items(basis with { Extended = new Dictionary<int, int>(basis.Extended) { [0x15] = 0x40 } }, 0x5021)));
        Is("nu:hang-vines", Quest("nu", Items(basis with { Extended = new Dictionary<int, int>(basis.Extended) { [0x15] = 0x40, [0x1E] = 0x80 } }, 0x5021)));
        Is("nu:master", Quest("nu", basis with { Extended = new Dictionary<int, int>(basis.Extended) { [0x1B] = 0x20 } }));
        Is("nu:report", Quest("nu", basis with { Extended = new Dictionary<int, int>(basis.Extended) { [0x15] = 0x80 } }));
        Assert.Null(Quest("nu", basis with { Extended = new Dictionary<int, int>(basis.Extended) { [0x19] = 12 } }));
    }

    [Fact]
    public void BuilderAcceptsPartialDeliveriesWithoutRestartingEarlierMaterials()
    {
        Is("materials:wood", Quest("materials", State((0x10, 3), (0x18, 3), (0x19, 1))));
        Is("materials:hammer", Quest("materials", State((0x10, 3), (0x18, 3), (0x19, 1), (0x1A, 1))));
        Is("materials:steel", Quest("materials", State((0x10, 3), (0x18, 3), (0x19, 1), (0x1A, 3))));
        Is("materials:deliver", Quest("materials", Items(State((0x10, 3), (0x18, 3), (0x19, 1)), 0x5020)));
        Assert.Null(Quest("materials", State((0x10, 3), (0x18, 3), (0x19, 3), (0x1A, 7))));
    }

    [Fact]
    public void BridgeFoodAndDefenseAreSeparatePrerequisites()
    {
        Is("bridge:revisit", Quest("bridge", State((0x19, 0x0A))));
        Is("bridge:request", Quest("bridge", State((0x19, 0x0A), (0x1A, 0x20))));
        Is("bridge:rescue", Quest("bridge", State((0x19, 0x1A), (0x16, 2))));
        Is("bridge:help", Quest("bridge", State((0x19, 0x1A), (0x16, 2), (0x1D, 2))));
        Is("bridge:lunch", Quest("bridge", State((0x19, 0x1A), (0x16, 2), (0x1D, 2), (0x15, 8))));
        Is("bridge:deliver-lunch", Quest("bridge", Items(State((0x19, 0x1A), (0x16, 2), (0x1D, 2), (0x15, 8)), 0x5022)));
        var fedLunch = State((0x19, 0x1A), (0x16, 2), (0x1D, 2), (0x15, 8), (0x1B, 0x80));
        Is("bridge:banana", Quest("bridge", Items(fedLunch, 0x401C, 0x401D, 0x401F, 0x4020)));
        Is("bridge:feed", Quest("bridge", Items(fedLunch, 0x401E)));
        var satisfied = fedLunch with { Extended = new Dictionary<int, int>(fedLunch.Extended) { [0x1D] = 0x82 } };
        Assert.Null(Quest("bridge", satisfied));
        Is("bridge:finish", Quest("bridge", satisfied with { Extended = new Dictionary<int, int>(satisfied.Extended) { [0x11] = 0x0B } }));
        Assert.Null(Quest("bridge", State((0x19, 0x3A))));
    }

    [Fact]
    public void WaystoneCaveDefenseAndTowerRetireConsumedItemsPermanently()
    {
        Is("waystone:hint", Quest("waystone", Items(State((0x10, 0x3F)), 0x501C)));
        Is("waystone:place", Quest("waystone", Items(State((0x10, 0x3F), (0x31, 1)), 0x501C)));
        Is("waystone:collect", Quest("waystone", State((0x10, 0x3F), (0x12, 0x28))));
        Is("cave:request", Quest("cave", Items(State((0x10, 0x3F), (0x12, 0x28), (0x1F, 5)), 0x501E)));
        Is("cave:light", Quest("cave", Items(State((0x10, 0x7F)), 0x501E)));
        Is("cave:fortress", Quest("cave", State((0x10, 0x7F), (0x13, 1))));
        Is("defend:leaders", Quest("defend", State((0x10, 0x7F), (0x11, 9))));
        Is("defend:report", Quest("defend", State((0x10, 0x7F), (0x11, 9), (0x14, 2))));
        Assert.Null(Quest("defend", State((0x11, 11))));
        Is("tower:request", Quest("tower", State((0x19, 0x20))));
        Is("tower:guardians", Quest("tower", State((0x19, 0x60))));
        Is("tower:idols", Quest("tower", State((0x19, 0x60), (0x1F, 9))));
        Is("tower:report", Quest("tower", State((0x19, 0x60), (0x1B, 1))));
        Assert.Null(Quest("tower", State((0x19, 0xE0))));
    }

    [Fact]
    public void SmithRewardsUsePersistentCompletionRatherThanHeldQuestItems()
    {
        Is("smith:deliver", Quest("smith", Items(State(), 0x5023)));
        Is("lumicite:deliver", Quest("lumicite", Items(State(), 0x5024)));
        Assert.Null(Quest("smith", State((0x14, 0x20))));
        Assert.Null(Quest("lumicite", State((0x14, 0x80))));
        Assert.Null(Quest("lumicite", State((0x14, 0x40)))); // No invented random-spawn position.
        Is("lumicite:hunt-587", Quest("lumicite", State((0x14, 0x40)), 587));
        Assert.Null(Quest("lumicite", State((0x14, 0xC0)), 587));
    }

    [Theory]
    [InlineData(0, 3)] [InlineData(1, 2)] [InlineData(2, 2)] [InlineData(4, 2)]
    [InlineData(3, 1)] [InlineData(5, 1)] [InlineData(6, 1)] [InlineData(7, 0)]
    public void ThreeVorticesCanBeCompletedInAnyOrder(int mask, int remaining)
    {
        var entries = Vortex(State((0x0A, 0), (0x22, mask)));
        Assert.Equal(remaining, entries.Length);
        Assert.Equal((mask & 1) == 0, entries.Contains("bonus:vortex:antiquity:enter"));
        Assert.Equal((mask & 2) == 0, entries.Contains("bonus:vortex:future:enter"));
        Assert.Equal((mask & 4) == 0, entries.Contains("bonus:vortex:present:enter"));
    }

    [Fact]
    public void GrottoVisitBitsAreNotShadeVictories()
    {
        Assert.Equal(3, Vortex(State((0x0A, 0), (0x21, 0xFF), (0x22, 0))).Length);
        var s = State((0x0A, 0));
        Assert.Contains("bonus:vortex:present:switch", Vortex(s, 608));
        Assert.Contains("bonus:vortex:present:ladder", Vortex(State((0x0A, 0), (0x23, 1)), 609));
        Assert.Contains("bonus:vortex:present:dalton", Vortex(State((0x0A, 0), (0x23, 3)), 613));
        Assert.Contains("bonus:vortex:present:grotto", Vortex(State((0x0A, 0), (0x21, 0x20)), 624));
    }

    [Fact]
    public void ZeroVortexCellsDoNotUnlockVorticesBeforeTheFirstGameClear()
    {
        var firstRun = State((0x0A, 0), (0x22, 0)) with
        { Globals = new Dictionary<int, int> { [0x1FB] = 0 } };
        Assert.Empty(Vortex(firstRun));
        Assert.Empty(Vortex(firstRun, 464));
        Assert.Empty(Vortex(firstRun with { Globals = new Dictionary<int, int>() }));
        Assert.Equal(["bonus:vortex:future:enter"], Vortex(firstRun with
            { Globals = new Dictionary<int, int> { [0x1FB] = 4 } }));
        Assert.Contains("bonus:vortex:future:cave", Vortex(firstRun, 663));
    }

    [Fact]
    public void ResearchLabRequiresAdministratorAndSecondLockRelease()
    {
        Assert.Contains("bonus:vortex:future:security", Vortex(State((0x0A, 0)), 620));
        Assert.Contains("bonus:vortex:future:door", Vortex(State((0x0A, 0), (0x2A, 1)), 620));
        Assert.Contains("bonus:vortex:future:alarm", Vortex(State((0x0A, 0), (0x2A, 2)), 617));
        Assert.Contains("bonus:vortex:future:register", Vortex(State((0x0A, 0), (0x2A, 0x12)), 622));
        Assert.Contains("bonus:vortex:future:release", Vortex(State((0x0A, 0), (0x2A, 0x16)), 618));
        Assert.Contains("bonus:vortex:future:grotto", Vortex(State((0x0A, 0), (0x2A, 0x17)), 620));
    }

    [Fact]
    public void EclipseNeedsGasparAndStageFourDoesNotMeanDreamDevourerDefeated()
    {
        Assert.Contains("bonus:eclipse:gaspar", Vortex(State((0x0A, 1), (0x22, 7)), 464));
        Assert.DoesNotContain("bonus:eclipse:gate", Vortex(State((0x0A, 1), (0x22, 7)), 464));
        Assert.Contains("bonus:eclipse:gate", Vortex(State((0x0A, 2), (0x22, 7)), 464));
        Assert.Contains("bonus:eclipse:devourer", Vortex(State((0x0A, 4), (0x22, 7)), 591));
        Assert.Empty(Vortex(State((0x0A, 4), (0x22, 7)), 592));
        Assert.Empty(Vortex(State((0x0A, 255)), 464));
    }

    [Fact]
    public void EveryGoalUsesARealActorRegionOrKnownTreasureAndOptionalCategory()
    {
        Assert.Equal(BonusStoryObjectives.All.Count, BonusStoryObjectives.All.Select(o => o.Id).Distinct().Count());
        foreach (var objective in BonusStoryObjectives.All)
        {
            Assert.Contains(objective.Category, new[] { NavigationCategory.People, NavigationCategory.Objects, NavigationCategory.Exits });
            Assert.NotEmpty(objective.Goals);
            foreach (var goal in objective.Goals)
            {
                var scene = Assert.IsType<GameNavigationCatalog.Scene>(GameNavigationCatalog.ForScene(goal.Scene));
                foreach (var actor in goal.Actors)
                    Assert.True(scene.Actors.Any(a => a.Id == actor), $"{objective.Id}: missing actor {goal.Scene}/{actor}");
                foreach (var target in goal.Targets)
                    Assert.True(target is "chest:301" or "chest:286" || scene.Regions.Any(r => r.Id == target),
                        $"{objective.Id}: missing native target {target}");
            }
        }
        foreach (var objective in BonusStoryObjectives.All.Where(o => o.Id.EndsWith(":enter")))
            Assert.DoesNotContain(objective.Goals.SelectMany(g => g.Actors), a => a == 9); // Return Gate.
    }

    [Fact]
    public void ForestSpatialBindingsRetainInitialAndRepeatEncounterGuards()
    {
        var scene = GameNavigationCatalog.ForScene(587)!;
        var trigger = scene.Regions.Where(r => r.Kind == "Encounter" && r.Left == 22 && r.Top == 23 &&
            r.Right == 29 && r.Bottom == 31).ToArray();
        var initial = State((0x12, 0), (0x17, 0));
        Assert.Contains(trigger, r => r.Value == 0x82 && r.Available(initial));
        Assert.DoesNotContain(trigger, r => r.Value == 2 && r.Available(initial));
        var cleared = State((0x12, 8), (0x17, 0));
        Assert.Contains(trigger, r => r.Value == 2 && r.Available(cleared));
        Assert.DoesNotContain(trigger, r => r.Available(State((0x12, 0), (0x17, 4))));
        Assert.DoesNotContain(trigger, r => r.Available(initial with
            { Locals = new Dictionary<int, int>(initial.Locals) { [0x0D] = 1 } }));
    }

    [Fact]
    public void GoldhammerIntroductionAndFightHaveDifferentNativeRegionsAndPrerequisites()
    {
        var regions = GameNavigationCatalog.ForScene(589)!.Regions;
        var intro = regions.Where(r => r.Left == 14 && r.Top == 10 && r.Right == 31 && r.Bottom == 14).ToArray();
        var fight = regions.Where(r => r.Left == 18 && r.Top == 47 && r.Right == 34 && r.Bottom == 51).ToArray();
        Assert.Contains(intro, r => r.Available(State((0x12, 0x18))));
        Assert.DoesNotContain(intro, r => r.Available(State((0x12, 0x18), (0x1B, 8))));
        Assert.DoesNotContain(fight, r => r.Available(State((0x12, 0x18), (0x1B, 8), (0x18, 1))));
        Assert.Contains(fight, r => r.Available(State((0x12, 0x18), (0x1B, 8), (0x18, 2))));
        Assert.DoesNotContain(fight, r => r.Available(State((0x12, 0x18), (0x1B, 0x18), (0x18, 2))));
    }

    [Fact]
    public void LaboratoryContactAndConsoleMetadataSurvivePartyBranchBudgets()
    {
        var door = GameNavigationCatalog.ForScene(618)!.Actors.Single(a => a.Id == 13);
        Assert.True(door.Marker && door.Touch);
        Assert.Contains(door.Actions, a => a.Touch && a.Available(State((0x2A, 1))));
        Assert.DoesNotContain(door.Actions, a => a.Available(State((0x2A, 0))));
        var console = GameNavigationCatalog.ForScene(622)!.Actors.Single(a => a.Id == 8);
        Assert.Contains(console.Actions, a => !a.Touch && a.Kind == "Switch" && a.Available(State((0x2A, 0x12))));
        Assert.DoesNotContain(console.Actions, a => a.Available(State((0x2A, 0x1A))));
    }
}
