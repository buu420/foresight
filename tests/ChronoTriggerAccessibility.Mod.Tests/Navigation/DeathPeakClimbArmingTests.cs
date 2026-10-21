using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class DeathPeakClimbArmingTests
{
    [Fact]
    public void ArmingBeforeEntrySurvivesNativeLoadingAndStartsOnlyOneClimb()
    {
        var h = new Harness(); h.Arm();
        Assert.Equal(0u, h.Runtime.ApplyWorldPad(1, 0));
        Assert.Equal(0x100u, h.Tick(0x100)); // Normal world movement remains the player's.
        h.IsField = true; h.Now += 2000;
        h.Frame = h.Frame with { CanNavigate = false };
        Assert.Equal(0u, h.Tick());
        h.Now += 2000; h.Frame = h.Frame with { CanNavigate = true };
        Assert.NotEqual(0u, h.Tick());
        Assert.Contains(h.Speech, s => s.Contains("Walking to Climb windy slope"));
        Assert.Equal(0x100u, h.Tick(0x100)); // Manual takeover ends the active assist.
        Assert.Equal(0u, h.Tick());
        h.IsField = false; h.Tick(); h.IsField = true;
        Assert.Equal(0u, h.Tick()); // A knockback/re-entry does not re-arm it.
    }

    [Fact]
    public void NativeEntryConfirmIsNeverSynthesizedOrSwallowed()
    {
        var h = new Harness(); h.Arm(); h.IsField = true;
        Assert.Equal(0x80u, h.Tick(0x80));
        Assert.DoesNotContain(h.Speech, s => s.Contains("Walking to Climb windy slope"));
        Assert.NotEqual(0u, h.Tick());
    }

    [Fact]
    public void LeavingBeforeReleasingEntryConfirmStillConsumesTheArmedAttempt()
    {
        var h = new Harness(); h.Arm(); h.IsField = true;
        Assert.Equal(0x80u, h.Tick(0x80));
        h.IsField = false; h.Tick(); h.IsField = true;
        Assert.Equal(0u, h.Tick());
    }

    [Theory]
    [InlineData("snapshot")]
    [InlineData("phase")]
    public void AnUnreadableEntryFloorCanApproachCoverButCannotTimeADeparture(string missing)
    {
        var h = new Harness(); h.Arm(); h.IsField = true;
        var floor = h.Floor;
        h.Floor = missing == "snapshot" ? null : floor!.Value with { WindBuilding = null };
        Assert.NotEqual(0u, h.Tick());
        var lower = h.Frame.Targets.Single(t => t.Id == DeathPeakClimbAssist.TargetId).AutomaticStages[0];
        h.Frame = h.Frame with { Player = lower.ApproachPoints[0] };
        Assert.Equal(0x800u, h.Tick());
        Assert.Contains(h.Speech, s => s.Contains("automatic departures are paused"));
        h.Floor = floor;
        Assert.Equal(0x800u, h.Tick());
        h.Floor = floor!.Value with { WindGusting = true }; Assert.Equal(0x800u, h.Tick());
        h.Floor = floor;
        Assert.NotEqual(0u, h.Tick());
        Assert.Contains(h.Speech, s => s.Contains("Moving to Upper shelter tree"));
    }

    [Theory]
    [InlineData("game menu opened")]
    [InlineData("game is not in the foreground")]
    [InlineData("controller disconnected")]
    public void CancellingAPendingArmExplainsTheReasonOnceWhenInputReturns(string reason)
    {
        var h = new Harness(); h.Arm(); h.Speech.Clear();
        h.Runtime.Suspend(reason);
        Assert.Empty(h.Speech);
        Assert.Equal(0u, h.Tick());
        Assert.Contains(h.Speech, s => s.Contains("armed Death Peak climb was cancelled") && s.Contains(reason));
        var count = h.Speech.Count; h.Tick();
        Assert.Equal(count, h.Speech.Count);
    }

    [Fact]
    public void AnUnreadableRunSettingStillStopsAtTheSteepSlope()
    {
        var h = new Harness(); h.Arm(); h.IsField = true; h.Floor = null;
        h.Frame = h.Frame with { MovingFloor = new(NavigationDirection.South, 16) };
        Assert.Equal(0u, h.Tick());
        Assert.Contains(h.Speech, s => s.Contains("run setting", StringComparison.OrdinalIgnoreCase));
        h.Floor = new(0x1000, 0x2000, 244, 6, 0, 16, 0, 1) { WindGusting = false, WindBuilding = false };
        Assert.Equal(0u, h.Tick());
    }

    [Fact]
    public void ControllerArmingSurvivesALoadingGapBeforeTheNextControllerPoll()
    {
        var h = new Harness();
        h.Button(NavigationPadButtons.RightStick); h.Button(NavigationPadButtons.RightShoulder);
        h.Button(NavigationPadButtons.West);
        h.IsField = true; h.Now += 2000;
        Assert.NotEqual(0u, h.Tick());
        Assert.Contains(h.Speech, s => s.Contains("Walking to Climb windy slope"));
    }

    [Theory]
    [InlineData("toggle")]
    [InlineData("foreground")]
    [InlineData("disable")]
    [InlineData("menu")]
    [InlineData("different-area")]
    [InlineData("manual-entry")]
    public void DisarmingOrTakingAnotherActionCannotStartALaterClimb(string cause)
    {
        var h = new Harness(); h.Arm();
        var initial = h.Frame;
        switch (cause)
        {
            case "toggle": h.Key('P'); break;
            case "foreground": h.Foreground = false; h.Tick(); h.Foreground = true; break;
            case "disable": h.Runtime.Disable(); h.Runtime.Enable(); h.Tick(); break;
            case "menu": h.Runtime.Suspend("game menu opened"); break;
            case "different-area":
                h.IsField = true;
                h.Frame = initial with { Scene = "00001000:245", Targets = [] }; h.Tick();
                break;
            case "manual-entry": h.IsField = true; Assert.Equal(0x100u, h.Tick(0x100)); break;
        }
        h.Frame = initial; h.IsField = true;
        Assert.Equal(0u, h.Tick());
        Assert.Equal(0u, h.Tick());
    }

    [Fact]
    public void EnteringWithoutArmingStillRequiresAnExplicitStart()
    {
        var h = new Harness(); h.IsField = true;
        Assert.Equal(0u, h.Tick()); Assert.Equal(0u, h.Tick());
    }

    [Fact]
    public void TheFirstDefaultArmOptionWorksAfterTheWorldContextChanges()
    {
        var h = new Harness(); h.Key('O');
        h.World = h.World with { Scene = "world:00000002:2" }; h.WorldEngine = 2;
        Assert.Equal(0u, h.Key('P'));
        h.IsField = true;
        Assert.NotEqual(0u, h.Tick());
    }

    [Fact]
    public void ControllerCanArmInTheWorldMenuWithoutProducingWorldMovement()
    {
        var h = new Harness();
        h.Button(NavigationPadButtons.RightStick);
        h.Button(NavigationPadButtons.RightShoulder);
        Assert.Equal(0u, h.Button(NavigationPadButtons.West));
        Assert.False(h.Pad.IsOpen);
        Assert.Equal(0u, h.Runtime.ApplyWorldPad(1, 0));
        h.IsField = true;
        Assert.NotEqual(0u, h.Tick());
    }

    private sealed class Harness
    {
        public readonly List<string> Speech = [];
        public readonly HashSet<int> Keys = [];
        public readonly NavigationGamepad Pad = new();
        public readonly FieldNavigationRuntime Runtime;
        public NavigationFrame Frame = DeathPeakClimbAssistTests.LiveFrame();
        public NavigationFrame World = WorldNavigationSourceTests.WindEntranceWorld();
        public bool IsField, Foreground = true;
        public nint WorldEngine = 1;
        public long Now;
        public FieldFloorSnapshot? Floor = new(0x1000, 0x2000, 244, 6, 0, 16, 0, 1)
            { WindGusting = false, WindBuilding = false };
        public Harness()
        {
            Runtime = new(_ => Frame, new(Keys.Contains, () => Foreground), () => Foreground,
                () => Now, Speech.Add, _ => { }, worldCapture: _ => World, gamepad: Pad,
                adjustFieldInput: (frame, result) => FieldFloorMovement.Adjust(frame, result, Floor),
                fieldFloor: () => Floor);
            Runtime.Enable(); Tick();
        }
        public uint Tick(uint input = 0)
        { Now += 16; return IsField ? Runtime.OnInput(0x1000, input) : Runtime.OnWorldInput(WorldEngine, input); }
        public uint Key(char key)
        { Keys.Clear(); Tick(); Keys.Add(key); var result = Tick(); Keys.Clear(); return result; }
        public void Arm() { Key('O'); Assert.Equal(0u, Key('P')); }
        public void Poll(NavigationPadButtons button = 0) => Runtime.FilterController(0, button, button == 0, true);
        public uint Button(NavigationPadButtons button) { Poll(); Tick(); Poll(button); return Tick(); }
    }
}
