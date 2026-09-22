using System.Reflection;
using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Manoria Cathedral, scene 130, against the installed map's own collision planes
/// rather than a hand-built room. Its doorways are one tile wide, and the leading-corner
/// probes leave the cell's boundary node as the only one the player can occupy — the exact
/// node the exit approach used to skip, which made the goal set disjoint from the reachable
/// set and reported no route to five of the six doors.
/// Diagnosis: artifacts/research/cathedral-tech-0328/cathedral-audit.md.</summary>
public sealed class CathedralExitRouteTests
{
    private const int Scene = 130;
    // The two positions the player reported the failure from.
    public static TheoryData<int, int> ReportedPositions => new() { { 10368, 6139 }, { 10368, 3291 } };

    [Theory]
    [MemberData(nameof(ReportedPositions))]
    public void TheInnerCathedralDoorRoutesFromWhereThePlayerStood(int x, int y)
    {
        var frame = Build(x, y);
        var target = Assert.Single(frame.Targets, t => t.Id == "exit:5");
        Assert.NotEmpty(target.ApproachPoints);
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route;
        Assert.NotNull(route);
    }

    [Theory]
    [InlineData("exit:0")]
    [InlineData("exit:1")]
    [InlineData("exit:2")]
    [InlineData("exit:5")]
    public void EveryDoorTheCathedralFloorCanReachIsRoutable(string id)
    {
        var frame = Build(10368, 6139);
        var target = Assert.Single(frame.Targets, t => t.Id == id);
        Assert.NotNull(NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route);
    }

    [Fact]
    public void TheWalledOffDoorStillHasNoRoute()
    {
        // Exit 3's cells are not in the reachable set at all; widening the goals
        // must not invent a way in.
        var frame = Build(10368, 6139);
        var target = Assert.Single(frame.Targets, t => t.Id == "exit:3");
        Assert.Null(NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route);
    }

    [Fact]
    public void TheWideChapelDoorKeepsRouting()
    {
        var frame = Build(10368, 6139);
        var target = Assert.Single(frame.Targets, t => t.Id == "exit:4");
        Assert.NotNull(NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route);
    }

    [Fact]
    public void EveryOfferedApproachLiesInsideItsOwnNativeExitCell()
    {
        var map = Map();
        var graph = new FieldNavigationGraph(map);
        var frame = Build(10368, 6139);
        foreach (var id in new[] { 0, 1, 2, 3, 4, 5 })
        {
            var target = Assert.Single(frame.Targets, t => t.Id == $"exit:{id}");
            Assert.All(target.ApproachPoints, p => Assert.Equal(id, graph.ExitAt(p.X, p.Y)));
        }
    }

    [Fact]
    public void TheRouteEndsInsideTheCellSoTheGameWillActuallyWarp()
    {
        var map = Map();
        var graph = new FieldNavigationGraph(map);
        var frame = Build(10368, 6139);
        var target = Assert.Single(frame.Targets, t => t.Id == "exit:5");
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route;
        Assert.NotNull(route);
        Assert.Equal(5, graph.ExitAt(route[^1].X, route[^1].Y));
    }

    private static NavigationFrame Build(int x, int y)
    {
        var player = FullGameNavigationTests.Actor(1, x, y) with { IsPartyMember = true, ClassTag = 0 };
        var field = FullGameNavigationTests.Field(Scene) with { LeadPlayer = player, Actors = [player] };
        return new FieldNavigationSource(new NoMemory(), _ => { })
            .Build(field, Map(), new(0, 0, 16384, 16384), [], new FieldStoryState(21, false));
    }

    /// <summary>The installed scene 130 planes. See the fixture's provenance field.</summary>
    internal static FieldMapSnapshot Map()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("ChronoTriggerAccessibility.Mod.Tests.Navigation.cathedral-map-0328.json")
            ?? throw new InvalidOperationException("cathedral-map-0328.json is not embedded.");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var width = root.GetProperty("width").GetInt32();
        var height = root.GetProperty("height").GetInt32();
        var shapes = root.GetProperty("collisionShapes").GetBytesFromBase64();
        var layers = root.GetProperty("collisionLayers").GetBytesFromBase64();
        var cells = root.GetProperty("exitCells").GetBytesFromBase64();
        return new(width, height, shapes, new byte[shapes.Length], layers, 1, false, width, height, cells);
    }

    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
