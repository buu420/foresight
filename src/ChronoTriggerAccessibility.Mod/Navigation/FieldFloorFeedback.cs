using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

public sealed class FieldFloorFeedback(Action<string> speak, Action<string> diagnostic)
{
    private (uint Engine, uint Actors, int Scene, int Actor)? identity;
    private (int X, int Y)? announced;
    private bool? announcedWind;
    private bool? announcedBuilding;
    public FieldFloorSnapshot? Current { get; private set; }
    public string? Status
    {
        get
        {
            if (Current is not { } floor) return null;
            if (floor.Scene == 244) return string.Join(" ", new[]
            {
                floor.WindGusting != true && floor.WindBuilding == true ? "Wind changing." : Wind(floor.WindGusting),
                floor.ForceX != 0 || floor.ForceY != 0 ? WindPush(floor) : "No downhill push here.",
            }.Where(s => s is not null));
            return floor.ForceX != 0 || floor.ForceY != 0 ? Describe(floor) : null;
        }
    }
    public void Reset() { Current = null; identity = null; announced = null; announcedWind = null; announcedBuilding = null; }
    /// <summary>Revoke current movement/status facts without repeating unchanged
    /// announcements after a navigation-menu or input pause.</summary>
    public void Suspend() => Current = null;
    public void Observe(FieldFloorSnapshot? floor)
    {
        Current = floor;
        // An unavailable probe is not evidence of leaving the belt. Preserve
        // the announcement cursor, while revoking movement and repeat facts.
        if (floor is not { } value) return;
        var context = (value.Engine, value.ActorBase, value.Scene, value.Actor);
        if (identity != context) { identity = context; announced = null; announcedWind = null; announcedBuilding = null; }
        if (value.Scene == 244)
        {
            var facts = new List<string>();
            var forceNow = (value.ForceX, value.ForceY);
            if (announced != forceNow)
            {
                var pushed = announced is { } prior && prior != (0, 0);
                announced = forceNow;
                if (forceNow != (0, 0)) facts.Add(WindPush(value));
                else if (pushed) facts.Add("The downhill push stops here.");
            }
            if (value.WindGusting is { } wind && announcedWind != wind)
            {
                var previousWind = announcedWind;
                announcedWind = wind;
                if (wind)
                {
                    announcedBuilding = false;
                    facts.Insert(0, "Strong wind.");
                }
                else if (previousWind is true && value.WindBuilding != true) facts.Insert(0, "Gust passed.");
            }
            if (value.WindGusting != true && value.WindBuilding is { } building)
            {
                if (building && announcedBuilding != true) facts.Insert(0, "Wind changing.");
                announcedBuilding = building;
            }
            if (facts.Count != 0)
            {
                diagnostic($"Death Peak wind: gusting={value.WindGusting?.ToString() ?? "?"}; force=({value.ForceX},{value.ForceY}).");
                speak(string.Join(" ", facts));
            }
            return;
        }
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
    private static string? Wind(bool? gusting) => gusting switch
    {
        true => "Strong wind.",
        false => "No strong gust.",
        _ => null,
    };
    private static string WindPush(FieldFloorSnapshot floor)
    {
        var direction = floor.ForceX < 0 ? "west" : floor.ForceX > 0 ? "east" : floor.ForceY < 0 ? "north" : "downhill";
        var speed = Math.Max(Math.Abs(floor.ForceX), Math.Abs(floor.ForceY));
        return "Wind pushes you " + direction + "." +
            (speed == 16 ? " Run to climb against it." : speed == 32 ? " You cannot move against this push." : "");
    }
    private static string Describe(FieldFloorSnapshot floor)
    {
        var direction = floor.ForceX < 0 ? "west" : floor.ForceX > 0 ? "east" : floor.ForceY < 0 ? "north" : "south";
        var speed = Math.Max(Math.Abs(floor.ForceX), Math.Abs(floor.ForceY));
        return (IsConveyor(floor.Scene) ? "Conveyor" : "Moving floor") + " moving " + direction + "." +
            (speed == 32 ? " You cannot move against this belt." : speed == 16 ? " Run to move against it." : "");
    }
}
