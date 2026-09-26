using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using System.Text.Json;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class MagicLessonTests
{
    [Fact]
    public void EveryClockwiseCheckpointAndReturnIsGuidedFromNativeProgress()
    {
        // Atel0284 actors16..20 advance locals09..0D in order. Returning to
        // actor16 advances local0E; after three laps it instead resets local09.
        var counts = new int[5];
        var targets = Enumerable.Range(16, 5).Select(i => Target($"landmark:{i}", i)).ToArray();
        for (var lap = 0; lap < 3; lap++)
        {
            for (var next = 0; next < 5; next++)
            {
                var state = State(counts, lap);
                var goal = Assert.Single(FutureStoryTargets.Build(465, state, targets, default));
                Assert.False(goal.IsStoryNote);
                Assert.Equal(targets[next].ApproachPoints, goal.ApproachPoints);
                counts[next] = lap + 1;
            }
        }
        var home = Assert.Single(FutureStoryTargets.Build(465, State(counts, 2), targets, default));
        Assert.Equal(targets[0].ApproachPoints, home.ApproachPoints);
        counts[0] = 0;
        var spekkio = Target("actor:10:5:225", 30);
        var final = Assert.Single(FutureStoryTargets.Build(465, State(counts, 3), [.. targets, spekkio], default));
        Assert.Equal(spekkio.ApproachPoints, final.ApproachPoints);
        Assert.Contains("Spekkio", final.Label);
    }

    [Fact]
    public void MissingProgressDoesNotGuessTheNextCornerAndInterruptedLessonReturnsToSpekkio()
    {
        var spekkio = Target("actor:10:5:225", 30);
        Assert.True(Assert.Single(FutureStoryTargets.Build(465, new(76, false), [spekkio], default)).IsStoryNote);
        var interrupted = Assert.Single(FutureStoryTargets.Build(465, State([2, 1, 1, 1, 1], 4), [spekkio], default));
        Assert.Equal(spekkio.ApproachPoints, interrupted.ApproachPoints);
    }

    [Fact]
    public void NativeRoomGeometryCanReachEachLiveCheckpointWithoutDiscovery()
    {
        using var resource = typeof(MagicLessonTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.spekkio-initial-map-0332.json")!;
        var map = JsonSerializer.Deserialize<FieldMapSnapshot>(resource)!;
        // Placements from Atel0284 0AF5..0D00; normal navigation uses the live
        // actors, not these research fixture positions.
        var tiles = new[] { (5, 13), (1, 12), (5, 3), (13, 7), (9, 15) };
        var actors = tiles.Select((p, i) => Actor(i + 16, p.Item1 * 256 + 128, p.Item2 * 256 + 255)).ToArray();
        var at = new NavigationPoint(7 * 256 + 128, 12 * 256 + 128, 1);
        var counts = new int[5];
        foreach (var stage in Enumerable.Range(0, 6))
        {
            var player = Actor(0, at.X, at.Y) with { ClassTag = 2, DrawMode = 1, IsPartyMember = true };
            var field = new FieldNavigationSnapshot(0x1000, 0x4000, 0x2000, 0x20000,
                21, 465, true, 1, 0, 0, 0, player, [player, .. actors])
                { ActorCollisionRadius = FieldActorCollisionRules.Radius(465) };
            var frame = new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, map,
                new(0, 0, 128, 128), [], State(counts, 0));
            var goal = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.StoryEvents);
            Assert.False(goal.IsStoryNote);
            Assert.False(goal.Discovered);
            var route = NavigationPathfinder.Search(frame.Graph, frame.Player, goal.ApproachPoints).Route;
            Assert.True(route is not null, $"Stage {stage}: {goal.Label}, from {at}, goals {string.Join(';', goal.ApproachPoints)}");
            at = route[^1];
            // Independent 178980 contact calculation, including strict bounds,
            // carry from the empty last party slot and directional leading probes.
            var marker = actors[stage % 5];
            foreach (var dx in new[] { -32, 0, 32 })
            foreach (var dy in new[] { -32, 0, 32 })
            {
                var px = at.X + dx; var py = at.Y + dy;
                if (goal.ContactDirection == NavigationDirection.North) py -= 112;
                if (goal.ContactDirection == NavigationDirection.West) { px -= 112; py -= 64; }
                if (goal.ContactDirection == NavigationDirection.East) { px += 112; py -= 64; }
                Assert.True(Math.Abs(marker.FineX - px - 1) < 224 && Math.Abs(marker.FineY - py - 1) < 224,
                    $"Stage {stage}: contact outside native radius at {at}, arrival delta {dx},{dy}");
            }
            if (stage < 5) counts[stage] = 1;
        }
    }

    private static FieldActorSnapshot Actor(int id, int x, int y) =>
        new(id, x / 256, x, x % 256, y / 256, y, y % 256, 0, 0, 0, 0, 7, 0, 1, 1, false, true, true, true);
    private sealed class NoMemory : IReadableMemory
    { public bool TryRead(nuint address, Span<byte> destination) => false; }

    private static FieldStoryState State(int[] counts, int lap) => new(76, false)
    { Locals = counts.Select((v, i) => (v, i)).ToDictionary(p => p.i + 9, p => p.v)
        .Append(new KeyValuePair<int, int>(14, lap)).ToDictionary() };
    private static NavigationTarget Target(string id, int x) =>
        new(id, id, NavigationCategory.Objects, new(x * 256, 512, 1), [new(x * 256, 512, 1)], false, false);
}
