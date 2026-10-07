using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class FieldFloorRuntimeTests
{
    [Fact]
    public void IdleFieldInputObservesEveryTickAndLeavesThePhysicalPadIntact()
    {
        var speech = new List<string>();
        var keys = new HashSet<int>();
        var observations = 0;
        var foreground = true;
        long now = 0;
        var runtime = new FieldNavigationRuntime(_ => Frame(), new(keys.Contains, () => foreground),
            () => foreground, () => now, speech.Add, _ => { },
            observeFieldInput: _ => observations++, fieldStatus: () => "Conveyor moving east.");
        runtime.Enable();
        for (var i = 0; i < 20; i++) { Assert.Equal(0x400u, runtime.OnInput(0x1000, 0x400)); now += 16; }
        Assert.Equal(20, observations);
        foreground = false;
        Assert.Equal(0x100u, runtime.OnInput(0x1000, 0x100));
        Assert.Equal(20, observations);
        Assert.Empty(speech);
    }

    [Fact]
    public void RepeatReadsTheBeltDirectionWithTheSelectedTarget()
    {
        var speech = new List<string>();
        var keys = new HashSet<int>();
        long now = 0;
        var runtime = new FieldNavigationRuntime(_ => Frame(), new(keys.Contains, () => true),
            () => true, () => now, speech.Add, _ => { }, fieldStatus: () => "Conveyor moving east.");
        runtime.Enable(); runtime.OnInput(0x1000, 0); now += 16;
        keys.Add('K');
        Assert.Equal(0u, runtime.OnInput(0x1000, 0));
        Assert.Contains(speech, s => s.Contains("Destination") && s.Contains("Conveyor moving east"));
    }

    [Fact]
    public void ARejectedFloorRunStopsTheRouteAndDoesNotResumeIt()
    {
        var speech = new List<string>();
        var keys = new HashSet<int>();
        long now = 0;
        var reject = true;
        var runtime = new FieldNavigationRuntime(_ => Frame(), new(keys.Contains, () => true),
            () => true, () => now, speech.Add, _ => { },
            adjustFieldInput: (_, result) => reject ? new(0, "running is required on this conveyor") :
                new(FieldNavigationRuntime.DirectionBits(result.Direction)));
        runtime.Enable(); runtime.OnInput(0x1000, 0); now += 16;
        keys.Add('P');
        Assert.Equal(0u, runtime.OnInput(0x1000, 0));
        Assert.Contains(speech, s => s.Contains("running is required"));
        keys.Clear(); reject = false; now += 16;
        Assert.Equal(0u, runtime.OnInput(0x1000, 0));
        now += 16;
        Assert.Equal(0u, runtime.OnInput(0x1000, 0));
    }

    private static NavigationFrame Frame()
    {
        var goal = new NavigationPoint(256, 0, 1);
        return new("field", true, new(0, 0, 1),
            [new("goal", "Destination", NavigationCategory.People, goal, [goal], true, true)], new Line(), 256);
    }
    private sealed class Line : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point)
        {
            if (point.X < 512) yield return point with { X = point.X + 64 };
            if (point.X > 0) yield return point with { X = point.X - 64 };
        }
    }
}
