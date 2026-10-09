using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class FieldNavigationRuntimeTests
{
    [Fact]
    public void HeldNativeDashKeepsWalkingAndPreservesThePlayersDashBit()
    {
        var h = new Harness(); h.Enable(); h.Press('P');
        Assert.Equal(0x108u, h.Tick(8));
        Assert.Equal(0x108u, h.Tick(8));
        Assert.Equal(0x100u, h.Tick());
        Assert.Equal(0x88u, h.Tick(0x88)); // Confirm still cancels, including while Dash is held.
        Assert.Equal(0u, h.Tick());
    }
    [Fact]
    public void GuidanceNeverInjectsInputAndWalkingOnlyAddsNativeDirectionBits()
    {
        var h = new Harness(); h.Enable();
        Assert.Equal(0u, h.Press('I'));
        Assert.Contains(h.Speech, s => s.Contains("Guidance to"));
        Assert.Equal(0x100u, h.Press('P'));
        Assert.Equal(0x100u, h.Tick());
        Assert.Equal(0u, h.Press('P'));
        Assert.Equal(0u, h.Tick());
    }

    [Theory]
    [InlineData("foreground")]
    [InlineData("gap")]
    [InlineData("engine")]
    [InlineData("menu")]
    [InlineData("capture")]
    [InlineData("manual")]
    [InlineData("disable")]
    public void EveryLossOfMovementAuthorityStopsWithoutAutomaticResumption(string cause)
    {
        var h = new Harness(); h.Enable(); Assert.Equal(0x100u, h.Press('P'));
        switch (cause)
        {
            case "foreground": h.Foreground = false; break;
            case "gap": h.Now += 1000; break;
            case "engine": h.Engine = 2; break;
            case "menu": h.Runtime.Suspend("menu opened"); break;
            case "capture": h.FailCapture = true; break;
            case "disable": h.Runtime.Disable(); break;
        }
        var original = cause == "manual" ? 0x800u : 0u;
        Assert.Equal(original, h.Tick(original));
        h.Foreground = true; h.FailCapture = false;
        Assert.Equal(0u, h.Tick());
        Assert.Equal(0u, h.Tick());
    }

    [Fact]
    public void NoCaptureWorkIsDoneUntilRequestedAndFailuresRemainReviewable()
    {
        var h = new Harness(); h.Enable(); h.Tick();
        Assert.Equal(0, h.Captures);
        h.FailCapture = true;
        Assert.Equal(0u, h.Press('K'));
        Assert.Contains(h.Speech, s => s.Contains("unavailable"));
        h.Tick(); Assert.Equal(1, h.Captures);
    }

    [Fact]
    public void LogsRequestedRoutesAndThrottledProgressIncludingTheBlockedPosition()
    {
        var h = new Harness(); h.Enable();
        for (var i = 0; i < 40; i++) h.Tick();
        Assert.Empty(h.Diagnostics);
        h.Press('P');
        Assert.Contains(h.Diagnostics, s => s.Contains("command=ToggleWalk") && s.Contains("player=(0,0,1)") &&
            s.Contains("target=person") && s.Contains("goal=(16,0,1)") && s.Contains("pad=0x100"));
        for (var i = 0; i < 100; i++) h.Tick();
        Assert.Contains(h.Diagnostics, s => s.Contains("movement is blocked") && s.Contains("player=(0,0,1)"));
        Assert.InRange(h.Diagnostics.Count, 3, 10);
        var count = h.Diagnostics.Count;
        for (var i = 0; i < 40; i++) h.Tick();
        Assert.Equal(count, h.Diagnostics.Count);
    }

    [Fact]
    public void SlowIdleObservationDoesNotDiscardTheNextKeyPress()
    {
        long now = 0;
        var keys = new HashSet<int>();
        var speech = new List<string>();
        var target = new NavigationPoint(16, 0, 1);
        var runtime = new FieldNavigationRuntime(_ => new("room", true, new(0, 0, 1),
            [new("person", "Person", NavigationCategory.People, target, [target], true, false)], new Line()),
            new(keys.Contains, () => true), () => true, () => now, speech.Add, _ => { },
            _ => now += 600);
        runtime.Enable();
        runtime.OnInput(1, 0);
        now += 16;
        keys.Add('K');
        runtime.OnInput(1, 0);
        Assert.Contains(speech, s => s.Contains("Person"));
    }

    [Fact]
    public void EveryWorldCombineSiteReceivesTheDirectionAndDeliveryIsReported()
    {
        // 264C40 rebuilds the pad at three combine sites and the direction dispatch reads
        // whichever one ran last, so all three have to see the synthetic direction.
        long now = 0;
        var keys = new HashSet<int>();
        var diagnostics = new List<string>();
        var target = new NavigationPoint(16, 0, 1);
        var runtime = new FieldNavigationRuntime(_ => null,
            new(keys.Contains, () => true), () => true, () => now, _ => { }, diagnostics.Add,
            worldCapture: _ => new("world:1:0", true, new(0, 0, 1),
                [new("exit", "Exit", NavigationCategory.People, target, [target], true, false)],
                new Line()),
            worldObserve: _ => { });
        runtime.Enable();

        // A combine site that runs before any world tick must not inject anything.
        Assert.Equal(0u, runtime.ApplyWorldPad(1, 0));
        runtime.OnWorldInput(1, 0);
        keys.Add('L');
        runtime.OnWorldInput(1, 0);
        keys.Clear();
        keys.Add('P');
        var accepted = runtime.OnWorldInput(1, 0);
        keys.Clear();

        Assert.NotEqual(0u, accepted);
        Assert.Equal(accepted, runtime.ApplyWorldPad(1, 0));
        Assert.Equal(accepted, runtime.ApplyWorldPad(1, 0));
        Assert.Equal(accepted, runtime.ApplyWorldPad(1, 0));
        // A foreign world context never receives this context's direction.
        Assert.Equal(0u, runtime.ApplyWorldPad(2, 0));
        // A physical pad at a combine site is manual control, never an injection.
        Assert.Equal(0x20u, runtime.ApplyWorldPad(1, 0x20));

        var report = diagnostics.Last(line => line.Contains("worldPad=", StringComparison.Ordinal));
        Assert.Contains("worldPad=calls:1,applied:0,manual:0", report, StringComparison.Ordinal);
        Assert.Contains("worldPadReject=not world mode", report, StringComparison.Ordinal);
    }

    private sealed class Harness
    {
        public HashSet<int> Keys { get; } = [];
        public List<string> Speech { get; } = [];
        public List<string> Diagnostics { get; } = [];
        public bool Foreground = true;
        public bool FailCapture;
        public long Now;
        public nint Engine = 1;
        public int Captures;
        public FieldNavigationRuntime Runtime { get; }
        public Harness() => Runtime = new(_ =>
        {
            Captures++;
            if (FailCapture) return null;
            var point = new NavigationPoint(16, 0, 1);
            return new("room", true, new(0, 0, 1), [new("person", "Person", NavigationCategory.People,
                point, [point], true, false)], new Line());
        }, new(Keys.Contains, () => Foreground), () => Foreground, () => Now, Speech.Add, Diagnostics.Add);
        public void Enable() { Runtime.Enable(); Tick(); }
        public uint Tick(uint input = 0) { Now += 16; return Runtime.OnInput(Engine, input); }
        public uint Press(int key) { Keys.Clear(); Tick(); Keys.Add(key); var value = Tick(); Keys.Clear(); return value; }
    }

    private sealed class Line : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point)
        {
            if (point.X < 16) yield return point with { X = point.X + 4 };
        }
    }
}
