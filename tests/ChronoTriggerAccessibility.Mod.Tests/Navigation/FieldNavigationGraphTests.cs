using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class FieldNavigationGraphTests
{
    [Fact]
    public void PlayerBodyCannotClipTheWallAboveAnOtherwiseWalkableFootPoint()
    {
        var map = Map(3, 2); map.CollisionLayers[1] = 0;
        var graph = new FieldNavigationGraph(map);
        var foot = new NavigationPoint(320, 256, 1);
        Assert.True(graph.TryPosition(384, 256, 1, out _));
        Assert.DoesNotContain(new NavigationPoint(384, 256, 1), graph.Neighbours(foot));
        Assert.Contains(new NavigationPoint(384, 384, 1), graph.Neighbours(new(320, 384, 1)));
    }

    [Fact]
    public void FineCoordinatesJoinTheGridWithoutLosingTheNativeFootPosition()
    {
        var graph = new FieldNavigationGraph(Map(2, 2));
        var start = new NavigationPoint(128, 255, 1);
        var path = NavigationPathfinder.Find(graph, start, [new(320, 320, 1)]);
        Assert.NotNull(path);
        Assert.Equal(start, path[0]);
        Assert.Equal(new(320, 320, 1), path[^1]);
        foreach (var (a, b) in path.Zip(path.Skip(1)))
        {
            Assert.True(a.X == b.X || a.Y == b.Y);
            Assert.InRange(Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y), 1, 64);
        }
    }

    [Fact]
    public void BlockedTileIsAvoidedAndOtherExitsCannotBeUsedAsThroughRoutes()
    {
        var map = Map(3, 2);
        map.CollisionLayers[1] = 0;
        var graph = new FieldNavigationGraph(map);
        var path = NavigationPathfinder.Find(graph, new(128, 128, 1), [new(640, 128, 1)]);
        Assert.NotNull(path);
        Assert.DoesNotContain(path, p => p.X / 256 == 1 && p.Y / 256 == 0);
        map.ExitCells[4] = 0;
        Assert.Null(NavigationPathfinder.Find(graph, new(128, 128, 1), [new(640, 128, 1)]));
        Assert.Null(NavigationPathfinder.Find(graph, new(128, 128, 1), [new(256, 256, 1)]));
        Assert.NotNull(NavigationPathfinder.Find(graph, new(128, 128, 1), [new(384, 384, 1)]));
    }

    [Fact]
    public void PhysicalLayersRequireNativeTransitionTiles()
    {
        var map = Map(3, 1);
        map.CollisionLayers[2] = 2;
        var graph = new FieldNavigationGraph(map);
        Assert.Null(NavigationPathfinder.Find(graph, new(128, 128, 1), [new(640, 128, 2)]));
        map.CollisionLayers[1] = 3;
        var path = NavigationPathfinder.Find(graph, new(128, 128, 1), [new(640, 128, 2)]);
        Assert.NotNull(path);
        Assert.Contains(path, p => p.Layer == 3);
    }

    private static FieldMapSnapshot Map(int width, int height) => new(width, height, new byte[width * height],
        new byte[width * height], Enumerable.Repeat((byte)1, width * height).ToArray(), 1, false,
        width, height, Enumerable.Repeat((byte)128, width * height).ToArray());
}
