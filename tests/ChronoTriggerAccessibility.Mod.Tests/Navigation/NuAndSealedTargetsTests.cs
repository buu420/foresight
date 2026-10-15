using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Native evidence: artifacts/research/epoch-seals-0362/claude-seals/findings.md.
/// load_enemy (16B220) sets class 5 and keeps the enemy id at +0x44 (Nu = 0 and 130 in
/// monster.txt). Keeper's Dome dialogue names its Nu "Strange Construct" (mesi0 0x13/0x21/
/// 0x22/0x24/0x6E). Sealed doors open at story 0xAB (241, 208, 210, 217; 242 has no gate) and
/// record it in a scene local; 241/242 each have a second marker that forwards to the door.
/// The Forest Ruins pyramid (scene 42) opens when the leader stands on tile row 18 at story
/// 0xAB or later; global 0x19C bit 04 records it, and its contents stay hidden until then.</summary>
public sealed class NuAndSealedTargetsTests
{
    // ---- Nu -----------------------------------------------------------------------------

    [Theory]
    [InlineData(241, 9)]
    [InlineData(241, 10)]
    [InlineData(242, 16)]
    [InlineData(243, 14)]
    public void TheKeepersDomeConstructIsNamedAsTheGameNamesIt(int scene, int index)
    {
        var target = Assert.Single(Build(scene, 0xAB, Nu(index)).Targets, t => t.Id.StartsWith($"actor:{index}:"));
        Assert.Equal("Nu, Strange Construct", target.Label);
        Assert.Equal(NavigationCategory.People, target.Category);
    }

