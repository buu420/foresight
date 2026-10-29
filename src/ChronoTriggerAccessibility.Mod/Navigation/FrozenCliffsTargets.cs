using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Atel0604's current cave entrances. Opcode24 samples the leader's
/// facing into Local3 every loop; North is an interaction finish, not a lock.</summary>
internal static class FrozenCliffsTargets
{
    private static readonly (int X, int Y, int Destination, string Label)[] caves =
    [
        (23, 10, 602, "Upper cave A"), (7, 14, 605, "Left cave B"),
        (33, 23, 605, "Middle cave C"), (13, 30, 602, "Lower cave D"),
        (27, 42, 605, "Lower cave E"), (10, 52, 602, "Bottom cave F"),
    ];
    public const string CaveInstruction = "Move up into the cave. Hold Up against the downhill slide.";
    public static bool IsCave(int scene, GameNavigationCatalog.Region region) => scene == 604 &&
        region is { Actor: 9, Kind: "Warp" } && region.Left == region.Right && region.Top == region.Bottom &&
        caves.Any(c => (c.X, c.Y, c.Destination) == (region.Left, region.Top, region.Destination));
    public static string? CaveLabel(int scene, GameNavigationCatalog.Region region) => IsCave(scene, region)
        ? caves.First(c => c.X == region.Left && c.Y == region.Top).Label : null;
    public static bool RegionAvailable(int scene, GameNavigationCatalog.Region region, FieldStoryState? story) =>
        !IsCave(scene, region) ? region.Available(story) : story?.Local(3) is >= 0 and <= 3 &&
            region.Guards.All(g => g is { Source: "Local", Index: 3, Operation: 1, Value: 0, Expected: false } || g.Allows(story));
    public static bool WatcherAvailable(FieldNavigationSnapshot field, GameNavigationCatalog.Region region) =>
        !IsCave(field.SceneId, region) || field.SceneIdCoherent && field.Actors.Any(a =>
            a is { Index: 9, ClassTag: 7, IsUsable: true, IsPartyMember: false, ScriptProcessingEnabled: true,
                ScriptPriority: 7, ScriptAddress: >= 0x288 and <= 0x361, FineX: 65280, FineY: 65280 } &&
            a.CurrentScriptOpcode is 0x00 or 0x10 or 0x11 or 0x12 or 0x22 or 0x24 or 0xE3 or 0xF0 or 0xF2 or 0xE1);
}
