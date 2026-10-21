using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Mod.Navigation;
using System.Text.Json;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class DeathPeakClimbAssistTests
{
    [Fact]
    public void EntryOffersAQuickOptInClimbWithoutClaimingOffscreenTreesWereSeen()
    {
        var frame = LiveFrame();
        var climb = Assert.Single(frame.Targets, t => t.Id == "death-peak:climb");
        Assert.Equal(climb, frame.Targets.First(t => t.Category == NavigationCategory.Exits));
        Assert.Contains("assist", climb.Label, StringComparison.OrdinalIgnoreCase);
        Assert.True(climb.GuideAvailable);
        Assert.False(climb.Visible);
        Assert.False(climb.Discovered);
        Assert.DoesNotContain(frame.Targets, t => t.Id == "death-peak:shelter:lower");
    }

    [Fact]
    public void AutomaticClimbHoldsBothTreesAndLeavesOnlyAfterAnObservedGustEnds()
    {
        var h = new Harness();
        h.Key('O');
        Assert.NotEqual(0u, h.Key('P'));
        h.AtStage(0);
        Assert.Equal(0x800u, h.Tick());
        Assert.Contains("Lower shelter tree", h.Speech.Last());
        for (var tick = 0; tick < 400; tick++) Assert.Equal(0x800u, h.Tick());
        Assert.DoesNotContain(h.Speech, s => s.Contains("Moving to Upper shelter tree"));
        h.Wind(true);
        Assert.Equal(0x800u, h.Tick());
        h.Wind(false);
        Assert.NotEqual(0u, h.Tick());
        Assert.Contains(h.Speech, s => s.Contains("Moving to Upper shelter tree"));
        h.AtStage(1);
        Assert.Equal(0x800u, h.Tick());
        Assert.Contains("Upper shelter tree", h.Speech.Last());
        h.Wind(true); Assert.Equal(0x800u, h.Tick());
        h.Wind(false); Assert.NotEqual(0u, h.Tick());
        Assert.Contains(h.Speech, s => s.Contains("Moving to Death Peak, Map 1"));
        h.AtStage(2); Assert.Equal(0u, h.Tick());
        Assert.Equal(0u, h.Tick());
    }

    [Fact]
    public void WindAndElapsedTimeNeverStartAnUnselectedAssist()
    {
        var h = new Harness();
        for (var tick = 0; tick < 10; tick++)
        {
            h.Wind(tick % 2 == 0);
            Assert.Equal(0u, h.Tick());
        }
        Assert.DoesNotContain(h.Speech, s => s.Contains("Moving to"));
    }

    [Fact]
    public void ABlockedNextLegKeepsShelterInsteadOfReleasingThePlayerIntoTheWind()
    {
        var h = new Harness(); h.Key('O'); h.Key('P'); h.AtStage(0);
        Assert.Equal(0x800u, h.Tick());
        h.Frame = h.Frame with { Graph = new NoEdges() };
        h.Wind(true); Assert.Equal(0x800u, h.Tick());
        h.Wind(false); Assert.Equal(0x800u, h.Tick());
        h.Frame = h.Frame with { Graph = LiveFrame().Graph };
        for (var tick = 0; tick < 20; tick++) Assert.Equal(0x800u, h.Tick());
        h.Wind(true); h.Tick(); h.Wind(false); h.Tick();
        Assert.Contains(h.Speech, s => s.Contains("Moving to Upper shelter tree"));
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("confirm")]
    [InlineData("stop")]
    [InlineData("foreground")]
    [InlineData("capture")]
    [InlineData("control")]
    [InlineData("scene")]
    [InlineData("layer")]
    [InlineData("bounds")]
    [InlineData("target")]
    [InlineData("gap")]
    [InlineData("disable")]
    public void CancellationOrLostProofNeverSilentlyRestartsTheClimb(string cause)
    {
        var h = new Harness(); h.Key('O'); h.Key('P'); h.AtStage(0); h.Tick();
        var initial = h.Frame;
        uint input = 0;
        switch (cause)
        {
            case "manual": input = 0x100; break;
            case "confirm": input = 0x80; break;
            case "stop": Assert.Equal(0u, h.Key('P')); break;
            case "foreground": h.Foreground = false; break;
            case "capture": h.Unreadable = true; break;
            case "control": h.Frame = h.Frame with { CanNavigate = false }; break;
            case "scene": h.Frame = h.Frame with { Scene = "00001000:245" }; break;
            case "layer": h.Frame = h.Frame with { Player = h.Frame.Player with { Layer = 2 } }; break;
            case "bounds": h.Frame = h.Frame with { Player = h.Frame.Player with { X = h.Frame.Player.X + 16 } }; break;
            case "target": h.Frame = h.Frame with { Targets = [] }; break;
            case "gap": h.Now += 1000; break;
            case "disable": h.Runtime.Disable(); break;
        }
        Assert.Equal(input, h.Tick(input));
        h.Foreground = true; h.Unreadable = false; h.Frame = initial;
        if (cause == "disable") h.Runtime.Enable();
        h.Floor = new(0x1000, 0x2000, 244, 6, 0, 16, 0, 1) { WindGusting = true, WindBuilding = false };
        Assert.Equal(0u, h.Tick()); h.Wind(false);
        Assert.Equal(0u, h.Tick());
    }

    [Fact]
    public void RunningMustBeAvailableBeforeTheFirstExposedLegStarts()
    {
        var h = new Harness(); h.Floor = h.Floor!.Value with { RunToggle = 2 };
        h.Key('O'); Assert.Equal(0u, h.Key('P'));
        Assert.Contains(h.Speech, s => s.Contains("running", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TurningRunningOffInShelterNeverReleasesCover()
    {
        var h = new Harness(); h.Key('O'); h.Key('P'); h.AtStage(0); h.Tick();
        h.Floor = h.Floor!.Value with { RunToggle = 2 };
        h.Wind(true); Assert.Equal(0x800u, h.Tick());
        h.Wind(false); Assert.Equal(0x800u, h.Tick());
        Assert.Contains(h.Speech, s => s.Contains("running", StringComparison.OrdinalIgnoreCase));
        h.Floor = h.Floor!.Value with { RunToggle = 1 };
        Assert.Equal(0x800u, h.Tick());
        h.Wind(true); h.Tick(); h.Wind(false); h.Tick();
        Assert.Contains(h.Speech, s => s.Contains("Moving to Upper shelter tree"));
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(1, 1, 8)]
    [InlineData(1, 2, 8)]
    [InlineData(2, 0, 0)]
    public void AssistedTravelOwnsRunningEvenOnTheGentleSlopeAndWhenDashIsHeld(int mode, int toggle, uint dash)
    {
        var h = new Harness(); h.Floor = h.Floor!.Value with { RunMode = mode, RunToggle = toggle };
        h.Key('O');
        Assert.Equal(dash, h.Key('P') & 8u);
        Assert.Equal(dash, h.Tick(8) & 8u);
        h.AtStage(0);
        Assert.Equal(0x800u, h.Tick(8)); // Holding needs plain Up in every run mode.
    }

    [Fact]
    public void ARunFailureDuringTravelCannotLeaveAStaleUpperStageOnTheNextStart()
    {
        var h = new Harness(); h.Key('O'); h.Key('P'); h.AtStage(0); h.Tick();
        h.Wind(true); h.Tick(); h.Wind(false); h.Tick();
        h.Frame = h.Frame with { Player = h.Frame.Player with { X = 14464 } }; // Past the first eastward leg.
        h.Floor = h.Floor!.Value with { RunToggle = 2 };
        Assert.Equal(0u, h.Tick());
        h.Frame = h.Frame with { Player = new(14080, 15871, 1) };
        h.Floor = h.Floor!.Value with { RunToggle = 1 };
        h.Speech.Clear(); h.Key('P'); h.AtStage(0);
        Assert.Equal(0x800u, h.Tick());
        Assert.Contains("Lower shelter tree", h.Speech.Last());
    }

    [Theory]
    [InlineData("snapshot")]
    [InlineData("phase")]
    [InlineData("identity")]
    public void UnknownWindPausesDeparturesWhileASeparateFreshFrameStillProvesShelter(string cause)
    {
        var h = new Harness(); h.Key('O'); h.Key('P'); h.AtStage(0); h.Tick();
        h.Wind(true); h.Tick(); var known = h.Floor;
        h.Floor = cause switch
        {
            "snapshot" => null,
            "phase" => known!.Value with { WindGusting = null },
            _ => known!.Value with { Scene = 245 },
        };
        Assert.Equal(0x800u, h.Tick());
        h.Floor = known; h.Wind(false);
        for (var tick = 0; tick < 40; tick++) Assert.Equal(0x800u, h.Tick());
        Assert.DoesNotContain(h.Speech, s => s.Contains("Moving to Upper shelter tree"));
        h.Wind(true); h.Tick(); h.Wind(false); h.Tick();
        Assert.Contains(h.Speech, s => s.Contains("Moving to Upper shelter tree"));
    }

    [Fact]
    public void LosingTheWindReadingAfterLeavingCoverStopsTheExposedLeg()
    {
        var h = new Harness(); h.Key('O'); h.Key('P'); h.AtStage(0); h.Tick();
        h.Wind(true); h.Tick(); h.Wind(false); h.Tick();
        h.Floor = null;
        Assert.Equal(0u, h.Tick());
        Assert.Contains(h.Speech, s => s.Contains("wind state is unavailable"));
        h.Floor = new(0x1000, 0x2000, 244, 6, 0, 16, 0, 1) { WindGusting = false, WindBuilding = false };
        Assert.Equal(0u, h.Tick());
    }

    [Theory]
    [InlineData(47, "Lower shelter tree")]
    [InlineData(29, "Upper shelter tree")]
    public void StartingJustOutsideShelterReturnsToNearbyCoverBeforeContinuing(int row, string shelter)
    {
        var h = new Harness(); h.Frame = h.Frame with { Player = new(58 * 256, row * 256 + 128, 1) };
        h.Key('O'); h.Key('P');
        var stage = h.Frame.Targets.Single(t => t.Id == "death-peak:climb").AutomaticStages.Single(t => t.Label == shelter);
        h.Frame = h.Frame with { Player = stage.ApproachPoints[0] };
        Assert.Equal(0x800u, h.Tick());
        Assert.Contains(shelter, h.Speech.Last());
    }

    [Fact]
    public void ManualGuidanceStopsAtTheShelterAndNeverTimesItsOwnDeparture()
    {
        var h = new Harness(); h.Key('O'); h.Key('I'); h.AtStage(0); h.Tick();
        h.Wind(true); Assert.Equal(0u, h.Tick());
        h.Wind(false); Assert.Equal(0u, h.Tick());
        Assert.DoesNotContain(h.Speech, s => s.Contains("Moving to Upper shelter tree"));
    }

    [Fact]
    public void BrowsingAtTheGustEndDoesNotScheduleALateDeparture()
    {
        var h = new Harness(); h.Key('O'); h.Key('P'); h.AtStage(0); h.Tick();
        h.Wind(true); h.Tick(); h.Wind(false); h.Keys.Add('K'); h.Tick(); h.Keys.Clear();
        for (var tick = 0; tick < 40; tick++) Assert.Equal(0x800u, h.Tick());
        Assert.DoesNotContain(h.Speech, s => s.Contains("Moving to Upper shelter tree"));
        h.Wind(true); h.Tick(); h.Wind(false); h.Tick();
        Assert.Contains(h.Speech, s => s.Contains("Moving to Upper shelter tree"));
    }

    [Fact]
    public void ControllerMenuKeepsCoverAndConsumesAnyGustEndWhileItIsOpen()
    {
        var h = new Harness(); h.Key('O'); h.Key('P'); h.AtStage(0); h.Tick();
        h.Button(NavigationPadButtons.RightStick);
        Assert.True(h.Pad.IsOpen);
        h.Wind(true); h.Poll(); h.Tick(); h.Wind(false); h.Poll();
        Assert.Equal(0x800u, h.Tick());
        h.Button(NavigationPadButtons.East);
        Assert.False(h.Pad.IsOpen);
        for (var tick = 0; tick < 40; tick++) { h.Poll(); Assert.Equal(0x800u, h.Tick()); }
        Assert.DoesNotContain(h.Speech, s => s.Contains("Moving to Upper shelter tree"));
        h.Wind(true); h.Poll(); h.Tick(); h.Wind(false); h.Poll(); h.Tick();
        Assert.Contains(h.Speech, s => s.Contains("Moving to Upper shelter tree"));
    }

    [Fact]
    public void KeyboardDashCannotSilentlyCloseTheControllerMenuAtAGustEnd()
    {
        var h = new Harness(); h.Key('O'); h.Key('P'); h.AtStage(0); h.Tick();
        h.Button(NavigationPadButtons.RightStick);
        h.Wind(true); h.Poll(); h.Tick(); h.Wind(false); h.Poll();
        Assert.Equal(0x800u, h.Tick(8));
        Assert.True(h.Pad.IsOpen);
        Assert.DoesNotContain(h.Speech, s => s.Contains("Moving to Upper shelter tree"));
    }

    [Fact]
    public void StartingFromTheUpperShelterDoesNotRouteBackDownhill()
    {
        var h = new Harness(); h.AtStage(1); h.Key('O');
        Assert.Equal(0x800u, h.Key('P'));
        h.Wind(true); h.Tick(); h.Wind(false); h.Tick();
        Assert.Contains(h.Speech, s => s.Contains("Moving to Death Peak, Map 1"));
        Assert.DoesNotContain(h.Speech, s => s.Contains("Moving to Upper shelter tree"));
    }

    [Fact]
    public void TheNativeDollInteractionRemainsUnderPlayerConfirm()
    {
        var live = LiveMap();
        var map = DeathPeakNavigationTests.Map(2) with
            { ExitCells = live.ExitCells, ExitDestinations = live.ExitDestinations };
        var frame = DeathPeakNavigationTests.Frame(2, doll: DeathPeakNavigationTests.Doll(), map: map);
        var climb = Assert.Single(frame.Targets, t => t.Id == "death-peak:climb");
        Assert.Single(climb.AutomaticStages);
        Assert.Null(climb.ArrivalHold);
        var h = new Harness { Frame = frame with { Player = climb.ApproachPoints[0] }, Map = map };
        h.Key('O'); Assert.Equal(0u, h.Key('P'));
        Assert.Contains(h.Speech, s => s.Contains("Choose Doll in navigation"));
        h.Wind(true); Assert.Equal(0u, h.Tick()); h.Wind(false); Assert.Equal(0u, h.Tick());
    }

    [Fact]
    public void AJustTransformedTreeUsesItsTrunkEvenWhileTheCalmPatchRemains()
    {
        var live = LiveMap();
        var map = DeathPeakNavigationTests.Map(6, live: true) with
            { ExitCells = live.ExitCells, ExitDestinations = live.ExitDestinations };
        var frame = DeathPeakNavigationTests.Frame(6, map: map);
        var climb = Assert.Single(frame.Targets, t => t.Id == "death-peak:climb");
        Assert.NotNull(climb.ArrivalHold);
        Assert.Equal((14192, 14223), climb.ArrivalXRange);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    public void UnknownOrUnavailableLowerProtectionDoesNotOfferAClimb(int? flags)
    {
        Assert.DoesNotContain(DeathPeakNavigationTests.Frame(flags, map: LiveMap()).Targets,
            t => t.Id == "death-peak:climb");
    }

    [Fact]
    public void AnUnreadableExitCannotBeReplacedWithAnAssumedDestination()
    {
        Assert.DoesNotContain(DeathPeakNavigationTests.Frame(7, map: LiveMap() with { ExitDestinations = null }).Targets,
            t => t.Id == "death-peak:climb");
    }

    private sealed class NoEdges : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point) => [];
    }

    private sealed class Harness
    {
        public readonly List<string> Speech = [];
        public readonly HashSet<int> Keys = [];
        public readonly FieldNavigationRuntime Runtime;
        public readonly NavigationGamepad Pad = new();
        public NavigationFrame Frame = LiveFrame();
        public FieldMapSnapshot Map = LiveMap();
        public FieldFloorSnapshot? Floor = new(0x1000, 0x2000, 244, 6, 0, 16, 0, 1)
            { WindGusting = false, WindBuilding = false };
        public long Now;
        public bool Foreground = true, Unreadable;
        public Harness()
        {
            Runtime = new(_ => Unreadable ? null : Frame, new(Keys.Contains, () => Foreground), () => Foreground,
                () => Now, Speech.Add, _ => { }, gamepad: Pad,
                adjustFieldInput: (frame, result) => FieldFloorMovement.Adjust(frame, result, Floor),
                fieldFloor: () => Floor);
            Runtime.Enable(); Tick();
        }
        public uint Tick(uint original = 0)
        {
            var p = Frame.Player;
            var moving = FieldFloorMovement.FromFlags(Map.TerrainFlags[p.Y / 256 * Map.Width + p.X / 256]);
            Frame = Frame with { MovingFloor = moving };
            var speed = moving?.Speed ?? 0;
            if (Floor is { } value) Floor = value with
            {
                ForceX = moving?.Direction == NavigationDirection.West ? -speed : moving?.Direction == NavigationDirection.East ? speed : 0,
                ForceY = moving?.Direction == NavigationDirection.North ? -speed : moving?.Direction == NavigationDirection.South ? speed : 0,
            };
            Now += 16; return Runtime.OnInput(0x1000, original);
        }
        public uint Key(int key)
        {
            Keys.Clear(); Tick(); Keys.Add(key);
            var result = Tick(); Keys.Clear(); return result;
        }
        public void Wind(bool gusting) => Floor = Floor!.Value with { WindGusting = gusting, WindBuilding = false };
        public void Poll(NavigationPadButtons button = 0) => Runtime.FilterController(0, button, button == 0, true);
        public uint Button(NavigationPadButtons button) { Poll(); Tick(); Poll(button); return Tick(); }
        public void AtStage(int index)
        {
            var climb = Frame.Targets.Single(t => t.Id == "death-peak:climb");
            Frame = Frame with { Player = climb.AutomaticStages[index].ApproachPoints[0] };
        }
    }

    internal static FieldMapSnapshot LiveMap()
    {
        using var input = typeof(DeathPeakClimbAssistTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.death-peak-244-live-0371.json")!;
        using var json = JsonDocument.Parse(input);
        return json.RootElement.GetProperty("map").Deserialize<FieldMapSnapshot>()!;
    }

    internal static NavigationFrame LiveFrame() => DeathPeakNavigationTests.Frame(7,
        viewport: new(48 * 256, 52 * 256, 64 * 256, 64 * 256), map: LiveMap()) with
        { Player = new(14080, 15871, 1) };
}
