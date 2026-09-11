using ChronoTriggerAccessibility.Core.Navigation;

namespace ChronoTriggerAccessibility.Mod.Navigation;

public sealed class FieldFootstepRuntime(Func<nint, FootstepFrame?> capture,
    Func<bool> isForeground, Func<int, bool> isDown, Func<long> clock,
    Action play, Action stop, Action<string> speak, Action<string> diagnostic,
    Func<string>? describeCapture = null)
{
    private readonly FootstepTracker tracker = new();
    private bool enabled;
    private bool soundEnabled = true;
    private bool armed;
    private bool previousKey;
    private bool failed;
    private readonly Dictionary<string, long> observations = new(StringComparer.Ordinal);
    private long lastDiagnostic = -1;
    private long suspensions;

    public void Enable() { enabled = true; Suspend(); }
    public void Disable() { enabled = false; Suspend(); }
    public void Suspend() { tracker.Reset(); armed = false; suspensions++; stop(); }

    public void OnInput(nint engine, uint acceptedPad)
    {
        if (!enabled || failed) return;
        try
        {
            var now = clock();
            if (!isForeground()) { Suspend(); Trace("background", null, acceptedPad, now); return; }
            var frame = capture(engine);
            if (frame is null) { Suspend(); Trace("capture-unavailable", null, acceptedPad, now); return; }
            var down = isDown(0x77); // F8
            var toggle = armed && down && !previousKey &&
                !isDown(0x10) && !isDown(0x11) && !isDown(0x12) && !isDown(0x5B) && !isDown(0x5C);
            previousKey = down;
            armed = true;
            if (toggle)
            {
                soundEnabled = !soundEnabled;
                tracker.Reset();
                stop();
                speak(soundEnabled ? "Footsteps on." : "Footsteps off.");
            }
            if (soundEnabled && tracker.Update(frame, acceptedPad, clock())) play();
            Trace(soundEnabled ? tracker.State : "off", frame, acceptedPad, now);
        }
        catch (Exception error)
        {
            failed = true;
            tracker.Reset();
            try { stop(); } catch { }
            diagnostic($"Footsteps stopped: {error.GetType().Name}: {error.Message}");
            speak("Footsteps are unavailable. Navigation is still available.");
        }
    }

    private void Trace(string state, FootstepFrame? frame, uint pad, long now)
    {
        observations[state] = observations.GetValueOrDefault(state) + 1;
        if (lastDiagnostic >= 0 && now >= lastDiagnostic && now - lastDiagnostic < 2000) return;
        lastDiagnostic = now;
        diagnostic($"Footsteps motion: on={soundEnabled}; " +
            $"states={string.Join(",", observations.Select(pair => $"{pair.Key}:{pair.Value}"))}; " +
            $"suspends={suspensions}; scene={frame?.Scene}; actor={frame?.Actor}; " +
            $"position=({frame?.X},{frame?.Y}); pad=0x{pad:X}; elapsed={tracker.Elapsed}; " +
            $"distance={tracker.Distance:F1}; capture={describeCapture?.Invoke() ?? "unspecified"}.");
    }
}
