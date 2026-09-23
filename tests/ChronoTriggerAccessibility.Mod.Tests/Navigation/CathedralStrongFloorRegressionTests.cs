using System.Reflection;
using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Scene 131's right-hand strip is a speed-32 southward floor, so it can be
/// walked down and never up. The graph read only the shape and z-plane planes, which are
/// walkable there, so it planned straight up it and the player could not move at all.
/// These fix the three facts the reported session proves about that floor, each against
/// the live capture in cathedral-rear-live-0329.json rather than a hand-built room.
/// Evidence: docs/cathedral-dialogue-0329.md and the fixture provenance.</summary>
public sealed class CathedralStrongFloorRegressionTests
{
    /// <summary>Cols 31-33, rows 36-41: the tiles the copy gives terrain flag 0x0D.</summary>
    private static bool InStrip(NavigationPoint p) => p.X / 256 is >= 31 and <= 33 && p.Y / 256 is >= 36 and <= 41;
    private static bool ClimbsTheStrip(IReadOnlyList<NavigationPoint> route) =>
        route.Zip(route.Skip(1)).Any(e => e.Second.Y < e.First.Y && (InStrip(e.First) || InStrip(e.Second)));

    /// <summary>The four positions the player reported being stuck at, from the session log.</summary>
    public static TheoryData<int, int> ReportedStalls => new() { { 8073, 10758 }, { 8079, 10752 }, { 8050, 10744 }, { 8057, 10745 } };

    [Theory]
    [MemberData(nameof(ReportedStalls))]
    public void IgnoringTheTerrainPlaneIsWhatPlannedTheImpossibleClimb(int x, int y)
    {
        // Red: with the terrain plane discarded, exactly as the graph used to read the
        // map, the cheapest way to the top of the strip is straight up it. That is the
        // route the player was given, and pressing up against it moved them nowhere.
        var route = Route(Map() with { TerrainFlags = new byte[Map().TerrainFlags.Length] }, x, y);
        Assert.NotNull(route);
        Assert.True(ClimbsTheStrip(route), "without the terrain plane the planner climbs the southward floor");
    }

    [Theory]
    [MemberData(nameof(ReportedStalls))]
    public void TheStripIsNeverClimbedOnceTheStrongFloorIsRead(int x, int y)
    {
        // Green: the same destination stays reachable, but never by opposing the floor.
        var route = Route(Map(), x, y);
        Assert.NotNull(route);
        Assert.False(ClimbsTheStrip(route), "the route must not oppose a speed-32 floor");
    }

    [Fact]
    public void TheStripIsStillWalkableDownwardsBecauseThePlayerDidExactlyThat()
    {
        // 01:37:00 in the session log: the player descended this strip at column 33,
        // (33,34) -> (33,36) and on to row 43. Blocking a forced floor outright would
        // take away a way down the player demonstrably used.
        var graph = new FieldNavigationGraph(Map());
        var inside = new NavigationPoint(8576, 9344, 1);
        Assert.Contains(inside with { Y = 9408 }, graph.Neighbours(inside));
        Assert.DoesNotContain(inside with { Y = 9280 }, graph.Neighbours(inside));
    }

    [Fact]
    public void TheMirroredLeftStripStaysClimbableBecauseItCarriesNoForce()
    {
        // 01:42:10, plan 27: the player walked the left strip upward, (12,42) -> (12,34).
        // Both strips permit standing on layer 1, but this one has no floor force.
        // Over-reading the flag would stop a working route.
        var graph = new FieldNavigationGraph(Map());
        var inside = new NavigationPoint(3200, 10368, 1);
        Assert.Contains(inside with { Y = 10304 }, graph.Neighbours(inside));
    }

    private static IReadOnlyList<NavigationPoint>? Route(FieldMapSnapshot map, int x, int y)
    {
        var graph = new FieldNavigationGraph(map);
        Assert.True(graph.TryPosition(x, y, 1, out var start));
        Assert.True(graph.TryPosition(8064, 9344, 1, out var top));   // (31,36), the strip's far end
        return NavigationPathfinder.Find(graph, start, [top]);
    }

    private static FieldMapSnapshot Map()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.cathedral-rear-live-0329.json")!;
        using var doc = JsonDocument.Parse(stream);
        var r = doc.RootElement;
        return new(r.GetProperty("width").GetInt32(), r.GetProperty("height").GetInt32(),
            r.GetProperty("collisionShapes").GetBytesFromBase64(), r.GetProperty("terrainFlags").GetBytesFromBase64(),
            r.GetProperty("collisionLayers").GetBytesFromBase64(), 1, false,
            r.GetProperty("exitWidth").GetInt32(), r.GetProperty("exitHeight").GetInt32(),
            r.GetProperty("exitCells").GetBytesFromBase64());
    }
}
