using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class InteractionPositionTests
{
    [Fact]
    public void CoordinateRequirementDoesNotHideAnOtherwiseAvailableInteraction()
    {
        var action = new GameNavigationCatalog.Action("Terrain",
            [new("X", 6, 2, 17, false), new("Global", 255, 6, 4, false)], false);
        var state = new FieldStoryState(21, true)
        { Locals = new Dictionary<int, int> { [6] = 30 }, Globals = new Dictionary<int, int> { [255] = 2 } };
        Assert.True(action.Available(state));
        Assert.False(action.Available(state with { Globals = new Dictionary<int, int> { [255] = 6 } }));
    }

    [Theory]
    [InlineData("X")]
    [InlineData("Y")]
    public void ArrivalToleranceMustStayOnThePermittedSide(string axis)
    {
        var action = new GameNavigationCatalog.Action("Terrain", [new(axis, 6, 2, 17, false)], false);
        Assert.True(action.AcceptsPosition(17 * 256 + 128, 17 * 256 + 128, 32));
        Assert.False(action.AcceptsPosition(18 * 256 - 16, 18 * 256 - 16, 32));
        Assert.False(action.AcceptsPosition(18 * 256, 18 * 256, 32));
    }
}
