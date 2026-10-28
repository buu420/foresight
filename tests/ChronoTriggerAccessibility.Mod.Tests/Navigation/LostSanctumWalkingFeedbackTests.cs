using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class LostSanctumWalkingFeedbackTests
{
    [Theory]
    [InlineData(18917, 0, 1, "Hold Dash")]
    [InlineData(18917, 1, 2, "Release Dash")]
    [InlineData(18917, 1, 1, "Tap and release Dash")]
    [InlineData(18917, 2, 0, "Hold Dash")]
    [InlineData(31620, 0, 1, "Hold Dash")]
    [InlineData(31620, 1, 2, "Release Dash")]
    [InlineData(31620, 1, 1, "Tap and release Dash")]
    [InlineData(31620, 2, 0, "Hold Dash")]
    public void RunningManualGuidanceExplainsTheNativeWalkingControlAndThenFinishes(int line, int mode, int toggle, string control)
    {
        var h = new Harness(line, mode, toggle);
        h.Press('I');
        var responded = false;
        var handledSpeech = 0;
        var walkingDirection = NavigationDirection.None;
        for (var i = 0; i < 6000 && !h.Reached; i++)
        {
            if (h.Walking && h.Leg?.Direction != walkingDirection)
            {
                h.Walking = false;
                if (mode == 1 && toggle == 1) h.Toggle = 1; // Native player tap to resume the prior gait.
            }
            if (h.Speech.Skip(handledSpeech).Any(s => s.Contains("walking speed", StringComparison.Ordinal)))
            {
                responded |= h.Speech.Any(s => s.Contains(control, StringComparison.Ordinal));
                // Model the player's response; the runtime must never change the toggle.
                h.Walking = true;
                walkingDirection = h.Leg?.Direction ?? NavigationDirection.None;
                if (mode == 1 && toggle == 1) h.Toggle = 2;
            }
            handledSpeech = h.Speech.Count;
            var pad = FieldNavigationRuntime.DirectionBits(h.Leg?.Direction ?? NavigationDirection.None) | h.ManualDash;
            Assert.Equal(pad, h.Tick(pad));
        }
        Assert.True(responded, string.Join(" | ", h.Speech.TakeLast(8)));
        Assert.True(h.Reached, $"{h.Frame.Player}: {h.Probes}; {string.Join(" | ", h.Speech.TakeLast(8))}");
        Assert.DoesNotContain(h.Speech, s => s.Contains("movement is blocked", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(18917)] [InlineData(31620)]
    public void ForcedRunningAutoWalkExplainsHowToSelectWalkingAndCanBeRestarted(int line)
    {
        var h = new Harness(line, 1, 1);
        h.Press('P');
        for (var i = 0; i < 4000 && !h.Speech.Any(s => s.Contains("Tap and release Dash", StringComparison.Ordinal)); i++) h.Tick();
        Assert.Contains(h.Speech, s => s.Contains("walking speed", StringComparison.Ordinal) && s.Contains("Tap and release Dash", StringComparison.Ordinal));
        Assert.DoesNotContain(h.Speech, s => s.Contains("movement is blocked", StringComparison.Ordinal));
        Assert.Equal(0u, h.Tick()); // No stale movement after the bounded cancellation.
        h.Toggle = 2; // The player selects walking with the native control.
        h.Press('P');
        for (var i = 0; i < 6000 && !h.Reached; i++) h.Tick();
        Assert.True(h.Reached, $"{h.Frame.Player}: {string.Join(" | ", h.Speech.TakeLast(8))}");
    }

    [Fact]
    public void AStalledManualPressDoesNotRepeatTheWalkingCueEveryFrame()
    {
        var h = new Harness(18917, 0, 1);
        h.Press('I');
        for (var i = 0; i < 90; i++) h.Tick(0x200);
        Assert.Single(h.Speech, s => s.Contains("Hold Dash", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("unknown")] [InlineData("foreign")] [InlineData("walking")] [InlineData("other-scene")]
    public void AWalkingCueRequiresTheCurrentNativeSettingAndANarrowSanctumRoute(string variant)
    {
        var h = new Harness(18917, 0, 1);
        if (variant == "unknown") h.Mode = null;
        if (variant == "foreign") h.FloorEngine = 0x2000;
        if (variant == "walking") h.Walking = true;
        if (variant == "other-scene") h.Frame = h.Frame with { Scene = "00001000:100" };
        h.Press('I', h.ManualDash);
        for (var i = 0; i < 4; i++) h.Tick(0x200 | h.ManualDash);
        Assert.DoesNotContain(h.Speech, s => s.Contains("walking speed", StringComparison.Ordinal));
    }

    private sealed class Harness
    {
        private readonly HashSet<int> keys = [];
        private readonly FieldMapSnapshot map;
        private readonly NavigationTarget target;
        private readonly int scene;
        private long now;
        public NavigationFrame Frame;
        public NavigationLeg? Leg;
        public List<string> Speech { get; } = [];
        public int? Mode;
        public int Toggle;
        public uint FloorEngine = 0x1000;
        public bool Walking;
        public uint ManualDash => Mode == 1 ? Walking ? 0u : Toggle == 1 ? 0u : 8u : Walking ? 8u : 0u;
        private FieldFloorSnapshot Floor => new(FloorEngine, 0x4000, scene, 0, 0, 0, Mode, Toggle);
        private readonly FieldNavigationRuntime runtime;
        public Harness(int line, int mode, int toggle)
        {
            var sample = LostSanctumNavigationTests.Live(line);
            map = sample.Map; scene = sample.Field.SceneId; Mode = mode; Toggle = toggle;
            target = sample.Frame.Targets.Single(t => t.Id == (line == 18917 ? "chest:301" : "script-region:0:580:0:2:4:9"));
            // One selected row, without changing the captured geometry or interaction.
            Frame = sample.Frame with { Targets = [target with { Category = NavigationCategory.People }] };
            runtime = new(_ => Frame, new(keys.Contains, () => true), () => true, () => now, Speech.Add, _ => { },
                synchronizeFootsteps: leg => Leg = leg,
                adjustFieldInput: (frame, result) => FieldFloorMovement.Adjust(frame, result, Floor), fieldFloor: () => Floor);
            runtime.Enable(); Tick();
        }
        public bool Reached => scene == 578
            ? Frame.Player.X / 256 <= 4 && Frame.Player.Y / 256 is >= 2 and <= 9
            : target.ApproachPoints.Any(p => p.Layer == Frame.Player.Layer && Math.Abs(p.X - Frame.Player.X) <= 32 && Math.Abs(p.Y - Frame.Player.Y) <= 32);
        public string Probes => string.Join("; ", new[] { NavigationDirection.North, NavigationDirection.South, NavigationDirection.West, NavigationDirection.East }
            .Select(d => $"{d}16={NativeWalkingReplay.Move(map, Frame.Player, d, 16)} 32={NativeWalkingReplay.Move(map, Frame.Player, d, 32)}"));
        public uint Press(int key, uint pad = 0)
        {
            keys.Clear(); Tick(pad); keys.Add(key); var value = Tick(pad); keys.Clear(); return value;
        }
        public uint Tick(uint pad = 0)
        {
            now += 16;
            var delivered = runtime.OnInput(0x1000, pad);
            var direction = (delivered & 0xF00) switch
            {
                0x100 => NavigationDirection.East, 0x200 => NavigationDirection.West,
                0x400 => NavigationDirection.South, 0x800 => NavigationDirection.North,
                0x500 => NavigationDirection.SouthEast, 0x600 => NavigationDirection.SouthWest,
                0x900 => NavigationDirection.NorthEast, 0xA00 => NavigationDirection.NorthWest, _ => NavigationDirection.None,
            };
            var toggle = Toggle == 0 ? Mode switch { 0 => 1, 1 => 2, _ => 0 } : Toggle;
            var running = Mode == 1 ? (delivered & 8) != 0 || toggle == 1 : (delivered & 8) == 0 && toggle != 2;
            if (direction != NavigationDirection.None)
                Frame = Frame with { Player = NativeWalkingReplay.Move(map, Frame.Player, direction, running ? 32 : 16), PlayerFacing = direction };
            return delivered;
        }
    }
}
