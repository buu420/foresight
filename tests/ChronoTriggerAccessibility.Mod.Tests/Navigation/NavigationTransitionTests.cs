using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class NavigationTransitionTests
{
    [Fact]
    public void FieldWorldFieldSwitchesCaptureAndRearmsKeysEvenWhenPointersAreReused()
    {
        var keys = new HashSet<int>(); var speech = new List<string>(); long now = 0;
        var runtime = new FieldNavigationRuntime(_ => Frame("Inn", 128), new(keys.Contains, () => true),
            () => true, () => now, speech.Add, _ => { }, worldCapture: _ => Frame("World map", -128));
        runtime.Enable(); Tick(false);
        Assert.Equal(0x100u, Press(false, 'P'));
        Assert.Equal(0u, Tick(true)); // Held P across zoning cannot start a new route.
        Assert.Equal(0u, runtime.ApplyWorldPad(1, 0));
        Press(true, 'K'); Assert.Contains("World map", speech[^1]);
        Assert.Equal(0x200u, Press(true, 'P'));
        Assert.Equal(0x200u, runtime.ApplyWorldPad(1, 0));
        Assert.Equal(0u, Tick(false));
        Press(false, 'K'); Assert.Contains("Inn", speech[^1]);
        Assert.Equal(0u, runtime.ApplyWorldPad(1, 0));

        uint Tick(bool world) { now += 16; return world ? runtime.OnWorldInput(1, 0) : runtime.OnInput(1, 0); }
        uint Press(bool world, int key) { keys.Clear(); Tick(world); keys.Add(key); return Tick(world); }
    }

    [Fact]
    public void WorldPadNeverSurvivesManualInputSuspensionOrMissingCapture()
    {
        var keys = new HashSet<int>(); long now = 0; var available = true;
        var runtime = new FieldNavigationRuntime(_ => null, new(keys.Contains, () => true), () => true,
            () => now, _ => { }, _ => { }, worldCapture: _ => available ? Frame("World", 128) : null);
        runtime.Enable(); runtime.OnWorldInput(1, 0);
        keys.Add('P'); now += 16; runtime.OnWorldInput(1, 0);
        Assert.Equal(0x100u, runtime.ApplyWorldPad(1, 0));
        Assert.Equal(0x20u, runtime.ApplyWorldPad(1, 0x20));
        Assert.Equal(0u, runtime.ApplyWorldPad(1, 0));
        keys.Clear(); now += 16; runtime.OnWorldInput(1, 0);
        keys.Add('P'); now += 16; runtime.OnWorldInput(1, 0);
        runtime.Suspend("menu opened"); Assert.Equal(0u, runtime.ApplyWorldPad(1, 0));
        available = false; keys.Clear(); now += 16; runtime.OnWorldInput(1, 0);
        Assert.Equal(0u, runtime.ApplyWorldPad(1, 0));
    }

    [Fact]
    public void WorldMovementHasFootstepsAndSharesTheF8SettingWithFields()
    {
        var keys = new HashSet<int>(); var speech = new List<string>();
        long now = 0; int x = 0, plays = 0;
        var runtime = new FieldFootstepRuntime(_ => new(1, 12, 1, 0, 0), () => true, keys.Contains,
            () => now, () => plays++, () => { }, speech.Add, _ => { },
            worldCapture: _ => new(2, 496, 0, x, 0));
        runtime.Enable();
        for (var i = 0; i < 33; i++) { x = i * 16; now += 33; runtime.OnWorldInput(1, 0x100); }
        Assert.Equal(1, plays);
        keys.Add(0x77); now += 33; runtime.OnWorldInput(1, 0);
        Assert.Equal("Footsteps off.", speech[^1]);
        runtime.Suspend();
        for (var i = 0; i < 33; i++) { x = i * 16; now += 33; runtime.OnInput(1, 0x100); }
        Assert.Equal(1, plays);
        Assert.Single(speech);
    }

    private static NavigationFrame Frame(string name, int dx)
    {
        var point = new NavigationPoint(1024 + dx, 1024, 1);
        return new(name, true, new(1024, 1024, 1), [new(name, name, NavigationCategory.People,
            point, [point], true, true)], new WorldNavigationGraph(new byte[6144], new byte[512]), 128);
    }
}
