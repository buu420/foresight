using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Minigames;

public sealed class BekklerMimicRuntime(
    Func<nint, int, BekklerMimicSnapshot?> capture,
    Func<BekklerMimicSnapshot, BekklerMimicCue?> complete, Func<bool> foreground,
    Action<string> speak, Action<string> diagnostic)
{
    private readonly object gate = new();
    private bool enabled, active, spoken;
    private int epoch, round;
    private (uint Context, uint Data, uint Actors, uint Field) owner;

    public void Enable() { lock (gate) { enabled = true; epoch++; Reset(); } }
    public void Disable() { lock (gate) { enabled = false; epoch++; Reset(); } }

    public void Dispatch(nint context, int opcode, Action original)
    {
        ArgumentNullException.ThrowIfNull(original);
        int capturedEpoch; bool observing;
        lock (gate) { observing = enabled; capturedEpoch = epoch; }
        BekklerMimicSnapshot? before = null;
        if (observing)
        {
            try { before = capture(context, opcode); }
            catch (Exception e) { Report($"Bekkler copy pre-capture failed: {e.GetType().Name}."); }
        }
        // Native execution is owned by this single call, even when observation fails.
        try { original(); }
        catch { lock (gate) Reset(); throw; }
        if (before is null) return;
        try
        {
            var proof = complete(before);
            if (proof is null || proof.Frame != before) return;
            var focused = foreground();
            lock (gate)
            {
                if (!enabled || capturedEpoch != epoch) return;
                if (!focused) { Reset(); return; }
                var text = Observe(proof);
                if (text is null) return;
                // There is no throttle or narration queue: a verified pose interrupts
                // the previous announcement immediately. Disable cannot race this output.
                speak(text);
                Report($"Bekkler copy cue: {text}");
            }
        }
        catch (Exception e)
        {
            lock (gate) { if (capturedEpoch == epoch) Reset(); }
            Report($"Bekkler copy feedback failed: {e.GetType().Name}.");
        }
    }

    private string? Observe(BekklerMimicCue cue)
    {
        var frame = cue.Frame;
        var identity = (frame.Context, frame.Data, frame.Actors, frame.Field);
        if (frame.Action == BekklerMimicAction.Window)
        {
            if (active && owner == identity && round == frame.Round) return null;
            active = true; owner = identity; round = frame.Round; spoken = false;
            return null;
        }
        if (!active || owner != identity) { Reset(); return null; }
        if (frame.Action is BekklerMimicAction.Failed or BekklerMimicAction.Won)
        {
            Reset();
            return frame.Action == BekklerMimicAction.Won ? "Copy game complete." : "Copy game ended.";
        }
        if (round != frame.Round || spoken) return null;
        var pose = frame.Action switch
        {
            BekklerMimicAction.RightArm => "Right arm.", BekklerMimicAction.LeftArm => "Left arm.",
            BekklerMimicAction.Surprise => "Surprise.", BekklerMimicAction.Laugh => "Laugh.", _ => null,
        };
        if (pose is null) return null;
        spoken = true;
        var text = cue.Button is "A" or "B" or "Y" or "X" ? $"{cue.Button}. {pose}" : pose;
        // The first pose can follow its window in the same update. One utterance
        // keeps the start audible and the button first, without self-interruption.
        return round == 1 ? $"{text} Copy game started." : text;
    }

    private void Reset() { active = spoken = false; owner = default; round = 0; }
    private void Report(string message) { try { diagnostic(message); } catch (Exception) { } }
}
