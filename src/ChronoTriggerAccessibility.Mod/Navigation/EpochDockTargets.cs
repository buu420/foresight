using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>End of Time's native boarding marker (scene464, Atel_0283 actor36).
/// Its fn2 reaches the boarding choice through opcode A4, which has one branch
/// operand in the PC handler at 1619E0. The old catalog misses that path.
///
/// At raw 199A, story203 or later with D8 bit1 set and bit2 clear diverts to
/// Gaspar's conversation. Otherwise 19C3 requires story174 and 19CB requires
/// globalAB bit80: scene473's disembark writes CD=4, then this actor's initializer
/// sets AB80 and CD=6. Boarding clears AB80, writes CD=5 and warps to scene473
/// only after the player answers choice A5. FC05 is an activation counter advance,
/// not a position predicate. The native touch dispatcher 16EF30 uses fn2 and
/// priority2; position/contact geometry is owned by 178980.
/// Evidence: artifacts/research/epoch-seals-0362/epoch-agent/.</summary>
public static class EpochDockTargets
{
    public const int Actor = 36;
    public const string Id = "epoch-dock:36";

    public static IReadOnlyList<NavigationTarget> Build(FieldNavigationSnapshot field, FieldMapSnapshot map,
        FieldViewport viewport, FieldStoryState? story)
    {
        if (!field.SceneIdCoherent || field.SceneId != 464 || field.ControlFlag == 0 || field.InputMode != 0 ||
            map.TransitionPending || field.LeadPlayer is not { IsUsable: true, IsDrawn: true } ||
            story is not { Point: >= 174 } || story.Flag(0xAB, 0x80) != true) return [];
        if (story.Point >= 203 && (story.Global(0xD8) is not { } afterFall ||
            (afterFall & 1) != 0 && (afterFall & 2) == 0)) return [];
        // xy 27,18 at 195D is the marker's only position setter. 8B (16C040)
        // places X at tile+80 and Y at tile+FF. It never loads a sprite.
        var marker = field.Actors.FirstOrDefault(a => a.Index == Actor && a.ClassTag == 7 &&
            a.IsUsable && !a.IsPartyMember && a.ScriptCallsEnabled &&
            a.ScriptProcessingEnabled && a.ScriptPriority is >= 3 && a.FineX == 10112 && a.FineY == 6399);
        if (marker is null) return [];
        var floor = new FieldNavigationGraph(map);
        var goals = new List<NavigationPoint>();
        for (var layer = 1; layer <= 3; layer++)
        foreach (var candidate in FieldTerrainGraph.Contacts(marker, layer))
            if (floor.TryPosition(candidate.X, candidate.Y, candidate.Layer, out var at) &&
                !floor.IsTerminal(at)) goals.Add(at);
        var positions = goals.Distinct().ToArray();
        if (positions.Length == 0) return [];
        return [new(Id, "Epoch dock", NavigationCategory.Objects,
            new(marker.FineX, marker.FineY, positions[0].Layer), positions,
            viewport.Intersects(39 * 256, 24 * 256, 40 * 256, 25 * 256), false)
        {
            ActorSlot = Actor, GuideAvailable = true,
            Instruction = "Approach the dock to open the game's boarding choice.",
            ArrivalInstruction = "Use the game's boarding choice to board the Epoch.",
            ContactPosition = new(marker.FineX - marker.CollisionOffsetX * 16 - 1,
                marker.FineY - (field.LastPartySlotRaw > Actor * 2 ? 1 : 0), positions[0].Layer),
        }];
    }
}
