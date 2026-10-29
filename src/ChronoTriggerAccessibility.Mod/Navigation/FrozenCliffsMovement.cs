using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

internal static class FrozenCliffsMovement
{
    public static bool IsSlide(string scene) => scene.EndsWith(":604", StringComparison.Ordinal);

    public static string RunAdvice(FieldFloorSnapshot floor) => floor.RunMode is 0 or 2 && floor.RunToggle == 2
        ? "Walking is selected. Briefly tap and release Dash to select running; holding it selects walking."
        : floor.RunMode is 0 or 2 ? "Release Dash to run; holding it selects walking."
        : floor.RunMode == 1 && floor.RunToggle == 1 ? "Running is selected."
        : floor.RunMode == 1 ? "Hold Dash to run." : "Use the game's Run control for slide guidance.";

    public static NavigationFrame ManualFrame(NavigationFrame frame, FieldFloorSnapshot? captured)
    {
        if (!IsSlide(frame.Scene) || captured is not { } floor || !Owned(frame, floor)) return frame;
        var advice = RunAdvice(floor);
        return frame with
        {
            ManualGuidanceInstruction = advice,
            ManualRecoveryInstruction = frame.ManualRecoveryHoldDirection != NavigationDirection.None
                ? "Off route. " + advice + " Hold Up while running to stop sliding for new directions." : frame.ManualRecoveryInstruction,
        };
    }

    private static bool Owned(NavigationFrame frame, FieldFloorSnapshot floor) =>
        frame.CompensateMovingFloor && frame.CanNavigate && frame.FieldMovementOwner is { } owner &&
        floor is { RunMode: >= 0 and <= 2, RunToggle: >= 0 and <= 2, ForceX: 0, ForceY: 0 or 32 } &&
        frame.Scene == $"{floor.Engine:X8}:{floor.Scene}" && owner == (floor.ActorBase, floor.Actor);

    public static FieldNavigationPad Adjust(NavigationFrame frame, NavigationResult result, FieldFloorSnapshot? captured)
    {
        if (captured is not { } floor || !Owned(frame, floor))
            return new(0, "the current slide movement or Run setting is unavailable");
        var toggle = floor.RunToggle!.Value;
        var mode = floor.RunMode!.Value;
        if (toggle == 0 && mode != 2) toggle = mode == 0 ? 1 : 2;
        if (mode != 1 && toggle is not (0 or 1))
            return new(0, "running is required on the slide. Enable running with the game's Dash control, then start the slide assist again");
        return new(FieldNavigationRuntime.DirectionBits(result.Direction) | (mode == 1 ? 8u : 0u)) { OverrideDash = true };
    }
}
