using System.Collections.Concurrent;
using ChronoTriggerAccessibility.Mod.Racing;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Racing;

public sealed class RaceTonePlayerTests
{
    [Theory]
    [InlineData(RaceTone.Above, 960)]
    [InlineData(RaceTone.Aligned, 640)]
    [InlineData(RaceTone.Below, 320)]
    public void PositionTonesHaveOrderedPitchesAndShortClickFreeModestWaveforms(RaceTone tone, int frequency)
    {
        var samples = RaceToneWaveform.Create(tone);
        Assert.Equal(3360, samples.Length); // 70ms at 48kHz.
        Assert.Equal(0, samples[0]);
        Assert.Equal(0, samples[^1]);
        Assert.InRange(samples.Max(value => Math.Abs((int)value)), 3000, 6000);
        var pitch = new[] { 320, 640, 960 }.MaxBy(candidate => Magnitude(samples, candidate));
        Assert.Equal(frequency, pitch);
        Assert.True(Magnitude(samples, frequency) > 1000, "The intended pitch must be audible.");
    }

    [Fact]
    public void AlignedToneHasAnAudibleHarmonicThatDistinguishesItFromDirectionalTones()
    {
        var samples = RaceToneWaveform.Create(RaceTone.Aligned);
        Assert.InRange(Magnitude(samples, 1280) / Magnitude(samples, 640), 0.25, 0.45);
        Assert.True(Magnitude(RaceToneWaveform.Create(RaceTone.Above), 1920) < 20);
        Assert.True(Magnitude(RaceToneWaveform.Create(RaceTone.Below), 640) < 20);
    }

    [Fact]
    public void BackgroundAndInvalidRequestsDoNotOpenAnAudioDevice()
    {
        var opens = 0;
        using var player = new RaceTonePlayer(() => false, null,
            () => { Interlocked.Increment(ref opens); return new Output(); });
        Assert.False(player.Play(RaceTone.Above));
        Assert.False(player.Play((RaceTone)99));
        Assert.Equal(0, opens);
    }

    [Fact]
    public async Task PendingRequestsKeepOnlyTheLatestPositionWhileDeviceOpens()
    {
        using var opening = new Opening();
        using var player = new RaceTonePlayer(() => true, null, opening.Create, () => 0);
        Assert.True(player.Play(RaceTone.Above));
        await opening.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        for (var index = 0; index < 100; index++) Assert.True(player.Play(RaceTone.Aligned));
        Assert.True(player.Play(RaceTone.Below));
        opening.Release.Set();
        await opening.Output.Played.Task.WaitAsync(TimeSpan.FromSeconds(3));
        player.Dispose();
        await opening.Output.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal([RaceTone.Below], opening.Output.Tones.ToArray());
    }

    [Fact]
    public async Task StopCancelsPendingToneAndPlaybackCanResume()
    {
        using var opening = new Opening();
        using var player = new RaceTonePlayer(() => true, null, opening.Create, () => 0);
        player.Play(RaceTone.Above);
        await opening.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        player.Stop();
        opening.Release.Set();
        await opening.Output.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Empty(opening.Output.Tones);
        Assert.True(player.Play(RaceTone.Aligned));
        await opening.Output.Played.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal([RaceTone.Aligned], opening.Output.Tones.ToArray());
    }

    [Fact]
    public async Task ForegroundIsCheckedAgainBeforeAQueuedToneReachesTheDevice()
    {
        using var opening = new Opening();
        var foreground = true;
        using var player = new RaceTonePlayer(() => Volatile.Read(ref foreground), null, opening.Create, () => 0);
        player.Play(RaceTone.Above);
        await opening.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Volatile.Write(ref foreground, false);
        opening.Release.Set();
        await opening.Output.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Empty(opening.Output.Tones);
    }

    [Fact]
    public async Task OldToneIsDiscardedAfterASlowDeviceOpen()
    {
        using var opening = new Opening();
        long now = 0;
        using var player = new RaceTonePlayer(() => true, null, opening.Create, () => Volatile.Read(ref now));
        player.Play(RaceTone.Above);
        await opening.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Volatile.Write(ref now, 151);
        opening.Release.Set();
        await opening.Output.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Empty(opening.Output.Tones);
        Assert.True(player.Play(RaceTone.Below));
        await opening.Output.Played.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal([RaceTone.Below], opening.Output.Tones.ToArray());
    }

    [Fact]
    public async Task DisposeCancelsPendingToneWithoutWaitingForAudioDriver()
    {
        using var opening = new Opening();
        var player = new RaceTonePlayer(() => true, null, opening.Create, () => 0);
        player.Play(RaceTone.Above);
        await opening.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        try
        {
            // Opening is blocked; joining the worker would time out here.
            await Task.Run(player.Dispose).WaitAsync(TimeSpan.FromSeconds(3));
            player.Dispose();
            Assert.False(player.Play(RaceTone.Below));
            player.Stop();
        }
        finally { opening.Release.Set(); }
        await opening.Output.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Empty(opening.Output.Tones);
    }

    [Fact]
    public async Task DriverFailureIsReportedAndFurtherRequestsAreRejected()
    {
        var diagnostic = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var player = new RaceTonePlayer(() => true, message => diagnostic.TrySetResult(message),
            () => throw new InvalidOperationException("device unavailable"));
        Assert.True(player.Play(RaceTone.Above));
        var message = await diagnostic.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Contains("device unavailable", message);
        Assert.False(player.Play(RaceTone.Below));
    }

    // Correlate an interior window with independent sine/cosine references so fade
    // edges cannot masquerade as a distinct pitch or harmonic.
    private static double Magnitude(short[] samples, int frequency)
    {
        double real = 0, imaginary = 0;
        const int start = 480, count = 2400;
        for (var i = start; i < start + count; i++)
        {
            var phase = 2 * Math.PI * frequency * i / 48000;
            real += samples[i] * Math.Cos(phase);
            imaginary += samples[i] * Math.Sin(phase);
        }
        return 2 * Math.Sqrt(real * real + imaginary * imaginary) / count;
    }

    private sealed class Opening : IDisposable
    {
        public ManualResetEventSlim Release { get; } = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Output Output { get; } = new();
        public IRaceToneOutput Create()
        {
            Entered.TrySetResult();
            Release.Wait();
            return Output;
        }
        public void Dispose() { Release.Set(); }
    }

    private sealed class Output : IRaceToneOutput
    {
        public ConcurrentQueue<RaceTone> Tones { get; } = new();
        public TaskCompletionSource Played { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Play(RaceTone tone) { Tones.Enqueue(tone); Played.TrySetResult(); }
        public void Stop() { Stopped.TrySetResult(); }
        public void Dispose() { Disposed.TrySetResult(); }
    }
}
