using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Guardia Castle's tower stairs are one scene holding three landings that never
/// touch, joined by exits that warp back into the same scene. Treasure 235 sits on the
/// middle landing of scene 468, so a player who walked in at the bottom has no path to it
/// on this map at all, and was simply told there was no route. The flight up is the route.
/// Landing geometry: artifacts/research/castle-0324/claude.</summary>
public sealed class LandingRouteTests
{
    private const int Scene = 468;

    [Fact]
    public void AChestOnAnotherLandingRoutesToTheFlightThatReachesIt()
    {
        var frame = Build();
        var target = Assert.Single(frame.Targets, t => t.Id == "chest:235");
        // Nothing on this floor reaches it.
        Assert.Null(NavigationPathfinder.Find(new FieldNavigationGraph(Stairwell()), frame.Player, target.ApproachPoints));
        var search = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints);
        Assert.NotNull(search.Route);
        Assert.Equal("exit:0", search.IntermediateId);
    }

    [Fact]
    public void TheFlightLegEndsOnTheStairsAndNotAtTheChest()
    {
        var frame = Build();
        var target = Assert.Single(frame.Targets, t => t.Id == "chest:235");
        var search = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints);
        Assert.NotNull(search.Route);
        var end = search.Route[^1];
        Assert.Equal(9, end.X / 256);
        Assert.InRange(end.Y / 256, 39, 40);
        Assert.DoesNotContain(end, target.ApproachPoints);
    }

    [Fact]
    public void AChestOnThisLandingStillRoutesDirectly()
    {
        var frame = Build(chestTile: (11, 45));
        var target = Assert.Single(frame.Targets, t => t.Id == "chest:235");
        var search = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints);
        Assert.NotNull(search.Route);
        Assert.Null(search.IntermediateId);
        Assert.Contains(search.Route[^1], target.ApproachPoints);
    }

    [Fact]
    public void AGoalNoFlightCanReachIsNotGivenAnInventedLeg()
    {
        // A pocket walled off on every landing: no self-warp arrival reaches it.
        var frame = Build(chestTile: (30, 30));
        var target = Assert.Single(frame.Targets, t => t.Id == "chest:235");
        var search = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints);
        Assert.Null(search.Route);
        Assert.Null(search.IntermediateId);
    }

    private static NavigationFrame Build((int X, int Y)? chestTile = null)
    {
        var tile = chestTile ?? (13, 28);
        var player = FullGameNavigationTests.Actor(1, 8 * 256 + 128, 40 * 256 + 128)
            with { IsPartyMember = true, ClassTag = 0 };
        var field = FullGameNavigationTests.Field(Scene) with { LeadPlayer = player, Actors = [player] };
        var chest = new FieldTreasure(235, tile.X * 256 + 128, tile.Y * 256 + 128);
        return new FieldNavigationSource(new NoMemory(), _ => { })
            .Build(field, Stairwell(), new(0, 0, 16384, 16384), [chest], new FieldStoryState(15, false));
    }

    /// <summary>Three landings that do not touch, with scene 468's own exit cells: the
    /// pair at column 9 climbs, the pair at column 7 descends. Synthetic, not a
    /// redistributed game map.</summary>
    internal static FieldMapSnapshot Stairwell()
    {
        var map = new FieldMapSnapshot(64, 64, Enumerable.Repeat((byte)4, 4096).ToArray(), new byte[4096],
            Enumerable.Repeat((byte)1, 4096).ToArray(), 1, false, 64, 64,
            Enumerable.Repeat((byte)128, 4096).ToArray());
        foreach (var (top, bottom) in new[] { (6, 13), (24, 30), (39, 47) })
            for (var y = top; y <= bottom; y++)
            for (var x = 4; x <= 13; x++) map.CollisionShapes[y * 64 + x] = 0;
        foreach (var (id, x, y) in new[] { (0, 9, 39), (1, 9, 24), (2, 9, 6), (3, 7, 6), (4, 7, 24), (5, 7, 39) })
        {
            map.ExitCells[y * 64 + x] = (byte)id;
            map.ExitCells[(y + 1) * 64 + x] = (byte)id;
        }
        return map;
    }

    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
