using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>
/// Save points are found the way the engine finds them. CanSave 0x213580 and the top-menu
/// builder 0x1D0FE0 allow saving in the field only while script global 0x1CF (FieldState+0x73C)
/// has bit 0x80 set. Each save point's checker actor loops over the leader's tile (opcode 22),
/// sets that bit on its own tile, and opens the Save UI (C8 40) when Confirm is pressed
/// (opcode 31); its person 0x79 sparkle marks the tile. The fixture is the user's live
/// scene 29 (Prison Towers, Guardroom): sparkle actor 3 and checker actor 4 at (3456, 2815).
/// </summary>
public sealed class FieldSavePointTests
{
    private const int Scene = 29;
    private const int SaveTileX = 13;
    private const int SaveTileY = 10;

    [Fact]
    public void GuardroomSavePointIsAnInteractableObjectWithStandingGoalsOnItsTile()
    {
        var frame = Build();

        var save = Assert.Single(frame.Targets, t => t.Label == "Save point");
        Assert.Equal(NavigationCategory.Objects, save.Category);
        Assert.True(save.Visible);
        Assert.NotEmpty(save.ApproachPoints);
        // The checker compares the leader's native tile (actor +80/+8C) with its own; every
        // goal, with the controller's one-eighth-tile arrival box, stays inside that tile.
        Assert.All(save.ApproachPoints, p =>
        {
            Assert.InRange(p.X, SaveTileX * 256 + 32, SaveTileX * 256 + 224);
            Assert.InRange(p.Y, SaveTileY * 256 + 32, SaveTileY * 256 + 224);
        });
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, save.ApproachPoints).Route;
        Assert.NotNull(route);
        Assert.Equal((SaveTileX, SaveTileY), (route[^1].X >> 8, route[^1].Y >> 8));
    }

    [Fact]
    public void SavePointTellsThePlayerTheNativeConfirmInteraction()
    {
        var save = Assert.Single(Build().Targets, t => t.Label == "Save point");
        Assert.Equal("Stand on it and press Confirm to save.", save.Instruction);
        Assert.Equal("Press Confirm to save.", save.ArrivalInstruction);
    }

    [Fact]
    public void SavePointStaysListedWhileThePlayerStandsOnItAndSavingIsEnabled()
    {
        // On the tile the checker has already set global 0x1CF bit 0x80; the catalog's
        // "bit not yet set" guard describes only its first frame there.
        var onTile = FullGameNavigationTests.Actor(5, 3573, 2776, true) with { ClassTag = 0 };
        var frame = Build(player: onTile, canSaveBit: 0x80);
        Assert.Single(frame.Targets, t => t.Label == "Save point");
    }

    [Fact]
    public void SavePointIsDiscoverableThroughTheGuideBeforeTheCameraShowsIt()
    {
        var frame = Build(viewport: new(0, 0, 512, 512));
        var save = Assert.Single(frame.Targets, t => t.Label == "Save point");
        Assert.False(save.Visible);
        Assert.True(save.GuideAvailable);
    }

    [Fact]
    public void HiddenSparkleIsNotOffered()
    {
        // Actor 3 runs opcode 91 (hide) before story point 0x2E; the checker still runs,
        // but a sighted player sees no save point there.
        var frame = Build(sparkle: Sparkle() with { DrawMode = FieldNavigationCapture.DrawModeHidden });
        Assert.DoesNotContain(frame.Targets, t => t.Label == "Save point");
    }

    [Fact]
    public void CheckerWithScriptProcessingDisabledIsNotOffered()
    {
        // Opcode 0B sets actor+30 bit 0x80; the runner then skips the checker's loop, so
        // standing on the sparkle no longer enables saving.
        var frame = Build(checker: Checker() with { ScriptProcessingEnabled = false });
        Assert.DoesNotContain(frame.Targets, t => t.Label == "Save point");
    }

    [Fact]
    public void CheckerCallGateAloneDoesNotDisableTheSavePoint()
    {
        // Opcode 08 (actor+E8) blocks confirm and touch calls only. The save test is
        // inline in the checker's own loop, which keeps running.
        var frame = Build(checker: Checker() with { ScriptCallsEnabled = false });
        Assert.Single(frame.Targets, t => t.Label == "Save point");
    }

    [Fact]
    public void RemovedOrMissingCheckerIsNotOffered()
    {
        Assert.DoesNotContain(Build(checker: Checker() with { ClassTag = 0x87 }).Targets, t => t.Label == "Save point");
        Assert.DoesNotContain(Build(omitChecker: true).Targets, t => t.Label == "Save point");
    }

    [Fact]
    public void SparkleWithoutACheckerIsNotASavePoint()
    {
        // Scene 13 (Truce, Mayor's House 1F) draws person 0x79 at (4, 24), but no script
        // there sets the CanSave bit, so the menu never allows saving on it.
        var sparkle = FullGameNavigationTests.Actor(9, 4 * 256 + 128, 24 * 256 + 255) with { VisualIndex = 0x79 };
        var player = FullGameNavigationTests.Actor(5, 1664, 1663, true) with { ClassTag = 0 };
        var field = FullGameNavigationTests.Field(13) with { LeadPlayer = player, Actors = [player, sparkle] };
        var frame = new FieldNavigationSource(new NoMemory(), _ => { })
            .Build(field, Room(), new(0, 0, 8192, 8192), [], Story());
        Assert.DoesNotContain(frame.Targets, t => t.Label == "Save point");
    }

    [Fact]
    public void CheckerAwayFromItsAuditedTileIsNotOffered()
    {
        var moved = Checker() with { TileX = 12, FineX = 12 * 256 + 128 };
        Assert.DoesNotContain(Build(checker: moved).Targets, t => t.Label == "Save point");
    }

    [Fact]
    public void StoryGatedSavePointAppearsOnlyAfterItsNativeGate()
    {
        // Scene 174 (Magus's Keep, Ozzie Battle Room): checker 9 is also the sparkle and
        // runs its save loop only from story point 137 on.
        var combined = FullGameNavigationTests.Actor(9, 56 * 256 + 128, 52 * 256 + 255) with
        {
            VisualIndex = 0x79, LoadedFlag = 0,
        };
        var player = FullGameNavigationTests.Actor(5, 1664, 1663, true) with { ClassTag = 0 };
        var field = FullGameNavigationTests.Field(174) with { LeadPlayer = player, Actors = [player, combined] };
        NavigationFrame At(int point) => new FieldNavigationSource(new NoMemory(), _ => { })
            .Build(field, Room(64, 64), new(0, 0, 16384, 16384), [], Story(point));
        Assert.DoesNotContain(At(136).Targets, t => t.Label == "Save point");
        Assert.Single(At(137).Targets, t => t.Label == "Save point");
    }

    [Fact]
    public void CombinedSparkleAndCheckerIsListedOnceEvenWhileTheEngineFlagsItInteractable()
    {
        // Runner 0x161560 sets +150/+152 on the actor executing opcode 31 at the sparkle's
        // tile, so a combined actor briefly becomes a native activation candidate.
        var combined = FullGameNavigationTests.Actor(9, 56 * 256 + 128, 52 * 256 + 255) with
        {
            VisualIndex = 0x79, LoadedFlag = 0, ActivationEnabled = 1, ActivationBinding = 1,
        };
        var player = FullGameNavigationTests.Actor(5, 56 * 256 + 128, 52 * 256 + 128, true) with { ClassTag = 0 };
        var field = FullGameNavigationTests.Field(174) with { LeadPlayer = player, Actors = [player, combined] };
        var frame = new FieldNavigationSource(new NoMemory(), _ => { })
            .Build(field, Room(64, 64), new(0, 0, 16384, 16384), [], Story(140));
        var objects = frame.Targets.Where(t => t.Category == NavigationCategory.Objects).ToArray();
        Assert.Equal("Save point", Assert.Single(objects).Label);
    }

    [Fact]
    public void EverySavePointInTheInstalledScriptsIsOfferedWhenItsSparkleIsDrawnAndItsCheckerRuns()
    {
        // The read-only census (artifacts/research/skywalk-savepoints-0334/claude) found 57
        // checkers in 54 scenes; the catalog must keep recording every one of them.
        var regions = GameNavigationCatalog.Scenes
            .SelectMany(scene => scene.Regions.Where(FieldSavePoints.IsSaveRegion).Select(region => (scene, region)))
            .ToArray();
        Assert.Equal(57, regions.Select(r => (r.scene.Id, r.region.Actor)).Distinct().Count());
        Assert.Equal(54, regions.Select(r => r.scene.Id).Distinct().Count());
        foreach (var (scene, region) in regions)
        {
            var x = region.Left * 256 + 128;
            var y = region.Top * 256 + 255;
            var combined = scene.Actors.Any(a => a.Id == region.Actor &&
                a.Loads.Any(l => l.Class == 4 && l.Visual == FieldSavePoints.SparkleVisual));
            var checker = FullGameNavigationTests.Actor(region.Actor, x, y) with
            {
                ClassTag = combined ? 4 : 7, VisualIndex = combined ? FieldSavePoints.SparkleVisual : 0, LoadedFlag = 0,
            };
            var player = FullGameNavigationTests.Actor(1, 384, 384, true) with { ClassTag = 0 };
            var actors = new List<FieldActorSnapshot> { player, checker };
            // Every separate sparkle in the census is the slot just below its checker, so the
            // unloaded checker is the first contact on the tile, as in the live scene 29.
            if (!combined)
                actors.Add(FullGameNavigationTests.Actor(region.Actor - 1, x, y) with { VisualIndex = FieldSavePoints.SparkleVisual });
            var field = FullGameNavigationTests.Field(scene.Id) with { LeadPlayer = player, Actors = actors.ToArray() };
            var frame = new FieldNavigationSource(new NoMemory(), _ => { })
                .Build(field, Room(128, 128), new(0, 0, 32768, 32768), [], Satisfying(region));
            var save = Assert.Single(frame.Targets, t => t.Label == FieldSavePoints.Label &&
                t.Id == $"save:{region.Actor}:{region.Left}:{region.Top}");
            Assert.NotEmpty(save.ApproachPoints);
            Assert.All(save.ApproachPoints, p => Assert.Equal((region.Left, region.Top), (p.X >> 8, p.Y >> 8)));
        }
    }

    /// <summary>A story state meeting one region's own gates, found by trying each gated
    /// index's candidate values; the CanSave self-guard is left clear as on a first visit.</summary>
    private static FieldStoryState Satisfying(GameNavigationCatalog.Region region)
    {
        var globals = new Dictionary<int, int> { [FieldSavePoints.CanSaveGlobal] = 0 };
        var locals = new Dictionary<int, int>();
        var point = 200;
        foreach (var group in region.Guards.Where(g => g.Source is "Global" or "Local" &&
                     !(g.Source == "Global" && g.Index == FieldSavePoints.CanSaveGlobal)).GroupBy(g => (g.Source, g.Index)))
        {
            var value = Enumerable.Range(0, 256).First(v => group.All(g => g.MatchesValue(v)));
            if (group.Key is ("Global", 0)) point = value;
            else if (group.Key.Source == "Global") globals[group.Key.Index] = value;
            else locals[group.Key.Index] = value;
        }
        return new FieldStoryState(point, true) { Globals = globals, Locals = locals };
    }

    private static NavigationFrame Build(FieldActorSnapshot? player = null, FieldActorSnapshot? sparkle = null,
        FieldActorSnapshot? checker = null, bool omitChecker = false, FieldViewport? viewport = null, int canSaveBit = 0)
    {
        player ??= FullGameNavigationTests.Actor(5, 1664, 1663, true) with { ClassTag = 0 };
        var actors = new List<FieldActorSnapshot> { player, sparkle ?? Sparkle() };
        if (!omitChecker) actors.Add(checker ?? Checker());
        var field = FullGameNavigationTests.Field(Scene) with { LeadPlayer = player, Actors = actors.ToArray() };
        return new FieldNavigationSource(new NoMemory(), _ => { })
            .Build(field, Room(16, 32), viewport ?? new(0, 256, 4224, 3456), [], Story(46, canSaveBit));
    }

    /// <summary>Actor 3: person 0x79 at tile (13, 10), foot at the tile's bottom row, solid.</summary>
    private static FieldActorSnapshot Sparkle() =>
        FullGameNavigationTests.Actor(3, 3456, 2815) with { VisualIndex = 0x79, ActivationEnabled = 0, ActivationBinding = 128 };

    /// <summary>Actor 4: sprite-less class 7 checker on the same tile, not solid (+D8 = 0).</summary>
    private static FieldActorSnapshot Checker() =>
        FullGameNavigationTests.Actor(4, 3456, 2815) with
        {
            VisualIndex = 0, ClassTag = 7, LoadedFlag = 0, ActivationEnabled = 0, ActivationBinding = 128,
        };

    private static FieldStoryState Story(int point = 46, int canSaveBit = 0) =>
        new(point, true) { Globals = new Dictionary<int, int> { [FieldSavePoints.CanSaveGlobal] = canSaveBit } };

    private static FieldMapSnapshot Room(int width = 32, int height = 32) => new(width, height,
        new byte[width * height], new byte[width * height], Enumerable.Repeat((byte)1, width * height).ToArray(),
        1, false, width, height, Enumerable.Repeat((byte)128, width * height).ToArray());

    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
