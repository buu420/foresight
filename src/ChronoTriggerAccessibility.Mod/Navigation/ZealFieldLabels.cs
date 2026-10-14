using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Exact installed Atel0208/0209 book identities and Atel0059..0066
/// teleport markers. Closed books keep neutral identifiers; their native
/// opening effects, rather than puzzle knowledge, supply the descriptions.</summary>
internal static class ZealFieldLabels
{
    public static string? Book(int scene, FieldActorSnapshot actor, FieldStoryState? state)
    {
        if (!actor.IsUsable || actor.IsPartyMember || !actor.ScriptCallsEnabled) return null;
        var letter = (scene, actor.Index, actor.ClassTag, actor.VisualIndex) switch
        {
            (355, 10, 4, 115) or (357, 11, 4, 115) => "A",
            (355, 11, 4, 128) or (357, 12, 4, 128) => "B",
            // Enhasa's closed book is map scenery. Actor13 receives Confirm;
            // actor12 is only its effect sprite and may be parked off map.
            (355, 13, 7, 0) or (357, 13, 4, 136) => "C",
            _ => null,
        };
        if (letter is null || actor.ClassTag != 7 && !actor.IsDrawn) return null;
        var local = letter switch { "A" => 10, "B" => 11, _ => 12 };
        return $"Book {letter}" + (state?.Local(local) switch { 0 => ", closed", > 0 => ", open", _ => "" });
    }

    public static bool IsBookEffect(int scene, FieldActorSnapshot actor) =>
        (scene, actor.Index, actor.ClassTag, actor.VisualIndex) == (355, 12, 4, 136);

    public static string? TeleportPad(int scene, FieldActorSnapshot actor)
    {
        if (actor.Index != 8 || actor.ClassTag != 7 || !actor.IsUsable ||
            actor.IsPartyMember || !actor.ScriptCallsEnabled) return null;
        return scene switch
        {
            336 or 352 or 353 => "Land Bridge teleport pad",
            337 or 354 or 356 => "Skyway teleport pad",
            _ => null,
        };
    }

    public static bool ConcealedExit(int scene, int exit) => (scene, exit) is (355, 1) or (357, 3);
    // Atel0208/0209 set local0E only after the three native E5 copies finish.
    // This also covers returning through the opening without solving again.
    public static bool PassageOpen(FieldStoryState? state) => state?.Local(14) == 1;

    public static string? Actor(int scene, FieldActorSnapshot actor) =>
        (scene, actor.Index, actor.ClassTag, actor.VisualIndex) is (355, 9, 4, 100) or (359, 8, 4, 100)
            ? "Small white doll" : null;
}
