using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class MountainTransitionRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeyboardResumesAfterALongSlideWithoutAnyAirborneInputCallback(bool manual)
    {
        var start = manual ? new NavigationPoint(13350, 5800, 2) : new NavigationPoint(13440, 5888, 2);
        var h = new Harness(MountainTransitionTests.Waterfalls(start));
        h.Start(guide: manual, targetId: "exit:2");
        // 1734D0 skips 175A40 throughout E3 00. No synthetic OnInput call in that interval.
        h.Frame = MountainTransitionTests.Waterfalls(new(13000, 9000, 2), used: true, activeActor: 18, control: false);
        h.Now += 9000;
        h.Frame = MountainTransitionTests.Waterfalls(new(12672, 12672, 2), used: true);
        var pad = h.Tick();
        Assert.Contains(h.Speech, s => s.Contains("Continuing to"));
        Assert.DoesNotContain(h.Speech, s => s.Contains("Navigation stopped"));
        Assert.Equal(manual, pad == 0);
    }

    [Fact]
    public void KeyboardWaitsForTheNativeReturnWithoutAnAirborneCallback()
    {
        var h = new Harness(); h.Start();
        h.Now += 1800;
        h.Frame = MountainTransitionTests.Mountain(new(1392, 4675, 1), progress: true) with { CanNavigate = true };
        Assert.Equal(0u, h.Tick());
        Assert.DoesNotContain(h.Speech, s => s.Contains("Continuing to"));
        h.Frame = MountainTransitionTests.Mountain(new(1392, 4675, 1));
        Assert.NotEqual(0u, h.Tick());
        Assert.Contains(h.Speech, s => s.Contains("Continuing to"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeInputPauseKeepsTheOwnedDropAndResumesOnlyOnAFreshLanding(bool controllerPoll)
    {
        var h = new Harness(); h.Start(controllerPoll);
        Assert.Equal(0x400u, h.LastPad);
        h.Frame = MountainTransitionTests.Mountain(new(1392, 4256, 1), progress: true);
        h.Now += 500; // player-input callback is absent during E3 00
        if (controllerPoll) h.Runtime.FilterController(0, NavigationPadButtons.None, true, true);
        else Assert.Equal(0u, h.Tick());
        Assert.DoesNotContain(h.Speech, s => s.Contains("Navigation stopped"));
        h.Frame = MountainTransitionTests.Mountain(new(1392, 4675, 1));
        h.Now += 500;
        if (controllerPoll) h.Runtime.FilterController(0, NavigationPadButtons.None, true, true);
        Assert.NotEqual(0u, h.Tick());
        Assert.Contains(h.Speech, s => s.Contains("Continuing to"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ControllerDisconnectionOrMissingPollStillCancelsDuringADrop(bool disconnect)
    {
        var h = new Harness(); h.Start(true);
        h.Frame = MountainTransitionTests.Mountain(new(1392, 4256, 1), progress: true);
        h.Now += 500;
        if (disconnect) h.Runtime.FilterController(0, NavigationPadButtons.None, true, false);
        Assert.Equal(0u, h.Tick());
        h.Frame = MountainTransitionTests.Mountain(new(1392, 4675, 1));
        h.Runtime.FilterController(0, NavigationPadButtons.None, true, true);
        Assert.Equal(0u, h.Tick());
        Assert.Contains(h.Speech, s => s.Contains("controller disconnected"));
    }

    [Theory]
    [InlineData("foreground")]
    [InlineData("read")]
    [InlineData("script")]
    [InlineData("scene")]
    [InlineData("engine")]
    [InlineData("manual")]
    [InlineData("menu")]
    [InlineData("timeout")]
    public void OwningADropDoesNotWaiveOtherMovementAuthority(string cause)
    {
        var h = new Harness(); h.Start();
        h.Frame = MountainTransitionTests.Mountain(new(1392, 4256, 1), progress: true);
        if (cause == "foreground") h.Foreground = false;
        if (cause == "read") h.Frame = null;
        if (cause == "script") h.Frame = h.Frame! with { ActiveTransitions = new HashSet<string>() };
        if (cause == "scene") h.Frame = h.Frame! with { Scene = "changed" };
        if (cause == "engine") h.Engine = 2;
        if (cause == "menu") h.Runtime.Suspend("menu opened");
        h.Now += cause == "timeout" ? 21000 : 500;
        var physical = cause == "manual" ? 0x80u : 0u;
        Assert.Equal(physical, h.Tick(physical));
        h.Frame = MountainTransitionTests.Mountain(new(1392, 4675, 1));
        h.Foreground = true;
        h.Engine = 1;
        Assert.Equal(0u, h.Tick());
        Assert.DoesNotContain(h.Speech, s => s.Contains("Continuing to"));
    }

    private sealed class Harness
    {
        private readonly HashSet<int> keys = [];
        public List<string> Speech { get; } = [];
        public NavigationFrame? Frame = MountainTransitionTests.Mountain(new(1408, 3808, 1));
        public long Now;
        public bool Foreground = true;
        public nint Engine = 1;
        public uint LastPad;
        public FieldNavigationRuntime Runtime { get; }

        public Harness(NavigationFrame? frame = null)
        {
            Frame = frame ?? Frame;
            Runtime = new(_ => Frame, new(keys.Contains, () => Foreground),
                () => Foreground, () => Now, Speech.Add, _ => { }, gamepad: new());
        }
        public uint Tick(uint pad = 0) { Now += 16; return LastPad = Runtime.OnInput(Engine, pad); }
        public void Start(bool physicalController = false, bool guide = false, string targetId = "exit:3")
        {
            var exit = Frame!.Targets.Single(t => t.Id == targetId);
            Frame = Frame with { Targets = [exit] };
            Runtime.Enable(); Tick();
            if (!physicalController) { Press('O'); Press(guide ? 'I' : 'P'); return; }
            Runtime.FilterController(0, NavigationPadButtons.None, true, true); Tick();
            Pad(NavigationPadButtons.RightStick);
            Pad(NavigationPadButtons.RightShoulder);
            Pad(NavigationPadButtons.West);
        }
        private void Pad(NavigationPadButtons button)
        {
            Runtime.FilterController(0, NavigationPadButtons.None, true, true); Tick();
            Runtime.FilterController(0, button, false, true); Tick();
            Runtime.FilterController(0, NavigationPadButtons.None, true, true); Tick();
        }
        private void Press(int key) { keys.Clear(); Tick(); keys.Add(key); Tick(); keys.Clear(); }
    }
}
