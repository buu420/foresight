using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

internal static class FrozenCliffsMovement
{
    public static bool IsSlide(string scene) => scene.EndsWith(":604", StringComparison.Ordinal);

    public static FieldNavigationPad Adjust(NavigationFrame frame, NavigationResult result, FieldFloorSnapshot? captured)
    {
        if (!frame.CompensateMovingFloor || !frame.CanNavigate || frame.FieldMovementOwner is not { } owner ||
            captured is not { RunMode: >= 0 and <= 2, RunToggle: >= 0 and <= 2 } floor ||
            frame.Scene != $"{floor.Engine:X8}:{floor.Scene}" || owner != (floor.ActorBase, floor.Actor) ||
            floor.ForceX != 0 || floor.ForceY is not (0 or 32))
            return new(0, "the current slide movement or Run setting is unavailable");
        var toggle = floor.RunToggle!.Value;
        var mode = floor.RunMode!.Value;
        if (toggle == 0 && mode != 2) toggle = mode == 0 ? 1 : 2;
        if (mode != 1 && toggle is not (0 or 1))
            return new(0, "running is required on the slide. Enable running with the game's Dash control, then start the slide assist again");
        return new(FieldNavigationRuntime.DirectionBits(result.Direction) | (mode == 1 ? 8u : 0u)) { OverrideDash = true };
    }
}
