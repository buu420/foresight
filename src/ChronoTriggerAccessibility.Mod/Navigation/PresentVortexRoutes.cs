using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>608's switch ledge is reached from615, not its lower entrance.
/// Select native current-room connections; never synthesize a walking point.</summary>
internal static class PresentVortexRoutes
{
    public static FullStoryObjective ForRoom(FullStoryObjective objective, int scene, FieldStoryState state,
        IReadOnlyList<NavigationTarget> available)
    {
        if (state.Extra(35) is not { } flags || (flags & 1) != 0 ||
            !objective.Goals.Any(g => g.Scene == 608 && g.Targets.Contains(PresentVortexTargets.FirstSwitch))) return objective;
        // A scene-only route otherwise returns to608's wrong ledge. These
        // native transfers are608A->610->614->615->608B. Once inB use the
        // ordinary switch target; after E23:01,609 is one connected floor.
        var target = scene switch
        {
            608 when available.Any(t => t.Id == PresentVortexTargets.FirstSwitch) => null,
            608 => "script-region:8:610:11:6:12:7",
            610 => "exit:1",
            614 => "exit:1",
            615 => "script-region:8:608:13:2:14:2",
            _ => null,
        };
        return target is null ? objective : objective with
        {
            Goals = [new(scene, [], [target])],
            Instruction = "Follow the volcano passages to the switch's ledge.",
        };
    }
}
