using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

public static class SealedFieldTargets
{
    public const string PyramidId = "landmark:pyramid";

    public static bool IsDoor(int scene, int actor) => DoorLocal(scene, actor) >= 0;

    // The forwarding marker and its handler are one physical doorway. Retain a
    // stable destination identity even when only the forwarding slot is usable.
    public static int DoorMarker(int scene, int actor) => (scene, actor) switch
    {
        (241, 12 or 13) => 13,
        (242, 14 or 15) => 15,
        _ => actor,
    };

    public static bool DoorClosed(int scene, FieldActorSnapshot actor, FieldStoryState? state) =>
        IsDoor(scene, actor.Index) && actor.IsUsable && actor.ClassTag == 7 &&
        !actor.IsPartyMember && actor.ScriptCallsEnabled &&
        state?.Local(DoorLocal(scene, actor.Index)) is { } opened && opened != 1;

    public static string? DoorInstruction(int scene, FieldStoryState? state) =>
        scene != 242 && state is { Point: < 0xAB } ? "A mysterious force seals the door." : null;

    public static bool PyramidClosed(int scene, FieldStoryState? state) =>
        scene == 42 && state?.Flag(0x19C, 0x04) == false;

    private static int DoorLocal(int scene, int actor) => (scene, actor) switch
    {
        (208, 8) or (210, 17) => 7,
        (217, 9) => 0x0A,
        (241, 12 or 13) or (242, 14 or 15) => 8,
        _ => -1,
    };
}
