using ChronoTriggerAccessibility.Mod.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class NavigationJoystickTests
{
    [Theory]
    [InlineData(0x05C4)]
    [InlineData(0x09CC)]
    [InlineData(0x0CE6)]
    public void KnownRawSonyDevicesUsePhysicalCrossSquareCircleAndR3(int product)
    {
        var calls = new List<NavigationPadButtons>();
        var queries = 0;
        var input = new NavigationJoystick((_, buttons, _, _) => { calls.Add(buttons); return false; },
            _ => { queries++; return new("Wireless Controller", 0x054C, (ushort)product, 14); });
        foreach (var buttons in new uint[] { 0x800, 2, 1, 4 }) input.Filter(0, Snapshot(buttons), 0);
        Assert.Equal([NavigationPadButtons.RightStick, NavigationPadButtons.South,
            NavigationPadButtons.West, NavigationPadButtons.East], calls);
        Assert.Equal(1, queries);
        input.Filter(0, [], 167);
        input.Filter(0, Snapshot(), 0);
        Assert.Equal(2, queries);
    }

    [Theory]
    [InlineData(0x045E, 0x028E)]
    [InlineData(0x28DE, 0x11FF)]
    [InlineData(0x054C, 0xFFFF)]
    public void TranslatedAndUnknownDevicesKeepTheGamesNativeButtonOrder(int manufacturer, int product)
    {
        var calls = new List<NavigationPadButtons>();
        var input = new NavigationJoystick((_, buttons, _, _) => { calls.Add(buttons); return false; },
            _ => new("Controller", (ushort)manufacturer, (ushort)product, 10));
        foreach (var buttons in new uint[] { 0x200, 1, 4, 2 }) input.Filter(0, Snapshot(buttons), 0);
        Assert.Equal([NavigationPadButtons.RightStick, NavigationPadButtons.South,
            NavigationPadButtons.West, NavigationPadButtons.East], calls);
    }

    [Theory]
    [InlineData(0x200u, NavigationPadButtons.RightStick)]
    [InlineData(0x10u, NavigationPadButtons.LeftShoulder)]
    [InlineData(0x20u, NavigationPadButtons.RightShoulder)]
    [InlineData(1u, NavigationPadButtons.South)]
    [InlineData(2u, NavigationPadButtons.East)]
    [InlineData(4u, NavigationPadButtons.West)]
    public void PhysicalXboxButtonsAreReadBeforeGameRebinding(uint physical, NavigationPadButtons expected)
    {
        var received = NavigationPadButtons.None;
        var input = new NavigationJoystick((id, buttons, _, _) => { Assert.Equal(2u, id); received = buttons; return false; });
        var state = Snapshot(physical);
        var original = state.ToArray();
        input.Filter(2, state, 0);
        Assert.Equal(expected, received);
        Assert.Equal(original, state);
    }

    [Theory]
    [InlineData(0u, NavigationPadButtons.Up)]
    [InlineData(4500u, NavigationPadButtons.Up)]
    [InlineData(31500u, NavigationPadButtons.Up)]
    [InlineData(18000u, NavigationPadButtons.Down)]
    [InlineData(13500u, NavigationPadButtons.Down)]
    [InlineData(22500u, NavigationPadButtons.Down)]
    [InlineData(9000u, NavigationPadButtons.None)]
    [InlineData(27000u, NavigationPadButtons.None)]
    [InlineData(65535u, NavigationPadButtons.None)]
    public void PovFollowsTheGamesEightDirectionTable(uint pov, NavigationPadButtons expected)
    {
        var actual = NavigationPadButtons.None;
        new NavigationJoystick((_, buttons, _, _) => { actual = buttons; return false; }).Filter(0, Snapshot(0, pov), 0);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void MenuAndHeldClosingPressAreHiddenBeforeNativeButtonsPovAndAxisEvents()
    {
        var pad = new NavigationGamepad();
        var input = new NavigationJoystick((id, buttons, neutral, connected) =>
            pad.Filter(buttons, neutral, true, connected: connected, deviceId: id));
        input.Filter(0, Snapshot(), 0);
        var open = Snapshot(0x200); input.Filter(0, open, 0);
        Assert.Equal([NavigationPadAction.Open], pad.Poll());
        input.Filter(1, Snapshot(), 0); // another device cannot release the owner's press
        input.Filter(0, Snapshot(0x200), 0);
        Assert.Empty(pad.Poll());
        input.Filter(0, Snapshot(), 0);
        var walk = Snapshot(4, 18000); walk[2] = 65535;
        // Select only Walk, while an analog stick is deflected; a second D-pad command
        // would intentionally be ambiguous and ignored.
        walk[10] = 65535;
        input.Filter(0, walk, 0);
        Assert.Equal([NavigationPadAction.Walk], pad.Poll());
        Assert.False(pad.IsOpen);
        Assert.Equal(0u, walk[8]); Assert.Equal(0u, walk[9]);
        Assert.Equal(65535u, walk[10]); Assert.Equal(32768u, walk[2]); Assert.Equal(32768u, walk[3]);
        var held = Snapshot(4); input.Filter(0, held, 0); Assert.Equal(0u, held[8]);
        var moving = Snapshot(); moving[2] = 65535; input.Filter(0, moving, 0); Assert.Equal(32768u, moving[2]);
        input.Filter(0, Snapshot(), 0);
        var nextPress = Snapshot(1); input.Filter(0, nextPress, 0); Assert.Equal(1u, nextPress[8]);
        Assert.Empty(pad.Poll());
    }

    [Fact]
    public void FailedPollDoesNotReadOrWriteAnUninitializedSnapshot()
    {
        var calls = 0;
        var input = new NavigationJoystick((id, buttons, neutral, connected) =>
        { calls++; Assert.Equal(4u, id); Assert.False(connected); return false; });
        input.Filter(4, [], 167);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void AFailedPollCannotReleaseTheConsumedPressWhenThePadReconnects()
    {
        var pad = new NavigationGamepad();
        var input = new NavigationJoystick((id, buttons, neutral, connected) =>
            pad.Filter(buttons, neutral, true, connected: connected, deviceId: id));
        input.Filter(0, Snapshot(), 0);
        input.Filter(0, Snapshot(0x200), 0);
        input.Filter(0, Snapshot(), 0);
        input.Filter(0, Snapshot(1), 0);
        input.Filter(0, [], 167);
        var held = Snapshot(1); input.Filter(0, held, 0);
        Assert.Equal(0u, held[8]);
        held = Snapshot(1); input.Filter(0, held, 0);
        Assert.Equal(0u, held[8]);
        input.Filter(0, Snapshot(), 0);
        var fresh = Snapshot(1); input.Filter(0, fresh, 0);
        Assert.Equal(1u, fresh[8]);
    }

    [Fact]
    public void SuppressionPreservesTheSnapshotsHeaderAndReservedWords()
    {
        var state = Snapshot(1);
        state[11] = 123; state[12] = 456;
        new NavigationJoystick((_, _, _, _) => true).Filter(0, state, 0);
        Assert.Equal(52u, state[0]); Assert.Equal(255u, state[1]);
        Assert.Equal(123u, state[11]); Assert.Equal(456u, state[12]);
    }

    private static uint[] Snapshot(uint buttons = 0, uint pov = 65535) =>
        [52, 255, 32768, 32768, 32768, 32768, 32768, 32768, buttons, 0, pov, 0, 0];
}
