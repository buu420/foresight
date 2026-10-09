using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class ControllerNavigationRuntimeTests
{
    [Theory]
    [InlineData(NavigationMode.Field, 0x108u)]
    [InlineData(NavigationMode.World, 0x108u)]
    [InlineData(NavigationMode.Epoch, 8u)]
    [InlineData(NavigationMode.Dactyl, 8u)]
    public void NativeDashDoesNotCancelWalkingButVehicleActionsStillTakeManualControl(NavigationMode mode, uint expected)
    {
        var h = new Harness(mode); h.Press(NavigationPadButtons.RightStick); h.Press(NavigationPadButtons.West);
        h.Filter(0);
        Assert.Equal(expected, h.Tick(8));
        if (mode == NavigationMode.World) Assert.Equal(expected, h.Runtime.ApplyWorldPad(h.Engine, 8));
        Assert.Equal(mode is NavigationMode.Field or NavigationMode.World ? 0x100u : 0u, h.Tick());
        Assert.Equal(0x808u, h.Tick(0x808));
        Assert.Equal(0u, h.Tick());
    }
    [Fact]
    public void OpeningReadsTheCategoryAndSelectionAndBrowsingUsesTheExistingTargets()
    {
        var h = new Harness();
        h.Press(NavigationPadButtons.RightStick);
        Assert.True(h.Pad.IsOpen);
        Assert.Contains(h.Speech, s => s.Contains("Navigation menu. People.") && s.Contains("Person"));
        h.Press(NavigationPadButtons.RightShoulder);
        Assert.Contains(h.Speech, s => s.Contains("Exits.") && s.Contains("Door"));
        h.Press(NavigationPadButtons.Down);
        Assert.Contains(h.Speech, s => s.Contains("Stairs"));
        h.Press(NavigationPadButtons.Up);
        Assert.Contains("Door", h.Speech.Last());
        h.Press(NavigationPadButtons.LeftShoulder);
        Assert.Contains("People.", h.Speech.Last());
    }

    [Theory]
    [InlineData(NavigationMode.Field)]
    [InlineData(NavigationMode.World)]
    [InlineData(NavigationMode.Epoch)]
    [InlineData(NavigationMode.Dactyl)]
    public void TheTwoStartButtonsCloseTheMenuAndUseGuidanceOrWalkingInEveryMode(NavigationMode mode)
    {
        var h = new Harness(mode);
        h.Press(NavigationPadButtons.RightStick);
        Assert.Equal(0u, h.Press(NavigationPadButtons.South));
        Assert.False(h.Pad.IsOpen);
        Assert.Contains("Guidance to Person", h.Speech.Last());
        h.Press(NavigationPadButtons.RightStick);
        Assert.Equal(0x100u, h.Press(NavigationPadButtons.West));
        Assert.False(h.Pad.IsOpen);
        Assert.Contains("Walking to Person", h.Speech.Last());
        Assert.Equal(0x100u, h.Tick());
        h.Press(NavigationPadButtons.RightStick);
        Assert.True(h.Pad.IsOpen);
        Assert.Equal(0u, h.Tick());
    }

    [Theory]
    [InlineData("menu")]
    [InlineData("scene")]
    [InlineData("context")]
    [InlineData("gap")]
    [InlineData("unavailable")]
    [InlineData("foreground")]
    [InlineData("disconnect")]
    [InlineData("disable")]
    public void StaleControllerRequestsCannotStartAPathAfterControlIsLost(string cause)
    {
        var h = new Harness();
        h.Press(NavigationPadButtons.RightStick);
        h.Release();
        Assert.True(h.Filter(NavigationPadButtons.West)); // queued, not yet processed
        switch (cause)
        {
            case "menu": h.Runtime.Suspend("menu opened"); break;
            case "scene": h.Scene = "new room"; break;
            case "context": h.Engine = 2; break;
            case "gap": h.Now += 1000; break;
            case "unavailable": h.Available = false; break;
            case "foreground": h.Foreground = false; break;
            case "disconnect": h.Runtime.FilterController(0, 0, true, false); break;
            case "disable": h.Runtime.Disable(); break;
        }
        Assert.Equal(0u, h.Tick());
        Assert.False(h.Pad.IsOpen);
        h.Foreground = h.Available = true;
        h.Release();
        Assert.Equal(0u, h.Tick());
        Assert.DoesNotContain(h.Speech, s => s.Contains("Walking to"));
    }

    [Fact]
    public void AnUnrelatedDisconnectedSlotDoesNotCloseTheMenuOrStopWalking()
    {
        var h = new Harness();
        h.Press(NavigationPadButtons.RightStick);
        h.Runtime.FilterController(3, 0, true, false);
        Assert.True(h.Pad.IsOpen);
        Assert.Equal(0x100u, h.Press(NavigationPadButtons.West));
        h.Runtime.FilterController(3, 0, true, false);
        Assert.Equal(0x100u, h.Tick());
        h.Runtime.FilterController(0, 0, true, false);
        Assert.Equal(0u, h.Tick());
    }

    [Fact]
    public void MissingControllerPollsStopAControllerRouteEvenWhenNativePlayerTicksContinue()
    {
        var h = new Harness();
        h.Press(NavigationPadButtons.RightStick);
        Assert.Equal(0x100u, h.Press(NavigationPadButtons.West));
        for (var i = 0; i < 20; i++) h.Tick();
        Assert.Equal(0u, h.Tick());
        Assert.Contains(h.Speech, s => s.Contains("controller disconnected"));
    }

    [Fact]
    public void KeyboardRouteIsIndependentAfterTakingOverFromTheController()
    {
        var h = new Harness();
        h.Press(NavigationPadButtons.RightStick);
        h.Press(NavigationPadButtons.South);
        h.Keys.Add('P');
        Assert.Equal(0x100u, h.Tick());
        h.Keys.Clear();
        h.Runtime.FilterController(0, 0, true, false);
        Assert.Equal(0x100u, h.Tick());
    }

    [Fact]
    public void ReopeningBeforeThePlayerTickCancelsAnEarlierQueuedWalk()
    {
        var h = new Harness();
        h.Press(NavigationPadButtons.RightStick);
        h.Filter(0);
        h.Filter(NavigationPadButtons.West);
        h.Filter(0);
        h.Filter(NavigationPadButtons.RightStick);
        Assert.True(h.Pad.IsOpen);
        Assert.Equal(0u, h.Tick());
        Assert.Equal(0u, h.Tick());
    }

    [Theory]
    [InlineData(NavigationMode.Field)]
    [InlineData(NavigationMode.World)]
    [InlineData(NavigationMode.Epoch)]
    [InlineData(NavigationMode.Dactyl)]
    public void CapturedFramesStayInsideTheirNativeInputBoundary(NavigationMode mode)
    {
        var h = new Harness(mode) { CaptureOnlyAtTick = true };
        h.Press(NavigationPadButtons.RightStick);
        Assert.True(h.Pad.IsOpen);
        Assert.Contains(h.Speech, s => s.Contains("Navigation menu."));
        Assert.Equal(0x100u, h.Press(NavigationPadButtons.West));
        Assert.Equal(0, h.OutOfBoundaryCaptures);
    }

    [Fact]
    public void UnavailableControlAndDisabledOrBackgroundStateNeverOpenTheMenu()
    {
        var h = new Harness();
        h.Available = false;
        h.Press(NavigationPadButtons.RightStick);
        Assert.False(h.Pad.IsOpen);
        h.Available = true; h.Foreground = false;
        h.Press(NavigationPadButtons.RightStick);
        Assert.False(h.Pad.IsOpen);
        h.Foreground = true; h.Runtime.Disable();
        h.Press(NavigationPadButtons.RightStick);
        Assert.False(h.Pad.IsOpen);
    }

    [Fact]
    public void ClosingRestoresControlAndKeyboardCommandsStillWork()
    {
        var h = new Harness();
        h.Press(NavigationPadButtons.RightStick);
        h.Press(NavigationPadButtons.East);
        Assert.False(h.Pad.IsOpen);
        Assert.Contains("Navigation menu closed.", h.Speech.Last());
        h.Release();
        Assert.False(h.Filter(NavigationPadButtons.South));
        h.Release();
        h.Press(NavigationPadButtons.RightStick);
        h.Keys.Add('P');
        Assert.Equal(0x100u, h.Tick());
        Assert.False(h.Pad.IsOpen);
    }

    private sealed class Harness
    {
        public readonly NavigationGamepad Pad = new();
        public readonly List<string> Speech = [];
        public readonly HashSet<int> Keys = [];
        public readonly FieldNavigationRuntime Runtime;
        public bool Foreground = true, Available = true;
        public string Scene = "room";
        public nint Engine = 1;
        public long Now;
        public bool CaptureOnlyAtTick;
        public int OutOfBoundaryCaptures;
        private bool inTick;
        private readonly NavigationMode mode;
        public Harness(NavigationMode mode = NavigationMode.Field)
        {
            this.mode = mode;
            NavigationFrame Frame()
            {
                if (CaptureOnlyAtTick && !inTick)
                {
                    OutOfBoundaryCaptures++;
                    return new(Scene, false, default, [], new Line());
                }
                var p = new NavigationPoint(16, 0, 1);
                return new(Scene, Available, new(0, 0, 1),
                    [new("person", "Person", NavigationCategory.People, p, [p], true, false),
                     new("door", "Door", NavigationCategory.Exits, p, [p], true, false),
                     new("stairs", "Stairs", NavigationCategory.Exits, p, [p], true, false)], new Line());
            }
            Runtime = new(_ => Frame(), new(Keys.Contains, () => Foreground), () => Foreground,
                () => Now, Speech.Add, _ => { }, worldCapture: _ => Frame(),
                vehicleCapture: (_, _) => Frame(), vehicleActive: (_, _) => true, gamepad: Pad);
            Runtime.Enable(); Tick(); Release();
        }
        public uint Tick(uint input = 0)
        {
            Now += 16;
            inTick = true;
            try
            {
            if (mode == NavigationMode.Field) return Runtime.OnInput(Engine, input);
            if (mode == NavigationMode.World) return Runtime.OnWorldInput(Engine, input);
            var kind = mode == NavigationMode.Epoch ? VehicleKind.Epoch : VehicleKind.Dactyl;
            Runtime.OnVehicleInput(Engine, input, kind);
            return Runtime.ApplyVehiclePad(Engine, input, kind);
            }
            finally { inTick = false; }
        }
        public bool Filter(NavigationPadButtons buttons) => Runtime.FilterController(0, buttons, buttons == 0, true);
        public void Release() { Filter(0); Tick(); }
        public uint Press(NavigationPadButtons buttons) { Release(); Filter(buttons); return Tick(); }
    }
    private sealed class Line : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint p)
        {
            if (p.X < 16) yield return p with { X = p.X + 4 };
        }
    }
}
