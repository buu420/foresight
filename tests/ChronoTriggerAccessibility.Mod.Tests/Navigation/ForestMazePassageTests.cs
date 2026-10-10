using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using Xunit;
using System.Reflection;
using System.Text.Json;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class ForestMazePassageTests
{
    [Fact]
    public void MazeDoorwaysWithTheSameWorldDestinationHaveDistinctLabels()
    {
        Assert.NotEqual(GameNavigationCatalog.ExitLabel(282, 0), GameNavigationCatalog.ExitLabel(282, 1));
    }

    [Fact]
    public void InstalledWorldTerrainSeparatesTheTwoMazeArrivals()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.prehistoric-world-map-0351.json")!;
        var terrain = JsonSerializer.Deserialize<WorldTerrain>(stream)!;
        var graph = new WorldNavigationGraph(terrain.Map, terrain.Properties);
        // World3's native Reptite Lair entrance occupies tiles33/34,52.
        var goals = (from x in Enumerable.Range(66, 4)
                     from y in Enumerable.Range(104, 2)
                     let point = new NavigationPoint(x * 128, y * 128, 1)
                     where graph.CanStand(point) select point).ToArray();
        Assert.NotEmpty(goals);
        Assert.Null(NavigationPathfinder.Find(graph, new(68 * 128, 88 * 128, 1), goals));
        Assert.NotNull(NavigationPathfinder.Find(graph, new(68 * 128, 95 * 128, 1), goals));
    }

    [Fact]
    public void LairGuidanceUsesTheSouthernArrivalInsteadOfTheNearbyEntrance()
    {
        var result = new SceneConnectionRouter().Next(282, new(123, false), [289], [Target(0), Target(1)]);
        Assert.Equal("exit:1", Assert.Single(result).Id);
    }

    [Fact]
    public void ReturningToIokaUsesTheNorthernArrival()
    {
        var result = new SceneConnectionRouter().Next(282, new(125, false), [274], [Target(0), Target(1)]);
        Assert.Equal("exit:0", Assert.Single(result).Id);
    }

    [Fact]
    public void MissingForwardExitDoesNotTurnTheEntranceIntoAForwardRoute()
    {
        Assert.Empty(new SceneConnectionRouter().Next(282, new(123, false), [289], [Target(0)]));
    }

    [Fact]
    public void AChangedLiveDestinationTakesPrecedenceOverTheAuditedArrival()
    {
        var result = new SceneConnectionRouter().Next(282, new(123, false), [289], [Target(0), Target(1)],
            liveFieldDestinations: new Dictionary<int, int> { [0] = 283, [1] = 1023 });
        Assert.Equal("exit:0", Assert.Single(result).Id);
    }

    private static NavigationTarget Target(int id) => new($"exit:{id}", $"Exit {id}", NavigationCategory.Exits,
        new(2304, id == 0 ? 496 : 12032, 1), [], true, true);
    private sealed record WorldTerrain(byte[] Map, byte[] Properties);
}
