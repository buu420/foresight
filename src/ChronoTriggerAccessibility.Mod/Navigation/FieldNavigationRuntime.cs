using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Which native input boundary currently drives navigation.</summary>
public enum NavigationMode { Field, World, Epoch, Dactyl }

public readonly record struct FieldNavigationPad(uint Pad, string? StopReason = null);

/// <summary>Runs only at the game's accepted field-input boundary. It returns one
/// ordinary pad value; it never holds OS keys or writes player coordinates.</summary>
public sealed class FieldNavigationRuntime(Func<nint, NavigationFrame?> capture, NavigationKeyboard keyboard,
    Func<bool> isForeground, Func<long> clock, Action<string> speak, Action<string> diagnostic,
    Action<nint>? observe = null, Action? resetDiscoveries = null, Action? resetMotion = null,
    Func<nint, NavigationFrame?>? worldCapture = null, Action<nint>? worldObserve = null,
    Action<NavigationLeg?>? synchronizeFootsteps = null,
    Func<nint, VehicleKind, NavigationFrame?>? vehicleCapture = null,
    Func<nint, VehicleKind, bool>? vehicleActive = null, NavigationGamepad? gamepad = null,
    Action<nint>? observeFieldInput = null, Func<string?>? fieldStatus = null,
    Func<NavigationFrame, NavigationResult, FieldNavigationPad>? adjustFieldInput = null)
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
    private bool controllerInputAllowed;
    private string? controllerMenuScene;
    private long lastControllerPoll = -1;

    /// <summary>Called before physical controller state is mapped into game actions.
    /// Commands are tentative until ProcessInput validates a fresh frame at the
    /// native task boundary. Vehicle capture uses transient task-local state and
    /// must never run from this global joystick poll.</summary>
    public bool FilterController(uint deviceId, NavigationPadButtons buttons, bool neutral, bool connected)
    {
        if (gamepad is null) return false;
        lock (gate)
        {
            if (!connected)
            {
                if (gamepad.OwnsDevice(deviceId)) Suspend("controller disconnected");
                return gamepad.Filter(buttons, neutral, false, connected: false, deviceId: deviceId);
            }
            var now = clock();
            var available = enabled && controllerInputAllowed && isForeground() && engine != 0 &&
                lastCall >= 0 && now >= lastCall && now - lastCall <= 250;
            try
            {
                if (!available && (gamepad.IsOpen || controller.IsActive)) Suspend("controller input is unavailable");
                var consumed = gamepad.Filter(buttons, neutral, available, deviceId: deviceId);
                if (gamepad.OwnsDevice(deviceId)) lastControllerPoll = clock();
                return consumed;
            }
            catch (Exception exception)
            {
                Suspend("controller state could not be read");
                diagnostic($"Controller navigation state read failed: {exception.GetType().Name}.");
                return gamepad.Filter(buttons, neutral, false, deviceId: deviceId);
            }
        }
    }

    private NavigationFrame? CaptureFrame(nint context, NavigationMode current) => current switch
    {
        NavigationMode.Field => capture(context),
        NavigationMode.World => worldCapture?.Invoke(context),
        _ => vehicleCapture?.Invoke(context, current == NavigationMode.Dactyl ? VehicleKind.Dactyl : VehicleKind.Epoch),
    };

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
            if (gamepad?.IsOpen == true) return originalPad;
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
        lock (gate) { enabled = true; lastCall = -1; controllerInputAllowed = false; keyboard.Suspend(); gamepad?.Suspend(); }
    }

    public void Disable()
    {
        lock (gate) { enabled = false; Suspend("accessibility disabled"); }
    }

    public void Suspend(string reason)
    {
        lock (gate)
        {
            worldDirection = 0;
            controllerInputAllowed = false;
            controllerMenuScene = null;
            var wasActive = controller.IsActive;
            var cancellation = controller.Cancel(reason);
            keyboard.Suspend();
            gamepad?.Suspend();
            // Revoke input before calling external motion, logging or speech code.
            // Their failure must not leave a route or a held command active.
            resetMotion?.Invoke();
            if (wasActive) diagnostic($"Navigation stopped: {reason}; last route: {lastRouteState}.");
            Emit(cancellation);
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
                if (gamepad?.HasOwner == true && (lastControllerPoll < 0 || now < lastControllerPoll ||
                    now - lastControllerPoll > 250)) Suspend("controller disconnected");
                if (engine != 0 && (engine != currentEngine || mode != current))
                {
                    Suspend("area changed");
                    lastObservation = -1;
                }
                mode = current;
                engine = currentEngine;
                lastCall = now;
                controllerInputAllowed = true;
                if (current == NavigationMode.Field) observeFieldInput?.Invoke(currentEngine);
                var commands = keyboard.Poll().ToList();
                var padActions = gamepad?.Poll() ?? [];
                if (commands.Count != 0 || originalPad != 0 && gamepad?.IsOpen == true)
                {
                    gamepad?.Suspend();
                    controllerMenuScene = null;
                    padActions = [];
                }
                if (!controller.IsActive && commands.Count == 0 && padActions.Count == 0 && gamepad?.IsOpen != true)
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
                var frame = CaptureFrame(currentEngine, current);
                if (frame is null || !frame.CanNavigate)
                {
                    Suspend("navigation state is unavailable");
                    if (commands.Count != 0 || padActions.Count != 0) speak("Navigation is unavailable here.");
                    return originalPad;
                }
                if (controllerMenuScene is not null && frame.Scene != controllerMenuScene)
                {
                    Suspend("area changed");
                    return originalPad;
                }
                // The rat catch uses the native held Confirm bit. Cancelling on that bit
                // defeats pursuit precisely when the player tries to catch it. Every other
                // action still takes manual control, and fresh frame/scene gates still apply.
                var manualPad = current == NavigationMode.Field && controller.AllowsConfirmWhileFollowing(frame)
                    ? originalPad & ~0x80u : originalPad;
                var result = controller.Update(frame, now, manualPad != 0);
                var speech = new List<string>(result.Speech);
                var processedCommands = new List<NavigationCommand>();
                void Handle(NavigationCommand command)
                {
                    processedCommands.Add(command);
                    if (command == NavigationCommand.Repeat && current == NavigationMode.Field && fieldStatus?.Invoke() is { } status)
                        speech.Add(status);
                    // A new manual turn or recovery message also answers a repeat.
                    if (command == NavigationCommand.Repeat && result.Guiding &&
                        !result.AutoWalking && result.Speech.Count != 0) return;
                    if (command == NavigationCommand.ToggleWalk && originalPad != 0)
                    {
                        speech.Add("Release the movement and action buttons before starting automatic walking.");
                        return;
                    }
                    result = controller.Handle(command, frame, now);
                    speech.AddRange(result.Speech);
                }
                // Preserve input order, including Open after a queued Walk. Menu
                // state and route state must agree when this native tick returns.
                foreach (var action in padActions)
                {
                    switch (action)
                    {
                        case NavigationPadAction.Open:
                            result = controller.Cancel("navigation menu opened");
                            resetMotion?.Invoke();
                            speech.Clear();
                            speech.Add($"Navigation menu. {controller.CurrentCategoryLabel}.");
                            Handle(NavigationCommand.Repeat);
                            break;
                        case NavigationPadAction.Close:
                            speech.Add("Navigation menu closed.");
                            break;
                        default:
                            Handle(action switch
                            {
                                NavigationPadAction.PreviousCategory => NavigationCommand.PreviousCategory,
                                NavigationPadAction.NextCategory => NavigationCommand.NextCategory,
                                NavigationPadAction.PreviousTarget => NavigationCommand.PreviousTarget,
                                NavigationPadAction.NextTarget => NavigationCommand.NextTarget,
                                NavigationPadAction.Guide => NavigationCommand.Guide,
                                NavigationPadAction.Walk => NavigationCommand.ToggleWalk,
                                _ => throw new InvalidOperationException("Unknown navigation controller action."),
                            });
                            break;
                    }
                }
                controllerMenuScene = gamepad?.IsOpen == true ? frame.Scene : null;
                foreach (var command in commands) Handle(command);
                var movementPad = DirectionBits(result.Direction);
                if (current == NavigationMode.Field && result.AutoWalking && movementPad != 0 && adjustFieldInput is not null)
                {
                    var adjusted = adjustFieldInput(frame, result);
                    if (adjusted.StopReason is { } reason)
                    {
                        result = controller.Cancel(reason);
                        speech.AddRange(result.Speech);
                        resetMotion?.Invoke();
                        movementPad = 0;
                    }
                    else movementPad = adjusted.Pad;
                }
                if (processedCommands.Count != 0 || speech.Count != 0 ||
                    (controller.IsActive && (lastDiagnostic < 0 || now < lastDiagnostic || now - lastDiagnostic >= 250)))
                {
                    if (controller.IsActive || processedCommands.Count != 0) lastRouteState = controller.DiagnosticState;
                    diagnostic($"Navigation: command={string.Join(",", processedCommands)}; mode={current}; scene={frame.Scene}; " +
                        $"player=({frame.Player.X},{frame.Player.Y},{frame.Player.Layer}); {lastRouteState}; " +
                        $"input=0x{originalPad:X}; pad=0x{movementPad:X}; " +
                        $"guiding={result.Guiding}; walking={result.AutoWalking}; " +
                        (current != NavigationMode.Field ? WorldPadDiagnostic() + "; " : string.Empty) +
                        string.Join(" ", speech));
                    lastDiagnostic = now;
                }
                // Flight is silent: the footstep tracker never counts vehicle motion and
                // a manual flight leg must not synchronize a footstep count.
                synchronizeFootsteps?.Invoke(flight ? null : result.ManualLeg);
                if (speech.Count != 0) speak(string.Join(" ", speech));
                if (current != NavigationMode.Field) worldDirection = movementPad;
                return originalPad | movementPad;
            }
            catch (Exception exception)
            {
                // Clear movement authority even when capture or speech throws. The shared
                // unmanaged guard reports a speech failure; no stale pad survives a fault.
                controller.Cancel("navigation error");
                keyboard.Suspend();
                gamepad?.Suspend();
                controllerInputAllowed = false;
                controllerMenuScene = null;
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
