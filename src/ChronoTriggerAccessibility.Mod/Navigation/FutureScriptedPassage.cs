using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Map regions tested by native startup watchers, without a
/// MapJump exit cell. Bounds are the exact player-tile predicates in the scripts,
/// not guide coordinates. The field graph still supplies every usable goal.</summary>
public sealed record FutureScriptedPassage(string Id, string Label, int Left, int Top, int Right, int Bottom, bool StoryOnly)
{
    public static IReadOnlyList<FutureScriptedPassage> ForScene(int scene, FieldStoryState? state,
        IReadOnlyList<FieldActorSnapshot> actors)
    {
        if (state is null) return [];
        // Atel_0345 actor 8: file 02AF reads the leader's tiles, 02B3 requires
        // X > 46 and 02B8 requires Y < 11. Reaching the marker at (43,5) is not enough.
        if (scene == 223 && state.Point is >= 55 and < 60 && Initialized(8))
            return [new("passage:eastern-highway", "Eastern highway", 47, 0, 63, 10, false)];
        // Atel_0007 actor 0 refreshes player Y into local 0A; actor 11 watches
        // Y == 42 at file 0487 before starting the first computer-room sequence.
        // This controller is intentionally parked at FF,FF, not at the computer.
        if (scene == 218 && state.Point == 53 && Initialized(11))
            return [new("passage:record-arrival", "Approach to the computer", 0, 42, 63, 42, true)];
        return [];

        bool Initialized(int index) => actors.Any(a => a.Index == index && a.IsUsable && a.ClassTag == 7 && !a.IsPartyMember);
    }
}
