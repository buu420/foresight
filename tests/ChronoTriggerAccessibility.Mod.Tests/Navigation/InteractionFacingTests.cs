using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>17D230 dispatches on the leader's facing (actor+0x60, doubled: 0 up 17D4C0,
/// 1 down 17D610, 2 left 17D760, 3 right 17D8B0) and each routine tests only the faced side.
/// Decompile: artifacts/research/engine-0332/native/functions/0017D230.c, 0017CFD0.c,
/// 0017D0C0.c, 0017D4C0.c, 0017D610.c, 0017D760.c, 0017D8B0.c.</summary>
public sealed class InteractionFacingTests
{
    [Fact]
    public void TheArmourSellerLogCaseOnlyAnswersAnUpwardConfirm()
    {
        // 0.3.37 log, scene 5: seller actor 24 at (3840,9536). The walk ended at (3680,9855)
        // after moving right, facing right; Confirm could not reach. Up would have.
        Assert.Equal([NavigationDirection.North], FieldInteractionRange.Facings(3680, 9855, 3840, 9536));
        Assert.DoesNotContain(NavigationDirection.East, FieldInteractionRange.Facings(3680, 9855, 3840, 9536));
        // The earlier walk ended facing up at (3968,9951), and the shop opened.
        Assert.Equal([NavigationDirection.North], FieldInteractionRange.Facings(3968, 9951, 3840, 9536));
    }

    [Theory]
    [InlineData(0, -300, NavigationDirection.North)]
    [InlineData(0, 300, NavigationDirection.South)]
    [InlineData(-300, 0, NavigationDirection.West)]
    [InlineData(300, 0, NavigationDirection.East)]
    public void OnlyTheSideTheActorIsOnCounts(int dx, int dy, NavigationDirection expected) =>
        Assert.Equal([expected], FieldInteractionRange.Facings(1000, 1000, 1000 + dx, 1000 + dy));

    [Fact]
    public void BoundsStayOneUnitInsideTheHandlersCarryDependentCompare()
    {
        Assert.Equal([NavigationDirection.North], FieldInteractionRange.Facings(0, 447, 0, 0));
        Assert.Empty(FieldInteractionRange.Facings(0, 448, 0, 0));
        Assert.Equal([NavigationDirection.North], FieldInteractionRange.Facings(191, 300, 0, 0));
        Assert.Empty(FieldInteractionRange.Facings(192, 300, 0, 0));
        Assert.Empty(FieldInteractionRange.Facings(500, 500, 500, 500));
    }

    [Fact]
    public void ADiagonalInsideBothLobesPrefersTheLargerGap()
    {
        Assert.Equal([NavigationDirection.North, NavigationDirection.West],
            FieldInteractionRange.Facings(1000, 1000, 900, 850));
    }

    [Fact]
    public void TheCollisionOffsetShiftsTheActorXAsAllFourRoutinesDo()
    {
        // X - 16 * [actor+0x14C]: an offset of 12 moves the tested X 192 left.
        Assert.Empty(FieldInteractionRange.Facings(1000, 1300, 1000, 1000, 12));
        Assert.Equal([NavigationDirection.North], FieldInteractionRange.Facings(808, 1300, 1000, 1000, 12));
    }

    [Theory]
    [InlineData(0, NavigationDirection.North)]
    [InlineData(1, NavigationDirection.South)]
    [InlineData(2, NavigationDirection.West)]
    [InlineData(3, NavigationDirection.East)]
    [InlineData(4, NavigationDirection.None)]
    public void NativeFacingValuesMapToTheHandlersDispatch(int value, NavigationDirection expected) =>
        Assert.Equal(expected, FieldInteractionRange.NativeFacing(value));

