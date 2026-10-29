using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Scene406's map177 packs six distinct rooms into three floor bands.
/// Its native self-exits connect rooms by warping; a nearby terrain interaction
/// cannot bridge the solid rows or join the left and right rooms.</summary>
internal static class OceanPalaceTargets
{
    /// <summary>These exact script placements use 8E00/04 below the opaque
    /// Palace background. Keep them in native collision, but do not expose
    /// their concealed sprite or battle identity as a visible destination.</summary>
    public static bool ConcealedSprite(int scene, FieldActorSnapshot actor) =>
        actor.RenderPriorityPacked == 0 && (scene, actor.Index, actor.ClassTag, actor.VisualIndex) is
            (406, 15 or 21, 5 or 6, 0x3B) or
            (406, >= 30 and <= 33, 4, 0x30) or
            (411, 16, 5 or 6, 0x3B) or
            // Atel0393 actor23 (07E8 8E 04), raised only by actor7's request (07F8 8E 33).
            (405, 23, 5 or 6, 0x3B) or
            // Atel0499 0494/050D 8E 04: Algetty's Schala and Janus after the scene (0xBA).
            (382, 11, 4, 0x09) or (382, 12, 4, 0x0A) or
            // Atel0399 035B 8E 04: the bridge knob beneath its panel (BridgePanel).
            (412, 9, 4, 0x64) or
            // Atel0222 04FA/055A 8E 00: both Golems wait until Dalton's summon (fn3 8E 22).
            (414, 15 or 16, 5 or 6, 0x95);

    /// <summary>Atel0499 parks the Algetty scene's cast on floorless tile (9,17) below the
    /// doorway while global 0x1A0 bit 0x20 says the scene is pending (set in the Mudbeast
    /// den, cleared at 05F6). Actor0 starts that scene on entry.</summary>
    public static bool ParkedCast(int scene, FieldActorSnapshot actor, FieldStoryState? story) =>
        scene == 382 && actor is { TileX: 9, TileY: 17 } && story?.Flag(0x1A0, 0x20) == true &&
        (actor.Index, actor.ClassTag, actor.VisualIndex) is
            (9, 4, 0x41) or (11, 4, 0x09) or (12, 4, 0x0A) or (13, 5 or 6, 0xA1);

    /// <summary>Dalton's own Confirm cannot start either of his scenes; a live controller
    /// loop does once the leader crosses a native boundary. Bind that boundary to Dalton.
    /// In 414 he is drawn from entry beyond the camera, so only once he has been seen.</summary>
    public static GameNavigationCatalog.Region? NpcRegion(FieldNavigationSnapshot field,
        FieldActorSnapshot actor, FieldStoryState? story, bool seen)
    {
        if (actor.ClassTag is not (5 or 6) || actor.VisualIndex != 0xA1) return null;
        // Atel0206 actor8's loop starts the throne room scene when the leader enters
        // Y<13, while local8 is zero.
        if (field.SceneId == 334 && actor.Index == 10 && story is { Point: >= 0xBB } &&
            field.Actors.Any(a => a.Index == 8 && a.ClassTag == 4 && a.VisualIndex == 151 &&
                a.IsUsable && a.ScriptProcessingEnabled))
            return GameNavigationCatalog.ForScene(334)?.Regions.FirstOrDefault(r =>
                r.Actor == 8 && r.Kind == "Encounter" && r.Value == 49298 && r.Available(story));
        // Atel0222 actor12 loops while local8 is zero and starts Dalton's scene once the
        // leader's row is below 0x13 (rows 0-18).
        if (field.SceneId == 414 && actor.Index == 14 && seen && story is not null &&
            field.Actors.Any(a => a.Index == 12 && a.ClassTag == 7 && a.IsUsable && a.ScriptProcessingEnabled))
            return GameNavigationCatalog.ForScene(414)?.Regions.FirstOrDefault(r =>
                r.Actor == 12 && r.Kind == "Encounter" && r.Value == 49298 && r.Available(story));
        return null;
    }

    /// <summary>Map175 deliberately repeats its 64 collision rows over the
    /// 128-row great stair. The native movers take tile coordinates modulo
    /// the plane dimensions (173750); exit and script coordinates stay full
    /// height. Vortex Map633 has byte-identical property planes and the same
    /// extent, with Atel0633's exit watcher at row121. Expand only these rooms.</summary>
    public static FieldMapSnapshot Map(FieldNavigationSnapshot field, FieldMapSnapshot map)
    {
        if (!field.SceneIdCoherent || field.SceneId is not (409 or 633) || map.Width != 32 || map.Height != 64 ||
            map.ExitWidth != 32 || map.ExitHeight != 128 || map.ExitCells.Length != 4096 ||
            map.CollisionShapes.Length != 2048 || map.TerrainFlags.Length != 2048 ||
            map.CollisionLayers.Length != 2048) return map;
        return map with { Height = 128, CollisionShapes = Repeat(map.CollisionShapes),
            TerrainFlags = Repeat(map.TerrainFlags), CollisionLayers = Repeat(map.CollisionLayers) };

        static byte[] Repeat(byte[] plane)
        {
            var result = new byte[4096];
            plane.CopyTo(result, 0); plane.CopyTo(result, 2048);
            return result;
        }
    }

