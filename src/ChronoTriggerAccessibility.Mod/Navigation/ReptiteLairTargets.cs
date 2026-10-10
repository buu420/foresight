using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Reptite Lair appearances proven from the installed sprite data. Labels name what
/// is drawn; they never reveal an ambush, the correct tunnel or a destination.</summary>
public static class ReptiteLairTargets
{
    // Atel_0344/0373/0374/0375 load person 0x85 (sheet c140) and set static assembly frame 2
    // (AC 02, mode 3 at 16EA90): an open pit with a rocky rim. Other scenes animate c140 differently.
    private static readonly HashSet<int> HoleScenes = [222, 284, 285, 286];
    private static readonly HashSet<int> LairScenes = [222, 283, 284, 285, 286, 287, 288, 289, 290];

    /// <summary>A drawn hole whose Confirm handler changes scene (catalog warp action).</summary>
    public static bool IsHole(int scene, FieldActorSnapshot actor) =>
        HoleScenes.Contains(scene) && actor.ClassTag == FieldNavigationCapture.ClassTagNpc && actor.VisualIndex == 133 &&
        GameNavigationCatalog.ActorInfo(scene, actor)?.Actions.Any(a => a.Kind == "Warp") == true;

    /// <summary>The scripts park unplaced holes at tile (0,0) (8B 00 00) until a burrowing
    /// beetle or a later event moves one (8C or 8B). That corner is solid rock in every
    /// hole map, so a parked hole has no Confirm contact.</summary>
    public static bool IsParked(FieldActorSnapshot actor) => actor.TileX == 0 && actor.TileY == 0;

    /// <summary>Stable, neutral names by the scene's own hole order.</summary>
    public static string HoleLabel(int scene, FieldActorSnapshot actor)
    {
        var holes = GameNavigationCatalog.ForScene(scene)?.Actors
            .Where(a => a.Loads.Any(l => l.Class == FieldNavigationCapture.ClassTagNpc && l.Visual == 133) &&
                a.Actions.Any(x => x.Kind == "Warp"))
            .Select(a => a.Id).Order().ToList() ?? [];
        var position = holes.IndexOf(actor.Index);
        return position is >= 0 and < 26 ? $"Hole {(char)('A' + position)}" : "Hole";
    }

    /// <summary>load_enemy sprites are 0x107 + id (16B220): c333 blue armored beetle, c301 pink
    /// flowered plant, c386 large horned green dinosaur, c264 small green lizard, c356
    /// mushroom-capped creature, c384 gold creature with green wings.</summary>
    public static string? CreatureAppearance(int scene, FieldActorSnapshot actor) =>
        !LairScenes.Contains(scene) || (actor.ClassTag & ~FieldNavigationCapture.ClassTagRemovedBit) is not
            (FieldNavigationCapture.ClassTagEnemy or FieldNavigationCapture.ClassTagEnemyPeaceful) ? null :
        actor.VisualIndex switch
        {
            0x46 => "Blue beetle",
            0x26 => "Pink flower creature",
            0x7B => "Large green dinosaur",
            0x01 => "Small green lizard",
            0x5D => "Mushroom creature",
            0x79 => "Gold winged creature",
            _ => null,
        };
}
