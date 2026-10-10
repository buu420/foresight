using System.Runtime.CompilerServices;
using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Holding a direction into a 45-degree wall slides the body one pixel sideways per
/// frame (1761C0 left/right, 176AE0 down, 176810 up). These installed maps join exits only
/// through that slide. Expected edges come from an independent port of those movers traced on
/// the same maps (artifacts/research/forest-maze-0351/native-audit).</summary>
public sealed class NativeCornerSlideTests
{
    private static readonly Dictionary<int, FieldMapSnapshot> Installed = Load();

    [Theory]
    [InlineData(282, 9, 2, 0, false)]
    [InlineData(282, 9, 2, 1, true)]
    [InlineData(282, 7, 46, 0, true)]
    [InlineData(282, 7, 46, 1, false)]
    [InlineData(290, 42, 59, 0, true)]
    [InlineData(196, 27, 44, 3, true)]
    [InlineData(196, 27, 60, 1, true)]
    [InlineData(262, 1, 8, 0, true)]
    [InlineData(262, 30, 11, 1, true)]
    [InlineData(264, 18, 28, 4, true)]
    public void ArrivalReachesTheExitThroughVerifiedNativeMovement(int scene, int tileX, int tileY, int exit, bool slideOnly)
    {
        var map = Installed[scene];
        var graph = new FieldNavigationGraph(map);
        Assert.True(graph.TryPosition(tileX * 256 + 128, tileY * 256 + 128, 1, out var start));
        var goals = ExitGoals(graph, map, exit, start);
        var search = NavigationPathfinder.Search(graph, start, goals);
        Assert.False(search.LimitReached);
        var route = Assert.IsAssignableFrom<IReadOnlyList<NavigationPoint>>(search.Route);
        Assert.Equal(exit, graph.ExitAt(route[^1].X, route[^1].Y));
        var routed = (FieldNavigationGraph)graph.ForGoals(goals);
        var slides = 0;
        foreach (var (from, to) in route.Zip(route.Skip(1)))
        {
            // Every node is a native standing point on its own physical layer.
            Assert.True(graph.TryPosition(to.X, to.Y, to.Layer, out var standing) && standing == to);
            Assert.Contains(to, routed.Neighbours(from));
            if (from.X == to.X || from.Y == to.Y) continue;
            slides++;
            var input = routed.InputDirection(from, to);
            var horizontal = input is NavigationDirection.East or NavigationDirection.West;
            var along = horizontal ? to.X - from.X : to.Y - from.Y;
            var across = Math.Abs(horizontal ? to.Y - from.Y : to.X - from.X);
            Assert.Equal(input is NavigationDirection.East or NavigationDirection.South ? 1 : -1, Math.Sign(along));
            Assert.InRange(Math.Abs(along), 16, 64);
            Assert.InRange(across, 16, Math.Abs(along));
        }
        if (slideOnly) Assert.NotEqual(0, slides);
    }

    [Theory]
    // Right: the foot corner meets the wall, so 1761C0 nudges up (state 2) and walks on.
    [InlineData(640, 5632, NavigationDirection.East, 704, 5616)]
    // Right: the head corner meets the wall, so 1761C0 nudges down (states 5, 7) along it.
    [InlineData(640, 5744, NavigationDirection.East, 704, 5808)]
    // Right: the nudged foot is still blocked, so the first frame only moves up (states 8, 9).
    [InlineData(656, 5648, NavigationDirection.East, 688, 5616)]
    [InlineData(128, 8208, NavigationDirection.West, 112, 8192)]
    [InlineData(128, 7776, NavigationDirection.West, 112, 7792)]
    // Up: 176810 nudges right when the left head corner is blocked, left for the right one.
    [InlineData(112, 7792, NavigationDirection.North, 160, 7744)]
    [InlineData(656, 5760, NavigationDirection.North, 640, 5696)]
    // Down: 176AE0 nudges right (state 3) or left (state 6, probing X + 0x61 by carry).
    [InlineData(112, 8192, NavigationDirection.South, 176, 8256)]
    [InlineData(656, 5616, NavigationDirection.South, 640, 5632)]
    // Two commands reach one point: the larger commanded advance wins, then horizontal input.
    [InlineData(9712, 4064, NavigationDirection.East, 9728, 4080)]
    [InlineData(9376, 6720, NavigationDirection.West, 9344, 6752)]
    public void TracedForestSlideIsAnEdgeWithItsHeldCommand(int x, int y, NavigationDirection command, int toX, int toY)
    {
        var graph = new FieldNavigationGraph(Installed[282]);
        var from = new NavigationPoint(x, y, 2);
        var to = new NavigationPoint(toX, toY, 2);
        Assert.Contains(to, graph.Neighbours(from));
        Assert.Equal(command, graph.InputDirection(from, to));
    }