    public static bool DifferentRoom(int scene, NavigationPoint player, NavigationTarget target) =>
        scene == 406 && !target.IsStoryNote && Section(player) != Section(target.Position);

    /// <summary>The live row of Atel0221 actor9's ambush. Its startup loop (029F)
    /// runs once the leader passes row 0x12, then stops itself (0B 12 at 02D0).</summary>
    public const string ElevatorAmbush = "script-encounter:9:16400:0:19:255:255";

    /// <summary>Spoken for a lower wall switch while its passage is closed.</summary>
    private const string BlockedPassage = "The passage to this switch is blocked.";
    private const string CreatureInFront = " A creature stands nearby, listed under Enemies.";

    public static bool IsBlockedPassage(string? restriction) =>
        restriction?.StartsWith(BlockedPassage, StringComparison.Ordinal) == true;

    /// <summary>Exact native switch and control sprites. The panel drawn in 407 has no
    /// handler; slot 8 beneath it runs the switch (Atel0395 actor8 041B on Confirm or
    /// contact). Scene 412's panel is listed through <see cref="BridgePanel"/>.</summary>
    public static string? ObjectLabel(int scene, FieldActorSnapshot actor) =>
        (scene, actor.Index, actor.ClassTag, actor.VisualIndex) switch
        {
            // Atel0394 load_npc A7 floor panels: contact function 2 sets G162 bit 4 or 2.
            (406, 10 or 16, 4, 0xA7) or (407, 8, 5, 0xD5) => "Floor switch",
            // Atel0221 actor10 / Atel0226 actor9: Confirm function 1 rides to 416 / 417.
            (411, 10, 4, 0x71) or (418, 9, 4, 0x71) => "Elevator control",
            _ => null,
        };

    /// <summary>Atel0399's bridge switch as drawn: load_npc A7 with 8E 26, above the
    /// background, wherever 0x15A bit 3 has placed it. It has no handler of its own.</summary>
    public static bool BridgePanel(int scene, FieldActorSnapshot actor) =>
        scene == 412 && (actor.Index, actor.ClassTag, actor.VisualIndex) is (8, 4, 0xA7) &&
        actor.RenderPriorityPacked != 0;

    /// <summary>Atel0399 actor9, load_npc 64 beneath the panel. Its contact function 2
    /// extends the bridge, then toggles it; its catalog guards say when.</summary>
    public static bool BridgeKnob(int scene, FieldActorSnapshot actor) =>
        scene == 412 && (actor.Index, actor.ClassTag, actor.VisualIndex) is (9, 4, 0x64);

    /// <summary>Spoken for the bridge panel while the knob has no available contact.</summary>
    public const string InactiveSwitch = "This switch is currently inactive.";

    /// <summary>Atel0394 markers 22/26: Confirm function 1 copies the wall tiles
    /// and sets G162 bit 0x10 or 0x20.</summary>
    public static string? MarkerLabel(int scene, FieldActorSnapshot actor) =>
        scene == 406 && actor.Index is 22 or 26 && actor.ClassTag == 7 ? "Wall switch" : null;

    /// <summary>Each lower wall switch is reached through a two-tile passage on row
    /// 31. Atel0394 loads two solid people there (load_npc 30); only the room
    /// creature's contact (actor25/29 function 2) hides them. Restrict the switch
    /// while both are drawn at their load positions with the native contact gate,
    /// and mention the creature only while it is drawn with its contact enabled.</summary>
    public static string? WallSwitchBlocked(FieldNavigationSnapshot field, FieldActorSnapshot marker)
    {
        if (field.SceneId != 406 || marker.ClassTag != 7) return null;
        (int Creature, (int Slot, int X)[] People) room = marker.Index switch
        {
            22 => (25, [(30, 3968), (31, 4224)]),
            26 => (29, [(32, 10112), (33, 9856)]),
            _ => (-1, []),
        };
        if (room.People.Length == 0 || !room.People.All(p => field.Actors.Any(a => a.Index == p.Slot &&
                a.ClassTag == 4 && a.VisualIndex == 0x30 && a.IsUsable && a.IsDrawn && (a.LoadedFlag & 1) != 0 &&
                a.ActivationBinding != 0 && a.FineX == p.X && a.FineY == 7936))) return null;
        var creature = field.Actors.Any(a => a.Index == room.Creature && a.ClassTag == 5 && a.VisualIndex == 0x52 &&
            a.IsUsable && a.IsDrawn && a.ScriptCallsEnabled);
        return creature ? BlockedPassage + CreatureInFront : BlockedPassage;
    }

    /// <summary>The ambush row is spent once its own controller has stopped.</summary>
    public static bool SpentRegion(FieldNavigationSnapshot field, GameNavigationCatalog.Region region) =>
        field.SceneId == 411 && region.Actor == 9 && region.Kind == "Encounter" && region.Value == 16400 &&
        field.Actors.Any(a => a.Index == 9 && a.ClassTag == 7 && a.IsUsable && !a.ScriptProcessingEnabled);

    private static int Section(NavigationPoint point) =>
        (point.X / 256 < 28 ? 0 : 1) + (point.Y / 256 < 24 ? 0 : point.Y / 256 < 44 ? 2 : 4);
}
