using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Explain a verified short-step corner using the current native Run
/// setting. Guidance supplies speech only; native Dash and its tap edge stay
/// with the player.</summary>
internal static class LostSanctumMovement
{
    public static string? GuidanceCue(NavigationFrame frame, NavigationResult result, FieldFloorSnapshot? native, uint pad)
    {
        if (result.AutoWalking || !Setting(frame, native, out var mode, out var toggle) ||
            !NeedsWalking(frame, result)) return null;
        var running = mode == 1 ? toggle == 1 || (pad & 8) != 0 : toggle != 2 && (pad & 8) == 0;
        if (!running) return null;
        return "This narrow corner needs walking speed. " + (mode == 1
            ? toggle == 1 ? "Tap and release Dash to select walking for this turn. You can select running again at the next direction."
                : "Release Dash for this turn, then resume your usual speed at the next direction."
            : "Hold Dash for this turn, then resume your usual speed at the next direction.");
    }

    public static string? ForcedRunStopReason(NavigationFrame frame, NavigationResult result, FieldFloorSnapshot? native) =>
        result.AutoWalking && Setting(frame, native, out var mode, out var toggle) && mode == 1 && toggle == 1 &&
        NeedsWalking(frame, result)
            ? "this narrow corner needs walking speed. Tap and release Dash to select walking, then start automatic walking again"
            : null;

    private static bool Setting(NavigationFrame frame, FieldFloorSnapshot? native, out int mode, out int toggle)
    {
        mode = toggle = -1;
        if (!LostSanctumTargets.NarrowCave(frame.Scene) ||
            native is not { RunMode: >= 0 and <= 2, RunToggle: >= 0 and <= 2, ForceX: 0, ForceY: 0 } floor ||
            frame.Scene != $"{floor.Engine:X8}:{floor.Scene}") return false;
        mode = floor.RunMode.Value;
        toggle = floor.RunToggle.Value;
        if (toggle == 0) toggle = mode switch { 0 => 1, 1 => 2, _ => 0 };
        return true;
    }

    private static bool NeedsWalking(NavigationFrame frame, NavigationResult result)
    {
        if (!result.Guiding || !frame.CompensateMovingFloor || result.NativeInteractionInput ||
            result.InteractionFacing != NavigationDirection.None || frame.CanTraverseFloorStep is not { } clear ||
            (result.AutoWalking ? result.SteeringPoint is null : result.ManualLeg is null)) return false;
        var direction = result.AutoWalking ? result.Direction : result.ManualLeg!.Direction;
        var bits = FieldNavigationRuntime.DirectionBits(direction);
        var dx = (bits & 0x100) != 0 ? 1 : (bits & 0x200) != 0 ? -1 : 0;
        var dy = (bits & 0x400) != 0 ? 1 : (bits & 0x800) != 0 ? -1 : 0;
        if (dx == 0 && dy == 0) return false;
        (int X, int Y) Step(int speed) => dx != 0 && dy != 0
            ? ((int)(dx * speed / 1.39f), (int)(dy * speed / 1.39f)) : (dx * speed, dy * speed);
        var run = Step(32); var walk = Step(16);
        return !clear(frame.Player, run.X, run.Y) && clear(frame.Player, walk.X, walk.Y);
    }
}
