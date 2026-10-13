using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Tyranno Lair interior (scene 302, Atel_0057), after the Turnkey battle.
///
/// Actor 0 loops from 0x0284: opcode 22 stores the leader tile in locals 8 and 9, then the
/// script requires X == 0x37 and Y from 0x13 to 0x19. At story 0x96 exactly, with local 0x0A
/// clear, it requests actor 9's fn3 (0x053A), which draws Kino and walks him to tile (49,19),
/// then opcode 75 sets local 0x0A for the rest of the load. Opcode 12 reads that local at
/// A+118B0+index*8 (1651A0); opcode 75 writes the same cell (169280). Kino's fn1 (0x04D6) is
/// a Confirm handler and ends with 5A 97; he has no touch handler.
///
/// Until that crossing the cell is empty to a sighted player, so the column is offered as a
/// story place, never as a person, and only while every native gate above holds.
/// Evidence: artifacts/research/tyranno-0357/findings.md.</summary>
public static class TyrannoLairTargets
{
    /// <summary>Bound by the "free-kino" objective (actor 9) while he is still hidden.</summary>
    public const string RevealId = "script-action:9:reveal:55:19:55:25";
    private const int Scene = 302, StoryPoint = 0x96, RevealedLocal = 0x0A;
    private const int StripX = 0x37, StripTop = 0x13, StripBottom = 0x19;
    // Actor 0's loop is 0x0284..0x02D8 in file offsets; +48 holds the offset minus one
    // (opcode at ScriptData+12001+pc), so 0283 reads opcode 22 as the log shows.
    private const int LoopFirst = 0x0283, LoopLast = 0x02D7;
    // Kino's initializer parks at 0x04D5 (B2) after 91; fn3 moves the PC on.
    private const int KinoParked = 0x04D4;

    public static IReadOnlyList<NavigationTarget> Build(FieldNavigationSnapshot field, FieldMapSnapshot map,
        FieldStoryState? story)
    {
        if (!field.SceneIdCoherent || field.SceneId != Scene || field.ControlFlag == 0 || field.InputMode != 0 ||
            map.TransitionPending || field.LeadPlayer is not { IsUsable: true, IsDrawn: true } lead ||
            story is not { Point: StoryPoint } || story.Local(RevealedLocal) != 0) return [];
        if (!field.Actors.Any(a => a.Index == 0 && a.ScriptProcessingEnabled &&
                a.ScriptAddress is >= LoopFirst and <= LoopLast)) return [];
        if (!field.Actors.Any(a => a.Index == 9 && a.IsUsable && !a.IsDrawn &&
                a.ClassTag == FieldNavigationCapture.ClassTagNpc && a.VisualIndex == 6 &&
                a.ScriptAddress == KinoParked)) return [];
        var floor = new FieldNavigationGraph(map);
        var goals = new List<NavigationPoint>();
        for (var y = StripTop; y <= Math.Min(StripBottom, map.Height - 1); y++)
        for (var layer = 1; layer <= 3; layer++)
            if (StripX < map.Width && floor.TryPosition(StripX * 256 + 128, y * 256 + 128, layer, out var point) &&
                !floor.IsTerminal(point)) goals.Add(point);
        if (goals.Count == 0) return [];
        var ordered = goals.Distinct().OrderBy(p => Math.Abs((long)p.X - lead.FineX) + Math.Abs((long)p.Y - lead.FineY)).ToArray();
        return [new NavigationTarget(RevealId, "Story event", NavigationCategory.Exits, ordered[0], ordered, false, false)
            { GuideAvailable = true }];
    }
}
