using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Minigames;

public sealed class IokaContestRuntime(
    Func<nint, int, IokaContestSnapshot?> capture,
    Func<IokaContestSnapshot, bool> complete,
    Func<bool> foreground, Func<long> clock, Func<string?> aylaName,
    Action<string> speak, Action<string> diagnostic)
{
    private readonly object gate = new();
    private bool enabled, active, unfocused, playerPending, aylaPending;
    private int epoch;
    private (uint Context, uint Data, uint Actors, uint Field) owner;
    private long lastSpeech, quietUntil;

    public void Enable() { lock (gate) { enabled = true; epoch++; Reset(); } }
    public void Disable() { lock (gate) { enabled = false; epoch++; Reset(); } }

    public void Dispatch(nint context, int opcode, Action original)
    {
        ArgumentNullException.ThrowIfNull(original);
        int capturedEpoch;
        bool observing;
        lock (gate) { observing = enabled; capturedEpoch = epoch; }
        IokaContestSnapshot? before = null;
        if (observing)
        {
            try { before = capture(context, opcode); }
            catch (Exception e) { Report($"Ioka contest pre-capture failed: {e.GetType().Name}."); }
        }

        // Accessibility observes only. Native execution happens once even if any reader,
        // speech provider, foreground check or diagnostic callback fails.
        try { original(); }
        catch { lock (gate) Reset(); throw; }
        if (before is null) return;
        try
        {
            if (!complete(before)) return;
            var focused = foreground();
            var now = clock();
            string? text;
            lock (gate)
            {
                if (!enabled || capturedEpoch != epoch) return;
                text = Observe(before, focused, now);
            }
            if (text is not null) speak(text);
        }
        catch (Exception e)
        {
            // Retry current instructions at the next verified action after a transient
            // provider failure; never keep a silent, partially announced session.
            lock (gate) { if (capturedEpoch == epoch) Reset(); }
            Report($"Ioka contest feedback failed: {e.GetType().Name}.");
        }
    }

    private string? Observe(IokaContestSnapshot frame, bool focused, long now)
    {
        var identity = (frame.Context, frame.Data, frame.Actors, frame.Field);
        if (frame.Action == IokaContestAction.Finished)
        {
            var announce = active && owner == identity && focused;
            Reset();
            return announce ? "Drinking contest finished." : null;
        }
        var starting = frame.Action == IokaContestAction.Started;
        var entering = !active || owner != identity;
        if (starting || entering)
        {
            Reset(); active = true; owner = identity;
        }
        if (!focused)
        { unfocused = true; playerPending = aylaPending = false; return null; }
        if (starting || entering || unfocused)
        {
            unfocused = false; playerPending = aylaPending = false;
            lastSpeech = now; quietUntil = now + 3500;
            var prefix = starting ? "Drinking contest started." : "Drinking contest in progress.";
            return $"{prefix} Rapidly press Confirm to drink more than {Name()}.";
        }
        if (frame.Action == IokaContestAction.PlayerDrinks) playerPending = true;
        if (frame.Action == IokaContestAction.AylaDrinks) aylaPending = true;
        if (now < lastSpeech) { lastSpeech = now; quietUntil = now + 3500; return null; }
        if (now < quietUntil || now - lastSpeech < 2000) return null;
        var player = playerPending ? "You drink." : null;
        var ayla = aylaPending ? $"{Name()} drinks." : null;
        playerPending = aylaPending = false; lastSpeech = now;
        return string.Join(" ", new[] { player, ayla }.Where(text => text is not null));
    }

    private string Name()
    {
        var name = aylaName();
        return string.IsNullOrWhiteSpace(name) ? "Ayla" : name.Trim();
    }
    private void Reset()
    {
        active = unfocused = playerPending = aylaPending = false;
        owner = default; lastSpeech = quietUntil = 0;
    }
    private void Report(string message) { try { diagnostic(message); } catch (Exception) { } }
}
