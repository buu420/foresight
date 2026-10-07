using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

public sealed class FieldFloorFeedback(Action<string> speak, Action<string> diagnostic)
{
    private (uint Engine, uint Actors, int Scene, int Actor)? identity;
    private (int X, int Y)? announced;
    public FieldFloorSnapshot? Current { get; private set; }
    public string? Status => Current is { } floor && (floor.ForceX != 0 || floor.ForceY != 0) ? Describe(floor) : null;
    public void Reset() { Current = null; identity = null; announced = null; }
    public void Observe(FieldFloorSnapshot? floor)
    {
        Current = floor;
        // An unavailable probe is not evidence of leaving the belt. Preserve
        // the announcement cursor, while revoking movement and repeat facts.
        if (floor is not { } value) return;
        var context = (value.Engine, value.ActorBase, value.Scene, value.Actor);
        if (identity != context) { identity = context; announced = null; }
        var force = (value.ForceX, value.ForceY);
        if (announced == force) return;
        var wasMoving = announced is { } previous && previous != (0, 0);
        announced = force;
        diagnostic($"Field floor: scene={value.Scene}; force=({value.ForceX},{value.ForceY}); " +
            $"runMode={value.RunMode?.ToString() ?? "?"}; runToggle={value.RunToggle?.ToString() ?? "?"}.");
        if (force != (0, 0)) speak(Describe(value));
        else if (wasMoving) speak(IsConveyor(value.Scene) ? "Off the conveyor." : "Off the moving floor.");
    }

    private static bool IsConveyor(int scene) => scene is 231 or 232 or 233 or 437;
    private static string Describe(FieldFloorSnapshot floor)
    {
        var direction = floor.ForceX < 0 ? "west" : floor.ForceX > 0 ? "east" : floor.ForceY < 0 ? "north" : "south";
        var speed = Math.Max(Math.Abs(floor.ForceX), Math.Abs(floor.ForceY));
        return (IsConveyor(floor.Scene) ? "Conveyor" : "Moving floor") + " moving " + direction + "." +
            (speed == 32 ? " You cannot move against this belt." : speed == 16 ? " Run to move against it." : "");
    }
}
