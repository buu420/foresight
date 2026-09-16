using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class VehicleContactGeometryTests
{
    // Expected boundaries were independently replayed through installed x86
    //264530/264690, including asymmetric opposing sprite extents.
    [Theory]
    [InlineData(17, 0, true)]
    [InlineData(18, 0, false)]
    [InlineData(-19, 0, true)]
    [InlineData(-20, 0, false)]
    [InlineData(0, 29, true)]
    [InlineData(0, 30, false)]
    [InlineData(0, -29, true)]
    [InlineData(0, -30, false)]
    public void OpposingExtentsHaveTheSameStrictBoundaryAsTheNativeRoutines(int dx, int dy, bool expected) =>
        Assert.Equal(expected, VehicleContactGeometry.Overlaps(400, 300, new(3, 5, 7, 11),
            new(400 + dx, 300 + dy), new(13, 17, 19, 23)));

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(23, 23, true)]
    [InlineData(-23, -23, true)]
    [InlineData(24, 0, false)]
    [InlineData(-24, 0, false)]
    [InlineData(0, 24, false)]
    [InlineData(0, -24, false)]
    public void BlackOmenPromptMatchesNativeDirectAndNeighbourContact(int dx, int dy, bool expected) =>
        Assert.Equal(expected, VehicleContactGeometry.BlackOmenContact(400 + dx, 300 + dy,
            new(8, 8, 8, 8), new(400, 300, 0xD30, new(8, 8, 8, 8), false)));
}
