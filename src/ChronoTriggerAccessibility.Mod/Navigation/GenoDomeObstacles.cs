using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Visible results of Atel0355's receiver and laser controls. A native
/// flag must agree with the actual copied floor; transitions stay unknown.</summary>
public static class GenoDomeObstacles
{
    public sealed record Obstacle(string Id,string Label,int X,int Y,int Width,int Height,bool Open,
        GameNavigationCatalog.TileCopy Opening)
    {
        public bool Visible(FieldViewport view)=>view.Intersects(X*256,Y*256,(X+Width)*256,(Y+Height)*256);
        public string Description=>Label+(Id=="laser"?(Open?" is inactive.":" is active."):(Open?" is open.":" is locked."));
        public string Change=>Id=="laser"?Description:Open?Label+" opened.":Description;
    }

    public static IReadOnlyList<Obstacle> Read(int scene,FieldMapSnapshot map,FieldStoryState? story)
    {
        if(scene!=256 || story is null || map.Width<63 || map.Height<63 ||
            new[]{map.CollisionShapes,map.TerrainFlags,map.CollisionLayers}.Any(p=>p.Length!=map.Width*map.Height))return [];
        var found=new List<Obstacle>();
        var doorReference=Enumerable.Range(0,3).All(y=>Enumerable.Range(0,3).All(x=>
            map.CollisionShapes[(60+y)*map.Width+60+x]==(x==0?0:4) &&
            map.TerrainFlags[(60+y)*map.Width+60+x]==0 && map.CollisionLayers[(60+y)*map.Width+60+x]==129));
        foreach(var (id,label,x,y,flag) in new[]{("south","Southern door",23,41,1),("west","Western door",5,12,4),
                    ("north","Northern door",21,15,2)})
        {
            if(!doorReference)break;
            var open=story.Flag(0x13E,flag);
            if(open is null)continue;
            // The three native closed doors share the open staging copy except
            // for their lower-left blocking half tile (shape25, layer0/1).
            // This exact pattern is present at all three doors in L2179.
            var matches=true;
            foreach(var (plane,kind) in new[]{(map.CollisionShapes,0),(map.TerrainFlags,1),(map.CollisionLayers,2)})
            for(var dy=0;dy<3;dy++)for(var dx=0;dx<3;dx++)
            {
                var expected=plane[(60+dy)*map.Width+60+dx];
                if(!open.Value && dx==0 && dy==2)expected=kind switch{0=>(byte)100,2=>(byte)136,_=>expected};
                if(plane[(y+dy)*map.Width+x+dx]!=expected)matches=false;
            }
            if(matches)found.Add(new(id,label,x,y,3,3,open.Value,new(60,60,62,62,x,y,0x38)));
        }
        var active=Matches(57,55,1,3,38,17);var inactive=Matches(58,55,1,3,38,17);
        if(story.Flag(0x13D,8) is { } off && active!=inactive && inactive==off)
            found.Add(new("laser","Laser barrier",38,17,1,3,off,new(58,55,58,57,38,17,0x38)));
        return found;

        bool Matches(int sx,int sy,int w,int h,int tx,int ty)=>
            new[]{map.CollisionShapes,map.TerrainFlags,map.CollisionLayers}.All(p=>
                Enumerable.Range(0,h).All(y=>Enumerable.Range(0,w).All(x=>p[(ty+y)*map.Width+tx+x]==p[(sy+y)*map.Width+sx+x])));
    }

    public static string? ControlInstruction(int scene,FieldActorSnapshot actor,FieldMapSnapshot map,
        FieldViewport viewport,bool? podOpen,IReadOnlyList<Obstacle> obstacles)
    {
        if(scene!=256)return null;
        var id=actor.Index switch{26=>"south",27=>"west",28=>"north",13=>"laser",_=>null};
        var obstacle=id is null?null:obstacles.FirstOrDefault(o=>o.Id==id);
        var lampVisible=viewport.Intersects(actor.TileX*256,(actor.TileY-1)*256,(actor.TileX+1)*256,actor.TileY*256);
        var text=string.Join(" ",new[]{GenoDomeTargets.PodInstruction(actor.Index,podOpen,obstacle?.Open),
            lampVisible?GenoDomeTargets.SwitchLamp(scene,actor,map):null,
            obstacle?.Visible(viewport)==true?obstacle.Description:null}.Where(s=>s is not null));
        return text.Length==0?null:text;
    }

    /// <summary>Called only after a real route search fails. An opened native
    /// copy must actually connect the current player to this target. This is
    /// an explanation, never walking authority or a request to open anything.</summary>
    public static string? Explain(FieldMapSnapshot map,NavigationPoint player,NavigationTarget target,
        IReadOnlyList<Obstacle> obstacles,FieldActorCollisionRules collisions,
        IReadOnlyList<(int Actor,IReadOnlyList<NavigationPoint> Goals)> touchGoals)
    {
        foreach(var obstacle in obstacles.Where(o=>!o.Open))
        {
            var preview=FieldTerrainGraph.Preview(map,obstacle.Opening);
            if(preview is null)continue;
            var graph=new FieldNavigationGraph(preview,collisions,touchGoals);
            if(NavigationPathfinder.Search(graph,player,target.ApproachPoints,32768).Route is not null)
                return obstacle.Label+(obstacle.Id=="laser"?" is active and blocks this route.":" is locked and blocks this route.");
        }
        return null;
    }
}
