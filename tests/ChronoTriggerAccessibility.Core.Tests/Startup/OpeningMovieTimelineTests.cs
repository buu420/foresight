using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Core.Startup;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.Startup;

public sealed class OpeningMovieTimelineTests
{
    [Fact]
    public void EntriesMatchTheReviewedVisualTimeline()
    {
        Assert.Equal(
        [
            (TimeSpan.Zero, "A silver pendant spins in sunlight above the ocean."),
            (TimeSpan.FromSeconds(10), "A framed group portrait gives way to close-ups of a knight-like frog and several young adventurers."),
            (TimeSpan.FromSeconds(25), "A clock races across different eras, from ancient prehistory to medieval kingdoms."),
            (TimeSpan.FromSeconds(45), "A red-haired swordsman battles through forests as a huge dinosaur and a metal robot appear."),
            (TimeSpan.FromSeconds(65), "An inventor with purple hair smiles; a dark, caped sorcerer turns beneath the moon."),
            (TimeSpan.FromSeconds(85), "A princess in white appears as shadowy monsters gather."),
            (TimeSpan.FromSeconds(105), "The heroes charge across a bridge, battling with lightning and fire."),
            (TimeSpan.FromSeconds(135), "A glowing circular time gate opens, then a machine flares with light."),
            (TimeSpan.FromSeconds(150), "The Chrono Trigger title appears."),
        ],
        OpeningMovieTimeline.Entries.Select(entry => (entry.Offset, entry.Text)));
    }

    [Fact]
    public async Task LeavingOpeningSceneCancelsEveryFutureLineImmediately()
    {
        var delay = new BlockingDelay();
        var timeline = new OpeningMovieTimeline(delay);
        var events = new List<TimedDescription>();
        using var cancellation = new CancellationTokenSource();

        var run = timeline.RunAsync(7, () => true, events.Add, cancellation.Token);
        Assert.Equal("A silver pendant spins in sunlight above the ocean.", Assert.Single(events).Text);

        cancellation.Cancel();
        await run;

        Assert.Single(events);
        Assert.True(delay.WasCancelled);
    }

    [Fact]
    public async Task SkipDirectlyToTitleRejectsAReleasedButNoLongerAuthoritativeLine()
    {
        var delay = new ControlledDelay();
        var timeline = new OpeningMovieTimeline(delay);
        var events = new List<TimedDescription>();
        var openingIsAuthoritative = true;

        var run = timeline.RunAsync(12, () => openingIsAuthoritative, events.Add, CancellationToken.None);
        Assert.Single(events);

        openingIsAuthoritative = false;
        delay.Release();
        await run;

        Assert.Single(events);
    }

    private sealed class BlockingDelay : IOpeningMovieDelay
    {
        public bool WasCancelled { get; private set; }

        public async ValueTask WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                WasCancelled = true;
                throw;
            }
        }
    }

    private sealed class ControlledDelay : IOpeningMovieDelay
    {
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => completion.TrySetResult();
        public ValueTask WaitAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            new(completion.Task.WaitAsync(cancellationToken));
    }
}
