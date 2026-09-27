using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Core.Startup;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.Startup;

public sealed class OpeningMovieTimelineTests
{
    [Fact]
    public async Task VerifiedNarratedMovieDoesNotAlsoSpeakScreenReaderTimeline()
    {
        var events = new List<TimedDescription>();
        var timeline = new OpeningMovieTimeline(new ControlledDelay(), _ => Task.FromResult(true));
        await timeline.RunAsync(3, () => true, events.Add, CancellationToken.None);
        Assert.Empty(events);
    }

    [Fact]
    public async Task SkippingDuringMovieVerificationCannotReleaseAStaleDescription()
    {
        var verification = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<TimedDescription>();
        var timeline = new OpeningMovieTimeline(new ControlledDelay(), _ => verification.Task);
        using var cancellation = new CancellationTokenSource();
        var run = timeline.RunAsync(3, () => true, events.Add, cancellation.Token);
        cancellation.Cancel();
        verification.SetResult(false);
        await run;
        Assert.Empty(events);
    }

    [Fact]
    public async Task SlowVerificationResumesAtCurrentMovieTimeWithoutBurstingOldCues()
    {
        var clock = new ManualClock();
        var delay = new RecordingDelay();
        var verification = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<TimedDescription>();
        var timeline = new OpeningMovieTimeline(delay, _ => verification.Task, clock);
        using var cancellation = new CancellationTokenSource();
        var run = timeline.RunAsync(3, () => true, events.Add, cancellation.Token);
        clock.Ticks = TimeSpan.FromSeconds(12).Ticks;
        verification.SetResult(false);
        await delay.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(OpeningMovieTimeline.Entries[2].Text, Assert.Single(events).Text);
        Assert.Equal(TimeSpan.FromSeconds(3), delay.Requested);
        cancellation.Cancel();
        await run;
    }

    [Fact]
    public void ReviewedCuesStayOrderedWithinTheVerifiedMovieDuration()
    {
        var offsets = OpeningMovieTimeline.Entries.Select(entry => entry.Offset.TotalSeconds).ToArray();
        Assert.Equal(offsets.Order(), offsets);
        Assert.Equal(offsets.Length, offsets.Distinct().Count());
        Assert.All(offsets, offset => Assert.InRange(offset, 0, 158.358208));
    }

    [Fact]
    public async Task LeavingOpeningSceneCancelsEveryFutureLineImmediately()
    {
        var delay = new BlockingDelay();
        var timeline = new OpeningMovieTimeline(delay);
        var events = new List<TimedDescription>();
        using var cancellation = new CancellationTokenSource();

        var run = timeline.RunAsync(7, () => true, events.Add, cancellation.Token);
        Assert.Equal("Sunlight shines in a blue sky.", Assert.Single(events).Text);

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

    private sealed class ManualClock : TimeProvider
    {
        public long Ticks { get; set; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Ticks;
    }

    private sealed class RecordingDelay : IOpeningMovieDelay
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TimeSpan Requested { get; private set; }
        public async ValueTask WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Requested = delay;
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
