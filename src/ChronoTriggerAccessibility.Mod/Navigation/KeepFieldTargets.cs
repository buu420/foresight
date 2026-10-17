using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Keep contacts and appearance boundaries. Identical lights retain
/// player-visible labels; private trap roles never supply navigation choices.</summary>
public static class KeepFieldTargets
{
    public const string EntranceDoorLabel = "Keep entrance door";
    // Atel0246 actor14 fn2: final E5 frame after the sound/animation wait.
    private static readonly GameNavigationCatalog.TileCopy OpenEntrance = new(4, 39, 7, 43, 6, 26, 0x3B);

    public static bool AmbiguousLights(int scene) => scene is 174 or 471;
    public static string LightId(int actor) => $"keep-light:{actor}";
    public static string? LightLabel(int scene, int actor) => (scene, actor) switch
    {
        (471, >= 9 and <= 12) => $"Glowing point {(char)('A' + actor - 9)}",
        (174, 8 or 9) => $"Glowing point {(char)('A' + actor - 8)}",
        _ => null,
    };
    public static string? LightLabel(int scene, FieldActorSnapshot actor) =>
        actor is { ClassTag: 4, VisualIndex: FieldSavePoints.SparkleVisual } ? LightLabel(scene, actor.Index) : null;

    public static bool SaveIsObservable(FieldNavigationSnapshot field, FieldStoryState? story, FieldSavePoints.SavePoint save) =>
        !AmbiguousLights(field.SceneId) || story?.Flag(FieldSavePoints.CanSaveGlobal, FieldSavePoints.CanSaveBit) == true &&
        field.LeadPlayer is { IsUsable: true } lead && lead.TileX == save.TileX && lead.TileY == save.TileY;

    // Atel0249's unopened pits are ordinary floor. Atel0290 chooses the roles
    // of four identical lights from D0. Atel0255 also draws identical lights.
    // Neither those hidden roles nor their terminal footprints are visible.
    public static bool ConcealedRegion(int scene, GameNavigationCatalog.Region region) =>
        scene == 168 && region.Actor is >= 10 and <= 15 ||
        scene == 471 && region.Actor is >= 9 and <= 12 || scene == 174 && region.Actor is 8 or 9;

    public static bool ConcealedActor(int scene, int actor) =>
        scene == 168 && actor is >= 10 and <= 15 ||
        scene == 166 && actor == 10 || LightLabel(scene, actor) is not null;

    public static bool ConcealedEncounter(int scene, FieldActorSnapshot actor) =>
        ConcealedActor(scene, actor.Index) || scene is 125 or 169 or 171 or 173 or 469 &&
        (actor.ClassTag & 0x7F) is not (5 or 6);

    public static bool IsTouchFigure(int scene, FieldActorSnapshot actor, FieldStoryState? story)
    {
        if (scene != 125 || actor.Index is not (9 or 10 or 13 or 14 or 15)) return false;
        var actions = GameNavigationCatalog.ActorInfo(scene, actor)?.Actions.Where(a => a.Available(story)).ToArray();
        return actions is { Length: > 0 } && actions.All(a => a.Touch) && actions.Any(a => a.Kind == "Talk");
    }

    public static string? RegionLightLabel(int scene, GameNavigationCatalog.Region region) => (scene, region.Actor) switch
    {
        (166, 10) => "Glowing point",
        (170, 8) or (447, 8) when region.Kind == "Warp" => "Return warp, " + GameNavigationCatalog.DestinationLabel(region.Destination),
        _ => null,
    };

    public static bool IsRegionLightSprite(int scene, FieldActorSnapshot actor) =>
        actor is { ClassTag: 4, VisualIndex: FieldSavePoints.SparkleVisual } &&
        ((scene, actor.Index) is (166, 9) or (170, 8) or (447, 8));

    public static bool RegionLightVisible(FieldNavigationSnapshot field, GameNavigationCatalog.Region region)
    {
        if (RegionLightLabel(field.SceneId, region) is null) return true;
        var sprite = field.SceneId == 166 ? 9 : 8;
        return field.Actors.Any(a => a.Index == sprite && a.IsUsable && a.IsDrawn &&
                   IsRegionLightSprite(field.SceneId, a) && a.TileX >= region.Left && a.TileX <= region.Right &&
                   a.TileY >= region.Top && a.TileY <= region.Bottom) &&
               field.Actors.Any(a => a.Index == region.Actor && a.IsUsable && a.ScriptProcessingEnabled);
    }

    public static GameNavigationCatalog.TileCopy? EntranceOpening(FieldNavigationSnapshot field, FieldStoryState? story)
    {
        if (!field.SceneIdCoherent) return null;
        var marker = field.Actors.FirstOrDefault(a => a.Index == 14);
        return marker is not null && IsEntranceDoor(field.SceneId, marker, story) ? OpenEntrance : null;
    }

    internal static bool IsEntranceDoor(int scene, FieldActorSnapshot marker, FieldStoryState? story)
    {
        if (scene != 165 || story?.Local(7) != 0 || marker.Index != 14 ||
            marker is not { ClassTag: 7, IsUsable: true, IsPartyMember: false,
                ScriptCallsEnabled: true, ScriptProcessingEnabled: true, FineX: 1920, FineY: 7679 } ||
            marker.ScriptPriority is not > 2) return false;
        return GameNavigationCatalog.ActorInfo(165, marker)?.Actions.Any(a =>
            a.Touch && a.Kind == "Terrain" && a.Copy == OpenEntrance && a.Available(story)) == true;
    }
}
