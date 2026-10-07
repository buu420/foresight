using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Racing;

public sealed class BikeRaceRuntime(Func<nuint, BikeRaceSnapshot?> capture, BikeRaceFeedback feedback,
    Func<bool> foreground, Func<bool> repeatKey, Func<long> clock, Action enter, Action<string> diagnostic)
{
    private readonly object gate = new();
    private readonly Dictionary<uint, bool> held = [];
    private nuint node;
    private long entered, lastSample = -1, lastTrace = -1;
    private bool keyHeld, pendingRepeat, missing;
    public bool HasContext { get { lock (gate) return node != 0; } }

    public void Update(nuint scene)
    {
        if (scene == 0) return;
        lock (gate)
        {
            var now = clock();
            var focused = foreground();
            var down = repeatKey();
            if (node != scene)
            {
                Close(); node = scene; entered = now; keyHeld = down;
                enter(); diagnostic($"Bike race enter: scene=0x{scene:X}.");
            }
            if (focused && down && !keyHeld) pendingRepeat = true;
            keyHeld = down;
            if (!focused) { pendingRepeat = false; held.Clear(); }
            if (lastSample >= 0 && now >= lastSample && now - lastSample < 50 && focused) return;
            lastSample = now;
            BikeRaceSnapshot? frame;
            try { frame = capture(scene); }
            catch (Exception e) { frame = null; diagnostic($"Bike race capture exception: {e.GetType().Name}."); }
            var repeat = pendingRepeat; pendingRepeat = false;
            if (frame is null && now - entered < 750 && !repeat) return;
            if (frame is null && !missing) diagnostic("Bike race capture unavailable; retrying on subsequent native updates.");
            missing = frame is null;
            if (frame is not null && (lastTrace < 0 || now - lastTrace >= 1000))
            {
                lastTrace = now;
                diagnostic($"Bike race: phase={frame.Phase}; result={frame.Result}; paused={frame.Paused}; " +
                    $"distance={frame.Distance}; score={frame.Score}; lead={frame.Lead}; lane={frame.JohnnyLane}; " +
                    $"boosts={(frame.BoostsEnabled ? frame.Boosts : -1)}; ready={frame.BoostReady}.");
            }
            feedback.Update(frame, now, focused, repeat);
        }
    }

    // Observe only. Returning through the joystick hook never masks steering,
    // boost, pause, or any other native button during the race.
    public void ObserveController(uint device, NavigationPadButtons buttons, bool connected)
    {
        lock (gate)
        {
            if (node == 0 || !connected || !foreground()) { held.Remove(device); return; }
            var down = (buttons & NavigationPadButtons.RightStick) != 0;
            if (held.TryGetValue(device, out var previous) && down && !previous) pendingRepeat = true;
            held[device] = down;
        }
    }
    public void Close(nuint scene = 0)
    {
        lock (gate)
        {
            if (scene != 0 && node != scene) return;
            feedback.Close(); node = 0; lastSample = lastTrace = -1;
            pendingRepeat = keyHeld = missing = false; held.Clear();
        }
    }
}
