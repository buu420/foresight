using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class VehicleLandingRulesTests
{
    private const int Step = WorldNavigationGraph.Step;

    [Fact]
    public void LandingNeedsAllSixSampledChipsClearNotOnlyTheWalkingPair()
    {
        var map = new byte[6144]; var properties = new byte[512];
        // Tile 1 has property 4 (walkable entrance ground) on its lower chip row only.
        properties[2] = 0x00; properties[3] = 0x44;
        map[30 * 96 + 40] = 1; // 16-px tile (40,30): chips x 80..81, y 60..61; row 61 carries the 4s.
        var walking = new WorldNavigationGraph(map, properties);
        var onEntranceRow = new NavigationPoint(80 * Step, 61 * Step, 1);
        Assert.True(walking.CanStand(onEntranceRow));
        Assert.False(VehicleLandingRules.ChipsClear(map, properties, 80 * 8, 61 * 8));
        Assert.False(VehicleLandingRules.ChipsClear(map, properties, 80 * 8, 60 * 8));
        Assert.False(VehicleLandingRules.ChipsClear(map, properties, 80 * 8, 62 * 8));
        Assert.True(VehicleLandingRules.ChipsClear(map, properties, 80 * 8, 63 * 8));
        Assert.True(VehicleLandingRules.ChipsClear(map, properties, 83 * 8, 61 * 8));
        Assert.False(VehicleLandingRules.ChipsClear(map, properties, 82 * 8, 61 * 8));
    }

    [Theory]
    [InlineData(VehicleKind.Epoch, 0, 0x41 * 8, 0x64 * 8, true)]
    [InlineData(VehicleKind.Epoch, 0, 0x40 * 8, 0x64 * 8, false)]
    [InlineData(VehicleKind.Epoch, 0, 0x71 * 8, 0x7E * 8, true)]
    [InlineData(VehicleKind.Epoch, 0, 0x72 * 8, 0x7E * 8, false)]
    [InlineData(VehicleKind.Epoch, 0, 0x50 * 8, 0x63 * 8, false)]
    [InlineData(VehicleKind.Epoch, 1, 0x60 * 8, 0x76 * 8, true)]
    [InlineData(VehicleKind.Epoch, 1, 0x6E * 8, 0x76 * 8, false)]
    [InlineData(VehicleKind.Epoch, 2, 0x50 * 8, 0x70 * 8, false)]
    [InlineData(VehicleKind.Epoch, 3, 0x0D * 8, 0x33 * 8, true)]
    [InlineData(VehicleKind.Epoch, 3, 0x0E * 8, 0x33 * 8, false)]
    [InlineData(VehicleKind.Epoch, 4, 0x1F * 8, 0x60 * 8, true)]
    [InlineData(VehicleKind.Epoch, 4, 0x1F * 8, 0x5F * 8, false)]
    [InlineData(VehicleKind.Epoch, 5, 0x00, 0x60 * 8, false)]
    [InlineData(VehicleKind.Dactyl, 3, 0x0D * 8, 0x33 * 8, true)]
    [InlineData(VehicleKind.Dactyl, 3, 0x0D * 8, 0x34 * 8, false)]
    public void ForbiddenRectanglesFollow286FE0PerEra(VehicleKind kind, int world, int px, int py, bool forbidden)
    {
        Assert.Equal(forbidden, VehicleLandingRules.InsideForbiddenRectangle(kind, world, px, py));
    }

    [Fact]
    public void CanLandCombinesTerrainRectanglesEraAndTheOtherVehicle()
    {
        var map = new byte[6144]; var properties = new byte[512];
        Assert.True(VehicleLandingRules.CanLand(VehicleKind.Epoch, 2, map, properties, 400, 300));
        Assert.False(VehicleLandingRules.CanLand(VehicleKind.Epoch, 0, map, properties, 0x50 * 8, 0x70 * 8));
        Assert.True(VehicleLandingRules.CanLand(VehicleKind.Epoch, 0, map, properties, 0x50 * 8, 0x70 * 8 - 8 * 13));
        Assert.False(VehicleLandingRules.CanLand(VehicleKind.Dactyl, 0, map, properties, 400, 300));
        Assert.True(VehicleLandingRules.CanLand(VehicleKind.Dactyl, 3, map, properties, 400, 300));
        var shape = new VehicleContactShape(8, 8, 8, 8);
        Assert.False(VehicleLandingRules.CanLand(VehicleKind.Dactyl, 3, map, properties, 400, 300, new WorldPixelPoint(415, 296), shape, shape));
        Assert.True(VehicleLandingRules.CanLand(VehicleKind.Dactyl, 3, map, properties, 400, 300, new WorldPixelPoint(416, 296), shape, shape));
        Assert.False(VehicleLandingRules.CanLand(VehicleKind.Dactyl, 3, map, properties, 400, 300, new WorldPixelPoint(440, 296)));
        Assert.False(VehicleLandingRules.CanLand(VehicleKind.Epoch, 2, map, properties, 1536, 300));
        Assert.False(VehicleLandingRules.CanLand(VehicleKind.Epoch, 2, map, properties, new NavigationPoint(400 * 16 + 8, 300 * 16, 1)));
        Assert.True(VehicleLandingRules.CanLand(VehicleKind.Epoch, 2, map, properties, new NavigationPoint(400 * 16, 300 * 16, 1)));
    }

    [Fact]
    public void LandingSitesAreWalkConnectedToTheEntranceAndNeverAcrossWater()
    {
        var (map, properties) = VehicleFlightGraphTests.Terrain(water: 0x11, cliff: 0x33);
        // The entrance tile carries property 4 on its top chip row, so the door itself is not landable.
        properties[6] = 0x44; properties[7] = 0x00;
        map[20 * 96 + 5] = 3; // 16-px tile (5,20): chips x 10..11, y 40..41
        var walking = new WorldNavigationGraph(map, properties);
        var contacts = new[] { new NavigationPoint(10 * Step, 41 * Step, 1), new NavigationPoint(11 * Step, 41 * Step, 1) };
        var predicate = VehicleLandingSites.Predicate(VehicleKind.Epoch, 2, map, properties, null);
        var sites = VehicleLandingSites.Near(contacts, walking, predicate);
        Assert.NotEmpty(sites);
        Assert.All(sites, s => Assert.True(walking.CanStand(s)));
        Assert.All(sites, s => Assert.True(predicate(s)));
        Assert.DoesNotContain(sites, s => s.X / Step >= 20); // the water band starts at chip 20
        Assert.InRange(sites.Count, 1, VehicleLandingSites.MaximumSites);
        // A door surrounded by water has no landing site at all.
        for (var y = 0; y < 64; y++) for (var x = 0; x < 10; x++) map[y * 96 + x] = 1;
        map[20 * 96 + 5] = 3;
        var island = new WorldNavigationGraph(map, properties);
        Assert.Empty(VehicleLandingSites.Near(contacts, island, predicate));
        Assert.Empty(VehicleLandingSites.Near([new(10 * Step + 4, 41 * Step, 1)], walking, predicate));
    }

    [Fact]
    public void ALongUnlandableApproachDoesNotHideAReachableEntrance()
    {
        var map = new byte[6144]; var properties = new byte[512];
        // A 16-tile-wide area is walkable but cannot be landed upon. The first legal
        // site is 16+ eight-pixel steps from the contact, beyond the old 12-step cap.
        properties[2] = properties[3] = 0x44;
        for (var y = 10; y < 27; y++) for (var x = 10; x < 27; x++) map[y * 96 + x] = 1;
        var walking = new WorldNavigationGraph(map, properties);
        var sites = VehicleLandingSites.Near([new(36 * Step, 36 * Step, 1)], walking,
            VehicleLandingSites.Predicate(VehicleKind.Epoch, 2, map, properties, null));
        Assert.NotEmpty(sites);
        Assert.All(sites, p => Assert.True(Math.Abs(p.X / Step - 36) + Math.Abs(p.Y / Step - 36) > 12));
    }
}