    [Theory]
    // Both leading corners are blocked.
    [InlineData(282, 896, 624, 1, NavigationDirection.East)]
    [InlineData(282, 368, 7536, 2, NavigationDirection.North)]
    [InlineData(282, 240, 10480, 2, NavigationDirection.South)]
    [InlineData(282, 368, 5488, 2, NavigationDirection.West)]
    // One corner is blocked, but the nudged body still meets a wall.
    [InlineData(282, 5440, 4400, 2, NavigationDirection.East)]
    [InlineData(282, 5440, 4416, 2, NavigationDirection.East)]
    [InlineData(282, 880, 7008, 1, NavigationDirection.North)]
    [InlineData(282, 3840, 9856, 2, NavigationDirection.South)]
    [InlineData(282, 992, 6768, 2, NavigationDirection.West)]
    // The map edge is not a slide surface: native tile indices wrap there.
    [InlineData(282, 112, 10480, 2, NavigationDirection.South)]
    // Clear corners, but 178FF0 refuses the foot on another physical layer.
    [InlineData(282, 1216, 7440, 1, NavigationDirection.East)]
    // A sideways nudge without forward progress is not that command's edge.
    [InlineData(282, 384, 7280, 1, NavigationDirection.North)]
    [InlineData(282, 368, 8464, 2, NavigationDirection.West)]
    [InlineData(290, 9824, 13472, 1, NavigationDirection.North)]
    // A floor push would change the slide; none is claimed on moving floors.
    [InlineData(262, 1264, 2288, 1, NavigationDirection.South)]
    public void RefusedNativeFrameGivesThatCommandNoEdge(int scene, int x, int y, int layer, NavigationDirection command)
    {
        var graph = new FieldNavigationGraph(Installed[scene]);
        var from = new NavigationPoint(x, y, layer);
        Assert.True(graph.TryPosition(x, y, layer, out var standing) && standing == from);
        Assert.DoesNotContain(graph.Neighbours(from), to => graph.InputDirection(from, to) == command);
    }

    [Fact]
    public void ASlideOntoAnotherPhysicalLayerIsRefusedWithItsFrame()
    {
        // The foot corner meets the solid tile at (2,1), so 1761C0 nudges the body up
        // into tile (1,0). 178FF0 commits that foot with the stored layer.
        var map = Grid(3, 3);
        map.CollisionLayers[5] = 0;
        var from = new NavigationPoint(400, 256, 1);
        var open = new FieldNavigationGraph(map);
        var slide = new NavigationPoint(448, 240, 1);
        Assert.Contains(slide, open.Neighbours(from));
        Assert.Equal(NavigationDirection.East, open.InputDirection(from, slide));

        var layered = Grid(3, 3);
        layered.CollisionLayers[5] = 0;
        layered.CollisionLayers[1] = 2;
        var graph = new FieldNavigationGraph(layered);
        Assert.DoesNotContain(graph.Neighbours(from), to => to.X > from.X);
    }

    [Fact]
    public void ActorAheadStillBlocksTheSlideUnlessItIsTheSelectedTouch()
    {
        var map = Installed[282];
        var from = new NavigationPoint(640, 5744, 2);
        var slide = new NavigationPoint(704, 5808, 2);
        // 178980 tests the unslid probe (X + 16 + 0x70, Y - 0x40) before any nudge.
        var actor = FullGameNavigationTests.Actor(10, 769, 5681);
        var rules = new FieldActorCollisionRules(FullGameNavigationTests.Field(282, actor));
        var graph = new FieldNavigationGraph(map, rules, [(10, [slide])]);
        Assert.DoesNotContain(graph.Neighbours(from), to => graph.InputDirection(from, to) == NavigationDirection.East);

        var touching = (FieldNavigationGraph)graph.ForGoals([slide]);
        Assert.Contains(slide, touching.Neighbours(from));
        Assert.Equal(NavigationDirection.East, touching.InputDirection(from, slide));
    }

    private static NavigationPoint[] ExitGoals(FieldNavigationGraph graph, FieldMapSnapshot map, int exit, NavigationPoint player)
    {
        // FieldNavigationSource.ExitApproach: lattice nodes and pixel standing rows of every exit tile.
        int[] offsets = [0, 64, 112, 128, 192, 240];
        var goals = new List<NavigationPoint>();
        for (var y = 0; y < map.ExitHeight; y++)
        for (var x = 0; x < map.ExitWidth; x++)
        {
            if (map.ExitCells[y * map.ExitWidth + x] != exit) continue;
            foreach (var dx in offsets)
            foreach (var dy in offsets)
            for (var layer = 1; layer <= 3; layer++)
                if (graph.TryPosition(x * 256 + dx, y * 256 + dy, layer, out var point)) goals.Add(point);
        }
        return goals.Distinct().OrderBy(p => Math.Abs((double)p.X - player.X) + Math.Abs((double)p.Y - player.Y))
            .Take(64).ToArray();
    }

    private static FieldMapSnapshot Grid(int width, int height) => new(width, height, new byte[width * height],
        new byte[width * height], Enumerable.Repeat((byte)1, width * height).ToArray(), 1, false,
        width, height, Enumerable.Repeat((byte)128, width * height).ToArray());

    private static Dictionary<int, FieldMapSnapshot> Load([CallerFilePath] string source = "")
    {
        using var json = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(Path.GetDirectoryName(source)!, "native-slide-maps-0351.json")));
        var maps = new Dictionary<int, FieldMapSnapshot>();
        foreach (var entry in json.RootElement.GetProperty("Maps").EnumerateArray())
        {
            var m = entry.GetProperty("Map");
            byte[] Plane(string name) => Convert.FromBase64String(m.GetProperty(name).GetString()!);
            maps[entry.GetProperty("Scene").GetInt32()] = new(m.GetProperty("Width").GetInt32(),
                m.GetProperty("Height").GetInt32(), Plane("CollisionShapes"), Plane("TerrainFlags"),
                Plane("CollisionLayers"), 1, false, m.GetProperty("ExitWidth").GetInt32(),
                m.GetProperty("ExitHeight").GetInt32(), Plane("ExitCells"));
        }
        return maps;
    }
}
