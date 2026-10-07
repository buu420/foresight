using System.Threading.Channels;

namespace ChronoTriggerAccessibility.Mod.Racing;

public enum RaceTone { Above, Aligned, Below }

/// <summary>A single voice owned exclusively by the race audio worker.</summary>
public interface IRaceToneOutput : IDisposable
{
    void Play(RaceTone tone);
    void Stop();
}

/// <summary>Posts only the latest position cue to a dedicated audio worker.
/// Device opening, playback, and cleanup never block a game hook.</summary>
public sealed class RaceTonePlayer : IDisposable
{
    private readonly Func<bool> foreground;
    private readonly Action<string>? diagnostic;
    private readonly Func<IRaceToneOutput> createOutput;
    private readonly Func<long> milliseconds;
    private readonly object scheduling = new();
    private readonly Channel<Request> queue = Channel.CreateBounded<Request>(
        new BoundedChannelOptions(1) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private long latest;
    private int started, disposed, failed;

    public RaceTonePlayer(Func<bool> foreground, Action<string>? diagnostic = null)
        : this(foreground, diagnostic, () => new RaceToneWaveOutput()) { }

    /// <summary>Injects the device boundary and monotonic clock without changing scheduling.</summary>
    public RaceTonePlayer(Func<bool> foreground, Action<string>? diagnostic,
        Func<IRaceToneOutput> createOutput, Func<long>? milliseconds = null)
    {
        ArgumentNullException.ThrowIfNull(foreground);
        ArgumentNullException.ThrowIfNull(createOutput);
        this.foreground = foreground;
        this.diagnostic = diagnostic;
        this.createOutput = createOutput;
        this.milliseconds = milliseconds ?? (() => Environment.TickCount64);
    }

    /// <summary>Returns whether a cue was accepted, not whether the device has played it.</summary>
    public bool Play(RaceTone tone)
    {
        if (Volatile.Read(ref disposed) != 0 || Volatile.Read(ref failed) != 0 ||
            tone is < RaceTone.Above or > RaceTone.Below) return false;
        if (!foreground()) { Stop(); return false; }
        lock (scheduling)
        {
            if (disposed != 0 || Volatile.Read(ref failed) != 0) return false;
            var version = Interlocked.Increment(ref latest);
            if (!queue.Writer.TryWrite(new Request(tone, milliseconds(), version))) return false;
            if (started == 0)
            {
                started = 1;
                _ = Task.Run(Run);
            }
            return true;
        }
    }

    public void Stop()
    {
        lock (scheduling)
        {
            if (disposed != 0) return;
            var version = Interlocked.Increment(ref latest);
            if (started != 0) queue.Writer.TryWrite(new Request(null, 0, version));
        }
    }

    /// <summary>Cancels pending cues immediately; the audio worker releases its device asynchronously.</summary>
    public void Dispose()
    {
        lock (scheduling)
        {
            if (disposed != 0) return;
            Volatile.Write(ref disposed, 1);
            Interlocked.Increment(ref latest);
            queue.Writer.TryComplete();
        }
    }

    private async Task Run()
    {
        try
        {
            using var output = createOutput();
            await foreach (var request in queue.Reader.ReadAllAsync())
            {
                if (Volatile.Read(ref disposed) != 0) break;
                if (request.Version != Volatile.Read(ref latest)) continue;
                if (request.Tone is not { } tone || !foreground() || milliseconds() - request.Time > 100)
                {
                    output.Stop();
                    continue;
                }
                // Focus checks and device initialization can take time; recheck
                // cancellation immediately before entering the driver.
                if (request.Version != Volatile.Read(ref latest) || Volatile.Read(ref disposed) != 0) continue;
                output.Play(tone);
                // A stop or focus change racing with waveOutWrite must reset the
                // just-started cue rather than let it outlive the race scene.
                if (request.Version != Volatile.Read(ref latest) || Volatile.Read(ref disposed) != 0 || !foreground())
                    output.Stop();
            }
        }
        catch (Exception error)
        {
            Volatile.Write(ref failed, 1);
            queue.Writer.TryComplete();
            try { diagnostic?.Invoke($"Race tones unavailable: {error.GetType().Name}: {error.Message}"); }
            catch { /* A diagnostic callback must not fault the audio worker. */ }
        }
    }

    private readonly record struct Request(RaceTone? Tone, long Time, long Version);
}
