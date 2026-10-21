using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Scoped to the explicitly selected wind-slope destination.</summary>
public sealed class DeathPeakClimbAssist
{
    public const string TargetId = "death-peak:climb";
    public const string ArmId = "death-peak:arm";
    private string? scene;
    private int stage;
    private bool active, sawGust;
    private string? pause, notice;
    private static readonly NavigationResult North = new([], NavigationDirection.North, true, true);
    public string? Status => pause;
    public string? TakeNotice() { var value = notice; notice = null; return value; }

    public void Reset() { scene = null; stage = 0; active = sawGust = false; pause = notice = null; }

    public NavigationFrame Project(NavigationFrame frame)
    {
        var target = frame.Targets.FirstOrDefault(t => t.Id == TargetId);
        if (target is null || target.AutomaticStages.Count == 0) return frame;
        if (!active)
        {
            // A restart just above a tree should return to nearby cover, not
            // commit to the next exposed stretch. Compare current native bounds.
            stage = target.AutomaticStages.Select((leg, index) => (leg, index))
                .Where(t => t.leg.ArrivalHold is not null)
                .OrderBy(t => Distance(t.leg.ArrivalHold!, frame.Player)).Select(t => t.index).FirstOrDefault();
            var exit = target.AutomaticStages[^1];
            if (target.AutomaticStages.Count > 1 && frame.Graph.IsTerminal(frame.Player) &&
                exit.ApproachPoints.Any(p => p.Layer == frame.Player.Layer &&
                    Math.Abs((long)p.X - frame.Player.X) <= 32 && Math.Abs((long)p.Y - frame.Player.Y) <= 32))
                stage = target.AutomaticStages.Count - 1;
        }
        if (stage >= target.AutomaticStages.Count) return frame;
        var leg = target.AutomaticStages[stage] with
        {
            Id = target.Id, Label = target.Label, Category = target.Category,
            GuideAvailable = target.GuideAvailable, AutomaticStages = target.AutomaticStages,
            Instruction = target.Instruction,
        };
        return frame with { Targets = frame.Targets.Select(t => t.Id == TargetId ? leg : t).ToArray() };
    }

    public string? Validate(NavigationFrame frame, NavigationController controller,
        NavigationResult result, FieldFloorSnapshot? floor)
    {
        if (!result.AutoWalking || !controller.IsActive || controller.TargetId != TargetId)
        { Reset(); return null; }
        var target = frame.Targets.FirstOrDefault(t => t.Id == TargetId);
        if (target is null || stage >= target.AutomaticStages.Count ||
            active && scene != frame.Scene || !frame.CanNavigate)
        { Reset(); return "the climbing route is unavailable"; }
        if (!active)
        {
            // Commands can start after an inactive Update in this same tick.
            // Bind to the actual projected leg, rather than a reset stage cursor.
            stage = target.AutomaticStages.ToList().FindIndex(t => t.ArrivalHold == target.ArrivalHold &&
                t.ApproachPoints.SequenceEqual(target.ApproachPoints));
            if (stage < 0) { Reset(); return "the climbing stage is unavailable"; }
            active = true; scene = frame.Scene; sawGust = false;
        }
        if (floor is not { Scene: 244, WindGusting: not null, WindBuilding: not null } wind ||
            frame.Scene != $"{wind.Engine:X8}:{wind.Scene}")
        {
            if (controller.IsHolding)
            {
                Pause("The wind reading is unavailable. Still holding shelter; automatic departures are paused.");
                return null;
            }
            // The entry's terrain probe can remain unreadable until movement.
            // Reaching the first cover makes no wind-timed departure decision.
            if (stage != 0) { Reset(); return "the climbing wind state is unavailable"; }
        }
        if (RunningInput(frame, North, floor).StopReason is { } runReason)
        {
            if (!controller.IsHolding) { Reset(); return runReason; }
            Pause(floor is { RunMode: not null, RunToggle: not null }
                ? "Running is not ready. Enable running with the game's Dash control or setting. Still holding shelter."
                : "The running setting is unavailable. Still holding shelter; automatic departures are paused.");
            return null;
        }
        pause = null;
        return null;
    }

    private void Pause(string message)
    {
        if (pause != message) notice = message;
        pause = message;
        sawGust = false;
    }

    public static FieldNavigationPad RunningInput(NavigationFrame frame, NavigationResult result, FieldFloorSnapshot? floor)
    {
        if (floor is not { RunMode: not null, RunToggle: not null } value ||
            frame.Scene != $"{value.Engine:X8}:{value.Scene}")
            // The fresh map still proves whether this tile needs running.
            // Ordinary movement can start on the unforced/gentle entry tiles;
            // the steep slope retains the audited run-data requirement.
            return FieldFloorMovement.Adjust(frame, result, null);
        // Reuse the audited native run rules even on the gentle approach. This
        // local value probes capability; it does not alter the map or the game.
        var probe = FieldFloorMovement.Adjust(frame with { MovingFloor = new(NavigationDirection.South, 16) }, North, floor);
        if (probe.StopReason is not null) return new(0, probe.StopReason);
        // 175C90 always runs in mode1 with held Dash, independently of a physical
        // release changing its toggle later in this native tick.
        return new(FieldNavigationRuntime.DirectionBits(result.Direction) | (floor?.RunMode == 1 ? 8u : 0u));
    }

    private static long Distance(NavigationHold hold, NavigationPoint player) =>
        Math.Abs((long)player.X - Math.Clamp(player.X, hold.Minimum.X, hold.Maximum.X)) +
        Math.Abs((long)player.Y - Math.Clamp(player.Y, hold.Minimum.Y, hold.Maximum.Y));

    public bool Advance(NavigationFrame frame, NavigationController controller,
        FieldFloorSnapshot floor, bool canDepart, out string? announcement)
    {
        announcement = null;
        if (!active || !controller.IsHolding || pause is not null) { sawGust = false; return false; }
        if (floor.WindGusting == true) { sawGust = true; return false; }
        var passed = sawGust;
        // Consume the edge even if the player is browsing at that instant.
        // Closing a menu later must not depart in an unknown portion of a lull.
        sawGust = false;
        var target = frame.Targets.First(t => t.Id == TargetId);
        if (!passed || floor.WindBuilding != false || !canDepart ||
            stage + 1 >= target.AutomaticStages.Count) return false;
        var next = target.AutomaticStages[stage + 1];
        if (!string.IsNullOrWhiteSpace(next.GuidanceRestriction) ||
            NavigationPathfinder.Search(frame.Graph, frame.Player, next.ApproachPoints).Route is null)
        {
            announcement = "The next climbing route is unavailable. Still holding shelter.";
            return false;
        }
        stage++;
        var label = target.AutomaticStages[stage].Label;
        if (label.StartsWith("To ", StringComparison.Ordinal)) label = label[3..];
        announcement = $"Wind eased. Moving to {label}.";
        return true;
    }
}
