using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class VehicleNavigationRuntimeTests
{
    [Fact]
    public void AParkedVehicleTickNeverPollsResetsOrCancelsTheWalkingController()
    {
        var h = new Harness();
        h.WalkTick();
        Assert.Equal(0x100u, h.Press(false, 'P'));
        Assert.Equal(0x100u, h.Runtime.ApplyWorldPad(1, 0));
        // The Epoch task ticks every frame while parked. Nothing about the walking
        // route may change, and no capture or keyboard poll happens for it.
        var captures = h.VehicleCaptures; var polls = h.Polls;
        Assert.False(h.Runtime.OnVehicleInput(1, 0, VehicleKind.Epoch));
        Assert.False(h.Runtime.OnVehicleInput(1, 0, VehicleKind.Dactyl));
        Assert.Equal(captures, h.VehicleCaptures);
        Assert.Equal(polls, h.Polls);
        Assert.Equal(0x100u, h.Runtime.ApplyWorldPad(1, 0));
        Assert.Equal(0u, h.Runtime.ApplyVehiclePad(1, 0, VehicleKind.Epoch));
        Assert.DoesNotContain(h.Speech, s => s.Contains("stopped"));
    }

    [Fact]
    public void FlightUsesTheSameKeysInjectsOnlyAtItsOwnSitesAndCancelsOnManualInput()
    {
        var h = new Harness { Flying = VehicleKind.Epoch };
        Assert.True(h.Runtime.OnVehicleInput(1, 0, VehicleKind.Epoch));
        h.Keys.Add('K'); h.VehicleTick(); h.Keys.Clear();
        Assert.Contains(h.Speech, s => s.Contains("Landing"));
        Assert.Equal(0u, h.Runtime.ApplyVehiclePad(1, 0, VehicleKind.Epoch));
        h.Keys.Add('P'); var accepted = h.VehicleTick(); h.Keys.Clear();
        Assert.Equal(0x100u, accepted);
        Assert.Equal(0x100u, h.Runtime.ApplyVehiclePad(1, 0, VehicleKind.Epoch));
        Assert.Equal(0u, h.Runtime.ApplyVehiclePad(1, 0, VehicleKind.Dactyl));
        Assert.Equal(0u, h.Runtime.ApplyWorldPad(1, 0));
        Assert.Equal(0x800u, h.Runtime.ApplyVehiclePad(1, 0x800, VehicleKind.Epoch));
        Assert.Contains(h.Speech, s => s.Contains("manual control"));
        Assert.Equal(0u, h.VehicleTick());
        Assert.Contains(h.Diagnostics, d => d.Contains("mode=Epoch") && d.Contains("worldPad="));
    }

    [Fact]
    public void FlightNeverSynchronizesAFootstepLegAndModeChangesResetMotion()
    {
        var h = new Harness { Flying = VehicleKind.Dactyl };
        h.VehicleTick();
        h.Keys.Add('I'); h.VehicleTick(); h.Keys.Clear();
        Assert.Contains(h.Speech, s => s.Contains("Guidance to"));
        Assert.All(h.Legs, leg => Assert.Null(leg));
        Assert.NotEmpty(h.Legs);
        var resets = h.MotionResets;
        h.Flying = null;
        h.WalkTick();
        Assert.True(h.MotionResets > resets);
        Assert.Contains(h.Speech, s => s.Contains("area changed"));
        h.Keys.Add('I'); h.WalkTick(); h.Keys.Clear();
        Assert.Contains(h.Legs, leg => leg is not null);
    }

    [Fact]
    public void LandingEndsFlightControlWithoutStrandingAStaleDirection()
    {
        var h = new Harness { Flying = VehicleKind.Epoch };
        h.VehicleTick();
        h.Keys.Add('P'); h.VehicleTick(); h.Keys.Clear();
        Assert.Equal(0x100u, h.Runtime.ApplyVehiclePad(1, 0, VehicleKind.Epoch));
        h.Flying = null; // the flying bit cleared: the task keeps ticking but is no longer the transport
        Assert.False(h.Runtime.OnVehicleInput(1, 0, VehicleKind.Epoch));
        Assert.Equal(0u, h.Runtime.ApplyVehiclePad(1, 0, VehicleKind.Epoch));
        Assert.Equal(0u, h.WalkTick());
        Assert.Equal(0u, h.Runtime.ApplyWorldPad(1, 0));
    }

    private sealed class Harness
    {
        public HashSet<int> Keys { get; } = [];
        public List<string> Speech { get; } = [];
        public List<string> Diagnostics { get; } = [];
        public List<NavigationLeg?> Legs { get; } = [];
        public long Now;
        public int VehicleCaptures, Polls, MotionResets;
        public VehicleKind? Flying;
        public FieldNavigationRuntime Runtime { get; }

        public Harness()
        {
            Runtime = new(_ => Frame("field", NavigationUnits.LocalStep, "person"),
                new(key => { Polls++; return Keys.Contains(key); }, () => true), () => true, () => Now,
                Speech.Add, Diagnostics.Add, resetMotion: () => MotionResets++,
                worldCapture: _ => Frame("world:1:0", NavigationUnits.WorldStep, "Truce Inn"),
                synchronizeFootsteps: Legs.Add,
                vehicleCapture: (_, kind) => { VehicleCaptures++; return Frame($"world:1:0:{kind}", NavigationUnits.WorldStep, "Landing near Truce Inn"); },
                vehicleActive: (_, kind) => Flying == kind);
            Runtime.Enable();
        }

        public uint WalkTick(uint pad = 0) { Now += 16; return Runtime.OnWorldInput(1, pad); }
        public uint VehicleTick(uint pad = 0)
        {
            Now += 16;
            var kind = Flying ?? VehicleKind.Epoch;
            return Runtime.OnVehicleInput(1, pad, kind) ? Runtime.ApplyVehiclePad(1, pad, kind) : pad;
        }
        public uint Press(bool vehicle, int key)
        {
            Keys.Clear(); if (vehicle) VehicleTick(); else WalkTick();
            Keys.Add(key); var value = vehicle ? VehicleTick() : WalkTick(); Keys.Clear(); return value;
        }

        private static NavigationFrame Frame(string scene, int units, string label)
        {
            var target = new NavigationPoint(1024 + 4 * units, 1024, 1);
            return new(scene, true, new(1024, 1024, 1),
                [new(label, label, NavigationCategory.People, target, [target], true, true)], new Line(units), units);
        }
    }

    private sealed class Line(int units) : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point)
        {
            if (point.X < 1024 + 4 * units) yield return point with { X = point.X + units };
            if (point.X > 1024) yield return point with { X = point.X - units };
        }
    }
}
