using ChronoTriggerAccessibility.Core.Navigation;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Runs only at the game's accepted field-input boundary. It returns one
/// ordinary pad value; it never holds OS keys or writes player coordinates.</summary>
public sealed class FieldNavigationRuntime(Func<nint, NavigationFrame?> capture, NavigationKeyboard keyboard,
    Func<bool> isForeground, Func<long> clock, Action<string> speak, Action<string> diagnostic,
    Action<nint>? observe = null, Action? resetDiscoveries = null)
{
    private readonly NavigationController controller = new();
    private readonly object gate = new();
    private bool enabled;
    private nint engine;
    private long lastCall = -1;
    private long lastObservation = -1;
    private long lastDiagnostic = -1;
    private string lastRouteState = "none";

    public void Enable()
    {
        lock (gate) { enabled = true; lastCall = -1; keyboard.Suspend(); }
    }

    public void Disable()
    {
        lock (gate) { enabled = false; controller.Cancel("accessibility disabled"); keyboard.Suspend(); }
    }

    public void Suspend(string reason)
    {
        lock (gate)
        {
            if (controller.IsActive) diagnostic($"Navigation stopped: {reason}; last route: {lastRouteState}.");
            Emit(controller.Cancel(reason));
            keyboard.Suspend();
        }
    }

    public void ResetDiscoveries()
    {
        lock (gate) { resetDiscoveries?.Invoke(); lastObservation = -1; }
    }

    public uint OnInput(nint currentEngine, uint originalPad)
    {
        lock (gate)
        {
            if (!enabled) return originalPad;
            try
            {
                var now = clock();
                if (!isForeground())
                {
                    Suspend("game is not in the foreground");
                    lastCall = now;
                    return originalPad;
                }
                if (lastCall >= 0 && (now < lastCall || now - lastCall > 250)) Suspend("player input was paused");
                if (engine != 0 && engine != currentEngine) Suspend("area changed");
                engine = currentEngine;
                lastCall = now;
                var commands = keyboard.Poll();
                if (!controller.IsActive && commands.Count == 0)
                {
                    if (observe is not null && (lastObservation < 0 || now - lastObservation >= 500))
                    {
                        lastObservation = now;
                        try { observe(currentEngine); }
                        catch (Exception exception) { diagnostic($"Navigation discovery read failed: {exception.GetType().Name}."); }
                    }
                    return originalPad;
                }
                var frame = capture(currentEngine);
                if (frame is null || !frame.CanNavigate)
                {
                    Suspend("navigation state is unavailable");
                    if (commands.Count != 0) speak("Navigation is unavailable here.");
                    return originalPad;
                }
                var result = controller.Update(frame, now, originalPad != 0);
                var speech = new List<string>(result.Speech);
                foreach (var command in commands)
                {
                    if (command == NavigationCommand.ToggleWalk && originalPad != 0)
                    {
                        speech.Add("Release the movement and action buttons before starting automatic walking.");
                        continue;
                    }
                    result = controller.Handle(command, frame, now);
                    speech.AddRange(result.Speech);
                }
                if (commands.Count != 0 || speech.Count != 0 ||
                    (controller.IsActive && (lastDiagnostic < 0 || now < lastDiagnostic || now - lastDiagnostic >= 250)))
                {
                    if (controller.IsActive || commands.Count != 0) lastRouteState = controller.DiagnosticState;
                    diagnostic($"Navigation: command={string.Join(",", commands)}; scene={frame.Scene}; " +
                        $"player=({frame.Player.X},{frame.Player.Y},{frame.Player.Layer}); {lastRouteState}; " +
                        $"input=0x{originalPad:X}; pad=0x{DirectionBits(result.Direction):X}; " +
                        $"guiding={result.Guiding}; walking={result.AutoWalking}; {string.Join(" ", speech)}");
                    lastDiagnostic = now;
                }
                if (speech.Count != 0) speak(string.Join(" ", speech));
                lastCall = clock();
                return originalPad | DirectionBits(result.Direction);
            }
            catch (Exception exception)
            {
                // Clear movement authority even when capture or speech throws. The shared
                // unmanaged guard reports a speech failure; no stale pad survives a fault.
                controller.Cancel("navigation error");
                keyboard.Suspend();
                diagnostic($"Field navigation failure: {exception}");
                speak("Navigation stopped because its state could not be read.");
                return originalPad;
            }
        }
    }

    private void Emit(NavigationResult result)
    {
        if (result.Speech.Count != 0) speak(string.Join(" ", result.Speech));
    }

    public static uint DirectionBits(NavigationDirection direction) => direction switch
    {
        NavigationDirection.North => 0x800, NavigationDirection.South => 0x400,
        NavigationDirection.West => 0x200, NavigationDirection.East => 0x100,
        NavigationDirection.NorthWest => 0xA00, NavigationDirection.NorthEast => 0x900,
        NavigationDirection.SouthWest => 0x600, NavigationDirection.SouthEast => 0x500,
        _ => 0,
    };
}
