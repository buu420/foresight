using ChronoTriggerAccessibility.Core.Navigation;

namespace ChronoTriggerAccessibility.Mod.Navigation;

public sealed class FieldFootstepRuntime(Func<nint, FootstepFrame?> capture,
    Func<bool> isForeground, Func<int, bool> isDown, Func<long> clock,
    Action play, Action stop, Action<string> speak, Action<string> diagnostic,
    Func<string>? describeCapture = null, Func<nint, FootstepFrame?>? worldCapture = null)
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
    private bool worldMode;
    private NavigationLeg? guidance;

    public void SetGuidance(NavigationLeg? value) => guidance = value;

    public void OnWorldInput(nint engine, uint acceptedPad) => ProcessInput(engine, acceptedPad, true);

    public void Enable() { enabled = true; Suspend(); }
    public void Disable() { enabled = false; Suspend(); }
    public void Suspend() { tracker.Reset(); guidance = null; armed = false; suspensions++; stop(); }

    public void OnInput(nint engine, uint acceptedPad) => ProcessInput(engine, acceptedPad, false);

    private void ProcessInput(nint engine, uint acceptedPad, bool world)
    {
        if (!enabled || failed) return;
        try
        {
            if (worldMode != world) { Suspend(); worldMode = world; }
            var now = clock();
            if (!isForeground()) { Suspend(); Trace("background", null, acceptedPad, now); return; }
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
            var frame = world ? worldCapture?.Invoke(engine) : capture(engine);
            if (frame is null)
            {
                // The native input boundary is still active. Keep F8's key edge
                // alive while dropping movement that cannot be measured.
                tracker.Reset(); Trace("capture-unavailable", null, acceptedPad, now); return;
            }
            if (soundEnabled && tracker.Update(frame, acceptedPad, clock(), guidance))
                for (var i = 0; i < tracker.Steps; i++) play();
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
