using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

public static class FieldFloorMovement
{
    public static NavigationFloor? FromFlags(byte flags)
    {
        var speed = (flags & 12) switch { 4 => 8, 8 => 16, 12 => 32, _ => 0 };
        return speed == 0 ? null : new((flags & 3) switch
        {
            0 => NavigationDirection.North, 1 => NavigationDirection.South,
            2 => NavigationDirection.West, _ => NavigationDirection.East,
        }, speed);
    }

    public static FieldNavigationPad Adjust(NavigationFrame frame, NavigationResult result, FieldFloorSnapshot? native)
    {
        var pad = FieldNavigationRuntime.DirectionBits(result.Direction);
        if (frame.MovingFloor is not { Speed: 16 } floor || !Opposes(result.Direction, floor.Direction) || pad == 0)
            return new(pad);
        if (native is not { RunMode: { } mode, RunToggle: { } toggle } value ||
            frame.Scene != $"{value.Engine:X8}:{value.Scene}")
            return new(0, "the conveyor's run setting is unavailable");
        // 175C90 first initializes a zero toggle from the configured mode.
        if (toggle == 0) toggle = mode switch { 0 => 1, 1 => 2, _ => 0 };
        // A synthetic held Dash changes the speed in mode1; it does not
        // synthesize the separate release edge that changes native toggles.
        if (mode == 1) return new(pad | (toggle == 1 ? 0u : 8u));
        if (mode is 0 or 2 && toggle is 0 or 1) return new(pad);
        return new(0, "running is required to move against this conveyor. Use your Dash control to enable running, then start automatic walking again");
    }

    private static bool Opposes(NavigationDirection direction, NavigationDirection floor) => (direction, floor) is
        (NavigationDirection.North, NavigationDirection.South) or (NavigationDirection.South, NavigationDirection.North) or
        (NavigationDirection.West, NavigationDirection.East) or (NavigationDirection.East, NavigationDirection.West);
}
