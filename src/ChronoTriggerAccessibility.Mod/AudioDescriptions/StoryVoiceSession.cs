using ChronoTriggerAccessibility.Mod.Runtime;

namespace ChronoTriggerAccessibility.Mod.AudioDescriptions;

/// <summary>Orders recorded descriptions and following Prism announcements. The wave
/// driver reports completion of our recordings; it does not report NVDA completion.</summary>
public sealed class StoryVoiceSession(IRuntimePrismSession inner, Func<IStoryVoiceOutput> output,
    Func<StoryNarration, StoryVoiceClip> clip, Func<bool> foreground, Action<string> diagnostic,
    Func<long>? milliseconds = null, Action<string>? coverageFailure = null) : IRuntimePrismSession
{
    private readonly object gate = new();
    private readonly Queue<Pending> pending = new();
    private IStoryVoiceOutput? wave;
    private Task? worker;
    private bool running, disposed, voiceActive;
    private int voiceEpoch, speechEpoch;
    private readonly Func<long> clock = milliseconds ?? (() => Environment.TickCount64);

    public void OutputAfterNarration(string text, bool interrupt)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!running) { inner.Output(text, interrupt); return; }
            // The preceding question may still be unheard behind the recording.
            // Preserve it, and queue the initial choices without cancelling it in NVDA.
            pending.Enqueue(new(null, text, false, speechEpoch));
        }
    }

    public string BackendName => inner.BackendName;

    public void Narrate(StoryNarration cue)
    {
        ArgumentNullException.ThrowIfNull(cue);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            pending.Enqueue(new(cue, null, false, voiceEpoch));
            StartWorker();
        }
    }

    public void Output(string text, bool interrupt)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (interrupt)
            {
                voiceEpoch++;
                speechEpoch++;
                pending.Clear();
                StopVoice();
            }
            if (!running || interrupt) inner.Output(text, interrupt);
            else pending.Enqueue(new(null, text, interrupt, speechEpoch));
        }
    }

    public void CancelNarration()
    {
        lock (gate)
        {
            if (disposed) return;
            voiceEpoch++;
            var speech = pending.Where(item => item.Cue is null).ToArray();
            pending.Clear();
            foreach (var item in speech) pending.Enqueue(item);
            StopVoice();
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            voiceEpoch++;
            pending.Clear();
            speechEpoch++;
            StopVoice();
            inner.Stop();
        }
    }

    public void Dispose()
    {
        Task? finishing;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            voiceEpoch++;
            pending.Clear();
            StopVoice();
            finishing = worker;
        }
        finishing?.GetAwaiter().GetResult();
        try { wave?.Dispose(); }
        catch (Exception exception) { Diagnose($"Story voice cleanup failed: {exception.Message}"); }
        finally { inner.Dispose(); }
    }

    private void StartWorker()
    {
        if (running) return;
        running = true;
        worker = Task.Run(Process);
    }

    private async Task Process()
    {
        while (true)
        {
            Pending item;
            lock (gate)
            {
                if (disposed || pending.Count == 0) { running = false; return; }
                item = pending.Dequeue();
            }
            if (item.Cue is not null) await Play(item);
            else
            {
                string? failure = null;
                lock (gate)
                {
                    if (disposed || item.Epoch != speechEpoch) continue;
                    try { inner.Output(item.Text!, item.Interrupt); }
                    catch (Exception exception) { failure = $"Deferred Prism announcement failed: {exception.Message}"; }
                }
                if (failure is not null) ReportFailure(failure);
            }
        }
    }

    private async Task Play(Pending item)
    {
        try
        {
            var recording = clip(item.Cue!);
            long deadline;
            lock (gate)
            {
                if (disposed || item.Epoch != voiceEpoch || !foreground()) return;
                wave ??= output();
                try { inner.Braille(item.Cue!.Text); }
                catch (Exception exception) { Diagnose($"Story description braille failed: {exception.Message}"); }
                deadline = clock() + checked((long)Math.Ceiling(recording.Duration * 1000)) + 2000;
                if (!wave.TryPlay(recording.Wave)) throw new IOException("The story voice device is busy.");
                voiceActive = true;
                Diagnose($"Story voice: cue={item.Cue!.CueId}; text={recording.Text}");
            }
            while (true)
            {
                lock (gate)
                {
                    if (disposed || item.Epoch != voiceEpoch) return;
                    if (!foreground()) { CancelNarration(); return; }
                    if (!wave.Playing) { voiceActive = false; return; }
                    if (clock() >= deadline) throw new IOException("The story voice driver did not report completion.");
                }
                await Task.Delay(20).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            string? failure = null;
            lock (gate)
            {
                if (disposed || item.Epoch != voiceEpoch) return;
                StopVoice();
                Diagnose($"Story voice unavailable; speaking the description through Prism: {exception.Message}");
                try { inner.Output(item.Cue!.Text, false); }
                catch (Exception speechError) { failure = $"Story description output failed: {speechError.Message}"; }
            }
            if (failure is not null) ReportFailure(failure);
        }
    }

    // Every waveOut call is serialized by gate, including cancellation and disposal.
    private void StopVoice()
    {
        if (!voiceActive) return;
        voiceActive = false;
        try { wave!.Stop(); }
        catch (Exception exception) { Diagnose($"Story voice stop failed: {exception.Message}"); }
    }

    private void Diagnose(string message)
    {
        try { diagnostic(message); } catch (Exception) { }
    }

    private void ReportFailure(string message)
    {
        Diagnose(message);
        try { coverageFailure?.Invoke(message); } catch (Exception) { }
    }

    private sealed record Pending(StoryNarration? Cue, string? Text, bool Interrupt, int Epoch);
}
