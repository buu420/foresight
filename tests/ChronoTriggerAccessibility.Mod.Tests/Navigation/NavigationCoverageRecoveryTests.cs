using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Mod.Runtime;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class NavigationCoverageRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MenuCoverageFailureStopsMovementButDoesNotPermanentlySilenceNavigation(bool world)
    {
        var keys = new HashSet<int>();
        var speech = new List<string>();
        long now = 0;
        var point = new NavigationPoint(1152, 1024, 1);
        var frame = new NavigationFrame("test", true, new(1024, 1024, 1),
            [new("npc", "Shopkeeper", NavigationCategory.People, point, [point], true, true)],
            new WorldNavigationGraph(new byte[6144], new byte[512]), 128);
        var runtime = new FieldNavigationRuntime(_ => frame, new(keys.Contains, () => true),
            () => true, () => now, speech.Add, _ => { }, worldCapture: _ => frame);
        var sink = new RecordingDispatcher();
        var dispatcher = new NavigationDispatcher(sink, runtime);
        runtime.Enable();
        Tick();
        keys.Add('P');
        Assert.Equal(0x100u, Tick());

        dispatcher.ReportCoverageFailure("Shop selection could not be read.");

        Assert.Equal("Shop selection could not be read.", Assert.Single(sink.Failures));
        Assert.Equal(0u, runtime.ApplyWorldPad(1, 0));
        Assert.Equal(0u, Tick()); // Holding P through the error cannot restart walking.
        keys.Clear(); Tick();
        speech.Clear();
        keys.Add('K'); Tick();
        Assert.Contains(speech, text => text.Contains("Shopkeeper", StringComparison.Ordinal));
        keys.Clear(); Tick();
        keys.Add('P');
        Assert.Equal(0x100u, Tick());

        uint Tick()
        {
            now += 16;
            return world ? runtime.OnWorldInput(1, 0) : runtime.OnInput(1, 0);
        }
    }

    [Theory]
    [InlineData(false, "speech")]
    [InlineData(true, "speech")]
    [InlineData(false, "diagnostic")]
    [InlineData(true, "diagnostic")]
    public void AFailedStopAnnouncementStillClearsMovementAndReportsTheCoverageError(bool world, string failingCallback)
    {
        var keys = new HashSet<int>();
        long now = 0;
        var throwOnStop = false;
        var point = new NavigationPoint(1152, 1024, 1);
        var frame = new NavigationFrame("test", true, new(1024, 1024, 1),
            [new("npc", "Shopkeeper", NavigationCategory.People, point, [point], true, true)],
            new WorldNavigationGraph(new byte[6144], new byte[512]), 128);
        var runtime = new FieldNavigationRuntime(_ => frame, new(keys.Contains, () => true),
            () => true, () => now, _ => Callback("speech"), _ => Callback("diagnostic"), worldCapture: _ => frame);
        var sink = new RecordingDispatcher();
        var dispatcher = new NavigationDispatcher(sink, runtime);
        runtime.Enable();
        Tick();
        keys.Add('P');
        Assert.Equal(0x100u, Tick());

        throwOnStop = true;
        _ = Record.Exception(() => dispatcher.ReportCoverageFailure("Shop selection could not be read."));
        throwOnStop = false;

        Assert.Equal("Shop selection could not be read.", Assert.Single(sink.Failures));
        Assert.Equal(0u, runtime.ApplyWorldPad(1, 0));
        Assert.Equal(0u, Tick());
        keys.Clear(); Tick();
        keys.Add('P');
        Assert.Equal(0x100u, Tick());

        void Callback(string kind)
        {
            if (throwOnStop && kind == failingCallback) throw new InvalidOperationException("Stop callback failed.");
        }
        uint Tick()
        {
            now += 16;
            return world ? runtime.OnWorldInput(1, 0) : runtime.OnInput(1, 0);
        }
    }

    private sealed class RecordingDispatcher : ISemanticEventDispatcher
    {
        public List<string> Failures { get; } = [];
        public int Generation => 0;
        public void Attach(IRuntimePrismSession session) { }
        public void Detach(IRuntimePrismSession session) { }
        public void Publish(AccessibilityEvent accessibilityEvent) { }
        public void ReportCoverageFailure(string message) => Failures.Add(message);
    }
}
