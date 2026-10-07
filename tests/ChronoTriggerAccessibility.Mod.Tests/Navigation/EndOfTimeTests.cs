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
        var pillars = frame.Targets.Where(t => t.Label == "Pillar of light").ToArray();
        Assert.Equal(3, pillars.Length);
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
    [InlineData(74, true)]
    [InlineData(75, false)]
    public void InactiveDoorDoesNotInventAnOpening(int point, bool calls)
    {
        var door = Actor(25, 8064, 4095) with { ScriptCallsEnabled = calls };
        var frame = Build(464, [door], State(point), NativeMap(), new(7392, 5343, 1));
        var goal = Assert.Single(frame.Targets, t => t.Id == "exit:0");
        Assert.Null(NavigationPathfinder.Search(frame.Graph, frame.Player, goal.ApproachPoints).Route);
    }

    [Fact]
    public void AdditionalPillarsFollowTheirNativeDrawingFlags()
    {
        var actors = Enumerable.Range(15, 9).Select(i => Actor(i, 65280, 65280)).ToArray();
        var state = State(77) with { Globals = new Dictionary<int, int> { [0xA6] = 7 } };
        var frame = Build(464, actors, state, OpenMap(), new(3456, 3064, 1));
        Assert.Equal(9, frame.Targets.Count(t => t.Label == "Pillar of light"));
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

    private sealed class NoMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination) => false;
    }
}
