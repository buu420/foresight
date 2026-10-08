using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class EndOfTimeTests
{
    [Fact]
    public void ReportedSpekkioClassHasAReachableStoryBinding()
    {
        // Tester log line 3632: class 6, visual E1, position (1920,2559).
        var actor = Actor(10, 1920, 2559) with { ClassTag = 6, VisualIndex = 225, LoadedFlag = 1, DrawMode = 1 };
        var frame = Build(465, [actor], State(75), OpenMap(), new(1536, 3359, 1));
        Assert.Equal("Spekkio", Assert.Single(frame.Targets, t => t.Id == "actor:10:6:225").Label);
        var objective = Assert.Single(frame.Targets, t => t.Id == "story:future:spekkio");
        Assert.False(objective.IsStoryNote);
        Assert.NotEmpty(objective.ApproachPoints);
    }

    [Theory]
    [InlineData(3, "Return to Spekkio")]
    [InlineData(4, "Speak with Spekkio to restart the walking lesson")]
    public void MagicLessonReturnsToReportedSpekkioAfterCompletionOrInterruption(int laps, string label)
    {
        var actor = Actor(10, 1920, 2559) with { ClassTag = 6, VisualIndex = 225, LoadedFlag = 1, DrawMode = 1 };
        var locals = Enumerable.Range(0, 256).ToDictionary(i => i, _ => 0);
        for (var i = 9; i <= 13; i++) locals[i] = 1;
        locals[14] = laps;
        var state = State(76) with { Locals = locals };
        var frame = Build(465, [actor], state, OpenMap(), new(1536, 3359, 1));
        var objective = Assert.Single(frame.Targets, t => t.Id == "story:future:magic-lesson-checkpoint");
        Assert.Equal(label, objective.Label);
        Assert.False(objective.IsStoryNote);
        Assert.NotEmpty(objective.ApproachPoints);
    }

    [Theory]
    [InlineData(72)]
    [InlineData(77)]
    public void DrawnBasePillarsRemainTrackableWhenTheirScriptActorsAreParked(int point)
    {
        var actors = Enumerable.Range(15, 9).Select(i => Actor(i, 65280, 65280)).ToArray();
        var frame = Build(464, actors, State(point), OpenMap(), new(3456, 3064, 1));
        var pillars = frame.Targets.Where(t => t.Label.StartsWith("Pillar of light", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, pillars.Length);
        Assert.Equal(new[] { "Pillar of light 1", "Pillar of light 2", "Pillar of light 3" }, pillars.Select(t => t.Label));
        Assert.All(pillars, t =>
        {
            Assert.Equal(NavigationCategory.Exits, t.Category);
            Assert.NotEmpty(t.ApproachPoints);
            Assert.Contains(point < 77 ? "lesson" : "Confirm", t.ArrivalInstruction);
        });
    }

    [Fact]
    public void InitialPlatformRouteReachesTheOldManFromTheReportedArrival()
    {
        var oldMan = Actor(28, 7552, 5631) with { ClassTag = 4, VisualIndex = 72, DrawMode = 1, LoadedFlag = 1 };
        var stairs = Actor(24, 6528, 4351);
        var frame = Build(464, [oldMan, stairs], State(72), NativeMap(), new(3456, 3064, 1));
        var goal = Assert.Single(frame.Targets, t => t.Id == "story:future:end-old-man");
        Assert.NotNull(NavigationPathfinder.Search(frame.Graph, frame.Player, goal.ApproachPoints).Route);
    }

    [Fact]
    public void DoorTouchCanStageTheRouteIntoSpekkiosRoom()
    {
        // The native marker is a touch terrain copy after point 75, even though
        // its earlier Confirm handler also has a talk action.
        var door = Actor(25, 8064, 4095);
        var frame = Build(464, [door], State(75), NativeMap(), new(7392, 5343, 1));
        var goal = Assert.Single(frame.Targets, t => t.Id == "story:future:spekkio-room");
        Assert.NotNull(NavigationPathfinder.Search(frame.Graph, frame.Player, goal.ApproachPoints).Route);
    }

    [Theory]
    [InlineData(75)]
    [InlineData(220)]
    public void DoorRouteFromThePillarPlatformPlansTheStairsBeforeTheDoor(int point)
    {
        var frame = Build(464, [Actor(24, 6528, 4351), Actor(25, 8064, 4095)], State(point),
            NativeMap(), new(3456, 3064, 1));
        var exit = Assert.Single(frame.Targets, t => t.Id == "exit:0");
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, exit.ApproachPoints);
        Assert.NotNull(route.Route);
        Assert.Equal("landmark:24", route.IntermediateId);
        Assert.NotNull(route.IntermediateContact);
    }

    [Theory]
    [InlineData(75, 1)]
    [InlineData(75, 2)]
    [InlineData(75, 3)]
    [InlineData(77, 1)]
    [InlineData(77, 2)]
    [InlineData(77, 3)]
    [InlineData(220, 1)]
    [InlineData(220, 2)]
    [InlineData(220, 3)]
    public void DoorRouteCanBePlannedWhileFacingElsewhereAndFinishesUpward(int point, int facing)
    {
        // Atel0283's main loop writes the lead's facing into local 0B.
        // The final upward movement satisfies it; the earlier route need not.
        var locals = State(point).Locals.ToDictionary(p => p.Key, p => p.Value);
        locals[11] = facing;
        var frame = Build(464, [Actor(25, 8064, 4095)], State(point) with { Locals = locals },
            NativeMap(), new(7392, 5343, 1));
        var exit = Assert.Single(frame.Targets, t => t.Id == "exit:0");
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, exit.ApproachPoints);
        Assert.NotNull(route.Route);
        Assert.Equal("landmark:25", route.IntermediateId);
        var contact = Assert.IsType<NavigationPoint>(route.IntermediateContact);
        Assert.Equal(contact.X, route.Route[^1].X);
        Assert.True(route.Route[^1].Y > contact.Y);
    }

    [Theory]
    [InlineData(NavigationCommand.Guide)]
    [InlineData(NavigationCommand.ToggleWalk)]
    public void DoorGuidanceSurvivesFacingChangesWhileApproaching(NavigationCommand command)
    {
        var door = Actor(25, 8064, 4095);
        var locals = State(77).Locals.ToDictionary(p => p.Key, p => p.Value);
        locals[11] = 1;
        var first = Build(464, [door], State(77) with { Locals = locals }, NativeMap(), new(7392, 5343, 1));
        var nav = new NavigationController();
        nav.Handle(NavigationCommand.NextCategory, first, 0);
        var result = nav.Handle(command, first, 16);
        Assert.True(result.Guiding);
        Assert.Equal(command == NavigationCommand.ToggleWalk, result.AutoWalking);
        locals[11] = 3;
        var exit = Assert.Single(first.Targets, t => t.Id == "exit:0");
        var route = NavigationPathfinder.Search(first.Graph, first.Player, exit.ApproachPoints).Route!;
        var next = Build(464, [door], State(77) with { Locals = locals }, NativeMap(), route[1]);
        result = nav.Update(next, 32);
        Assert.True(result.Guiding, string.Join(" ", result.Speech));
        Assert.Equal(command == NavigationCommand.ToggleWalk, result.AutoWalking);
        var endpoint = Build(464, [door], State(77) with { Locals = locals }, NativeMap(), route[^1]);
        result = nav.Update(endpoint, 48);
        Assert.True(result.Guiding, string.Join(" ", result.Speech));
        Assert.Contains("Continue up until the passage opens.", result.Speech);
        Assert.Equal(command == NavigationCommand.ToggleWalk ? NavigationDirection.North : NavigationDirection.None, result.Direction);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(4, 0)]
    [InlineData(1, 1)]
    public void DoorPlanningKeepsUnknownFacingAndTheHandledCopyGuardClosed(int facing, int handled)
    {
        var locals = State(77).Locals.ToDictionary(p => p.Key, p => p.Value);
        if (facing == -1) locals.Remove(11); else locals[11] = facing;
        locals[8] = handled;
        var frame = Build(464, [Actor(25, 8064, 4095)], State(77) with { Locals = locals },
            NativeMap(), new(7392, 5343, 1));
        var exit = Assert.Single(frame.Targets, t => t.Id == "exit:0");
        Assert.Null(NavigationPathfinder.Search(frame.Graph, frame.Player, exit.ApproachPoints).Route);
    }

    [Fact]
    public void SuccessiveLiveCapturesAdvanceFromStairsToDoorToOpenedExit()
    {
        var actors = new[] { Actor(24, 6528, 4351), Actor(25, 8064, 4095) };
        var locals = State(77).Locals.ToDictionary(p => p.Key, p => p.Value); locals[11] = 1;
        var state = State(77) with { Locals = locals };
        var map = NativeMap();
        var first = Build(464, actors, state, map, new(3456, 3064, 1));
        var exit = Assert.Single(first.Targets, t => t.Id == "exit:0");
        var stairs = NavigationPathfinder.Search(first.Graph, first.Player, exit.ApproachPoints);
        Assert.Equal("landmark:24", stairs.IntermediateId);
        Assert.All(stairs.Route!, p => Assert.True(new FieldNavigationGraph(map).TryPosition(p.X, p.Y, p.Layer, out _)));
        var stairCopy = NativeOpenedCopy(map, 24);
        locals[7] = 1; locals[11] = 3;
        var next = Build(464, actors, state, stairCopy, stairs.Route![^1]);
        exit = Assert.Single(next.Targets, t => t.Id == "exit:0");
        var door = NavigationPathfinder.Search(next.Graph, next.Player, exit.ApproachPoints);
        Assert.Equal("landmark:25", door.IntermediateId);
        Assert.All(door.Route!, p => Assert.True(new FieldNavigationGraph(stairCopy).TryPosition(p.X, p.Y, p.Layer, out _)));
        var doorCopy = NativeOpenedCopy(stairCopy, 25);
        locals[8] = 1;
        var last = Build(464, actors, state, doorCopy, door.Route![^1]);
        exit = Assert.Single(last.Targets, t => t.Id == "exit:0");
        var opened = NavigationPathfinder.Search(last.Graph, last.Player, exit.ApproachPoints);
        Assert.NotNull(opened.Route);
        Assert.Null(opened.IntermediateId);
    }

    [Theory]
    [InlineData(74, true)]
    [InlineData(75, false)]
    public void InactiveDoorDoesNotInventAnOpening(int point, bool calls)
    {
        var door = Actor(25, 8064, 4095) with { ScriptCallsEnabled = calls };
        var frame = Build(464, [door], State(point), NativeMap(), new(7392, 5343, 1));
        var goal = Assert.Single(frame.Targets, t => t.Id == "exit:0");
        Assert.Null(NavigationPathfinder.Search(frame.Graph, frame.Player, goal.ApproachPoints).Route);
    }

    [Theory]
    [InlineData(73)]
    [InlineData(74)]
    public void PlatformStagingKeepsTheUnfinishedIntroductionDoorClosed(int point)
    {
        var frame = Build(464, [Actor(24, 6528, 4351), Actor(25, 8064, 4095)], State(point),
            NativeMap(), new(3456, 3064, 1));
        var exit = Assert.Single(frame.Targets, t => t.Id == "exit:0");
        Assert.Null(NavigationPathfinder.Search(frame.Graph, frame.Player, exit.ApproachPoints).Route);
    }

    [Fact]
    public void AdditionalPillarsFollowTheirNativeDrawingFlags()
    {
        var actors = Enumerable.Range(15, 9).Select(i => Actor(i, 65280, 65280)).ToArray();
        var state = State(77) with { Globals = new Dictionary<int, int> { [0xA6] = 7 } };
        var frame = Build(464, actors, state, OpenMap(), new(3456, 3064, 1));
        var pillars = frame.Targets.Where(t => t.Label.StartsWith("Pillar of light", StringComparison.Ordinal)).ToArray();
        Assert.Equal(Enumerable.Range(1, 9).Select(i => $"Pillar of light {i}"), pillars.Select(t => t.Label));
    }

    [Fact]
    public void ExistingPillarsKeepTheirNumbersWhenMoreLightsAppearOrThePlayerMoves()
    {
        var actors = Enumerable.Range(15, 9).Select(i => Actor(i, 65280, 65280)).ToArray();
        var first = Build(464, actors, State(77), OpenMap(), new(3456, 3064, 1)).Targets
            .Where(t => t.Label.StartsWith("Pillar of light", StringComparison.Ordinal)).ToDictionary(t => t.Id, t => t.Label);
        var all = Build(464, actors, State(220) with { Globals = new Dictionary<int, int> { [0xA6] = 7 } },
            OpenMap(), new(6272, 5695, 1)).Targets;
        Assert.All(first, p => Assert.Equal(p.Value, Assert.Single(all, t => t.Id == p.Key).Label));
        Assert.Equal(9, all.Where(t => t.Label.StartsWith("Pillar of light", StringComparison.Ordinal)).Select(t => t.Label).Distinct().Count());
    }

    [Theory]
    [InlineData(223, 8, 11136, 1535, 55)]
    [InlineData(225, 9, 14464, 2559, 55)]
    [InlineData(223, 8, 11136, 1535, 150)]
    [InlineData(225, 9, 14464, 2559, 150)]
    public void BikeParkingInteractionIsATrackableObject(int scene, int index, int x, int y, int point)
    {
        // Atel0345 actor 8 startup: (43,5); Atel0346 actor 9: (56,9).
        var state = State(point) with
        {
            Globals = new Dictionary<int, int> { [340] = 1 },
            Inventory = new Dictionary<int, int> { [20486] = 1 },
            Party = new[] { 0, 1, 2, 3, 255, 255, 255, 255, 255 },
        };
        var frame = Build(scene, [Actor(index, x, y)], state, OpenMap(), new(x - 512, y, 1));
        var bike = Assert.Single(frame.Targets, t => t.Id == $"landmark:{index}");
        Assert.Equal("Jet bike", bike.Label);
        Assert.Equal(NavigationCategory.Objects, bike.Category);
        Assert.NotEmpty(bike.ApproachPoints);
        Assert.Contains("Confirm", bike.ArrivalInstruction);
    }

    private static FieldStoryState State(int point) => new(point, false)
    {
        Globals = new Dictionary<int, int> { [0xA6] = 0 },
        Locals = Enumerable.Range(0, 256).ToDictionary(i => i, _ => 0),
    };

    private static FieldActorSnapshot Actor(int index, int x, int y) =>
        new(index, x >> 8, x, x & 255, y >> 8, y, y & 255, 0, 1, 0, 0, 7, 0, 0, 0, false, true, true, true);

    private static NavigationFrame Build(int scene, FieldActorSnapshot[] actors, FieldStoryState state,
        FieldMapSnapshot map, NavigationPoint player)
    {
        var lead = Actor(1, player.X, player.Y) with { ClassTag = 0, IsPartyMember = true, LoadedFlag = 1 };
        var field = new FieldNavigationSnapshot(0x1000, 0x4000, 0x2000, 0x20000, actors.Length + 1,
            scene, true, 1, 0, 0, 0, lead, [lead, .. actors]);
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, map, new(0, 0, 16384, 16384), [], state);
    }

    private static FieldMapSnapshot OpenMap() => new(64, 64, new byte[4096], new byte[4096],
        Enumerable.Repeat((byte)1, 4096).ToArray(), 1, false, 64, 64, Enumerable.Repeat((byte)128, 4096).ToArray());

    private static FieldMapSnapshot NativeMap()
    {
        using var stream = typeof(EndOfTimeTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.end-of-time-map-0345.json")!;
        using var json = JsonDocument.Parse(stream);
        var root = json.RootElement;
        byte[] Plane(string name) => Convert.FromBase64String(root.GetProperty(name).GetString()!);
        var width = root.GetProperty("Width").GetInt32();
        var height = root.GetProperty("Height").GetInt32();
        return new(width, height, Plane("CollisionShapes"), Plane("TerrainFlags"), Plane("CollisionLayers"),
            1, false, width, height, Plane("ExitCells"));
    }

    private static FieldMapSnapshot NativeOpenedCopy(FieldMapSnapshot map, int actor)
    {
        // Native E5 copies all three collision planes for these two scripts.
        var copy = GameNavigationCatalog.ForScene(464)!.Actors.Single(a => a.Id == actor).Actions
            .Single(a => a.Kind == "Terrain").Copy!;
        var result = map with { CollisionShapes = (byte[])map.CollisionShapes.Clone(),
            TerrainFlags = (byte[])map.TerrainFlags.Clone(), CollisionLayers = (byte[])map.CollisionLayers.Clone() };
        foreach (var (original, updated) in new[] { (map.CollisionShapes, result.CollisionShapes),
                     (map.TerrainFlags, result.TerrainFlags), (map.CollisionLayers, result.CollisionLayers) })
        for (var y = 0; y <= copy.Bottom - copy.Top; y++)
        for (var x = 0; x <= copy.Right - copy.Left; x++)
            updated[(copy.Y + y) * map.Width + copy.X + x] = original[(copy.Top + y) * map.Width + copy.Left + x];
        return result;
    }

    private sealed class NoMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination) => false;
    }
}
