using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Atel0474's unmarked wall and its layer-2 cover. The local is set by
/// native interaction; the copied shape bit confirms the cover's update.</summary>
public static class OzzieFortScenery
{
    public static bool ConcealedMarker(int scene,FieldActorSnapshot actor)=>scene==179&&actor.Index==1&&actor.ClassTag==7;
    public static bool IsBladePart(int scene,FieldActorSnapshot actor)=>scene==179&&actor.ClassTag==4&&
        (actor.Index,actor.VisualIndex) is (10,90) or (11,91);
    public static bool ConcealedActor(int scene,FieldActorSnapshot actor,FieldMapSnapshot map,FieldStoryState? state)=>
        scene==179&&actor.Index==13&&actor.ClassTag==4&&actor.VisualIndex==112&&!Revealed(map,state,actor.FineX,actor.FineY);
    public static bool ConcealedTreasure(int scene,FieldTreasure chest,FieldMapSnapshot map,FieldStoryState? state)=>
        scene==179&&chest.Index is >=85 and <=87&&!Revealed(map,state,chest.FineX,chest.FineY);

    private static bool Revealed(FieldMapSnapshot map,FieldStoryState? state,int x,int y)
    {
        x/=256;y/=256;
        return state?.Local(11)==1&&x>=0&&y>=0&&x<map.Width&&y<map.Height&&
            (map.CollisionShapes[y*map.Width+x]&2)==0;
    }

    public static FieldActorSnapshot? VisibleBlade(FieldNavigationSnapshot field,FieldViewport viewport)
    {
        if(!field.SceneIdCoherent||field.SceneId!=179)return null;
        var blade=field.Actors.FirstOrDefault(a=>a.Index==10&&a.ClassTag==4&&a.VisualIndex==90&&a.IsUsable&&a.IsDrawn);
        if(blade is null||!field.Actors.Any(a=>a.Index==11&&a.ClassTag==4&&a.VisualIndex==91&&a.IsUsable&&a.IsDrawn&&
            a.FineX==blade.FineX&&a.FineY==blade.FineY))return null;
        // c097's native cel parts range16..60 pixels above the shared anchor.
        // Require the full possible height in view; no animation-state inference.
        return viewport.Contains(blade.FineX,blade.FineY-60*16)&&viewport.Contains(blade.FineX,blade.FineY-16*16)?blade:null;
    }
}
