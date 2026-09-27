using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>
/// Field save points, found the way the engine decides where saving is allowed.
/// <para>CanSave 0x213580 and the top-menu builder 0x1D0FE0 allow a field save only while
/// FieldState+0x73C bit 7 is set; FieldState is ActorBase+0x110B0, the script global array, so
/// that word is global 0x1CF. Every save point has a checker actor whose loop reads the leader's
/// tile (opcode 22, 0x165E20: actor +80/+8C), sets that bit (65 87 CF) while the leader stands on
/// its one tile, clears it (66 87 CF) elsewhere, and opens the Save UI (C8 40) when Confirm is
/// pressed there (opcode 31, 0x166360). A person 0x79 sprite marks the tile; the script runner
/// 0x161560 pairs that sprite with the checker's Confirm test.</para>
/// <para>The installed catalog records each checker loop as a Switch region that sets global
/// 0x1CF bit 0x80 on exactly that tile: 57 checkers in 54 scenes, all of them paired with a
/// person 0x79 on the same tile. A sparkle drawn anywhere else, with no such region, cannot
/// enable saving and is not offered. Only a drawn sparkle and a running checker in its audited
/// place make a save point, so script-hidden and script-disabled save points stay hidden.</para>
/// </summary>
public static class FieldSavePoints
{
    public const int CanSaveGlobal = 0x1CF;
    public const int CanSaveBit = 0x80;
    public const int SparkleVisual = 0x79;
    public const string Label = "Save point";
    public const string Instruction = "Stand on it and press Confirm to save.";
    public const string ArrivalInstruction = "Press Confirm to save.";

    /// <param name="Checker">The actor whose loop enables saving.</param>
    /// <param name="Actors">The checker and every sparkle drawn on its tile.</param>
    public sealed record SavePoint(int Checker, int TileX, int TileY, IReadOnlyList<int> Actors);

    public static bool IsSaveRegion(GameNavigationCatalog.Region region) =>
        region is { Kind: "Switch", Source: "Global", Index: CanSaveGlobal, Set: true } &&
        (region.Value & CanSaveBit) != 0 && region.Left == region.Right && region.Top == region.Bottom;

    public static IReadOnlyList<SavePoint> Find(FieldNavigationSnapshot field, FieldStoryState? story)
    {
        if (!field.SceneIdCoherent || GameNavigationCatalog.ForScene(field.SceneId) is not { } scene) return [];
        var result = new List<SavePoint>();
        foreach (var region in scene.Regions.Where(IsSaveRegion))
        {
            if (result.Any(s => s.Checker == region.Actor && s.TileX == region.Left && s.TileY == region.Top)) continue;
            // The catalog's "bit not yet set" guard only picks the loop's first frame on the tile
            // (sound and set) over the later frames (Confirm opens the Save UI); both are the
            // save point. Every other guard is the script's own gate and must hold.
            if (!region.Guards.Where(g => !(g.Source == "Global" && g.Index == CanSaveGlobal)).All(g => g.Allows(story)))
                continue;
            var checker = field.Actors.FirstOrDefault(a => a.Index == region.Actor);
            if (checker is null || !checker.IsUsable || checker.IsPartyMember || !checker.ScriptProcessingEnabled ||
                (checker.ClassTag & FieldNavigationCapture.ClassTagRemovedBit) != 0 ||
                checker.TileX != region.Left || checker.TileY != region.Top) continue;
            var sparkles = field.Actors.Where(a => a.IsUsable && a.IsDrawn && !a.IsPartyMember && a.ClassTag == 4 &&
                a.VisualIndex == SparkleVisual && a.TileX == region.Left && a.TileY == region.Top)
                .Select(a => a.Index).ToArray();
            if (sparkles.Length == 0) continue;
            result.Add(new(region.Actor, region.Left, region.Top, sparkles.Prepend(region.Actor).Distinct().ToArray()));
        }
        return result;
    }
}
