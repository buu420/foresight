using System.Diagnostics;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;
using Xunit.Abstractions;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Building a frame runs at capture rate, so widening an approach must not cost
/// a map walk. An earlier attempt decided reachability by flooding the graph and took
/// 107 ms per build on an open map; these pin both shapes it has to stay cheap on.</summary>
public sealed class ApproachCostTests(ITestOutputHelper output)
{
    [Fact]
    public void BuildingAFrameThatWidensAFencedApproachStaysCheap()
    {
        var map = OpenMap();
        // A one-tile pocket: fewer than four steps are open, so the confirm-range scan
        // runs on every build.
        map.CollisionShapes[0] = 0;
        var sealedActor = FullGameNavigationTests.Actor(8, 128, 128) with { VisualIndex = 49 };
        var player = FullGameNavigationTests.Actor(1, 32 * 256 + 128, 32 * 256 + 128)
            with { IsPartyMember = true, ClassTag = 0 };
        var field = FullGameNavigationTests.Field(118) with
            { LeadPlayer = player, Actors = [player, sealedActor] };
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var story = new FieldStoryState(18, false);
        var viewport = new FieldViewport(0, 0, 16384, 16384);

        source.Build(field, map, viewport, [], story);        // warm the JIT
        var clock = Stopwatch.StartNew();
        const int builds = 20;
        for (var i = 0; i < builds; i++) source.Build(field, map, viewport, [], story);
        clock.Stop();

        var each = clock.Elapsed.TotalMilliseconds / builds;
        output.WriteLine($"Build widening a fenced approach: {each:F2} ms");
        Assert.True(each < 50, $"a frame build took {each:F2} ms");
    }

    [Fact]
    public void AFrameOfOrdinaryOpenFloorActorsStaysCheap()
    {
        var map = OpenMap();
        for (var y = 1; y < 63; y++)
        for (var x = 1; x < 63; x++) map.CollisionShapes[y * 64 + x] = 0;
        var ordinary = FullGameNavigationTests.Actor(8, 20 * 256 + 128, 20 * 256 + 128) with { VisualIndex = 49 };
        var player = FullGameNavigationTests.Actor(1, 32 * 256 + 128, 32 * 256 + 128)
            with { IsPartyMember = true, ClassTag = 0 };
        var field = FullGameNavigationTests.Field(118) with
            { LeadPlayer = player, Actors = [player, ordinary] };
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var story = new FieldStoryState(18, false);
        var viewport = new FieldViewport(0, 0, 16384, 16384);

        source.Build(field, map, viewport, [], story);
        var clock = Stopwatch.StartNew();
        const int builds = 50;
        for (var i = 0; i < builds; i++) source.Build(field, map, viewport, [], story);
        clock.Stop();

        var each = clock.Elapsed.TotalMilliseconds / builds;
        output.WriteLine($"Build with open-floor actors only: {each:F2} ms");
        Assert.True(each < 5, $"an ordinary frame build took {each:F2} ms");
    }

    private static FieldMapSnapshot OpenMap() => new(64, 64,
        Enumerable.Repeat((byte)4, 4096).ToArray(), new byte[4096],
        Enumerable.Repeat((byte)1, 4096).ToArray(), 1, false, 64, 64,
        Enumerable.Repeat((byte)128, 4096).ToArray());

    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