    [Fact]
    public void TheShopkeeperIsFinishedFacingTheCounterThatRunsTheirScript()
    {
        var frame = Market(facing: 3);
        Assert.Equal(NavigationDirection.East, frame.PlayerFacing);
        var keeper = Assert.Single(frame.Targets, t => t.Id == "actor:8:4:49");
        Assert.NotNull(keeper.ConfirmFacings);
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, keeper.ApproachPoints).Route;
        Assert.NotNull(route);
        var facings = keeper.ConfirmFacings!(route[^1]);
        Assert.NotEmpty(facings);
        // Every offered facing reaches the counter (actor 9) itself, not merely the region.
        Assert.All(facings, f => Assert.Contains(f, FieldInteractionRange.Facings(route[^1].X, route[^1].Y, 14336, 2032)));
    }

    [Fact]
    public void AutomaticWalkToTheShopkeeperTurnsBeforeItCallsItArrived()
    {
        var frame = Market(facing: 3);
        var keeper = frame.Targets.Single(t => t.Id == "actor:8:4:49");
        var goal = NavigationPathfinder.Search(frame.Graph, frame.Player, keeper.ApproachPoints).Route![^1];
        var controller = new NavigationController();
        for (var i = 0; i < 4 && controller.Handle(NavigationCommand.NextTarget, frame, 0).Speech
            .All(s => !s.StartsWith("Shopkeeper")); i++) { }
        controller.Handle(NavigationCommand.ToggleWalk, frame, 0);
        var there = controller.Update(frame with { Player = goal }, 100);
        Assert.DoesNotContain(there.Speech, s => s.StartsWith("Arrived"));
        Assert.True(there.AutoWalking);
        Assert.Contains(there.Direction, keeper.ConfirmFacings!(goal));
        var facing = controller.Update(frame with { Player = goal, PlayerFacing = there.Direction }, 150);
        Assert.Contains("Arrived at Shopkeeper.", facing.Speech);
        Assert.False(facing.AutoWalking);
    }

    [Fact]
    public void AMixedStoryObjectiveKeepsEachAlternativesOwnFinish()
    {
        var frame = Market(facing: 0);
        Assert.All(frame.Targets.Where(t => t.Category == NavigationCategory.Exits), t => Assert.Null(t.ConfirmFacings));
        var keeper = frame.Targets.Single(t => t.Id == "actor:8:4:49");
        var exitGoal = new NavigationPoint(15488, 3200, 1);
        var exit = new NavigationTarget("exit:0", "Outside", NavigationCategory.Exits, exitGoal, [exitGoal], true, true);
        var mixed = StoryTarget.BindAny("x", "Either", [.. frame.Targets, exit], ["actor:8:4:49", "exit:0"], frame.Player);
        // The exit's goal is an ordinary arrival; every keeper goal still needs the counter facing.
        Assert.Null(mixed.ConfirmAt(exitGoal));
        Assert.All(keeper.ApproachPoints.Where(mixed.ApproachPoints.Contains), goal => Assert.NotNull(mixed.ConfirmAt(goal)));
        // The customer-side goal the route actually reaches (the keeper's own floor is walled off).
        var keeperGoal = NavigationPathfinder.Search(frame.Graph, frame.Player,
            keeper.ApproachPoints.Where(mixed.ApproachPoints.Contains).ToArray()).Route![^1];
        Assert.Equal(keeper.ConfirmFacings!(keeperGoal), mixed.ConfirmAt(keeperGoal)!(keeperGoal));

        // Walked to the keeper's goal facing away, the mixed objective turns instead of arriving.
        var controller = new NavigationController();
        var story = frame with { Targets = [mixed with { Category = NavigationCategory.People, ApproachPoints = [keeperGoal] }] };
        controller.Handle(NavigationCommand.ToggleWalk, story, 0);
        var away = new[] { NavigationDirection.North, NavigationDirection.South, NavigationDirection.West, NavigationDirection.East }
            .First(f => !keeper.ConfirmFacings!(keeperGoal).Contains(f));
        var atKeeper = controller.Update(story with { Player = keeperGoal, PlayerFacing = away }, 100);
        Assert.DoesNotContain(atKeeper.Speech, s => s.StartsWith("Arrived"));
        Assert.Contains(atKeeper.Direction, keeper.ConfirmFacings!(keeperGoal));

        // Walked to the exit's goal, it arrives without any turn.
        controller = new NavigationController();
        var toExit = frame with { Player = exitGoal with { Y = exitGoal.Y - 64 },
            Targets = [mixed with { Category = NavigationCategory.People, ApproachPoints = [exitGoal] }] };
        controller.Handle(NavigationCommand.ToggleWalk, toExit, 0);
        var atExit = controller.Update(toExit with { Player = exitGoal, PlayerFacing = NavigationDirection.South }, 100);
        Assert.Contains("Arrived at Either.", atExit.Speech);

        var person = StoryTarget.Bind("actor:8:4:49", "Talk to the shopkeeper", frame.Targets);
        Assert.NotNull(person.ConfirmFacings);
    }

    [Fact]
    public void AnActorWithACollisionOffsetIsRoutedToWhereConfirmActuallyReachesIt()
    {
        // Actor+0x14C = 12 moves the X that 17D230 tests 192 pixels left of the sprite. Goals
        // around the sprite X (the old four one-step goals, or the old symmetric box) include
        // (1152,896) and (1408,1152); from neither can Confirm reach the native point (960,1152).
        var frame = Open(Npc(8, 1152, 1152) with { CollisionOffsetX = 12 });
        var target = Assert.Single(frame.Targets, t => t.Id == "actor:8:4:14");
        Assert.NotEmpty(target.ApproachPoints);
        Assert.DoesNotContain(new NavigationPoint(1152, 896, 1), target.ApproachPoints);
        Assert.DoesNotContain(new NavigationPoint(1408, 1152, 1), target.ApproachPoints);
        Assert.All(target.ApproachPoints, goal =>
            Assert.NotEmpty(FieldInteractionRange.FacingsWithin(goal.X, goal.Y, 1152, 1152, 12, 32)));
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route;
        Assert.NotNull(route);
        foreach (var dx in new[] { -32, 32 })
        foreach (var dy in new[] { -32, 32 })
            Assert.NotEmpty(target.ConfirmFacings!(route[^1] with { X = route[^1].X + dx, Y = route[^1].Y + dy }));

        // The controller reaches the goal and then faces the native point.
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.ToggleWalk, frame, 0);
        var there = controller.Update(frame with { Player = route[^1], PlayerFacing = NavigationDirection.None }, 100);
        Assert.DoesNotContain(there.Speech, s => s.StartsWith("Arrived"));
        Assert.Contains(there.Speech, s => s.Contains("Face "));
    }

    [Fact]
    public void AFacingAHigherSlotActorWouldTakeIsNotOffered()
    {
        // 17D230 scans from the highest slot down and stops at the first eligible actor in
        // range on the faced side. Actor 9, north of actor 8, takes an upward Confirm from
        // below actor 8; actor 7 in the same place would not, because 8 is scanned first.
        var south = new NavigationPoint(1152, 1408, 1);
        FieldActorSnapshot[] actors = [Npc(8, 1152, 1152), Npc(9, 1100, 1000) with { VisualIndex = 15 }];
        var blocked = Open(actors);
        var target = blocked.Targets.Single(t => t.Id == "actor:8:4:14");
        Assert.Equal(9, FieldInteractionRange.ConfirmWinner(actors, 1, south.X, south.Y, NavigationDirection.North));
        Assert.DoesNotContain(NavigationDirection.North, target.ConfirmFacings!(south));
        Assert.DoesNotContain(south, target.ApproachPoints);
        Assert.All(target.ApproachPoints, goal => Assert.NotEmpty(target.ConfirmFacings!(goal)));

        var lower = Open(Npc(8, 1152, 1152), Npc(7, 1100, 1000) with { VisualIndex = 15 });
        Assert.Contains(NavigationDirection.North, lower.Targets.Single(t => t.Id == "actor:8:4:14").ConfirmFacings!(south));

        // An actor the scan skips (binding byte zero) cannot take it either.
        var idle = Open(Npc(8, 1152, 1152), Npc(9, 1100, 1000) with { VisualIndex = 15, ActivationBinding = 0x100 });
        Assert.Contains(NavigationDirection.North, idle.Targets.Single(t => t.Id == "actor:8:4:14").ConfirmFacings!(south));
    }

    [Fact]
    public void TreasureChestArrivalStandsOnAnAdjacentTileAndFacesTheChest()
    {
        // 179940 via 179C30/179CA0/179CF0/179D40 and the chest grid test at 179D90.
        Func<int, int, bool> none = (_, _) => false;
        Assert.Equal([NavigationDirection.North], FieldInteractionRange.TreasureFacings(1152, 1408, 4, 4, none));
        Assert.Equal([NavigationDirection.North], FieldInteractionRange.TreasureFacings(1152, 1664, 4, 4, none));
        Assert.Empty(FieldInteractionRange.TreasureFacings(1152, 1664, 4, 4, (x, y) => (x, y) == (4, 5)));
        Assert.Equal([NavigationDirection.South], FieldInteractionRange.TreasureFacings(1152, 896, 4, 4, none));
        Assert.Empty(FieldInteractionRange.TreasureFacings(1152, 640, 4, 4, none));
        Assert.Equal([NavigationDirection.East], FieldInteractionRange.TreasureFacings(896, 1152, 4, 4, none));
        Assert.Equal([NavigationDirection.West], FieldInteractionRange.TreasureFacings(1408, 1152, 4, 4, none));
        Assert.Empty(FieldInteractionRange.TreasureFacings(1408, 1408, 4, 4, none));

        var frame = Open(treasures: [new FieldTreasure(3, 4 * 256 + 128, 4 * 256 + 128)]);
        var chest = Assert.Single(frame.Targets, t => t.Id == "chest:3");
        Assert.Equal(4, chest.ApproachPoints.Count);
        // Every goal, anywhere in its arrival box, is a tile the native probe tests.
        Assert.All(chest.ApproachPoints, goal =>
        {
            foreach (var dx in new[] { -32, 32 })
            foreach (var dy in new[] { -32, 32 })
                Assert.Single(chest.ConfirmFacings!(goal with { X = goal.X + dx, Y = goal.Y + dy }));
        });
        var controller = new NavigationController();
        for (var i = 0; i < 2; i++) controller.Handle(NavigationCommand.NextCategory, frame, 0);
        controller.Handle(NavigationCommand.ToggleWalk, frame, 0);
        var goal = NavigationPathfinder.Search(frame.Graph, frame.Player, chest.ApproachPoints).Route![^1];
        var wrong = chest.ConfirmFacings!(goal)[0] == NavigationDirection.North ? NavigationDirection.South : NavigationDirection.North;
        var there = controller.Update(frame with { Player = goal, PlayerFacing = wrong }, 100);
        Assert.DoesNotContain(there.Speech, s => s.StartsWith("Arrived"));
        Assert.Equal(chest.ConfirmFacings!(goal)[0], there.Direction);
        Assert.Contains("Arrived at Treasure chest.",
            controller.Update(frame with { Player = goal, PlayerFacing = there.Direction }, 150).Speech);
    }

    [Fact]
    public void AnOffCameraTargetIsStillRoutedButArrivalWaitsForItsLiveScanGate()
    {
        // +0x20 is camera-cull state (17A4D0 -> 17A6C0 clears it off camera), so planning stays
        // permissive; 17D230 skips a slot whose byte is zero, so arrival is not ready yet.
        var culled = Open(Npc(8, 1152, 1152) with { ActivationBinding = 0 });
        var target = culled.Targets.Single(t => t.Id == "actor:8:4:14");
        Assert.NotEmpty(target.ApproachPoints);
        var route = NavigationPathfinder.Search(culled.Graph, culled.Player, target.ApproachPoints).Route;
        Assert.NotNull(route);
        var goal = route[^1];
        var facing = FieldInteractionRange.Facings(goal.X, goal.Y, 1152, 1152)[0];
        Assert.Empty(target.ConfirmFacings!(goal));
        Assert.True(target.ConfirmPending!(goal));

        var controller = new NavigationController();
        controller.Handle(NavigationCommand.ToggleWalk, culled, 0);
        var waiting = controller.Update(culled with { Player = goal, PlayerFacing = facing }, 100);
        Assert.DoesNotContain(waiting.Speech, s => s.StartsWith("Arrived") || s.Contains("stopped"));
        Assert.Contains(waiting.Speech, s => s.Contains("would not reach it yet"));
        Assert.True(waiting.AutoWalking);

        // The next capture sees the byte set (the target is now drawn on camera): ready.
        var live = Open(Npc(8, 1152, 1152) with { ActivationBinding = 0x80 });
        var ready = controller.Update(live with { Player = goal, PlayerFacing = facing }, 150);
        Assert.Contains("Arrived at Person.", ready.Speech);

        // Walking gives up honestly if the gate never opens.
        controller = new NavigationController();
        controller.Handle(NavigationCommand.ToggleWalk, culled, 0);
        controller.Update(culled with { Player = goal, PlayerFacing = facing }, 100);
        var gave = controller.Update(culled with { Player = goal, PlayerFacing = facing }, 1700);
        Assert.DoesNotContain(gave.Speech, s => s.StartsWith("Arrived"));
        Assert.Contains(gave.Speech, s => s.Contains("did not become ready"));
    }

    [Fact]
    public void TheReviewersBoundaryCompetitorTakesTheUpwardConfirmExactlyAsTheScanDoes()
    {
        // Leader slot 1 at (1056,1344); target slot 8 at (1024,1088); competitor slot 9 at (1248,1088).
        // 17D4C0 for slot 9 (carry 1, slot > leader): along 1088-1344 = -256 is in [-448,-1];
        // across: 1248 + ~1056 + 0 = 191 (the along add left carry 0), under 0xC0: accepted.
        FieldActorSnapshot[] actors =
            [FullGameNavigationTests.Actor(1, 1056, 1344, true) with { ClassTag = 0 }, Npc(8, 1024, 1088), Npc(9, 1248, 1088) with { VisualIndex = 15 }];
        Assert.Equal(9, FieldInteractionRange.ConfirmWinner(actors, 1, 1056, 1344, NavigationDirection.North));
        var frame = Open(actors[1..]);
        var target = frame.Targets.Single(t => t.Id == "actor:8:4:14");
        Assert.DoesNotContain(NavigationDirection.North, target.ConfirmFacings!(new NavigationPoint(1056, 1344, 1)));
        // (1024,1344) needs an upward Confirm and its arrival box includes X 1056.
        Assert.DoesNotContain(new NavigationPoint(1024, 1344, 1), target.ApproachPoints);
        Assert.All(target.ApproachPoints, goal =>
        {
            foreach (var dx in new[] { -32, 0, 32 })
            foreach (var dy in new[] { -32, 0, 32 })
                Assert.NotEmpty(target.ConfirmFacings!(goal with { X = goal.X + dx, Y = goal.Y + dy }));
        });
    }

    [Theory]
    // Along the faced axis, gap = actor - leader; C is the carry the 17D230 loop leaves.
    [InlineData(NavigationDirection.North, 0, 0, 0, true)]      // up, C=0: [-447, 0]
    [InlineData(NavigationDirection.North, 0, 0, 1, false)]     // up, C=1: [-448, -1]
    [InlineData(NavigationDirection.North, 0, -448, 1, true)]
    [InlineData(NavigationDirection.North, 0, -448, 0, false)]
    [InlineData(NavigationDirection.South, 0, 448, 0, true)]    // down, C=0: [1, 448]
    [InlineData(NavigationDirection.South, 0, 448, 1, false)]   // down, C=1: [0, 447]
    [InlineData(NavigationDirection.South, 0, 0, 1, true)]
    [InlineData(NavigationDirection.South, 0, 0, 0, false)]
    [InlineData(NavigationDirection.West, 0, 0, 0, true)]
    [InlineData(NavigationDirection.West, -448, 0, 1, true)]
    [InlineData(NavigationDirection.East, 448, 0, 0, true)]
    [InlineData(NavigationDirection.East, 0, 0, 1, true)]
    [InlineData(NavigationDirection.East, 0, 0, 0, false)]
    // Across the axis, [-191, 192] whatever the carry.
    [InlineData(NavigationDirection.North, 192, -256, 0, true)]
    [InlineData(NavigationDirection.North, 193, -256, 1, false)]
    [InlineData(NavigationDirection.North, -191, -256, 1, true)]
    [InlineData(NavigationDirection.North, -192, -256, 0, false)]
    [InlineData(NavigationDirection.East, 256, 192, 1, true)]
    [InlineData(NavigationDirection.East, 256, -192, 1, false)]
    public void NativeAcceptanceFollowsEachRoutinesCarryExactly(NavigationDirection facing, int dx, int dy, int carry, bool accepted) =>
        Assert.Equal(accepted, FieldInteractionRange.NativeAccepts(1000, 1000, 1000 + dx, 1000 + dy, 0, facing, carry));

    [Fact]
    public void TheScanCarryComesFromTheClassByteOrTheSlotCompare()
    {
        Assert.Equal(1, FieldInteractionRange.ScanCarry(Npc(9, 0, 0), 3));
        Assert.Equal(0, FieldInteractionRange.ScanCarry(Npc(2, 0, 0), 3));
        Assert.Equal(1, FieldInteractionRange.ScanCarry(Npc(2, 0, 0) with { ClassTag = 7 }, 3));
    }

    [Theory]
    [InlineData(16, 16, true)]    // the touched actor is the target: any facing is ready
    [InlineData(18, 18, false)]   // another actor is touched: 17FA20 would run it instead
    [InlineData(0x80, 16, true)]  // no contact: the facing scan decides (target wins up)
    public void AContactedActorOverridesTheFacingScan(int contact, int confirm, bool ready)
    {
        var frame = Open([Npc(8, 1152, 1152), Npc(9, 1600, 1600) with { VisualIndex = 15 }],
            adjust: f => f with { ContactActorRaw = contact, ConfirmActorRaw = confirm });
        var target = frame.Targets.Single(t => t.Id == "actor:8:4:14");
        var below = new NavigationPoint(1152, 1408, 1);
        Assert.Equal(ready, target.ConfirmFacings!(below).Contains(NavigationDirection.North));
        if (contact == 16) Assert.Contains(NavigationDirection.West, target.ConfirmFacings!(below));
        if (!ready)
        {
            Assert.True(target.ConfirmPending!(below));
            var controller = new NavigationController();
            controller.Handle(NavigationCommand.ToggleWalk, frame, 0);
            var there = controller.Update(frame with { Player = below, PlayerFacing = NavigationDirection.North }, 100);
            Assert.DoesNotContain(there.Speech, s => s.StartsWith("Arrived"));
        }
    }

    [Fact]
    public void UnreadableContactStateIsNeverReportedReady()
    {
        var frame = Open([Npc(8, 1152, 1152)], adjust: f => f with { ContactActorRaw = null });
        var target = frame.Targets.Single(t => t.Id == "actor:8:4:14");
        var below = new NavigationPoint(1152, 1408, 1);
        Assert.Empty(target.ConfirmFacings!(below));
        Assert.True(target.ConfirmPending!(below));
    }

    [Fact]
    public void AChestCanBeOpenedFromTwoTilesBelowWhenNothingShadowsTheUpProbe()
    {
        // Constructed corridor verifying the native path 179C30 allows (row - 1, then row - 2);
        // not a live reproduction. Chest at tile (4,2); tile (4,3) is solid, so the only way to
        // face the chest from below is from (4,4), and only if (4,3) holds no grid record.
        var map = FullGameNavigationTests.Map();
        map.CollisionShapes[3 * 8 + 4] = 4;
        var chest = new FieldTreasure(3, 4 * 256 + 128, 2 * 256 + 128);
        var twoBelow = new NavigationPoint(1152, 1152, 1);
        bool Grid(int x, int y) => (x, y) == (4, 2);
        var open = Open(treasures: [chest], map: map, treasureGrid: Grid).Targets.Single(t => t.Id == "chest:3");
        Assert.Contains(twoBelow, open.ApproachPoints);
        Assert.Equal([NavigationDirection.North], open.ConfirmFacings!(twoBelow));

        // An opened record on (4,3) would stop the probe there; so would not knowing the grid.
        bool Shadowed(int x, int y) => (x, y) is (4, 2) or (4, 3);
        var shadowed = Open(treasures: [chest], map: map, treasureGrid: Shadowed).Targets.Single(t => t.Id == "chest:3");
        Assert.DoesNotContain(twoBelow, shadowed.ApproachPoints);
        Assert.Empty(shadowed.ConfirmFacings!(twoBelow));
        var unknown = Open(treasures: [chest], map: map).Targets.Single(t => t.Id == "chest:3");
        Assert.DoesNotContain(twoBelow, unknown.ApproachPoints);

        // With the tile below walkable, the adjacent goal is offered and the far one is not.
        var adjacent = Open(treasures: [chest], treasureGrid: Grid).Targets.Single(t => t.Id == "chest:3");
        Assert.Contains(new NavigationPoint(1152, 896, 1), adjacent.ApproachPoints);
        Assert.DoesNotContain(twoBelow, adjacent.ApproachPoints);
    }

    private static FieldActorSnapshot Npc(int index, int x, int y) =>
        FullGameNavigationTests.Actor(index, x, y) with { ClassTag = 4 };

    private static NavigationFrame Open(params FieldActorSnapshot[] actors) => Open(actors, []);

    private static NavigationFrame Open(FieldActorSnapshot[]? actors = null, FieldTreasure[]? treasures = null,
        Func<FieldNavigationSnapshot, FieldNavigationSnapshot>? adjust = null, FieldMapSnapshot? map = null,
        Func<int, int, bool>? treasureGrid = null, (int X, int Y)? leader = null)
    {
        // An open 8x8 field with no catalogue entry for the scene: only native facts apply.
        var player = FullGameNavigationTests.Actor(1, leader?.X ?? 384, leader?.Y ?? 384, true) with { ClassTag = 0, Facing = 1 };
        var field = FullGameNavigationTests.Field(999) with
            { LeadPlayer = player, LeadPlayerActorIndex = 1, Actors = [player, .. actors ?? []] };
        field = adjust?.Invoke(field) ?? field;
        return new FieldNavigationSource(new NoMemory(), _ => { })
            .Build(field, map ?? FullGameNavigationTests.Map(), new(0, 0, 2048, 2048), treasures ?? [], null, treasureGrid);
    }

    private static NavigationFrame Market(int facing)
    {
        // ShopProxyTests' Truce Market, scene 118, with the leader's live facing set.
        var player = FullGameNavigationTests.Actor(1, 15488, 2879) with { IsPartyMember = true, ClassTag = 0, Facing = facing };
        var keeper = FullGameNavigationTests.Actor(8, 14336, 1888) with { VisualIndex = 49 };
        var counter = FullGameNavigationTests.Actor(9, 14336, 2032) with { VisualIndex = 100 };
        var field = FullGameNavigationTests.Field(118) with { LeadPlayer = player, Actors = [player, keeper, counter] };
        return new FieldNavigationSource(new NoMemory(), _ => { })
            .Build(field, CounterApproachTests.Market(), new(12288, 768, 16512, 3968), [], new FieldStoryState(18, false));
    }

    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
