using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class FieldNavigationSourceTests
{
    [Fact]
    public void ListsVisibleEligiblePeopleExitsAndChestsAndExcludesPartyAndHiddenActors()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var field = Field(Actor(1, 128, 128, party: true), Actor(2, 512, 256),
            Actor(3, 768, 256) with { DrawMode = 0 }, Actor(4, 900, 256) with { ClassTag = 0x84 });
        var map = Map(); map.ExitCells[3] = 0;
        var frame = source.Build(field, map, new(0, 0, 1024, 1024), [new(5, 640, 640)]);
        Assert.True(frame.CanNavigate);
        Assert.Single(frame.Targets, t => t.Category == NavigationCategory.People);
        Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Exits);
        Assert.Equal("Treasure chest", Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Objects).Label);
    }

    [Fact]
    public void OffscreenActorsAreNotDiscoveredOrTrackedAtTheirUnseenPositions()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var field = Field(Actor(1, 128, 128, party: true), Actor(2, 768, 256));
        Assert.Empty(source.Build(field, Map(), new(0, 0, 512, 512), []).Targets);
        var seen = Assert.Single(source.Build(field, Map(), new(0, 0, 1024, 512), []).Targets);
        var moved = field with { Actors = [field.Actors[0], Actor(2, 900, 256)] };
        var known = Assert.Single(source.Build(moved, Map(), new(0, 0, 512, 512), []).Targets);
        Assert.False(known.Visible);
        Assert.True(known.Discovered);
        Assert.Equal(seen.Position, known.Position);
        source.Reset();
        Assert.Empty(source.Build(moved, Map(), new(0, 0, 512, 512), []).Targets);
    }

    [Fact]
    public void NativeFixedPointFrameAndExitGoalsRemainStableAsPlayerMoves()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var field = Field(Actor(1, 128, 128, party: true));
        var map = Map(); map.ExitCells[3] = 0;
        var first = source.Build(field, map, new(0, 0, 1024, 1024), []);
        var second = source.Build(field with { LeadPlayer = Actor(1, 192, 128, party: true) }, map, new(0, 0, 1024, 1024), []);
        Assert.Equal(256, first.UnitsPerTile);
        Assert.Equal(Assert.Single(first.Targets).ApproachPoints, Assert.Single(second.Targets).ApproachPoints);
        Assert.All(Assert.Single(first.Targets).ApproachPoints, goal =>
        {
            Assert.InRange(goal.X % 256, 64, 192);
            Assert.InRange(goal.Y % 256, 64, 192);
        });
    }

    [Fact]
    public void LargeExitKeepsItsDiscoveredApproachesAcrossPlayerAndCameraMovement()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var field = Field(Actor(1, 128, 128, party: true));
        var map = Map(); Array.Fill(map.ExitCells, (byte)0);
        var first = Assert.Single(source.Build(field, map, new(0, 0, 1024, 1024), []).Targets);
        Assert.Equal(64, first.ApproachPoints.Count);
        var moved = field with { LeadPlayer = Actor(1, 960, 960, party: true) };
        var second = Assert.Single(source.Build(moved, map, new(512, 512, 1024, 1024), []).Targets);
        Assert.Equal(first.ApproachPoints, second.ApproachPoints);
        map.ExitCells[0] = 128;
        var changed = Assert.Single(source.Build(moved, map, new(0, 0, 1024, 1024), []).Targets);
        Assert.DoesNotContain(changed.ApproachPoints, p => p.X < 256 && p.Y < 256);
    }

    [Fact]
    public void OpeningBedroomListsStairsPartlyInsideTheObservedCameraWindow()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var field = Field(Actor(0, 6308, 2303, party: true)) with { SceneId = 2 };
        // Installed scene 2 exit record 170d810b01000c06: one column, two rows.
        var map = Map(64);
        map.ExitCells[13 * 64 + 23] = 0;
        map.ExitCells[14 * 64 + 23] = 0;
        // Captured at first control: the upper half of row 13 is visible, but
        // its center falls exactly on the camera's exclusive bottom edge.
        var viewport = new FieldViewport(4096, 256, 8320, 3456);
        var frame = source.Build(field, map, viewport, [], new(3, false));

        var stairs = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Exits);
        Assert.Equal("Stairs downstairs", stairs.Label);
        Assert.True(stairs.Visible);
        Assert.NotEmpty(stairs.ApproachPoints);
        Assert.All(stairs.ApproachPoints, p =>
        {
            Assert.Equal(23, p.X / 256);
            Assert.InRange(p.Y / 256, 13, 14);
        });
        Assert.Contains(stairs.ApproachPoints, p => p.Y / 256 == 14);
        var story = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.Equal("Go downstairs", story.Label);
        Assert.Equal(stairs.ApproachPoints, story.ApproachPoints);
    }

    [Fact]
    public void PartlyVisibleStairsUseTheirConnectedReachableEntryBeyondTheCameraEdge()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var field = Field(Actor(1, 384, 640, party: true)) with { SceneId = 2 };
        var map = Map();
        // A two-tile stair trigger: the upper tile is visible decoration with no
        // traversable floor, while the connected lower tile is the usable entry.
        map.ExitCells[1 * 4 + 2] = map.ExitCells[2 * 4 + 2] = 0;
        map.CollisionLayers[1 * 4 + 2] = 0;
        var frame = source.Build(field, map, new(256, 0, 768, 320), [], new(3, false));
        var stairs = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Exits);
        var story = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.StoryEvents);
        var search = NavigationPathfinder.Search(frame.Graph, frame.Player, stairs.ApproachPoints);

        Assert.NotNull(search.Route);
        Assert.False(search.LimitReached);
        Assert.Equal(2, search.Route[^1].Y / 256);
        Assert.Equal(stairs.ApproachPoints, story.ApproachPoints);
        Assert.Contains(search.Route[^1], story.ApproachPoints);
        var offscreen = source.Build(field, map, new(0, 0, 256, 256), [], new(3, false));
        var known = Assert.Single(offscreen.Targets, t => t.Category == NavigationCategory.Exits);
        Assert.False(known.Visible);
        Assert.Equal(stairs.ApproachPoints, known.ApproachPoints);
    }

    [Fact]
    public void ConnectedExitGeometryDoesNotDiscoverHiddenExitsOrDisconnectedSameIdTiles()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var field = Field(Actor(1, 384, 640, party: true));
        var map = Map();
        map.ExitCells[1 * 4 + 2] = map.ExitCells[2 * 4 + 2] = 0;
        map.ExitCells[3 * 4 + 0] = 0; // Disconnected reuse of the same exit id.
        map.ExitCells[3 * 4 + 3] = 0; // Diagonal contact is not a cardinal connection.
        map.ExitCells[2 * 4 + 1] = 1; // Adjacent but a different, unseen exit.
        var frame = source.Build(field, map, new(512, 0, 768, 320), []);
        var stairs = Assert.Single(frame.Targets);
        Assert.Equal("exit:0", stairs.Id);
        Assert.Contains(stairs.ApproachPoints, p => p.Y / 256 == 2);
        Assert.All(stairs.ApproachPoints, p =>
        {
            Assert.Equal(2, p.X / 256);
            Assert.InRange(p.Y / 256, 1, 2);
        });
    }

    [Fact]
    public void StoryUsesAnUnseenExitWithoutMarkingItDiscovered()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var field = Field(Actor(0, 6308, 2303, party: true)) with { SceneId = 2 };
        var map = Map(64);
        map.ExitCells[13 * 64 + 23] = 0;
        var targets = source.Build(field, map, new(4096, 256, 8320, 3328), [], new(3, false)).Targets;
        var story = Assert.Single(targets);
        Assert.False(story.IsStoryNote);
        Assert.False(story.Visible);
        Assert.False(story.Discovered);
        Assert.NotEmpty(story.ApproachPoints);
    }

    [Theory]
    [InlineData(0, 0, 384, 768, true)]
    [InlineData(400, 0, 768, 768, true)]
    [InlineData(0, 0, 768, 384, true)]
    [InlineData(0, 400, 768, 768, true)]
    [InlineData(0, 0, 256, 768, false)]
    [InlineData(512, 0, 768, 768, false)]
    [InlineData(0, 0, 768, 256, false)]
    [InlineData(0, 512, 768, 768, false)]
    public void RenderedChestAndExitTilesRequirePositiveVisibleArea(int left, int top, int right, int bottom, bool visible)
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var map = Map(); map.ExitCells[5] = 0;
        var frame = source.Build(Field(Actor(0, 128, 128, party: true)), map,
            new(left, top, right, bottom), [new(5, 384, 384)]);
        Assert.Equal(visible ? 2 : 0, frame.Targets.Count);
        Assert.All(frame.Targets, t => Assert.True(t.Visible));
    }

    [Fact]
    public void OpeningLabelsAndStoryEventsRemainBoundToVisibleNativeRecords()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var mother = Actor(15, 512, 256) with { VisualIndex = 0x25 };
        var field = Field(Actor(1, 128, 128, party: true), mother) with { SceneId = 1 };
        var frame = source.Build(field, Map(), new(0, 0, 1024, 1024), [], new(3, false));
        Assert.Equal("Mother", Assert.Single(frame.Targets, t => t.Category == NavigationCategory.People).Label);
        Assert.Equal("Talk with Mother", Assert.Single(frame.Targets, t => t.Category == NavigationCategory.StoryEvents).Label);
        var hidden = field with { Actors = [field.Actors[0], mother with { DrawMode = 0 }] };
        Assert.True(Assert.Single(source.Build(hidden, Map(), new(0, 0, 1024, 1024), [], new(3, false)).Targets).IsStoryNote);
        var otherAppearance = field with { Actors = [field.Actors[0], mother with { VisualIndex = 0x4C }] };
        Assert.DoesNotContain(source.Build(otherAppearance, Map(), new(0, 0, 1024, 1024), [], new(3, false)).Targets,
            t => t.Label == "Mother" || (t.Category == NavigationCategory.StoryEvents && !t.IsStoryNote));
        var machine = Field(Actor(1, 128, 128, party: true), Actor(11, 512, 256) with { VisualIndex = 0x63 }) with { SceneId = 8 };
        var telepod = Assert.Single(source.Build(machine, Map(), new(0, 0, 1024, 1024), []).Targets);
        Assert.Equal("Fallen pendant", telepod.Label); Assert.Equal(NavigationCategory.Objects, telepod.Category);
    }

    [Fact]
    public void VisibleCatAndMotherDoNotRequireAConfirmAction()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var cat = Actor(9, 6016, 2047) with { VisualIndex = 0x3B, ActivationEnabled = 0 };
        var bedroom = Field(Actor(0, 6308, 2303, party: true), cat) with { SceneId = 2 };
        Assert.Equal("Cat", Assert.Single(source.Build(bedroom, Map(64), new(4096, 256, 8320, 3456), []).Targets).Label);

        var mother = Actor(15, 512, 256) with { VisualIndex = 0x25, ActivationEnabled = 0 };
        var kitchen = Field(Actor(0, 128, 128, party: true), mother) with { SceneId = 1 };
        var frame = source.Build(kitchen, Map(), new(0, 0, 1024, 1024), [], new(3, false));
        Assert.Equal("Mother", Assert.Single(frame.Targets, t => t.Category == NavigationCategory.People).Label);
        Assert.Equal("Talk with Mother", Assert.Single(frame.Targets, t => t.Category == NavigationCategory.StoryEvents).Label);
    }

    [Fact]
    public void PeopleCanLackActionsButObjectsStillRequireNativeInteractionEligibility()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var cat = Actor(1, 256, 256) with { VisualIndex = 0x3B, ActivationEnabled = 0, ActivationBinding = 0 };
        var noAction = Actor(2, 512, 256) with { VisualIndex = 0x63, ActivationEnabled = 0 };
        var noBinding = Actor(3, 768, 256) with { VisualIndex = 0x63, ActivationBinding = 0 };
        var interactable = Actor(4, 512, 512) with { VisualIndex = 0x63 };
        var unknownClass = Actor(5, 768, 512) with { ClassTag = 7 };
        var removed = Actor(6, 256, 512) with { ClassTag = 0x84 };
        var field = Field(Actor(0, 128, 128, party: true), cat, noAction, noBinding, interactable, unknownClass, removed);
        var frame = source.Build(field, Map(), new(0, 0, 1024, 1024), []);
        Assert.Equal(2, frame.Targets.Count);
        Assert.Equal("Cat", Assert.Single(frame.Targets, t => t.Category == NavigationCategory.People).Label);
        Assert.Equal("actor:4:4:99", Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Objects).Id);
    }

    [Fact]
    public void InventoryDiagnosticsExplainEmptyListsWithoutRepeatingUnchangedFrames()
    {
        var log = new List<string>();
        var source = new FieldNavigationSource(new NoMemory(), log.Add);
        var field = Field(Actor(0, 128, 128, party: true));
        var map = Map(); map.ExitCells[5] = 0;
        var hiddenView = new FieldViewport(0, 0, 256, 256);
        source.Build(field, map, hiddenView, []);
        var first = Assert.Single(log, entry => entry.StartsWith("Navigation inventory:"));
        Assert.Contains("people=0; exits=0; objects=0; storyEvents=0", first);
        Assert.Contains("exitCells=1; visibleExitCells=0", first);
        Assert.Contains("storyPoint=unknown", first);
        Assert.Single(log, entry => entry.StartsWith("Navigation actor facts:"));
        var count = log.Count;
        for (var i = 0; i < 60; i++) source.Build(field, map, hiddenView, []);
        Assert.Equal(count, log.Count);

        source.Build(field, map, new(0, 0, 512, 512), []);
        var changed = log.Last(entry => entry.StartsWith("Navigation inventory:"));
        Assert.Contains("exits=1", changed);
        Assert.Contains("visibleExitCells=1", changed);
        source.Reset();
        source.Build(field, map, hiddenView, []);
        Assert.Equal(3, log.Count(entry => entry.StartsWith("Navigation inventory:")));
    }

    private static FieldActorSnapshot Actor(int index, int x, int y, bool party = false) =>
        new(index, x >> 8, x, x & 255, y >> 8, y, y & 255, 1, 1, 1, 14, 4, 0x2020, 1, 1, party, true, true, true);
    private static FieldNavigationSnapshot Field(params FieldActorSnapshot[] actors) =>
        new(0x1000, 0x4000, 0x2000, 0x20000, actors.Length, 0, true, 1, 0, 1, 1, actors[0], actors);
    private static FieldMapSnapshot Map(int side = 4) => new(side, side, new byte[side * side], new byte[side * side],
        Enumerable.Repeat((byte)1, side * side).ToArray(), 1, false, side, side,
        Enumerable.Repeat((byte)128, side * side).ToArray());
    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
