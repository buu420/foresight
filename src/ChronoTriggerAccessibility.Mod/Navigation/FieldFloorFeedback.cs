using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

public sealed class FieldFloorFeedback(Action<string> speak, Action<string> diagnostic,
    Func<long>? clock = null, Action<string>? background = null)
{
    private (uint Engine, uint Actors, int Scene, int Actor)? identity;
    private (int X, int Y)? announced;
    private bool? announcedWind;
    private bool? announcedBuilding;
    private long lastSandSpeech = -1;
    private bool sandPending;
    private (int? Mode, int? Toggle)? sandRun;
    private bool announcedSlide;
    public FieldFloorSnapshot? Current { get; private set; }
    public string? Status
    {
        get
        {
            if (Current is not { } floor) return null;
            if (floor.Scene == 604 && floor.ForceX == 0 && floor.ForceY is 0 or 32)
                return floor.ForceY == 0 ? "Off the downhill slide." : DescribeSlide(floor);
            if (floor.Scene == 244) return string.Join(" ", new[]
            {
                floor.WindGusting != true && floor.WindBuilding == true ? "Wind changing." : Wind(floor.WindGusting),
                floor.ForceX != 0 || floor.ForceY != 0 ? WindPush(floor) : "No downhill push here.",
            }.Where(s => s is not null));
            return floor.ForceX != 0 || floor.ForceY != 0
                ? SunkenDesertNavigation.IsScene(floor.Scene) ? DescribeSand(floor, true) : Describe(floor) : null;
        }
    }
    public void Reset() { Current = null; identity = null; announced = null; announcedWind = null; announcedBuilding = null; lastSandSpeech = -1; sandPending = false; sandRun = null; announcedSlide = false; }
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
        if (identity != context) { identity = context; announced = null; announcedWind = null; announcedBuilding = null; lastSandSpeech = -1; sandPending = false; sandRun = null; announcedSlide = false; }
        if (value.Scene == 604 && value.ForceX == 0 && value.ForceY is 0 or 32)
        {
            // Narrow calm lips and rejected probes are not new slide sections.
            // Keep this cue once per owner/scene; Repeat reports current facts.
            if (value.ForceY == 32 && !announcedSlide)
            {
                announcedSlide = true;
                diagnostic($"Frozen Cliffs slide: force=(0,32); runMode={value.RunMode}; runToggle={value.RunToggle}.");
                (background ?? speak)(DescribeSlide(value));
            }
            return;
        }
        if (SunkenDesertNavigation.IsScene(value.Scene))
        {
            var forceNow = (value.ForceX, value.ForceY);
            var movingBefore = announced is { } sandPrevious && sandPrevious != (0, 0);
            if (announced != forceNow)
            {
                sandPending = forceNow != (0, 0) || movingBefore;
                announced = forceNow;
                diagnostic($"Field floor: scene={value.Scene}; force=({value.ForceX},{value.ForceY}); runMode={value.RunMode?.ToString() ?? "?"}; runToggle={value.RunToggle?.ToString() ?? "?"}.");
            }
            var run = (value.RunMode, value.RunToggle);
            var runChanged = value.RunMode is not null && value.RunToggle is not null && sandRun is not null && sandRun != run;
            if (value.RunMode is not null && value.RunToggle is not null) sandRun = run;
            var now = clock?.Invoke() ?? Environment.TickCount64;
            if ((!sandPending && !runChanged) ||
                !runChanged && lastSandSpeech >= 0 && now >= lastSandSpeech && now - lastSandSpeech < 1500) return;
            var text = forceNow == (0, 0) ? sandPending ? "Off the shifting sand." : RunAdvice(value).Trim()
                : DescribeSand(value, lastSandSpeech < 0 || runChanged);
            (background ?? speak)(text);
            sandPending = false; lastSandSpeech = now;
            return;
        }
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

    private static bool IsConveyor(int scene) => scene is 231 or 232 or 233 or 256 or 437;
    private static string DescribeSlide(FieldFloorSnapshot floor) =>
        "Downhill slide. While running, hold Up to stop sliding. Select a cave and start automatic walking for slide assistance." +
        " " + FrozenCliffsMovement.RunAdvice(floor);
    private static string DescribeSand(FieldFloorSnapshot floor, bool advice)
    {
        var direction = floor.ForceX < 0 ? "west" : floor.ForceX > 0 ? "east" : floor.ForceY < 0 ? "north" : "south";
        // A rejected +138 probe is not a new surface. The owned committed
        // +88/+8C value still proves the push 1734D0 actually applies.
        return (floor.TerrainSampleRejected ? $"Shifting sand pushes you {direction}." : $"Shifting sand flows {direction}.") +
            (advice ? RunAdvice(floor) : "");
    }
    private static string RunAdvice(FieldFloorSnapshot floor) => floor.RunMode is 0 or 2 && floor.RunToggle == 2
            ? " Walking is selected. Briefly tap and release Dash to select running; holding it selects walking."
            : floor.RunMode is 0 or 2 ? " Release Dash to run; holding it selects walking."
            : floor.RunMode == 1 && floor.RunToggle == 1 ? " Running is selected."
            : floor.RunMode == 1 ? " Hold Dash to run against the sand." : " Run to move against the sand.";
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
            (speed == 16 ? " Walking cannot climb against this push; running can." :
                speed == 32 ? " You cannot move against this push." : "");
    }
    private static string Describe(FieldFloorSnapshot floor)
    {
        var direction = floor.ForceX < 0 ? "west" : floor.ForceX > 0 ? "east" : floor.ForceY < 0 ? "north" : "south";
        var speed = Math.Max(Math.Abs(floor.ForceX), Math.Abs(floor.ForceY));
        return (IsConveyor(floor.Scene) ? "Conveyor" : "Moving floor") + " moving " + direction + "." +
            (speed == 32 ? " You cannot move against this belt." : speed == 16 ? " Run to move against it." : "");
    }
}
