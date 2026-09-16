using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class VehicleFlightGraphTests
{
    private const int Step = WorldNavigationGraph.Step;

    [Fact]
    public void EpochFliesOverWaterCliffsAndEveryOtherTerrainTheWalkerCannotCross()
    {
        var (map, properties) = Terrain(water: 0x11, cliff: 0x33);
        var walking = new WorldNavigationGraph(map, properties);
        var epoch = new EpochFlightGraph();
        var overWater = new NavigationPoint(20 * Step, 20 * Step, 1);
        var overCliff = new NavigationPoint(60 * Step, 20 * Step, 1);
        Assert.False(walking.CanStand(overWater));
        Assert.False(walking.CanStand(overCliff));
        Assert.True(epoch.CanFly(overWater));
        Assert.True(epoch.CanFly(overCliff));
        Assert.Equal(4, epoch.Neighbours(overWater).Count());
        var route = NavigationPathfinder.Find(epoch, new(10 * Step, 20 * Step, 1), [new(70 * Step, 20 * Step, 1)]);
        Assert.NotNull(route);
        Assert.Equal(61, route.Count);
    }

    [Fact]
    public void DactylsRefuseChipsWithBothLowPropertyBitsButCrossWater()
    {
        var (map, properties) = Terrain(water: 0x11, cliff: 0x33);
        var dactyl = new DactylFlightGraph(map, properties);
        Assert.True(dactyl.CanFly(new(20 * Step, 20 * Step, 1)));
        Assert.False(dactyl.CanFly(new(60 * Step, 20 * Step, 1)));
        // 267E40 samples columns x-1..x of rows y-1..y at the target: the first blocked
        // column is the one whose chip x-1 lies in the cliff band.
        Assert.True(dactyl.CanFly(new(49 * Step, 20 * Step, 1)));
        Assert.False(dactyl.CanFly(new(50 * Step, 20 * Step, 1)));
        Assert.False(dactyl.CanFly(new(64 * Step, 20 * Step, 1)));
        Assert.True(dactyl.CanFly(new(65 * Step, 20 * Step, 1)));
        // The cliff band spans every row, so no Dactyl route exists across it at all.
        Assert.Null(NavigationPathfinder.Find(dactyl, new(40 * Step, 20 * Step, 1), [new(70 * Step, 20 * Step, 1)], 4096));
        var along = NavigationPathfinder.Find(dactyl, new(40 * Step, 20 * Step, 1), [new(49 * Step, 60 * Step, 1)]);
        Assert.NotNull(along);
        Assert.DoesNotContain(along, p => p.X / Step is >= 50 and <= 64);
        Assert.Equal(VehicleKind.Dactyl, dactyl.Kind);
        Assert.Equal(VehicleKind.Epoch, new EpochFlightGraph().Kind);
    }

    [Fact]
    public void NeighboursNeverLeaveTheMapAndRoutesNeverCrossTheWrappingEdge()
    {
        var epoch = new EpochFlightGraph();
        var left = new NavigationPoint(0, 8 * Step, 1);
        Assert.All(epoch.Neighbours(left), p => Assert.InRange(p.X, 0, VehicleFlightGraph.Width - 1));
        Assert.Equal(3, epoch.Neighbours(left).Count());
        var right = new NavigationPoint(VehicleFlightGraph.Width - Step, 8 * Step, 1);
        Assert.Equal(3, epoch.Neighbours(right).Count());
        Assert.Empty(epoch.Neighbours(new(VehicleFlightGraph.Width, 8 * Step, 1)));
        Assert.Empty(epoch.Neighbours(new(-Step, 8 * Step, 1)));
        Assert.Empty(epoch.Neighbours(new(0, VehicleFlightGraph.Height, 1)));
        Assert.Empty(epoch.Neighbours(new(0, 0, 2)));
        // Both edges are adjacent on the native torus, but the route walks the long way.
        var route = NavigationPathfinder.Find(epoch, left, [right]);
        Assert.NotNull(route);
        Assert.Equal(VehicleFlightGraph.Width / Step, route.Count);
        Assert.All(route.Zip(route.Skip(1)), pair => Assert.Equal(Step, Math.Abs(pair.First.X - pair.Second.X) + Math.Abs(pair.First.Y - pair.Second.Y)));
    }

    [Fact]
    public void AMidSegmentPositionJoinsOnlyItsNativeCommittedEndpoint()
    {
        var end = new NavigationPoint(611 * 16, 213 * 16, 1);
        var between = new NavigationPoint(607 * 16, 209 * 16, 1);
        var epoch = new EpochFlightGraph(end, end);
        Assert.Equal([end], epoch.Neighbours(between).ToArray());
        Assert.All(epoch.Neighbours(end), p => { Assert.Equal(3 * 16, p.X % Step); Assert.Equal(5 * 16, p.Y % Step); });
        Assert.Empty(new EpochFlightGraph().Neighbours(between));
    }

    [Fact]
    public void CollisionTablesMustBeComplete()
    {
        Assert.Throws<ArgumentException>(() => new DactylFlightGraph(new byte[10], new byte[512]));
        Assert.Throws<ArgumentException>(() => new DactylFlightGraph(new byte[6144], new byte[10]));
    }

    /// <summary>Tile 1 is water (property nibble 1 on both rows), tile 2 a cliff
    /// (nibble 3). Columns 10..19 (16-px tiles) are water, 25..31 cliff, all rows.</summary>
    internal static (byte[] Map, byte[] Properties) Terrain(byte water, byte cliff)
    {
        var map = new byte[6144]; var properties = new byte[512];
        properties[2] = properties[3] = water;
        properties[4] = properties[5] = cliff;
        for (var y = 0; y < 64; y++)
        {
            for (var x = 10; x < 20; x++) map[y * 96 + x] = 1;
            for (var x = 25; x < 32; x++) map[y * 96 + x] = 2;
        }
        return (map, properties);
    }
}
