using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class GeneralLevelCrossingTests
{
    [Theory]
    [InlineData(282)]
    [InlineData(290)]
    [InlineData(196)]
    [InlineData(262)]
    [InlineData(264)]
    public void OrdinaryCrossingsAreIdentifiableOutsideDenadoro(int scene)
    {
        var targets = new FieldLevelCrossings().Find(scene, Map());
        var crossing = Assert.Single(targets);
        Assert.Equal(NavigationCategory.Objects, crossing.Category);
        Assert.NotEmpty(crossing.ApproachPoints);
        Assert.All(crossing.ApproachPoints, p => Assert.Equal(3, p.Layer));
        Assert.NotNull(NavigationPathfinder.Find(new FieldNavigationGraph(Map()),
            new(128, 384, 1), crossing.ApproachPoints));
    }

    [Fact]
    public void ALayerThreeTileAloneDoesNotInventACrossing()
    {
        var map = Map();
        Array.Fill(map.CollisionLayers, (byte)1);
        map.CollisionLayers[4] = 3;
        Assert.Empty(new FieldLevelCrossings().Find(282, map));
    }

    private static FieldMapSnapshot Map() => new(3, 3, new byte[9], new byte[9],
        [1, 3, 2, 1, 3, 2, 1, 3, 2], 1, false, 3, 3, Enumerable.Repeat((byte)128, 9).ToArray());
}
