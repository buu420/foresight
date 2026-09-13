using System.Threading.Channels;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Owns a background audio worker. Game hooks only post a bounded request;
/// wave loading and device calls never run on the game's input thread.</summary>
public sealed class FootstepSound(Action<string> diagnostic, Action? unavailable = null,
    Func<IFootstepOutput>? createOutput = null, Action? timingInterrupted = null)
{
    private readonly Channel<(bool Play, long Time, long Generation)> queue =
        Channel.CreateBounded<(bool, long, long)>(new BoundedChannelOptions(32)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private int started;
    private long generation;
    private readonly object scheduling = new();
    private long nextBeat;
    private int interrupted, failed;

    public void WarmUp()
    {
        if (Interlocked.Exchange(ref started, 1) == 0) _ = Task.Run(Run);
    }

    public void Play()
    {
        WarmUp();
        if (Volatile.Read(ref failed) != 0) throw new InvalidOperationException("Footstep audio is unavailable.");
        lock (scheduling)
        {
            var due = Math.Max(Environment.TickCount64, nextBeat);
            if (queue.Writer.TryWrite((true, due, Volatile.Read(ref generation)))) nextBeat = due + 45;
            else InterruptTiming();
        }
    }

    public void Stop()
    {
        lock (scheduling)
        {
            nextBeat = 0;
            var current = Interlocked.Increment(ref generation);
            if (Volatile.Read(ref started) != 0) queue.Writer.TryWrite((false, 0, current));
        }
    }

    private void InterruptTiming()
    {
        Interlocked.Exchange(ref interrupted, 1);
        Stop();
    }

    private async Task Run()
    {
        var index = 0;
        long processedGeneration = 0, lastBeat = -1;
        long accepted = 0, rejected = 0, stale = 0, stops = 0, lastDiagnostic = -1;
        try
        {
            using var output = createOutput?.Invoke() ?? new FootstepWaveOutput();
            diagnostic("Footsteps audio: loaded 5 embedded waves; 8 dedicated waveOut voices.");
            await foreach (var request in queue.Reader.ReadAllAsync())
            {
                var now = Environment.TickCount64;
                var currentGeneration = Volatile.Read(ref generation);
                if (processedGeneration != currentGeneration)
                {
                    output.Stop();
                    processedGeneration = currentGeneration;
                    stops++;
                }
                if (Interlocked.Exchange(ref interrupted, 0) != 0)
                {
                    diagnostic("Footstep timing interrupted; pending count cancelled. Playback can resume.");
                    timingInterrupted?.Invoke();
                }
                if (!request.Play) continue;
                if (request.Generation != currentGeneration) { stale++; continue; }
                // The timestamp is the scheduled attack, including deliberate spacing.
                // Our own pacing must not make later beats in a batch look stale.
                var attackTime = Math.Max(request.Time, lastBeat < 0 ? request.Time : lastBeat + 45);
                while (attackTime > Environment.TickCount64 &&
                    request.Generation == Volatile.Read(ref generation))
                    await Task.Delay((int)Math.Clamp(attackTime - Environment.TickCount64, 1, 15));
                if (request.Generation != Volatile.Read(ref generation)) { stale++; continue; }
                now = Environment.TickCount64;
                if (now - request.Time > 150) { stale++; InterruptTiming(); }
                else
                {
                    var played = false;
                    while (request.Generation == Volatile.Read(ref generation) &&
                        Environment.TickCount64 - request.Time <= 150)
                    {
                        if (output.TryPlay(index)) { played = true; break; }
                        await Task.Delay(5);
                    }
                    if (!played) { rejected++; if (request.Generation == Volatile.Read(ref generation)) InterruptTiming(); }
                    else { accepted++; lastBeat = Environment.TickCount64; index = (index + 1) % 5; }
                }
                if (lastDiagnostic < 0 || now - lastDiagnostic >= 2000)
                {
                    diagnostic($"Footsteps audio: accepted={accepted}; rejected={rejected}; " +
                        $"stale={stale}; stops={stops}; requestAge={(request.Play ? now - request.Time : 0)}ms.");
                    lastDiagnostic = now;
                }
            }
        }
        catch (Exception error)
        {
            Volatile.Write(ref failed, 1);
            queue.Writer.TryComplete(error);
            diagnostic($"Footstep audio unavailable: {error.GetType().Name}: {error.Message}");
            unavailable?.Invoke();
        }
    }
}