    [Theory]
    [InlineData(42, 21)]    // Forest Ruins, after the pyramid opens
    [InlineData(292, 13)]   // Laruba Ruins
    [InlineData(315, 10)]   // Black Omen
    [InlineData(360, 17)]   // Kajar lab
    [InlineData(426, 8)]    // Surviving Village shop
    [InlineData(667, 10)]   // Arena of the Ages
    public void OtherTalkingNuArePeopleNamedNu(int scene, int index)
    {
        var target = Assert.Single(Build(scene, 0xB0, [Nu(index)],
            globals: scene == 360 ? (0xF5, 8) : null).Targets, t => t.Id.StartsWith($"actor:{index}:"));
        Assert.Equal("Nu", target.Label);
        Assert.Equal(NavigationCategory.People, target.Category);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(8, true)]
    public void TheKajarLabNuNeedsItsNativeTalkGuard(int? flag, bool named)
    {
        // Atel0228 actor17's Talk reads global F5 bit08 (scratching its back).
        var frame = Build(360, 0xB0, [Nu(17)], globals: flag is { } value ? (0xF5, value) : null);
        Assert.Equal(named, frame.Targets.Any(t => t.Label == "Nu"));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void BothNativeEnemyLoadModesKeepTheNuVisualIdentity(int type)
    {
        var frame = Build(426, 0xB0, Nu(8) with { ClassTag = type });
        Assert.Equal("Nu", Assert.Single(frame.Targets, t => t.Id.StartsWith("actor:8:")).Label);
    }

    [Fact]
    public void DisabledNativeCallsDoNotAuthorizeANuNpcLabel()
    {
        var frame = Build(426, 0xB0, Nu(8) with { ScriptCallsEnabled = false });
        Assert.DoesNotContain(frame.Targets, t => t.Label.StartsWith("Nu", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(291, 31, 0)]    // Hunting Range: battle only
    [InlineData(513, 8, 0)]     // battle only
    [InlineData(361, 8, 130)]   // Enhasa hidden room: its talk leads into a fight
    public void BattleNuAreNeverNamedAsPeople(int scene, int index, int enemy)
    {
        var frame = Build(scene, 0xB0, Nu(index) with { VisualIndex = enemy });
        Assert.DoesNotContain(frame.Targets, t => t.Label.StartsWith("Nu", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(4, 0)]      // a person sprite that shares the number: not the species
    [InlineData(4, 130)]
    [InlineData(5, 1)]      // another enemy in the same slot
    public void OnlyTheNuEnemyIdsOfClassFiveOrSixAreNu(int type, int visual)
    {
        var frame = Build(426, 0xB0, Nu(8) with { ClassTag = type, VisualIndex = visual });
        Assert.DoesNotContain(frame.Targets, t => t.Label.StartsWith("Nu", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUndrawnNuIsNotListed()
    {
        var frame = Build(42, 0xB0, Nu(21) with { DrawMode = 0 });
        Assert.DoesNotContain(frame.Targets, t => t.Id.StartsWith("actor:21:"));
    }

    [Fact]
    public void SeveralNuInOneRoomAreDistinguished()
    {
        var frame = Build(401, 0xB0, Nu(32, 18), Nu(33, 22), Nu(34, 26));
        var labels = frame.Targets.Where(t => t.Id.StartsWith("actor:3")).Select(t => t.Label).Order().ToArray();
        Assert.Equal(["Nu A", "Nu B", "Nu C"], labels);
    }

    [Fact]
    public void TheLabelHelperRejectsOtherScenesAndSlots()
    {
        Assert.Null(NuNpcLabels.ActorLabel(241, Nu(11)));
        Assert.Null(NuNpcLabels.ActorLabel(999, Nu(9)));
        Assert.Equal("Nu, Strange Construct", NuNpcLabels.ActorLabel(241, Nu(9)));
    }

    // ---- sealed doors ----------------------------------------------------------------------

    public static TheoryData<int, int, int> Doors => new()
    {
        // scene, door marker, the local its handler sets when the door opens
        { 208, 8, 7 }, { 210, 17, 7 }, { 217, 9, 0x0A }, { 241, 13, 8 }, { 242, 15, 8 },
    };

    [Theory]
    [MemberData(nameof(Doors))]
    public void AClosedSealedDoorIsNamedAtAnyStoryPoint(int scene, int index, int local)
    {
        foreach (var story in new[] { 0x40, 0x90, 0xAB, 0xC0 })
        {
            var target = Assert.Single(Build(scene, story, [Marker(index)], (local, 0)).Targets, t => t.Id == $"landmark:{index}");
            Assert.Equal("Sealed door", target.Label);
            Assert.Equal(NavigationCategory.Objects, target.Category);
            Assert.NotNull(target.ConfirmFacings);   // the player's own Confirm, never automatic
        }
    }

    [Theory]
    [MemberData(nameof(Doors))]
    public void AnOpenedDoorIsNoLongerOffered(int scene, int index, int local)
    {
        var frame = Build(scene, 0xC0, [Marker(index)], (local, 1));
        Assert.DoesNotContain(frame.Targets, t => t.Label == "Sealed door");
        Assert.DoesNotContain(frame.Targets, t => t.Id == $"landmark:{index}");
    }

    [Theory]
    [MemberData(nameof(Doors))]
    public void AnUnreadableDoorStateIsNotDescribedAsSealed(int scene, int index, int local)
    {
        var frame = Build(scene, 0xC0, [Marker(index)], (local + 1, 0));
        Assert.DoesNotContain(frame.Targets, t => t.Label == "Sealed door");
        Assert.DoesNotContain(frame.Targets, t => t.Id == $"landmark:{index}");
    }

    [Theory]
    [InlineData(241, 12, 13)]
    [InlineData(242, 14, 15)]
    public void OnePhysicalDoorIsOneTarget(int scene, int partner, int door)
    {
        var frame = Build(scene, 0xC0, [Marker(partner, 20), Marker(door, 22)], (8, 0));
        var target = Assert.Single(frame.Targets, t => t.Label == "Sealed door");
        Assert.Equal($"landmark:{door}", target.Id);
        // Both markers run the same native door handler: either one's standing room is offered.
        Assert.NotNull(target.ConfirmFacings);
        Assert.Contains(target.ApproachPoints, p => p.X < 20 * 256 + 128 && target.ConfirmFacings(p).Count != 0);
        Assert.Contains(target.ApproachPoints, p => p.X > 22 * 256 + 128 && target.ConfirmFacings(p).Count != 0);
    }

    [Theory]
    [InlineData(241, 12, 13)]
    [InlineData(242, 14, 15)]
    public void ADoorKeepsItsIdentityWhenOnlyTheForwardingMarkerIsUsable(int scene, int partner, int door)
    {
        var frame = Build(scene, 0xC0, [Marker(partner)], (8, 0));
        Assert.Equal($"landmark:{door}", Assert.Single(frame.Targets, t => t.Label == "Sealed door").Id);
    }

    [Theory]
    [InlineData(241, 13)]
    [InlineData(242, 15)]
    public void DisabledDoorCallsDoNotExposeAnInteraction(int scene, int index)
    {
        var frame = Build(scene, 0xC0, [Marker(index) with { ScriptCallsEnabled = false }], (8, 0));
        Assert.DoesNotContain(frame.Targets, t => t.Id == $"landmark:{index}");
    }

    [Theory]
    [InlineData(241, 0xAA, NavigationCommand.Guide)]
    [InlineData(241, 0xAA, NavigationCommand.ToggleWalk)]
    [InlineData(241, 0xAB, NavigationCommand.Guide)]
    [InlineData(241, 0xAB, NavigationCommand.ToggleWalk)]
    [InlineData(242, 0xAA, NavigationCommand.Guide)]
    [InlineData(242, 0xAA, NavigationCommand.ToggleWalk)]
    [InlineData(242, 0xAB, NavigationCommand.Guide)]
    [InlineData(242, 0xAB, NavigationCommand.ToggleWalk)]
    public void BothGuidanceModesReachTheNativeDoorWithoutCrossingItsClosedFloor(int scene, int story,
        NavigationCommand command)
    {
        var map = InstalledMap(scene);
        var marker = scene == 241 ? MarkerAt(13, 40 * 256, 20 * 256 - 1) : MarkerAt(15, 2 * 256 + 128, 4 * 256 - 1);
        var partner = scene == 241 ? marker with { Index = 12 } : MarkerAt(14, 3 * 256 + 128, 4 * 256 - 1);
        var player = scene == 241 ? new NavigationPoint(40 * 256, 25 * 256 + 128, 1) : new(20 * 256, 11 * 256 + 128, 1);
        var frame = Build(scene, story, [marker, partner], (8, 0), map: map, playerAt: player);
        var door = Assert.Single(frame.Targets, t => t.Label == "Sealed door");
        var route = NavigationPathfinder.Find(frame.Graph, frame.Player, door.ApproachPoints);
        Assert.NotNull(route);
        Assert.All(route, p => Assert.True(new FieldNavigationGraph(map).TryPosition(p.X, p.Y, p.Layer, out var at) && at == p));
        Assert.Null(NavigationPathfinder.Find(frame.Graph, frame.Player, Assert.Single(frame.Targets, t => t.Id == "exit:1").ApproachPoints));
        if (scene == 241 && story < 0xAB) Assert.Contains("mysterious force", door.Instruction);
        Assert.NotNull(door.ConfirmFacings);
        var controller = Select(frame, door.Id);
        var result = controller.Handle(command, frame, 50);
        Assert.True(result.Guiding, string.Join(" ", result.Speech));
        Assert.Equal(command == NavigationCommand.ToggleWalk, result.AutoWalking);
    }

    [Fact]
    public void OtherEarlyLandmarksKeepTheirOriginalStoryWindow()
    {
        Assert.Equal("Enertron", FutureAreaLabels.Landmark(210, new FieldStoryState(60, true), Marker(16)));
        Assert.Null(FutureAreaLabels.Landmark(210, new FieldStoryState(0xC0, true), Marker(16)));
    }

    // ---- Forest Ruins pyramid --------------------------------------------------------------

    [Fact]
    public void TheClosedPyramidIsAVisibleLandmarkApproachedOnRowEighteen()
    {
        var frame = Build(42, 0xAE, [Box(19), Box(20)], globals: (0x19C, 0));
        var pyramid = Assert.Single(frame.Targets, t => t.Id == SealedFieldTargets.PyramidId);
        Assert.Equal("Pyramid", pyramid.Label);
        Assert.Equal(NavigationCategory.Objects, pyramid.Category);
        Assert.True(pyramid.Visible);
        Assert.NotEmpty(pyramid.ApproachPoints);
        Assert.All(pyramid.ApproachPoints, p => Assert.Equal(18, p.Y / 256));
        Assert.Null(pyramid.ConfirmFacings);
        Assert.DoesNotContain("Confirm", (pyramid.Instruction ?? "") + (pyramid.ArrivalInstruction ?? ""));
        // The native contents stay hidden until the game draws them.
        Assert.DoesNotContain(frame.Targets, t => t.Id.StartsWith("actor:19:") || t.Id.StartsWith("actor:20:"));
    }

    [Theory]
    [InlineData(0x04, true)]    // opened: the pyramid is gone
    [InlineData(null, true)]    // unreadable: promise nothing
    public void ThePyramidIsOnlyOfferedWhileNativelyClosed(int? flag, bool absent)
    {
        var frame = flag is { } value ? Build(42, 0xAE, [], globals: (0x19C, value)) : Build(42, 0xAE, []);
        Assert.Equal(absent, !frame.Targets.Any(t => t.Id == SealedFieldTargets.PyramidId));
    }

    [Fact]
    public void ThePyramidBelongsOnlyToTheForestRuins()
    {
        Assert.DoesNotContain(Build(41, 0xAE, [], globals: (0x19C, 0)).Targets, t => t.Id == SealedFieldTargets.PyramidId);
    }

    [Theory]
    [InlineData(NavigationCommand.Guide)]
    [InlineData(NavigationCommand.ToggleWalk)]
    public void ManualAndAutomaticGuidanceReachTheRowInFrontOfThePyramid(NavigationCommand command)
    {
        var map = InstalledMap(42);
        var frame = Build(42, 0xAE, [], globals: (0x19C, 0), map: map,
            playerAt: new(8 * 256 + 128, 30 * 256 + 128, 1));
        var pyramid = Assert.Single(frame.Targets, t => t.Id == SealedFieldTargets.PyramidId);
        var route = NavigationPathfinder.Find(frame.Graph, frame.Player, pyramid.ApproachPoints);
        Assert.NotNull(route);
        Assert.Equal(18, route[^1].Y / 256);
        Assert.All(route, p => Assert.True(new FieldNavigationGraph(map).TryPosition(p.X, p.Y, p.Layer, out var at) && at == p));
        var controller = Select(frame, pyramid.Id);
        var result = controller.Handle(command, frame, 50);
        Assert.True(result.Guiding);
        Assert.Equal(command == NavigationCommand.ToggleWalk, result.AutoWalking);
        Assert.DoesNotContain(result.Speech, s => s.StartsWith("No route", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePyramidIsStillVisibleBeforeItsNativeOpeningThresholdWithoutSolutionHints()
    {
        var frame = Build(42, 0xAA, [], globals: (0x19C, 0));
        var pyramid = Assert.Single(frame.Targets, t => t.Id == SealedFieldTargets.PyramidId);
        Assert.DoesNotContain("pendant", pyramid.Instruction ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Confirm", pyramid.Instruction ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(pyramid.ApproachPoints);
    }

    [Fact]
    public void TheNativeOpeningReplacesThePyramidWithOnlyItsDrawnActors()
    {
        var frame = Build(42, 0xAE, [Box(19) with { DrawMode = 1 }, Box(20) with { DrawMode = 1 }, Nu(21)], globals: (0x19C, 4));
        Assert.DoesNotContain(frame.Targets, t => t.Id == SealedFieldTargets.PyramidId);
        Assert.Equal(2, frame.Targets.Count(t => t.Label == "Sealed box"));
        Assert.Single(frame.Targets, t => t.Label == "Nu");
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static FieldActorSnapshot Nu(int index, int tileX = 20) =>
        FullGameNavigationTests.Actor(index, tileX * 256 + 128, 20 * 256 + 128) with { ClassTag = 5, VisualIndex = 0 };

    private static FieldActorSnapshot Marker(int index, int tileX = 20) =>
        FullGameNavigationTests.Actor(index, tileX * 256 + 128, 20 * 256 + 255) with { ClassTag = 7, VisualIndex = 0 };

    private static FieldActorSnapshot MarkerAt(int index, int x, int y) =>
        FullGameNavigationTests.Actor(index, x, y) with { ClassTag = 7, VisualIndex = 0 };

    private static FieldActorSnapshot Box(int index) =>
        FullGameNavigationTests.Actor(index, (index == 19 ? 6 : 9) * 256 + 128, 10 * 256 + 255) with
            { ClassTag = 4, VisualIndex = 0xB7, DrawMode = 0 };

    private static NavigationFrame Build(int scene, int story, params FieldActorSnapshot[] actors) =>
        Build(scene, story, actors, null);

    private static NavigationFrame Build(int scene, int story, FieldActorSnapshot[] actors, (int Index, int Value)? local = null,
        (int Index, int Value)? globals = null, FieldMapSnapshot? map = null, NavigationPoint? playerAt = null)
    {
        var at = playerAt ?? new(8 * 256 + 128, 30 * 256 + 128, 1);
        var player = FullGameNavigationTests.Actor(1, at.X, at.Y, true) with { ClassTag = 0 };
        var field = FullGameNavigationTests.Field(scene) with { LeadPlayer = player, Actors = [player, .. actors], ActorCount = 40 };
        map ??= new FieldMapSnapshot(64, 64, new byte[4096], new byte[4096], Enumerable.Repeat((byte)1, 4096).ToArray(),
            1, false, 64, 64, Enumerable.Repeat((byte)128, 4096).ToArray());
        var locals = new Dictionary<int, int>();
        if (local is { } l) locals[l.Index] = l.Value;
        var global = new Dictionary<int, int> { [0] = story };
        if (globals is { } g) global[g.Index] = g.Value;
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, map, new(0, 0, 64 * 256, 64 * 256), [],
            new FieldStoryState(story, true) { Locals = locals, Globals = global });
    }

    private static NavigationController Select(NavigationFrame frame, string target)
    {
        var controller = new NavigationController();
        for (var n = 0; controller.TargetId != target; n++)
        {
            Assert.True(n < 40);
            controller.Handle(n == 0 ? NavigationCommand.NextCategory : NavigationCommand.NextTarget, frame, n);
            if (controller.TargetId is { } id && frame.Targets.First(t => t.Id == id).Category != NavigationCategory.Objects)
                controller.Handle(NavigationCommand.NextCategory, frame, n);
        }
        return controller;
    }

    private static FieldMapSnapshot InstalledMap(int scene)
    {
        // Initial resource planes, decoded with CTViewer's MapTable layout after checking
        // resources.bin 09914BF4...3C7E. This exercises native terrain, not a live game.
        using var stream = typeof(NuAndSealedTargetsTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.sealed-landmark-maps-0362.json")!;
        using var json = JsonDocument.Parse(stream);
        return json.RootElement.GetProperty(scene.ToString()).Deserialize<FieldMapSnapshot>()!;
    }

    private sealed class NoMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination) => false;
    }
}
