using ChronoTriggerAccessibility.Mod.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class NavigationGamepadTests
{
    [Fact]
    public void ASecondDeviceCannotRepeatOrLeakTheOwnersClosingPress()
    {
        var pad = new NavigationGamepad();
        pad.Filter(0, true, true, deviceId: 0);
        pad.Filter(0, true, true, deviceId: 1);
        Assert.True(pad.Filter(NavigationPadButtons.RightStick, false, true, deviceId: 0));
        Assert.True(pad.Filter(NavigationPadButtons.RightStick, false, true, deviceId: 1));
        Assert.Equal([NavigationPadAction.Open], pad.Poll());
        pad.Filter(0, true, true, deviceId: 0);
        pad.Filter(0, true, true, deviceId: 1);
        Assert.True(pad.Filter(NavigationPadButtons.West, false, true, deviceId: 0));
        Assert.True(pad.Filter(NavigationPadButtons.West, false, true, deviceId: 1));
        Assert.Equal([NavigationPadAction.Walk], pad.Poll());
        pad.Filter(0, true, true, deviceId: 0);
        Assert.True(pad.Filter(NavigationPadButtons.West, false, true, deviceId: 1));
        Assert.True(pad.OwnsDevice(0));
    }

    [Fact]
    public void RightStickOpensOnceAndConsumesAllControllerInputWhileBrowsing()
    {
        var pad = Ready();
        Assert.True(pad.Filter((NavigationPadButtons)0x0080, false, true));
        Assert.True(pad.IsOpen);
        Assert.Equal(NavigationPadAction.Open, Assert.Single(pad.Poll()));
        Assert.True(pad.Filter((NavigationPadButtons)0x0080, false, true));
        Assert.Empty(pad.Poll());
        Assert.True(pad.Filter(0, true, true));
        // Left stick motion, triggers and unrelated face buttons must not move Crono
        // or open a game menu while this menu owns the controller.
        Assert.True(pad.Filter(0, false, true));
        Assert.Empty(pad.Poll());
    }

    [Theory]
    [InlineData(0x0100, NavigationPadAction.PreviousCategory)]
    [InlineData(0x0200, NavigationPadAction.NextCategory)]
    [InlineData(0x0001, NavigationPadAction.PreviousTarget)]
    [InlineData(0x0002, NavigationPadAction.NextTarget)]
    public void BrowsingButtonsSelectOneEntryPerPress(int rawButton, NavigationPadAction expected)
    {
        var pad = Open();
        Assert.True(pad.Filter((NavigationPadButtons)rawButton, false, true));
        Assert.Equal(expected, Assert.Single(pad.Poll()));
        Assert.True(pad.IsOpen);
        pad.Filter((NavigationPadButtons)rawButton, false, true);
        Assert.Empty(pad.Poll());
        pad.Filter(0, true, true);
        pad.Filter((NavigationPadButtons)rawButton, false, true);
        Assert.Equal(expected, Assert.Single(pad.Poll()));
    }

    [Theory]
    [InlineData(0x1000, NavigationPadAction.Guide)]
    [InlineData(0x4000, NavigationPadAction.Walk)]
    [InlineData(0x2000, NavigationPadAction.Close)]
    [InlineData(0x0080, NavigationPadAction.Close)]
    public void LeavingTheMenuConsumesTheEntirePressThroughRelease(int button, NavigationPadAction action)
    {
        var pad = Open();
        Assert.True(pad.Filter((NavigationPadButtons)button, false, true));
        Assert.Equal(action, Assert.Single(pad.Poll()));
        Assert.False(pad.IsOpen);
        Assert.True(pad.Filter((NavigationPadButtons)button, false, true));
        Assert.Empty(pad.Poll());
        // A stick left deflected at close also needs to return to neutral.
        Assert.True(pad.Filter(0, false, true));
        pad.Filter(0, true, true);
        Assert.Equal(button == 0x0080, pad.Filter((NavigationPadButtons)button, false, true));
        if (button != 0x0080) Assert.Empty(pad.Poll());
        else Assert.Equal(NavigationPadAction.Open, Assert.Single(pad.Poll()));
    }

    [Fact]
    public void OrdinaryControllerButtonsPassThroughUntilTheMenuIsOpened()
    {
        var pad = Ready();
        foreach (var button in new[] { 0x1000, 0x2000, 0x4000, 0x0100, 0x0200, 1, 2 })
        {
            Assert.False(pad.Filter((NavigationPadButtons)button, false, true));
            Assert.Empty(pad.Poll());
            pad.Filter(0, true, true);
        }
    }

    [Fact]
    public void StartupHeldStickDoesNotOpenUntilReleasedAndPressedAgain()
    {
        var pad = new NavigationGamepad();
        Assert.False(pad.Filter((NavigationPadButtons)0x80, false, true));
        Assert.Empty(pad.Poll());
        pad.Filter(0, true, true);
        pad.Filter((NavigationPadButtons)0x80, false, true);
        Assert.Equal(NavigationPadAction.Open, Assert.Single(pad.Poll()));
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("background")]
    [InlineData("disconnect")]
    [InlineData("suspend")]
    public void LosingControlClosesTheMenuAndDiscardsQueuedRouteCommands(string cause)
    {
        var pad = Open();
        pad.Filter((NavigationPadButtons)0x4000, false, true);
        if (cause == "suspend") pad.Suspend();
        else pad.Filter((NavigationPadButtons)0x4000, false, cause != "unavailable",
            foreground: cause != "background", connected: cause != "disconnect");
        Assert.False(pad.IsOpen);
        Assert.Empty(pad.Poll());
        // A held button cannot re-arm on return or leak an old in-menu press.
        Assert.True(pad.Filter((NavigationPadButtons)0x4000, false, true));
        Assert.Empty(pad.Poll());
        pad.Filter(0, true, true);
        pad.Filter((NavigationPadButtons)0x80, false, true);
        Assert.Equal(NavigationPadAction.Open, Assert.Single(pad.Poll()));
    }

    [Fact]
    public void OpposingOrAmbiguousButtonsDoNotPickAnArbitraryDestinationOrRoute()
    {
        var pad = Open();
        foreach (var buttons in new[] { 0x0300, 0x0003, 0x5000 })
        {
            Assert.True(pad.Filter((NavigationPadButtons)buttons, false, true));
            Assert.Empty(pad.Poll());
            Assert.True(pad.IsOpen);
            pad.Filter(0, true, true);
        }
        // Closing takes precedence over a route selection in the same snapshot.
        pad.Filter((NavigationPadButtons)0x6000, false, true);
        Assert.Equal(NavigationPadAction.Close, Assert.Single(pad.Poll()));
    }

    private static NavigationGamepad Ready()
    {
        var pad = new NavigationGamepad();
        pad.Filter(0, true, true);
        return pad;
    }

    private static NavigationGamepad Open()
    {
        var pad = Ready();
        pad.Filter((NavigationPadButtons)0x0080, false, true);
        pad.Poll();
        pad.Filter(0, true, true);
        return pad;
    }
}
