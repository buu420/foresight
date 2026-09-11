using ChronoTriggerAccessibility.Core.Navigation;

namespace ChronoTriggerAccessibility.Mod.Navigation;

public sealed class FieldFootstepRuntime(Func<nint, FootstepFrame?> capture,
    Func<bool> isForeground, Func<int, bool> isDown, Func<long> clock,
    Action play, Action stop, Action<string> speak, Action<string> diagnostic)
{
    private readonly FootstepTracker tracker = new();
    private bool enabled;
    private bool soundEnabled = true;
    private bool armed;
    private bool previousKey;
    private bool failed;

    public void Enable() { enabled = true; Suspend(); }
    public void Disable() { enabled = false; Suspend(); }
    public void Suspend() { tracker.Reset(); armed = false; stop(); }

    public void OnInput(nint engine, uint acceptedPad)
    {
        if (!enabled || failed) return;
        try
        {
            if (!isForeground()) { Suspend(); return; }
            var frame = capture(engine);
            if (frame is null) { Suspend(); return; }
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
}
