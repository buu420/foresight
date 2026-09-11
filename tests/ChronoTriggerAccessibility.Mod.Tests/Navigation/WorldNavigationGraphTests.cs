using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class WorldNavigationGraphTests
{
    [Fact]
    public void OpenGroundUsesCardinalEightPixelSteps()
    {
        var graph = new WorldNavigationGraph(new byte[96 * 64], new byte[512]);
        var start = P(80, 80);
        Assert.True(graph.CanStand(start));
        Assert.Equal(new[] { P(80,72), P(80,88), P(72,80), P(88,80) }, graph.Neighbours(start));
        var route = NavigationPathfinder.Find(graph, start, [P(104,96)])!;
        Assert.Equal(6, route.Count);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public void NativePropertyMaskChecksBothFeet(int property, bool allowed)
    {
        var map = new byte[96 * 64]; var props = new byte[512];
        // Feet at pixel (32,24) sample chips (3,2) and (4,2), crossing tile boundaries.
        map[1 * 96 + 1] = 1; map[1 * 96 + 2] = 2;
        props[2] = (byte)property; // Tile 1 NE (left foot).
        Assert.Equal(allowed, new WorldNavigationGraph(map, props).CanStand(P(32,24)));
        props[2] = 0; props[4] = (byte)(property << 4); // Tile 2 NW (right foot).
        Assert.Equal(allowed, new WorldNavigationGraph(map, props).CanStand(P(32,24)));
    }

    [Fact]
    public void LowerHalfOfTileAndDetourUseLiveCollision()
    {
        var map = new byte[96 * 64]; var props = new byte[512];
        map[2 * 96 + 2] = 1; props[3] = 0x11;
        var graph = new WorldNavigationGraph(map, props);
        Assert.False(graph.CanStand(P(40,48)));
        var route = NavigationPathfinder.Find(graph, P(24,48), [P(56,48)])!;
        Assert.DoesNotContain(P(40,48), route);
        Assert.Contains(route, p => p.Y != 48 * 16);
        Assert.Null(NavigationPathfinder.Find(graph, P(24,48), [P(40,48)]));
    }

    [Fact]
    public void MidStepPositionConnectsToTheNativeLatticeWithoutDiagonalCuts()
    {
        var graph = new WorldNavigationGraph(new byte[96 * 64], new byte[512]);
        var route = NavigationPathfinder.Find(graph, P(83,80), [P(96,80)])!;
        Assert.Equal(P(83,80), route[0]);
        Assert.Equal(P(88,80), route[1]);
        Assert.All(route, p => Assert.Equal(80 * 16, p.Y));
    }

    private static NavigationPoint P(int x, int y) => new(x * 16, y * 16, 1);
}
