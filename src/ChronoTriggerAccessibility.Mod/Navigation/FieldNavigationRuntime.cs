using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Which native input boundary currently drives navigation.</summary>
public enum NavigationMode { Field, World, Epoch, Dactyl }

/// <summary>Runs only at the game's accepted field-input boundary. It returns one
/// ordinary pad value; it never holds OS keys or writes player coordinates.</summary>
public sealed class FieldNavigationRuntime(Func<nint, NavigationFrame?> capture, NavigationKeyboard keyboard,
    Func<bool> isForeground, Func<long> clock, Action<string> speak, Action<string> diagnostic,
    Action<nint>? observe = null, Action? resetDiscoveries = null, Action? resetMotion = null,
    Func<nint, NavigationFrame?>? worldCapture = null, Action<nint>? worldObserve = null,
    Action<NavigationLeg?>? synchronizeFootsteps = null,
    Func<nint, VehicleKind, NavigationFrame?>? vehicleCapture = null,
    Func<nint, VehicleKind, bool>? vehicleActive = null)
{
    private readonly NavigationController controller = new();
    private readonly object gate = new();
    private bool enabled;
    private nint engine;
    private long lastCall = -1;
    private long lastObservation = -1;
    private long lastDiagnostic = -1;
    private string lastRouteState = "none";
    private NavigationMode mode;
    private uint worldDirection;
    private long worldPadCalls;
    private long worldPadApplied;
    private long worldPadManual;
    private uint worldPadLastInput;
    private string worldPadLastReject = "none";

    public uint OnWorldInput(nint currentEngine, uint originalPad) => ProcessInput(currentEngine, originalPad, NavigationMode.World);

    /// <summary>Runs at a vehicle task's tick. A parked or foreign vehicle task must not
    /// disturb the walking controller, so nothing happens unless the capture proves this
    /// vehicle is the player's current flying transport. Returns that verdict.</summary>
    public bool OnVehicleInput(nint currentEngine, uint originalPad, VehicleKind kind)
    {
        if (!enabled || vehicleCapture is null) return false;
        if (vehicleActive?.Invoke(currentEngine, kind) != true)
        {
            lock (gate)
                if (engine == currentEngine && mode == Mode(kind)) Suspend("vehicle control ended");
            return false;
        }
        ProcessInput(currentEngine, originalPad, Mode(kind));
        return true;
    }

    public static NavigationMode Mode(VehicleKind kind) => kind == VehicleKind.Epoch ? NavigationMode.Epoch : NavigationMode.Dactyl;

    /// <summary>Runs at each native world pad-combine site. Counters distinguish
    /// requested directions from callbacks that accepted them. Position changes
    /// remain the evidence that the game actually moved.</summary>
    public uint ApplyWorldPad(nint currentEngine, uint originalPad) => ApplyPad(currentEngine, originalPad, NavigationMode.World);

    public uint ApplyVehiclePad(nint currentEngine, uint originalPad, VehicleKind kind) =>
        ApplyPad(currentEngine, originalPad, Mode(kind));

    private uint ApplyPad(nint currentEngine, uint originalPad, NavigationMode padMode)
    {
        lock (gate)
        {
            worldPadCalls++;
            worldPadLastInput = originalPad;
            var now = clock();
            var reject =
                !enabled ? "disabled" :
                mode == NavigationMode.Field ? "not world mode" :
                mode != padMode ? $"not {padMode} mode" :
                engine != currentEngine ? "different context" :
                !isForeground() ? "background" :
                lastCall < 0 ? "no tick yet" :
                now < lastCall || now - lastCall > 250 ? "tick is stale" : null;
            if (reject is not null)
            {
                worldPadLastReject = reject;
                return originalPad;
            }
            if (originalPad != 0)
            {
                worldPadManual++;
                worldPadLastReject = "physical input";
                if (worldDirection != 0) Suspend("manual control");
                return originalPad;
            }
            worldPadLastReject = worldDirection == 0 ? "no direction" : "none";
            if (worldDirection != 0) worldPadApplied++;
            return originalPad | worldDirection;
        }
    }

    private string WorldPadDiagnostic() =>
        $"worldPad=calls:{worldPadCalls},applied:{worldPadApplied},manual:{worldPadManual}; " +
        $"worldPadInput=0x{worldPadLastInput:X}; worldPadReject={worldPadLastReject}";

    public void Enable()
    {
        lock (gate) { enabled = true; lastCall = -1; keyboard.Suspend(); }
    }

    public void Disable()
    {
        lock (gate) { enabled = false; worldDirection = 0; controller.Cancel("accessibility disabled"); keyboard.Suspend(); resetMotion?.Invoke(); }
    }

    public void Suspend(string reason)
    {
        lock (gate)
        {
            worldDirection = 0;
            if (controller.IsActive) diagnostic($"Navigation stopped: {reason}; last route: {lastRouteState}.");
            Emit(controller.Cancel(reason));
            keyboard.Suspend();
            resetMotion?.Invoke();
        }
    }

    public void ResetDiscoveries()
    {
        lock (gate) { resetDiscoveries?.Invoke(); resetMotion?.Invoke(); lastObservation = -1; }
    }

    public uint OnInput(nint currentEngine, uint originalPad) => ProcessInput(currentEngine, originalPad, NavigationMode.Field);

    private uint ProcessInput(nint currentEngine, uint originalPad, NavigationMode current)
    {
        lock (gate)
        {
            if (!enabled) return originalPad;
            worldDirection = 0;
            var flight = current is NavigationMode.Epoch or NavigationMode.Dactyl;
            var kind = current == NavigationMode.Dactyl ? VehicleKind.Dactyl : VehicleKind.Epoch;
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
                if (engine != 0 && (engine != currentEngine || mode != current))
                {
                    Suspend("area changed");
                    lastObservation = -1;
                }
                mode = current;
                engine = currentEngine;
                lastCall = now;
                var commands = keyboard.Poll();
                if (!controller.IsActive && commands.Count == 0)
                {
                    Action<nint>? observer = current switch
                    {
                        NavigationMode.Field => observe,
                        NavigationMode.World => worldObserve,
                        _ => vehicleCapture is null ? null : context => vehicleCapture(context, kind),
                    };
                    if (observer is not null && (lastObservation < 0 || now - lastObservation >= 500))
                    {
                        try { observer(currentEngine); }
                        catch (Exception exception) { diagnostic($"Navigation discovery read failed: {exception.GetType().Name}."); }
                        lastObservation = clock();
                    }
                    return originalPad;
                }
                var frame = current switch
                {
                    NavigationMode.Field => capture(currentEngine),
                    NavigationMode.World => worldCapture?.Invoke(currentEngine),
                    _ => vehicleCapture?.Invoke(currentEngine, kind),
                };
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
                    // A new manual turn or recovery message also answers a simultaneous
                    // repeat request. Keep that message without reading the same leg twice.
                    if (command == NavigationCommand.Repeat && result.Guiding &&
                        !result.AutoWalking && result.Speech.Count != 0) continue;
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
                    diagnostic($"Navigation: command={string.Join(",", commands)}; mode={current}; scene={frame.Scene}; " +
                        $"player=({frame.Player.X},{frame.Player.Y},{frame.Player.Layer}); {lastRouteState}; " +
                        $"input=0x{originalPad:X}; pad=0x{DirectionBits(result.Direction):X}; " +
                        $"guiding={result.Guiding}; walking={result.AutoWalking}; " +
                        (current != NavigationMode.Field ? WorldPadDiagnostic() + "; " : string.Empty) +
                        string.Join(" ", speech));
                    lastDiagnostic = now;
                }
                // Flight is silent: the footstep tracker never counts vehicle motion and
                // a manual flight leg must not synchronize a footstep count.
                synchronizeFootsteps?.Invoke(flight ? null : result.ManualLeg);
                if (speech.Count != 0) speak(string.Join(" ", speech));
                if (current != NavigationMode.Field) worldDirection = DirectionBits(result.Direction);
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
            finally
            {
                // Native input pauses begin after our work finishes. Asset/camera
                // observation can take longer on area entry or a network install.
                lastCall = clock();
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
