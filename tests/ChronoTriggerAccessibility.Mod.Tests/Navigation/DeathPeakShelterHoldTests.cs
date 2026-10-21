using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class DeathPeakShelterHoldTests
{
    [Theory]
    [InlineData("lower")]
    [InlineData("upper")]
    public void AutomaticArrivalKeepsUpHeldWithoutAStallTimeout(string id)
    {
        var h = new Harness(id);
        Assert.Equal(0x800u, h.Start());
        Assert.Contains("Holding Up", h.Speech.Last());
        var announcements = h.Speech.Count;
        // The native tree blocks forward movement. That is the intended hold,
        // not a blocked route, even after the ordinary 1.5-second stall limit.
        for (var tick = 0; tick < 400; tick++) Assert.Equal(0x800u, h.Tick());
        Assert.Equal(announcements, h.Speech.Count);
    }

    [Fact]
    public void KeyboardBrowsingRetainsShelterAndANewWalkUsesTheNewSelection()
    {
        var h = new Harness(); Assert.Equal(0x800u, h.Start());
        Assert.Equal(0x800u, h.Key('O')); // Story Events, no current target.
        Assert.Equal(0x800u, h.Key('L'));
        Assert.Equal(0x800u, h.Key('K'));
        Assert.Equal(0x800u, h.Key('U')); // Objects again.
        Assert.Equal(0x800u, h.Key('L')); // Upper tree.
        h.Key('P');
        Assert.Contains("Walking to Upper shelter tree.", h.Speech.Last());
        Assert.DoesNotContain("Navigation stopped.", h.Speech.Last());
    }

    [Fact]
    public void ControllerMenuCanOpenBrowseAndCloseWithoutReleasingShelter()
    {
        var h = new Harness(); Assert.Equal(0x800u, h.Start()); h.Filter(0);
        Assert.Equal(0x800u, h.Button(NavigationPadButtons.RightStick));
        Assert.True(h.Pad.IsOpen);
        Assert.Equal(0x800u, h.Button(NavigationPadButtons.RightShoulder));
        Assert.Equal(0x800u, h.Button(NavigationPadButtons.Down));
        Assert.Equal(0x800u, h.Button(NavigationPadButtons.LeftShoulder));
        for (var tick = 0; tick < 120; tick++)
        {
            h.Filter(0);
            Assert.Equal(0x800u, h.Tick());
        }
        Assert.Equal(0x800u, h.Button(NavigationPadButtons.East));
        Assert.False(h.Pad.IsOpen);
        h.Filter(0);
        Assert.Equal(0x100u, h.Tick(0x100)); // Physical right takes over.
        Assert.Equal(0u, h.Tick());
    }

    [Fact]
    public void ControllerWalkFromShelterStartsTheBrowsedDestination()
    {
        var h = new Harness(); Assert.Equal(0x800u, h.Start()); h.Filter(0);
        h.Button(NavigationPadButtons.RightStick);
        h.Button(NavigationPadButtons.Down);
        h.Button(NavigationPadButtons.West);
        Assert.False(h.Pad.IsOpen);
        Assert.Contains("Walking to Upper shelter tree.", h.Speech.Last());
        Assert.DoesNotContain("Navigation stopped.", h.Speech.Last());
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("confirm")]
    [InlineData("menu")]
    [InlineData("foreground")]
    [InlineData("unreadable")]
    [InlineData("control")]
    [InlineData("scene")]
    [InlineData("context")]
    [InlineData("gap")]
    [InlineData("disable")]
    [InlineData("target")]
    [InlineData("horizontal")]
    [InlineData("downhill")]
    [InlineData("layer")]
    [InlineData("disconnect")]
    public void LosingManualOrNativeAuthorityReleasesTheHoldWithoutResuming(string cause)
    {
        var h = new Harness(); Assert.Equal(0x800u, h.Start());
        uint input = 0;
        switch (cause)
        {
            case "manual": input = 0x400; break;
            case "confirm": input = 0x80; break;
            case "menu": input = 0x20; break;
            case "foreground": h.Foreground = false; break;
            case "unreadable": h.Unreadable = true; break;
            case "control": h.Frame = h.Frame with { CanNavigate = false }; break;
            case "scene": h.Frame = h.Frame with { Scene = "00001000:245" }; break;
            case "context": h.Engine = 2; break;
            case "gap": h.Now += 1000; break;
            case "disable": h.Runtime.Disable(); break;
            case "target": h.Frame = h.Frame with { Targets = [] }; break;
            case "horizontal": h.Frame = h.Frame with { Player = h.Frame.Player with { X = h.Frame.Player.X + 16 } }; break;
            case "downhill": h.Frame = h.Frame with { Player = h.Frame.Player with { Y = 55 * 256 } }; break;
            case "layer": h.Frame = h.Frame with { Player = h.Frame.Player with { Layer = 2 } }; break;
            case "disconnect":
                h.Filter(0); h.Button(NavigationPadButtons.RightStick); h.Button(NavigationPadButtons.East);
                h.Runtime.FilterController(0, 0, true, false);
                break;
        }
        Assert.Equal(input, h.Tick(input));
        h.Foreground = true; h.Unreadable = false; h.Engine = 1;
        h.Frame = h.Initial;
        Assert.Equal(0u, h.Tick());
        Assert.Equal(0u, h.Tick());
    }

    [Fact]
    public void StartingManualGuidanceOrStoppingTheCurrentWalkReleasesTheHold()
    {
        var manual = new Harness(); Assert.Equal(0x800u, manual.Start());
        Assert.Equal(0u, manual.Key('I'));
        Assert.Contains("Hold Up now.", manual.Speech.Last());
        Assert.Equal(0u, manual.Tick());
        var stopped = new Harness(); Assert.Equal(0x800u, stopped.Start());
        Assert.Equal(0u, stopped.Key('P'));
        Assert.Contains("Navigation stopped.", stopped.Speech.Last());
        Assert.Equal(0u, stopped.Tick());
        var empty = new Harness(); Assert.Equal(0x800u, empty.Start()); empty.Key('O');
        Assert.Equal(0x800u, empty.Key('P'));
        Assert.Contains("No story guidance", empty.Speech.Last());
        Assert.Contains("Still holding shelter.", empty.Speech.Last());
        Assert.Equal(0x800u, empty.Tick());
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(6, true)]
    public void PushFreeShelterStillStopsNormally(int flags, bool live)
    {
        var frame = DeathPeakNavigationTests.Frame(flags,
            doll: flags == 2 ? DeathPeakNavigationTests.Doll() : null, live: live);
        var shelter = frame.Targets.Single(t => t.Id == "death-peak:shelter:lower");
        var h = new Harness(frame with { Player = shelter.ApproachPoints[0], Targets = [shelter] });
        Assert.Equal(0u, h.Start());
        Assert.Equal(0u, h.Tick());
        Assert.Contains("does not push", h.Speech.Last());
        Assert.DoesNotContain("Holding Up", h.Speech.Last());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    public void PlainUpHoldsInEveryNativeRunSettingAndPreservesUserDash(int mode, int toggle)
    {
        var h = new Harness() { RunMode = mode, RunToggle = toggle };
        h.Frame = h.Frame with { MovingFloor = new(NavigationDirection.South, 16) };
        Assert.Equal(0x800u, h.Start());
        Assert.Equal(0x808u, h.Tick(8));
        h.RunMode = 0; h.RunToggle = 2;
        Assert.Equal(0x800u, h.Tick());
        h.RunReadable = false;
        Assert.Equal(0x800u, h.Tick());
        Assert.DoesNotContain(h.Speech, s => s.Contains("running is required"));
    }

    [Fact]
    public void SmallDownhillMovementWithinProvenCoverDoesNotReleaseTheHeldDirection()
    {
        var h = new Harness(); Assert.Equal(0x800u, h.Start());
        var atTrunk = h.Frame.Player;
        // Cover extends below the trunk. The held Up must continue through native
        // movement/push samples, rather than requiring a motionless arrival point.
        foreach (var offset in new[] { 8, 16, 32, 16, 8, 0 })
        {
            h.Frame = h.Frame with { Player = atTrunk with { Y = atTrunk.Y + offset } };
            Assert.Equal(0x800u, h.Tick());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RevokingTheLiveHoldProofOrAddingANativeRestrictionStopsHolding(bool restriction)
    {
        var h = new Harness(); Assert.Equal(0x800u, h.Start());
        h.Frame = h.Frame with
        {
            Targets = h.Frame.Targets.Select(target => target.Id.EndsWith("lower")
                ? restriction ? target with { GuidanceRestriction = "The passage is blocked." }
                    : target with { ArrivalHold = null }
                : target).ToArray(),
        };
        Assert.Equal(0u, h.Tick());
        h.Frame = h.Initial;
        Assert.Equal(0u, h.Tick());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFailedNewRouteKeepsShelterUntilARouteActuallyStarts(bool restricted)
    {
        var h = new Harness(); Assert.Equal(0x800u, h.Start());
        h.Frame = h.Frame with
        {
            Targets = h.Frame.Targets.Select(target => restricted && target.Id.EndsWith("upper")
                ? target with { GuidanceRestriction = "This route is closed." } : target).ToArray(),
            Graph = restricted ? h.Frame.Graph : new NoEdges(),
        };
        Assert.Equal(0x800u, h.Key('L'));
        Assert.Equal(0x800u, h.Key('P'));
        Assert.Contains(restricted ? "This route is closed." : "No route to Upper shelter tree", h.Speech.Last());
        Assert.Contains("Still holding shelter.", h.Speech.Last());
        Assert.Equal(0x800u, h.Tick());
    }

    private sealed class NoEdges : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point) => [];
    }

    private sealed class Harness
    {
        public readonly NavigationGamepad Pad = new();
        public readonly List<string> Speech = [];
        public readonly HashSet<int> Keys = [];
        public readonly FieldNavigationRuntime Runtime;
        public readonly NavigationFrame Initial;
        public NavigationFrame Frame;
        public long Now;
        public nint Engine = 1;
        public bool Foreground = true, Unreadable, RunReadable = true;
        public int RunMode, RunToggle = 1;
        public Harness(string id = "lower") : this(AtTree(id)) { }
        public Harness(NavigationFrame frame)
        {
            Initial = Frame = frame;
            Runtime = new(_ => Unreadable ? null : Frame, new(Keys.Contains, () => Foreground),
                () => Foreground, () => Now, Speech.Add, _ => { }, gamepad: Pad,
                adjustFieldInput: (current, result) => FieldFloorMovement.Adjust(current, result,
                    RunReadable ? new(0x1000, 0x2000, 244, 6, 0, 16, RunMode, RunToggle) : null));
            Runtime.Enable(); Tick();
        }
        public uint Start() { Key('O'); Key('O'); return Key('P'); }
        public uint Tick(uint input = 0) { Now += 16; return Runtime.OnInput(Engine, input); }
        public uint Key(int key)
        {
            Keys.Clear(); Tick(); Keys.Add(key);
            var result = Tick(); Keys.Clear(); return result;
        }
        public bool Filter(NavigationPadButtons buttons) => Runtime.FilterController(0, buttons, buttons == 0, true);
        public uint Button(NavigationPadButtons button)
        {
            Filter(0); Tick(); Filter(button); return Tick();
        }
        private static NavigationFrame AtTree(string id)
        {
            var frame = DeathPeakNavigationTests.Frame(6);
            var trees = frame.Targets.Where(t => t.Id.StartsWith("death-peak:shelter:")).ToArray();
            return frame with
            {
                Player = trees.Single(t => t.Id == "death-peak:shelter:" + id).ApproachPoints[0],
                Targets = trees.OrderBy(t => t.Id.EndsWith(id) ? 0 : 1).ToArray(),
            };
        }
    }
}
