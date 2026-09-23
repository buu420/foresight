using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Widening an approach past the four single steps must not start handing out
/// floor the game would refuse to act from. Truce Market's own counter is covered by
/// <see cref="ShopProxyTests"/>; these are the rules that hold for any actor.</summary>
public sealed class CounterApproachTests
{
    private const int Scene = 118;

    [Fact]
    public void ALoadedActorOffersReachableStandingRoomOutsideItsBodyAndInsideConfirmRange()
    {
        var frame = Build();
        var target = Assert.Single(frame.Targets, t => t.Id == "actor:10:4:51");
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route;
        Assert.NotNull(route);
        Assert.True(FieldInteractionRange.ReachesWithin(route[^1].X, route[^1].Y, 14976, 2559, 32));
        Assert.Contains(target.ApproachPoints, p => Math.Abs(p.X - 14976) + Math.Abs(p.Y - 2559) > 272);
        Assert.All(target.ApproachPoints, p => Assert.True(FieldInteractionRange.ReachesWithin(p.X, p.Y, 14976, 2559, 32)));
    }

    [Fact]
    public void EveryOfferedApproachIsSomewhereTheGameWouldAcceptAConfirm()
    {
        var target = Assert.Single(Build().Targets, t => t.Id == "actor:8:4:49");
        Assert.NotEmpty(target.ApproachPoints);
        // Either at the shopkeeper themselves or at the counter that runs their script,
        // and in both cases with the arrival tolerance still inside the handler's range.
        Assert.All(target.ApproachPoints, p => Assert.True(
            FieldInteractionRange.ReachesWithin(p.X, p.Y, 14336, 1888, 32) ||
            FieldInteractionRange.ReachesWithin(p.X, p.Y, 14336, 2032, 32),
            $"{p} is out of confirm range of both the shopkeeper and the counter"));
    }

    [Fact]
    public void AnIsolatedTargetThreeTilesAwayAcrossWallsIsRejected()
    {
        var map = Market();
        // A one-tile pocket three tiles above the customer floor, walled off.
        map.CollisionShapes[5 * 64 + 51] = 0;
        var walled = FullGameNavigationTests.Actor(12, 51 * 256 + 128, 5 * 256 + 128) with { VisualIndex = 49 };
        var player = FullGameNavigationTests.Actor(1, 15488, 2879) with { IsPartyMember = true, ClassTag = 0 };
        var field = FullGameNavigationTests.Field(Scene) with { LeadPlayer = player, Actors = [player, walled] };
        var frame = new FieldNavigationSource(new NoMemory(), _ => { })
            .Build(field, map, new(0, 0, 16512, 3968), [], new FieldStoryState(18, false));
        var target = Assert.Single(frame.Targets, t => t.Id == "actor:12:4:49");
        Assert.Null(NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route);
    }

    private static NavigationFrame Build()
    {
        var player = FullGameNavigationTests.Actor(1, 15488, 2879) with { IsPartyMember = true, ClassTag = 0 };
        var keeper = FullGameNavigationTests.Actor(8, 14336, 1888) with { VisualIndex = 49 };
        var counter = FullGameNavigationTests.Actor(9, 14336, 2032) with { VisualIndex = 100 };
        var clerk = FullGameNavigationTests.Actor(10, 14976, 2559) with { VisualIndex = 51 };
        var field = FullGameNavigationTests.Field(Scene) with
            { LeadPlayer = player, Actors = [player, keeper, counter, clerk] };
        return new FieldNavigationSource(new NoMemory(), _ => { })
            .Build(field, Market(), new(12288, 768, 16512, 3968), [], new FieldStoryState(18, false));
    }

    /// <summary>The market's counter geometry from MapTable_0104: a staff strip on rows
    /// 6 and 7, a solid bench on row 8, and the customer floor on rows 9-12. Synthetic,
    /// not a redistributed game map.</summary>
    internal static FieldMapSnapshot Market()
    {
        var map = new FieldMapSnapshot(64, 64, Enumerable.Repeat((byte)4, 4096).ToArray(), new byte[4096],
            Enumerable.Repeat((byte)1, 4096).ToArray(), 1, false, 64, 64,
            Enumerable.Repeat((byte)128, 4096).ToArray());
        for (var x = 54; x <= 57; x++) map.CollisionShapes[6 * 64 + x] = 0;
        map.CollisionShapes[7 * 64 + 56] = 0;
        for (var y = 9; y <= 12; y++)
        for (var x = 51; x <= 60; x++) map.CollisionShapes[y * 64 + x] = 0;
        return map;
    }

    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
