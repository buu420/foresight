using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class FieldRegionIndexTests
{
    [Fact]
    public void SolidWallSeparatesGuideTargetsButAChangedDoorReconnectsThem()
    {
        var map = Map(5, 3);
        for (var y = 0; y < 3; y++) map.CollisionLayers[y * 5 + 2] = 0;
        var index = new FieldRegionIndex(); index.Update(map);
        Assert.False(index.Connected(new(128, 384, 1), new(1152, 384, 1)));
        map.CollisionLayers[7] = 1;
        index.Update(map);
        Assert.True(index.Connected(new(128, 384, 1), new(1152, 384, 1)));
    }

    [Fact]
    public void CrossingBridgeConnectsPhysicalLayersButNeutralFloorDoesNotJoinThem()
    {
        var map = Map(3, 1); map.CollisionLayers[1] = 4; map.CollisionLayers[2] = 2;
        var index = new FieldRegionIndex(); index.Update(map);
        Assert.False(index.Connected(new(128, 128, 1), new(640, 128, 2)));
        map.CollisionLayers[1] = 3; index.Update(map);
        Assert.True(index.Connected(new(128, 128, 1), new(640, 128, 2)));
    }

    [Fact]
    public void UnusedAlternateBitsDoNotCreateFloorThroughSolidTiles()
    {
        var map = Map(3, 1); map.CollisionLayers[1] = 8;
        var index = new FieldRegionIndex(); index.Update(map);
        Assert.False(index.Connected(new(128, 128, 1), new(640, 128, 1)));
        map.CollisionShapes[1] = 4; index.Update(map);
        Assert.True(index.Connected(new(128, 128, 1), new(640, 128, 1)));
    }

    [Fact]
    public void CoarseProofIsPermissiveAboutSplitTilesAndOneWayFloor()
    {
        var map = Map(3, 1); map.CollisionShapes[1] = 24 * 4; map.CollisionLayers[1] = 1 | (1 << 3);
        map.TerrainFlags[1] = 0xFF;
        var index = new FieldRegionIndex(); index.Update(map);
        Assert.True(index.Connected(new(128, 128, 1), new(640, 128, 1)));
    }

    [Fact]
    public void InvalidStartOrTargetHasNoDisconnectionProof()
    {
        var map = Map(3, 1); map.CollisionLayers[1] = 0;
        var index = new FieldRegionIndex(); index.Update(map);
        Assert.Null(index.Connected(new(128, 128, 1), new(384, 128, 1)));
        Assert.Null(index.Connected(new(-1, 128, 1), new(640, 128, 1)));
        Assert.Null(index.Connected(new(128, 128, 0), new(640, 128, 1)));
    }

    private static FieldMapSnapshot Map(int width, int height) => new(width, height,
        new byte[width * height], new byte[width * height], Enumerable.Repeat((byte)1, width * height).ToArray(),
        1, false, width, height, Enumerable.Repeat((byte)128, width * height).ToArray());
}
