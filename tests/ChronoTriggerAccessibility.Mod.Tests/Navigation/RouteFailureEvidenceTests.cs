using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Bounded evidence for route failures that cannot be explained from the log:
/// Kids' Treasure Room 171 (pickup with no approaches), Upon Entering 166 (Level crossing D)
/// and Trap 3 175 (conveyor). It records only what this frame was built from: the live
/// MapTable planes, actor snapshots, story words and the target's approaches.</summary>
public sealed class RouteFailureEvidenceTests
{
    [Fact]
    public void TheFrameCarriesTheCapturedMapActorsStoryAndTargetApproaches()
    {
        var (frame, map, pickup) = Build();
        var encoded = Assert.IsType<string>(frame.RouteEvidence!("no-route", pickup.Id));
        using var json = JsonDocument.Parse(RouteFailureEvidence.Decode(encoded));
        var root = json.RootElement;
        Assert.Equal("no-route", root.GetProperty("kind").GetString());
        Assert.Equal("live-maptable-planes", root.GetProperty("mapSource").GetString());
        Assert.Equal(Convert.ToBase64String(map.CollisionShapes), root.GetProperty("map").GetProperty("CollisionShapes").GetString());
        Assert.Equal(Convert.ToBase64String(map.CollisionLayers), root.GetProperty("map").GetProperty("CollisionLayers").GetString());
        Assert.Equal(171, root.GetProperty("field").GetProperty("scene").GetInt32());
        Assert.Equal(136, root.GetProperty("story").GetProperty("point").GetInt32());
        Assert.Contains(root.GetProperty("actors").EnumerateArray(), a => a.GetProperty("Index").GetInt32() == 9);
        var treasure = Assert.Single(root.GetProperty("treasures").EnumerateArray());
        Assert.Equal((80, 9 * 256 + 128, false), (treasure.GetProperty("index").GetInt32(), treasure.GetProperty("x").GetInt32(), treasure.GetProperty("chest").GetBoolean()));
        Assert.Equal(9, pickup.ActorSlot);
        var target = root.GetProperty("target");
        Assert.Equal(pickup.Id, target.GetProperty("id").GetString());
        Assert.Equal(pickup.ApproachPoints.Count, target.GetProperty("approaches").GetArrayLength());
    }

    [Fact]
    public void AnUnknownTargetStillRecordsTheFloorAndActors()
    {
        var (frame, _, _) = Build();
        using var json = JsonDocument.Parse(RouteFailureEvidence.Decode(frame.RouteEvidence!("movement-blocked", "gone")!));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("target").ValueKind);
        Assert.True(json.RootElement.GetProperty("actors").GetArrayLength() > 0);
    }

    [Fact]
    public void EncodedEvidenceStaysWithinItsBound()
    {
        var (frame, _, pickup) = Build();
        Assert.InRange(frame.RouteEvidence!("no-route", pickup.Id)!.Length, 1, RouteFailureEvidence.MaximumEncodedLength);
    }

    [Theory]
    [InlineData("No route to Item pickup is available.", "no-route")]
    [InlineData("Navigation stopped: movement is blocked.", "movement-blocked")]
    [InlineData("Ozzie is blocking the way. Guidance continues when the way clears.", "creature-blocked")]
    [InlineData("The way is temporarily blocked. Guidance continues when it clears.", "temporarily-blocked")]
    [InlineData("Arrived at Item pickup.", null)]
    public void OnlyRouteFailuresAreRecorded(string speech, string? kind) =>
        Assert.Equal(kind, RouteFailureEvidence.Kind([speech]));

    [Fact]
    public void TheRuntimeLogsOneSnapshotPerFailingPoseAndTargetWithinACap()
    {
        var h = new Harness(); h.Enable();
        h.Press('I');
        Assert.Contains(h.Speech, s => s.Contains("No route to Pickup is available."));
        var evidence = Assert.Single(h.Diagnostics, d => d.StartsWith("Navigation route evidence:", StringComparison.Ordinal));
        Assert.Contains("kind=no-route; scene=room; target=pickup; player=(0,0,1);", evidence);
        using var json = JsonDocument.Parse(RouteFailureEvidence.Decode(evidence[(evidence.IndexOf("data=", StringComparison.Ordinal) + 5)..]));
        Assert.Equal("no-route", json.RootElement.GetProperty("kind").GetString());

        // The same pose and target are not logged again.
        h.Press('I');
        Assert.Single(h.Diagnostics, d => d.StartsWith("Navigation route evidence:", StringComparison.Ordinal));

        // New poses are logged up to the session cap.
        for (var x = 1; x <= 20; x++) { h.PlayerX = x * 256; h.Press('I'); }
        Assert.Equal(RouteFailureEvidence.MaximumSnapshots,
            h.Diagnostics.Count(d => d.StartsWith("Navigation route evidence:", StringComparison.Ordinal)));
    }

    private static (NavigationFrame Frame, FieldMapSnapshot Map, NavigationTarget Pickup) Build()
    {
        var player = FullGameNavigationTests.Actor(1, 24 * 256 + 128, 24 * 256 + 128, true) with { ClassTag = 0 };
        var kid = FullGameNavigationTests.Actor(9, 20 * 256 + 128, 20 * 256 + 128) with { VisualIndex = 26 };
        var field = FullGameNavigationTests.Field(171) with { LeadPlayer = player, Actors = [player, kid], ActorCount = 18 };
        var shapes = new byte[4096];
        shapes[100] = 3;
        var layers = Enumerable.Repeat((byte)1, 4096).ToArray();
        layers[200] = 2;
        var map = new FieldMapSnapshot(64, 64, shapes, new byte[4096], layers, 1, false, 64, 64,
            Enumerable.Repeat((byte)128, 4096).ToArray());
        var frame = new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, map, new(0, 0, 64 * 256, 64 * 256),
            [new FieldTreasure(80, 9 * 256 + 128, 30 * 256 + 128) { IsChest = false }],
            new FieldStoryState(136, true) { Locals = new Dictionary<int, int> { [3] = 1 }, Globals = new Dictionary<int, int> { [0x54] = 8 } });
        return (frame, map, frame.Targets.First(t => t.Id.StartsWith("actor:9:", StringComparison.Ordinal)));
    }

    private sealed class Harness
    {
        public HashSet<int> Keys { get; } = [];
        public List<string> Speech { get; } = [];
        public List<string> Diagnostics { get; } = [];
        public long Now;
        public int PlayerX;
        public FieldNavigationRuntime Runtime { get; }
        public Harness() => Runtime = new(_ =>
        {
            var point = new NavigationPoint(64, 0, 1);
            return new("room", true, new(PlayerX, 0, 1), [new("pickup", "Pickup", NavigationCategory.People,
                point, [point], true, false)], new Line())
            { RouteEvidence = (kind, id) => RouteFailureEvidence.Encode(new { kind, target = id }) };
        }, new(Keys.Contains, () => true), () => true, () => Now, Speech.Add, Diagnostics.Add);
        public void Enable() { Runtime.Enable(); Tick(); }
        public uint Tick(uint input = 0) { Now += 16; return Runtime.OnInput(1, input); }
        public uint Press(int key) { Keys.Clear(); Tick(); Keys.Add(key); var value = Tick(); Keys.Clear(); return value; }
    }

    /// <summary>Only the first sixteen units are walkable; the pickup at 64 is unreachable.</summary>
    private sealed class Line : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point)
        {
            if (point.X < 16) yield return point with { X = point.X + 4 };
        }
    }

    private sealed class NoMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination) => false;
    }
}
