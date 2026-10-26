using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

internal static class BlackOmenTargets
{
    private static readonly GameNavigationCatalog.TileCopy FinalDoorCopy = new(30, 45, 31, 47, 11, 3, 0x79);

    public static string? ExitLabel(int scene, int exit) => scene == 324 && exit == 1
        ? "Passage through the northern door" : null;

    public static bool IsFinalDoor(int scene, FieldActorSnapshot actor) => scene == 324 &&
        actor is { Index: 20, ClassTag: 7, FineX: 3072, FineY: 1488, IsUsable: true, IsPartyMember: false };

    private static bool DoorContract(int scene, FieldActorSnapshot actor, FieldStoryState? story) =>
        IsFinalDoor(scene, actor) && actor.ScriptCallsEnabled && actor.ScriptProcessingEnabled &&
        story?.Local(10) == 0 && DoorAction()?.Available(story) == true;

    public static bool OpeningFinalDoor(int scene, FieldActorSnapshot actor, FieldStoryState? story) =>
        DoorContract(scene, actor, story) && actor.ScriptPriority == 2 && actor.ScriptAddress is >= 0x551 and <= 0x562;

    public static bool CanOpenFinalDoor(int scene, FieldActorSnapshot actor, FieldStoryState? story) =>
        DoorContract(scene, actor, story) && (actor.ScriptPriority is > 2 || OpeningFinalDoor(scene, actor, story));

    public static GameNavigationCatalog.TileCopy? DoorOpening(FieldNavigationSnapshot field, FieldStoryState? story) =>
        field.SceneIdCoherent && field.Actors.Any(a => CanOpenFinalDoor(field.SceneId, a, story)) ? DoorAction()?.Copy : null;

    private static GameNavigationCatalog.Action? DoorAction() => GameNavigationCatalog.ForScene(324)?.Actors
        .FirstOrDefault(a => a.Id == 20 && a.Marker)?.Actions.FirstOrDefault(a => a.Touch && a.Kind == "Terrain" &&
            a.Copy == FinalDoorCopy && a.Guards is [{ Source: "Local", Index: 10, Operation: 0, Value: 0, Expected: true }]);

    public static string? DoorRestriction(FieldActorSnapshot actor, FieldStoryState? story) =>
        story?.Local(10) is null ? "The door state is unavailable." : story.Local(10) == 1 ? null :
        CanOpenFinalDoor(324, actor, story) ? null : "The door interaction is currently unavailable.";

    public static FullStoryObjective ForRoom(FullStoryObjective objective, int scene, FieldStoryState state)
    {
        if (objective.Id != "full:lavos") return objective;
        if (scene == 99)
        {
            // Atel0455 and the optional elevator guide use this native stop
            // order. The return doorway is not forward at stops01/04.
            var flags = state.Global(0x1A7);
            int? actor = flags is not { } stop ? null : (stop & 1) != 0 ? 3 :
                (stop & (2 | 8)) != 0 ? 1 : (stop & 4) != 0 ? 2 : null;
            return objective with
            {
                Label = "Continue through Black Omen",
                // Empty goals deliberately suppress the generic End of Time
                // fallback when the current native stop cannot be established.
                Goals = actor is { } control ? [new(scene, [control], [])] : [],
                Instruction = actor is null ? "The elevator's current stop is unavailable. Check the local controls."
                    : actor == 1 ? "Continue through the elevator doorway."
                    : "Use the sparkle to ride the elevator.",
            };
        }
        StoryGoal? goal = scene switch
        {
            97 => new(scene, [], ["script-region:0:98:7:6:8:6"]),
            98 or 321 => new(scene, [], ["exit:0"]),
            320 => new(scene, [], ["script-region:0:321:0:38:255:38"]),
            324 when state.Local(10) == 1 => new(scene, [], ["exit:1"]),
            324 => new(scene, [20], []),
            _ => null,
        };
        // The End of Time gate remains the general alternative outside these
        // rooms. Within this route, guide the player's chosen dungeon forward.
        return goal is null ? objective : objective with
        {
            Label = "Continue through Black Omen",
            Goals = [goal],
            Instruction = scene == 324 && state.Local(10) != 1
                ? "Walk up to the northern door to open it."
                : scene is 97 or 320 ? "Step onto the teleport pad to continue."
                : "Continue along the passage through this room."
        };
    }

    // Atel0453/0454 and0495/0496 actor0: these are the four pad
    // contacts. Scene320 uses a full-row guard, bounded by its chamber's floor;
    // the separate ordinary return-doorway regions retain exit behavior.
    public static bool IsPad(int scene, GameNavigationCatalog.Region region) => region.Kind == "Warp" &&
        (scene, region.Actor, region.Destination, region.Left, region.Top, region.Right, region.Bottom) is
            (97, 0, 98, 7, 6, 8, 6) or (98, 0, 97, 23, 22, 24, 22) or
            (320, 0, 321, 0, 38, 255, 38) or (321, 0, 320, 23, 25, 24, 26);

    public static string? PadRestriction(int scene, GameNavigationCatalog.Region pad, FieldStoryState? story)
    {
        if (pad.Guards.Any(g => g.Source == "Global" && story?.Global(g.Index) is null))
            return "Teleportation status is unavailable.";
        return pad.Available(story) ? null : scene switch
        {
            320 => "Walk south to the corridor below the pad chamber, then return north to teleport.",
            321 => "Step off the pad to the north or south, then return to teleport.",
            _ => "Step away from the pad, then return to teleport."
        };
    }

    public static IEnumerable<NavigationPoint> PadPoints(int scene, GameNavigationCatalog.Region region,
        FieldNavigationGraph graph, int layer)
    {
        if (scene is not (97 or 98) || !IsPad(scene, region)) yield break;
        // The native pad spans two columns. Its reachable corridor is centered
        // on their shared boundary, not either tile center (Map12, shape edges).
        var x = (region.Left + region.Right + 1) * 128;
        var y = (region.Top + region.Bottom + 1) * 128;
        if (graph.TryPosition(x, y, layer, out var point) && !graph.IsTerminal(point)) yield return point;
    }

    public static FieldMapSnapshot Map(FieldNavigationSnapshot field, FieldMapSnapshot map)
    {
        // Scene98 is the lower-right landing of Map12. Native173750/16C3F0
        // wrap plane indices; the logged collision plane stays32x16 while
        // Atel0454 uses row22 and the actual exit occupies row30.
        if (!field.SceneIdCoherent || field.SceneId != 98 || map.TransitionPending ||
            map.Width != 32 || map.Height != 16 || map.PlayerLayer != 1 ||
            map.CollisionShapes.Length != 512 || map.TerrainFlags.Length != 512 || map.CollisionLayers.Length != 512 ||
            map.ExitWidth <= 24 || map.ExitHeight <= 30 || map.ExitCells.Length != map.ExitWidth * map.ExitHeight ||
            map.ExitCells[30 * map.ExitWidth + 23] != 0 || map.ExitCells[30 * map.ExitWidth + 24] != 0 ||
            map.ExitDestinations?.TryGetValue(0, out var destination) != true || destination != 99) return map;
        return map with
        {
            Height = 32,
            CollisionShapes = Repeat(map.CollisionShapes),
            TerrainFlags = Repeat(map.TerrainFlags),
            CollisionLayers = Repeat(map.CollisionLayers)
        };

        static byte[] Repeat(byte[] plane)
        {
            var result = new byte[1024];
            plane.CopyTo(result, 0); plane.CopyTo(result, 512);
            return result;
        }
    }
}
