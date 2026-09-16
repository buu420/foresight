using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class ClassicOptionalObjectivesTests
{
    private static FieldStoryState State(int point = 0xD4, params (int, int)[] flags)
    {
        var globals = Enumerable.Range(0, 512).ToDictionary(i => i, _ => 0);
        foreach (var (index, value) in flags) globals[index] = value;
        return new(point, false)
        {
            Globals = globals, Inventory = new Dictionary<int, int>(),
            Party = [0, 1, 2, 3, 4, 5, 6, 255, 255]
        };
    }

    private static FullStoryObjective? Quest(string family, FieldStoryState state, int scene = 496) =>
        ClassicOptionalObjectives.Build(scene, state).SingleOrDefault(o => o.Id.StartsWith("full:quest:" + family + ":"));

    private static FieldStoryState Locals(FieldStoryState state, params (int Index, int Value)[] cells) =>
        state with { Locals = Enumerable.Range(0, 256).ToDictionary(i => i,
            i => cells.FirstOrDefault(c => c.Index == i).Value) };

    [Fact]
    public void OptionalQuestsNeedReadableNativeState()
    {
        Assert.Empty(ClassicOptionalObjectives.Build(496, null));
        Assert.Empty(ClassicOptionalObjectives.Build(496, new FieldStoryState(0xD4, false)));
        Assert.Empty(ClassicOptionalObjectives.Build(226, State()));
    }

    [Fact]
    public void FlightQuestsDoNotWaitForTheBlackOmenCutscene()
    {
        Assert.Null(Quest("sun", State(0xD1)));
        Assert.NotNull(Quest("sun", State(0xD2)));
        Assert.NotNull(Quest("cyrus", State(0xD2)));
    }

    [Fact]
    public void DesertUsesTheDefeatFlagAndThenRoboAndTheShrine()
    {
        var emerging = State(0xD4, (0xF7, 2), (0x1AD, 0x80), (0x1A3, 4));
        Assert.Equal("full:quest:desert:retinite", Quest("desert", emerging)!.Id);
        var defeated = emerging with { Globals = new Dictionary<int, int>(emerging.Globals) { [0x1A3] = 1 } };
        Assert.Equal("full:quest:desert:robo", Quest("desert", defeated)!.Id);
        var planted = State(0xD4, (0xF7, 2), (0x1A3, 1), (0x19E, 1));
        Assert.Equal("full:quest:desert:recover", Quest("desert", planted)!.Id);
        Assert.Null(Quest("desert", State(0xD4, (0x19E, 3))));
    }

    [Fact]
    public void LuccasPastIsOnlyOfferedDuringTheActualCampfireSequence()
    {
        Assert.Null(Quest("lucca", State(), 57));
        Assert.Null(Quest("lucca", State(0xD4, (0x19E, 3)), 4));
        Assert.Equal("full:quest:lucca:gate", Quest("lucca", State(0xD4, (0x19E, 3)), 436)!.Id);
        Assert.Equal("full:quest:lucca:machine", Quest("lucca", State(0xD4, (0x19E, 3), (0x5A, 6)), 4)!.Id);
        Assert.Equal("full:quest:lucca:return", Quest("lucca", State(0xD4, (0x19E, 3), (0x5A, 6), (0x7C, 0x40)), 10)!.Id);
        Assert.Null(Quest("lucca", State(0xD4, (0x19E, 3), (0x6A, 0x10)), 496));
    }

    [Fact]
    public void CarpenterEraBitsDoNotMasqueradeAsCompletedRepairs()
    {
        var flags = State(0xD4, (0x19E, 0xF0), (0x1A3, 0x18));
        Assert.Equal("full:quest:cyrus:hire", Quest("cyrus", flags)!.Id);
        Assert.Equal("full:quest:cyrus:lower-monsters", Quest("cyrus", State(0xD4, (0x19E, 0xF0), (0x19F, 2)), 497)!.Id);
        Assert.Equal("full:quest:cyrus:lower-repair", Quest("cyrus", State(0xD4, (0x19E, 0xF0), (0x19F, 0x22)))!.Id);
        Assert.Equal("full:quest:cyrus:grave", Quest("cyrus", State(0xD4, (0x19E, 0xF0), (0x19F, 0x26)), 497)!.Id);
    }

    [Fact]
    public void SharedNorthernRuinsScenesMustNotSendThePartyToTheWrongEra()
    {
        var repaired = State(0xD4, (0x19E, 0xF0), (0x19F, 0x26));
        Assert.Null(Quest("cyrus", repaired, 496));
        Assert.Null(Quest("cyrus", repaired with { Globals = new Dictionary<int, int>(repaired.Globals) { [0x1A3] = 8 } }, 73));
        Assert.Equal("full:quest:cyrus:grave", Quest("cyrus", repaired, 497)!.Id);
        Assert.Equal("full:quest:cyrus:grave", Quest("cyrus", repaired with { Globals = new Dictionary<int, int>(repaired.Globals) { [0x1A3] = 0x10 } }, 73)!.Id);
    }

    [Fact]
    public void SunStoneKeepsThePurchaseGiftRecoveryAndRechargingSeparate()
    {
        Assert.Equal("full:quest:sun:moon-stone", Quest("sun", State(0xD4, (0x13A, 1)))!.Id);
        Assert.Equal("full:quest:sun:prehistoric", Quest("sun", State(0xD4, (0x13A, 3)))!.Id);
        Assert.Equal("full:quest:sun:missing", Quest("sun", State(0xD4, (0x13A, 7)))!.Id);
        Assert.Equal("full:quest:sun:jerky", Quest("sun", State(0xD4, (0x13A, 15)))!.Id);
        Assert.Equal("full:quest:sun:kindness", Quest("sun", State(0xD4, (0x13A, 15)) with
            { Inventory = new Dictionary<int, int> { [0x500C] = 1 } })!.Id);
        Assert.Equal("full:quest:sun:mayor", Quest("sun", State(0xD4, (0x13A, 15), (0x1D2, 4)))!.Id);
        Assert.Equal("full:quest:sun:replace", Quest("sun", State(0xD4, (0x13A, 23), (0x1D2, 4)))!.Id);
        Assert.Equal("full:quest:sun:future", Quest("sun", State(0xD4, (0x13A, 55)))!.Id);
        Assert.Null(Quest("sun", State(0xD4, (0x13A, 0xFF))));
    }

    [Fact]
    public void SpentQuestItemsDoNotRestartTheirFetchSteps()
    {
        Assert.Equal("full:quest:sun:future", Quest("sun", State(0xD4, (0x13A, 55), (0x1D2, 4)))!.Id);
        Assert.DoesNotContain("tools", Quest("cyrus", State(0xD4, (0x19E, 0x40)))!.Id);
        Assert.DoesNotContain("toma", Quest("rainbow", State(0xD4, (0x1A3, 0x80)))!.Id);
        Assert.DoesNotContain("doll", Quest("revival", State(0xD5, (0x70, 4)))!.Id);
    }

    [Fact]
    public void GenoUsesBothDollsAndTheActualMotherBrainCompletionFlag()
    {
        Assert.NotNull(Quest("geno", State(0xD4, (0x13F, 0x80))));
        Assert.Null(Quest("geno", State(0xD4, (0x13B, 0x10))));
        Assert.Equal("full:quest:geno:atropos", Quest("geno", State(0xD4, (0x13C, 7)))!.Id);
        Assert.Equal("full:quest:geno:pedestal-left", Quest("geno", State(0xD4, (0x13C, 7), (0x13B, 0x20)))!.Id);
        Assert.Equal("full:quest:geno:pedestal-right", Quest("geno", State(0xD4, (0x13C, 0x17), (0x13B, 0x20)))!.Id);
        Assert.Equal("full:quest:geno:brain", Quest("geno", State(0xD4, (0x13C, 0x37), (0x13B, 0x20)))!.Id);
    }

    [Fact]
    public void GenoChargingExpiresAndMiddleSwitchMustRemainOff()
    {
        Assert.Equal("full:quest:geno:switch-middle", Quest("geno", State(0xD4, (0x13C, 1), (0x13D, 0x70)), 256)!.Id);
        Assert.Equal("full:quest:geno:charge-first", Quest("geno", State(0xD4, (0x13C, 1), (0x13D, 0xD0)), 256)!.Id);
        Assert.Equal("full:quest:geno:pod-first", Quest("geno", State(0xD4, (0x13C, 0x41), (0x13D, 0xD0)), 256)!.Id);
    }

    [Fact]
    public void GenoConveyorControlUsesTheUpperCircuitUntilThePartyReachesItsOtherSide()
    {
        var firstDoll = State(0xD4, (0x13C, 5));
        Assert.Equal("full:quest:geno:circuit-start", Quest("geno", Locals(firstDoll, (8, 5), (9, 12)), 256)!.Id);
        Assert.Equal("full:quest:geno:circuit-lift", Quest("geno", Locals(firstDoll, (6, 0), (7, 39)), 127)!.Id);
        Assert.Equal("full:quest:geno:circuit-door", Quest("geno", firstDoll, 268)!.Id);
        Assert.Equal("full:quest:geno:circuit-door", Quest("geno", Locals(firstDoll), 267)!.Id);
        Assert.Equal("full:quest:geno:circuit-rear", Quest("geno", Locals(firstDoll, (6, 1)), 267)!.Id);
        Assert.Equal("full:quest:geno:circuit-lift", Quest("geno", Locals(firstDoll, (6, 11)), 127)!.Id);
        Assert.Equal("full:quest:geno:reverse-conveyor", Quest("geno", Locals(firstDoll, (8, 43), (9, 6)), 256)!.Id);
        // A stale coordinate pair in a different room cannot skip the circuit.
        Assert.Equal("full:quest:geno:circuit-start", Quest("geno", Locals(firstDoll, (8, 43), (9, 6)), 496)!.Id);
    }

    [Fact]
    public void GenoEscortDoesNotCallAnUnstartedFollowerComplete()
    {
        var opened = State(0xD4, (0x13C, 5), (0x13E, 2));
        Assert.Equal("full:quest:geno:escort", Quest("geno", Locals(opened), 256)!.Id);
        Assert.Equal("full:quest:geno:escort-guard", Quest("geno", Locals(opened, (12, 1)), 256)!.Id);
        Assert.Equal("full:quest:geno:doll-second", Quest("geno", State(0xD4, (0x13C, 0x85)), 256)!.Id);
    }

    [Fact]
    public void TomaTabIsNotThePromiseAndShellHasItsOwnBossStep()
    {
        Assert.Equal("full:quest:rainbow:toma", Quest("rainbow", State(0xD4, (0x1AC, 0x10)))!.Id);
        Assert.Equal("full:quest:rainbow:tomb", Quest("rainbow", State(0xD4, (0x1A0, 2), (0x1AC, 0x10)))!.Id);
        Assert.Equal("full:quest:rainbow:shell", Quest("rainbow", State(0xD4, (0x1D2, 0x40)))!.Id);
        Assert.Equal("full:quest:rainbow:leave-shell", Quest("rainbow", State(0xD4, (0x1D2, 0x40), (0x1AF, 2)), 197)!.Id);
        Assert.Equal("full:quest:rainbow:trial", Quest("rainbow", State(0xD4, (0xA9, 0x80)))!.Id);
        Assert.Equal("full:quest:rainbow:proof", Quest("rainbow", State(0xD4, (0xA9, 0x80), (0x50, 0x20)))!.Id);
        Assert.Equal("full:quest:rainbow:melchior", Quest("rainbow", State(0xD4, (0x50, 0x40), (0x6D, 0x10)))!.Id);
    }

    [Theory]
    [InlineData(195, 12, 35, "claw-entrance")]
    [InlineData(297, 49, 7, "claw-throne")]
    [InlineData(195, 24, 50, "claw-switch-room")]
    [InlineData(110, 11, 27, "claw-floor-switch")]
    [InlineData(110, 29, 19, "claw-skull-switch")]
    [InlineData(196, 27, 44, "claw-lower-path")]
    [InlineData(196, 6, 8, "claw-southern-ladder")]
    [InlineData(296, 54, 60, "claw-skull-hall")]
    [InlineData(109, 22, 10, "claw-east-door")]
    [InlineData(195, 10, 9, "claw-trap-room")]
    [InlineData(109, 54, 42, "claw-chest-trap")]
    [InlineData(109, 25, 30, "claw-cell-stairs")]
    [InlineData(109, 59, 10, "claw-bars")]
    public void GiantClawFollowsItsSeparateNativeRoomSections(int scene, int x, int y, string suffix)
    {
        var state = Locals(State(0xD4, (0x1A3, 0x80)), (7, x), (8, y));
        Assert.Equal("full:quest:rainbow:" + suffix, Quest("rainbow", state, scene)!.Id);
    }

    [Fact]
    public void GiantClawSwitchesAdvanceToTheirOwnPassageAndNeverTheDangerousRightSwitch()
    {
        var state = State(0xD4, (0x1A3, 0x80));
        var floor = Quest("rainbow", Locals(state, (7, 11), (8, 27)), 110)!;
        Assert.Equal(["script-terrain:1:0:10:26:10:26"], Assert.Single(floor.Goals).Targets);
        Assert.Equal("full:quest:rainbow:claw-drop", Quest("rainbow", Locals(state, (7, 10), (8, 26), (9, 1)), 110)!.Id);
        Assert.Equal("full:quest:rainbow:claw-lower-door", Quest("rainbow", Locals(state, (7, 29), (8, 25), (11, 1)), 110)!.Id);
        Assert.Equal("full:quest:rainbow:claw-boss-door", Quest("rainbow", Locals(state, (7, 51), (8, 20), (12, 1)), 109)!.Id);
    }

    [Fact]
    public void RevivalCollectsCronosDollAndDoesNotUseMagussDollBit()
    {
        var egg = State(0xD5, (0x7C, 1), (0x5E, 0x40));
        Assert.Equal("full:quest:revival:doll-game", Quest("revival", egg)!.Id);
        Assert.Equal("full:quest:revival:mother", Quest("revival", State(0xD5, (0x7C, 1), (0x5E, 1)))!.Id);
        Assert.Equal("full:quest:revival:doll-collect", Quest("revival", State(0xD5, (0x7C, 1), (0x5E, 1), (0x14F, 1)))!.Id);
        Assert.Equal("full:quest:revival:belthasar", Quest("revival", egg with
            { Inventory = new Dictionary<int, int> { [0x5013] = 1 } })!.Id);
        Assert.Null(Quest("revival", State(0xD5, (0x57, 0x40))));
    }

    [Fact]
    public void OzzieTracksTheActualBattleRooms()
    {
        Assert.Equal("full:quest:ozzie:slash", Quest("ozzie", State(0xD2, (0x1A1, 4)))!.Id);
        Assert.Equal("full:quest:ozzie:guillotine", Quest("ozzie", State(0xD2, (0x1A1, 12)))!.Id);
        Assert.Null(Quest("ozzie", State(0xD2, (0x1A1, 0x80))));
    }

    [Fact]
    public void BlackOmenUsesEraPresenceAndCanBeClearedAgainInAnEarlierEra()
    {
        Assert.NotNull(Quest("omen", State(0xD4, (0x1F7, 8)), 496));
        Assert.Null(Quest("omen", State(0xD4, (0x1F7, 4)), 496));
        Assert.NotNull(Quest("omen", State(0xD6, (0x1F7, 4)), 497));
        Assert.Null(Quest("omen", State(0xD6, (0x1F7, 0x10)), 498));
        Assert.Null(Quest("omen", State(0xD6), 464));
    }

    [Fact]
    public void BlackOmenDoesNotMistakeThePanelsForTheTerraMutant()
    {
        var panelsOnly = State(0xD4, (0x1A8, 1), (0x71, 0x10), (0x15A, 2));
        var terra = Quest("omen", panelsOnly, 325)!;
        Assert.Equal("full:quest:omen:terra", terra.Id);
        Assert.Equal([16], Assert.Single(terra.Goals).Actors);
        Assert.Equal("full:quest:omen:spawn", Quest("omen", State(0xD4, (0x1A8, 1), (0x71, 0x10), (0x15A, 3)), 325)!.Id);
    }

    [Theory]
    [InlineData(315, "nu-door")]
    [InlineData(324, "terra-door")]
    [InlineData(326, "spawn-door")]
    public void BlackOmenIncludesDoorsThatMustOpenBeforeTheirExitExists(int scene, string suffix) =>
        Assert.Equal("full:quest:omen:" + suffix, Quest("omen", Locals(State()), scene)!.Id);

    [Theory]
    [InlineData(1, "lift-down")]
    [InlineData(2, "lift-exit")]
    [InlineData(4, "lift-up")]
    [InlineData(8, "lift-exit")]
    public void BlackOmenLiftTracksItsNativeLandingInsteadOfChoosingEitherMotor(int flags, string suffix) =>
        Assert.Equal("full:quest:omen:" + suffix, Quest("omen", State(0xD4, (0x1A7, flags)), 99)!.Id);

    [Fact]
    public void GiantClawTerrainAndSelfSceneDropActuallyBindInACapturedFieldFrame()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var state = Locals(State(0xD4, (0x1A3, 0x80)), (7, 10), (8, 27));
        var field = Field(110, 10 * 256 + 128, 27 * 256 + 128,
            ActorSnapshot(0, 0, 0) with { DrawMode = 0 }, ActorSnapshot(1, 0, 0) with { DrawMode = 0 });
        var floor = source.Build(field, Map(), new(0, 0, 256, 256), [], state);
        var target = Assert.Single(floor.Targets, t => t.Id == "story:full:quest:rainbow:claw-floor-switch");
        Assert.False(target.IsStoryNote);
        Assert.True(target.GuideAvailable);
        Assert.Equal(NavigationCategory.Objects, target.Category);
        Assert.NotNull(NavigationPathfinder.Find(floor.Graph, floor.Player, target.ApproachPoints));

        var dropped = source.Build(field, Map(), new(0, 0, 256, 256), [], Locals(state, (7, 10), (8, 27), (9, 1)));
        Assert.DoesNotContain(dropped.Targets, t => t.Id == target.Id);
        var hole = Assert.Single(dropped.Targets, t => t.Id == "story:full:quest:rainbow:claw-drop");
        Assert.Equal(NavigationCategory.Exits, hole.Category);
        Assert.False(hole.IsStoryNote);
        Assert.NotNull(NavigationPathfinder.Find(dropped.Graph, dropped.Player, hole.ApproachPoints));
    }

    [Fact]
    public void OmenLiftObjectiveRequiresTheActualActiveMotor()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var state = Locals(State(0xD4, (0x1A7, 1)));
        var motor = ActorSnapshot(3, 2 * 256 + 80, 20 * 256 + 96) with { ClassTag = 4, VisualIndex = 113 };
        var frame = source.Build(Field(99, 8 * 256, 18 * 256, motor), Map(), new(0, 0, 256, 256), [], state);
        var target = Assert.Single(frame.Targets, t => t.Id == "story:full:quest:omen:lift-down");
        Assert.Equal(NavigationCategory.Exits, target.Category);
        Assert.False(target.IsStoryNote);
        Assert.NotNull(NavigationPathfinder.Find(frame.Graph, frame.Player, target.ApproachPoints));
        var absent = source.Build(Field(99, 8 * 256, 18 * 256), Map(), new(0, 0, 256, 256), [], state);
        Assert.DoesNotContain(absent.Targets, t => t.Id == target.Id);
    }

    [Fact]
    public void NativePassagesUseTheExitsCategory()
    {
        foreach (var objective in ClassicOptionalObjectives.All.Where(o => o.Goals.Any(g =>
                     g.Targets.Any(t => t.StartsWith("exit:") || t.StartsWith("script-region:")))))
            Assert.Equal(NavigationCategory.Exits, objective.Category);
        Assert.Equal(NavigationCategory.Exits,
            ClassicOptionalObjectives.All.Single(o => o.Id == "full:quest:lucca:gate").Category);
        Assert.Equal(NavigationCategory.People,
            ClassicOptionalObjectives.All.Single(o => o.Id == "full:quest:rainbow:melchior").Category);
    }

    [Theory]
    [InlineData("geno", 248)]
    [InlineData("rainbow:claw", 195)]
    [InlineData("omen", 449)]
    public void EveryDungeonGoalHasAChainOfNativeFieldConnections(string prefix, int entrance)
    {
        var router = new SceneConnectionRouter();
        var state = State(0xD4, (0x1A8, 8), (0x1F7, 14));
        foreach (var objective in ClassicOptionalObjectives.All.Where(o =>
                     o.Id.StartsWith("full:quest:" + prefix)))
        foreach (var goal in objective.Goals)
            Assert.True(router.DistanceWithinFields(entrance, state, goal.Scene).HasValue,
                $"{objective.Id}: native field connections cannot reach {goal.Scene} from {entrance}");
    }

    [Fact]
    public void EveryGoalHasANativeActorRegionExitOrAuditedAutomaticArrival()
    {
        var arrivals = new HashSet<int> { 65, 265 };
        foreach (var objective in ClassicOptionalObjectives.All)
        {
            Assert.NotEqual(NavigationCategory.StoryEvents, objective.Category);
            Assert.NotEmpty(objective.Goals);
            foreach (var goal in objective.Goals)
            {
                var scene = GameNavigationCatalog.ForScene(goal.Scene);
                Assert.NotNull(scene);
                var actors = scene!.Actors.Select(a => a.Id).Concat(scene.Regions.Select(r => r.Actor)).ToHashSet();
                foreach (var actor in goal.Actors)
                    Assert.True(actors.Contains(actor), $"{objective.Id}: no native actor/region {goal.Scene}/{actor}");
                foreach (var target in goal.Targets)
                    Assert.True(scene.Exits.Any(e => target == $"exit:{e.Id}") || scene.Regions.Any(r => r.Id == target),
                        $"{objective.Id}: no native target {goal.Scene}/{target}");
                if (goal.Actors.Length == 0 && goal.Targets.Length == 0)
                    Assert.True(arrivals.Contains(goal.Scene), $"{objective.Id}: unaudited automatic arrival");
            }
        }
        Assert.Equal(ClassicOptionalObjectives.All.Count,
            ClassicOptionalObjectives.All.Select(o => o.Id).Distinct().Count());
    }

    private static FieldActorSnapshot ActorSnapshot(int index, int x, int y) =>
        new(index, x >> 8, x, x & 255, y >> 8, y, y & 255, 0, 1, 0, 0, 7, 0, 1, 1, false, true, true, true);
    private static FieldNavigationSnapshot Field(int scene, int x, int y, params FieldActorSnapshot[] actors)
    {
        var lead = ActorSnapshot(7, x, y) with { ClassTag = 2, IsPartyMember = true };
        return new(0x1000, 0x4000, 0x2000, 0x20000, 8, scene, true, 1, 0, 0, 7, lead, [lead, .. actors]);
    }
    private static FieldMapSnapshot Map() => new(32, 32, new byte[1024], new byte[1024],
        Enumerable.Repeat((byte)1, 1024).ToArray(), 1, false, 32, 32, Enumerable.Repeat((byte)128, 1024).ToArray());
    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
