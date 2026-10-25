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
        var sand = SunkenDesertNavigation.IsScene(frame.Scene);
        if (frame.MovingFloor is not { Speed: 16 } floor || !sand && !Opposes(result.Direction, floor.Direction) || pad == 0)
            return new(pad);
        var wind = frame.Scene.EndsWith(":244", StringComparison.Ordinal);
        if (native is not { RunMode: { } mode, RunToggle: { } toggle } value ||
            frame.Scene != $"{value.Engine:X8}:{value.Scene}")
            return new(0, wind ? "the run setting for climbing against the wind is unavailable" :
                sand ? "the shifting sand's current Run setting is unavailable" : "the conveyor's run setting is unavailable");
        // 175C90 first initializes a zero toggle from the configured mode.
        if (toggle == 0) toggle = mode switch { 0 => 1, 1 => 2, _ => 0 };
        // A synthetic held Dash changes the speed in mode1; it does not
        // synthesize the separate release edge that changes native toggles.
        if (mode == 1) return new(sand ? SandPad(frame, result, value, toggle, pad) : pad | (toggle != 1 ? 8u : 0u));
        if (mode is 0 or 2 && toggle is 0 or 1)
            return new(sand ? SandPad(frame, result, value, toggle, pad) : pad) { DashMustBeReleased = sand };
        if (sand) return new(0, SunkenDesertNavigation.SelectRunning);
        return new(0, "running is required to move against " + (wind ? "the wind" : "this conveyor") +
            ". Use your Dash control to enable running, then start automatic walking again");
    }

    // Ordinary eight-direction input and the native held Dash gait. Predict only
    // the next additive floor step; native collision/layer/encounter code still runs.
    // A diagonal can counter lateral drift while advancing along the route.
    private static uint SandPad(NavigationFrame frame, NavigationResult result, FieldFloorSnapshot native, int toggle, uint fallback)
    {
        if (result.SteeringPoint is not { } goal) return fallback | (native.RunMode == 1 ? 8u : 0u);
        if (result.InteractionFacing != NavigationDirection.None)
        {
            var canWalk = native.RunMode != 1 || toggle != 1;
            var dash = native.RunMode == 1 ? !canWalk && toggle != 1 : canWalk;
            return FieldNavigationRuntime.DirectionBits(result.InteractionFacing) | (dash ? 8u : 0u);
        }
        uint best = fallback; var score = double.PositiveInfinity;
        for (var gait = 0; gait < 2; gait++)
        {
            var running = gait == 0;
            if (!running && native.RunMode == 1 && toggle == 1) continue;
            var speed = running ? 32 : 16;
            var dash = native.RunMode == 1 ? running && toggle != 1 : !running;
            foreach (var direction in Directions)
            {
                var bits = FieldNavigationRuntime.DirectionBits(direction);
                var dx = (bits & 0x100) != 0 ? speed : (bits & 0x200) != 0 ? -speed : 0;
                var dy = (bits & 0x400) != 0 ? speed : (bits & 0x800) != 0 ? -speed : 0;
                var x = dx != 0 && dy != 0 ? (int)(native.ForceX + dx / 1.39f) : native.ForceX + dx;
                var y = dx != 0 && dy != 0 ? (int)(native.ForceY + dy / 1.39f) : native.ForceY + dy;
                if (result.Direction == NavigationDirection.North && y >= 0 || result.Direction == NavigationDirection.South && y <= 0 ||
                    result.Direction == NavigationDirection.West && x >= 0 || result.Direction == NavigationDirection.East && x <= 0) continue;
                var horizontalWeight = result.Direction is NavigationDirection.North or NavigationDirection.South ? 16 : 1;
                var verticalWeight = result.Direction is NavigationDirection.West or NavigationDirection.East ? 16 : 1;
                var distance = horizontalWeight * Math.Pow((double)goal.X - frame.Player.X - x, 2) +
                    verticalWeight * Math.Pow((double)goal.Y - frame.Player.Y - y, 2);
                if (distance >= score) continue;
                score = distance; best = bits | (dash ? 8u : 0u);
            }
        }
        return best;
    }

    private static readonly NavigationDirection[] Directions = [NavigationDirection.North, NavigationDirection.South,
        NavigationDirection.West, NavigationDirection.East, NavigationDirection.NorthWest, NavigationDirection.NorthEast,
        NavigationDirection.SouthWest, NavigationDirection.SouthEast];

    internal static bool Opposes(NavigationDirection direction, NavigationDirection floor) => (direction, floor) is
        (NavigationDirection.North, NavigationDirection.South) or (NavigationDirection.South, NavigationDirection.North) or
        (NavigationDirection.West, NavigationDirection.East) or (NavigationDirection.East, NavigationDirection.West);
}
