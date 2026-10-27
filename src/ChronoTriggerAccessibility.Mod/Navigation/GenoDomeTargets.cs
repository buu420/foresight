using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Installed PC Geno Dome controls. These labels identify the visible
/// mechanism, not its solution. Coordinates and usable actions remain native.</summary>
public static class GenoDomeTargets
{
    public static (string Label, NavigationCategory Category)? Actor(int scene, FieldActorSnapshot actor) =>
        (scene,actor.Index,actor.ClassTag,actor.VisualIndex) switch
        {
            (256,8,4,170) => ("Mobile Sentinel",NavigationCategory.People),
            (256,29,4,170) => ("Guard Sentinel",NavigationCategory.People),
            (256,22,4,100) => ("Southern figurine",NavigationCategory.Objects),
            (256,23,4,100) => ("Northern figurine",NavigationCategory.Objects),
            _ => null,
        };

    public static bool IsElectricalEffect(int scene, FieldActorSnapshot actor) =>
        scene==256 && actor.Index is 9 or 24 && actor.ClassTag==4 && actor.VisualIndex==86;

    public static bool IsTerminalDisplay(int scene,FieldActorSnapshot actor)=>
        (scene,actor.Index) is (248,9) or (256,18) && actor.ClassTag==4 && actor.VisualIndex==127;

    public static bool MobileDroid(int scene,FieldActorSnapshot actor,FieldStoryState? story)=>
        scene==256 && actor.Index==8 && actor.ClassTag==4 && actor.VisualIndex==170 && actor.IsUsable &&
        actor.IsDrawn && actor.ScriptCallsEnabled && actor.ScriptProcessingEnabled && story?.Flag(0x13C,0x80)==false;

    public static GameNavigationCatalog.Region? DroidMeeting(int scene,FieldActorSnapshot actor,FieldStoryState? story)=>
        scene==256 && actor.Index==29 && actor.ClassTag==4 && actor.VisualIndex==170 && actor.IsUsable && actor.IsDrawn &&
        story?.Flag(0x13C,0x80)==false && story.Local(12)==1
            // Atel0355:0899 starts following unconditionally; 08AD/08B2
            // finish only after its movement when leader X<10 and Y==36.
            ? new(8,"Switch",-1,0x80,0,36,9,36,[],"Global",0x13C) : null;

    public static bool IsPod(int scene,FieldActorSnapshot actor)=>
        scene==256 && actor.Index is >=25 and <=28 && actor.ClassTag==7 && actor.IsUsable && actor.ScriptCallsEnabled;

    public static bool? PodOpen(int scene,FieldActorSnapshot actor,FieldMapSnapshot map,FieldStoryState? story)
    {
        if(!IsPod(scene,actor) || map.Width<63 || map.Height<59 ||
            new[]{map.CollisionShapes,map.TerrainFlags,map.CollisionLayers}.Any(p=>p.Length!=map.Width*map.Height))return null;
        var (dx,dy,flag)=actor.Index switch{25=>(24,23,0x80),26=>(26,40,1),28=>(24,14,2),_=>(8,11,0)};
        bool? expected=flag!=0?story?.Flag(0x13D,flag):story?.Local(14) is { } value && value is 0 or 1?value==1:null;
        if(expected is null)return null;
        // Atel0355's actual open/closed E5 patterns, not the flag alone.
        // Open: source59..60,55..58; closed:61..62,55..58.
        bool Matches(int sx)=>new[]{map.CollisionShapes,map.TerrainFlags,map.CollisionLayers}.All(p=>
            Enumerable.Range(0,4).All(y=>Enumerable.Range(0,2).All(x=>p[(dy+y)*map.Width+dx+x]==p[(55+y)*map.Width+sx+x])));
        var opened=Matches(59);var closed=Matches(61);
        return opened!=closed && opened==expected?expected:null;
    }

    public static string? PodInstruction(int actor,bool? opened,bool? receiverComplete=null)=>opened switch
    {
        false=>"The pod is closed.",
        true when actor==25=>"The charging pod is open. Walk inside to charge the party leader.",
        true when receiverComplete==false=>"The receiver pod is open. Walk inside with an electrical charge.",
        true=>"The receiver pod is open.",
        _=>null,
    };

    public static bool ChargingContactAccepted(FieldNavigationSnapshot field,FieldActorSnapshot actor,FieldStoryState? story)=>
        IsPod(field.SceneId,actor) && actor.Index==25 && story?.Flag(0x13C,0x40)==true && field.LeadPlayer is { } lead &&
        Math.Abs(lead.FineX-actor.FineX)<=64 && Math.Abs(lead.FineY-actor.FineY-256)<=64;

    public static string? SwitchLamp(int scene,FieldActorSnapshot actor,FieldMapSnapshot map)
    {
        if(scene!=256 || actor.Index is <10 or >17 || Marker(scene,actor) is null ||
            map.TileLayer1 is not { } first || map.TileLayer2 is not { } second ||
            first.Width!=map.Width || second.Width!=map.Width || first.Height!=map.Height || second.Height!=map.Height ||
            first.Tiles.Length!=map.Width*map.Height || second.Tiles.Length!=map.Width*map.Height ||
            map.CollisionShapes.Length!=map.Width*map.Height || map.Width<59 || map.Height<59 ||
            actor.TileX<0 || actor.TileX>=map.Width || actor.TileY<1 || actor.TileY>=map.Height)return null;
        var at=(actor.TileY-1)*map.Width+actor.TileX;
        var green=58*map.Width+57;var red=green+1;
        // Palette72/ChipTable0009, rendered from the installed assets: the
        // native E5 staging tiles are green101 and red102, with no overlay or
        // upper-bank graphic. Never infer colour from a switch flag.
        if(first.Tiles[green]!=101 || first.Tiles[red]!=102 || second.Tiles[green]!=0 || second.Tiles[red]!=0 ||
            second.Tiles[at]!=0 || (map.CollisionShapes[at]&3)!=0)return null;
        return first.Tiles[at] switch{101=>"Green lamp.",102=>"Red lamp.",_=>null};
    }

    public static bool ConveyorDoorOpen(int scene,FieldActorSnapshot actor,FieldMapSnapshot map,FieldStoryState? story)=>
        scene==250 && actor.Index==1 && actor.ClassTag==7 && story?.Local(6)==1 && map.Width>=59 && map.Height>=63 &&
        new[]{map.CollisionShapes,map.TerrainFlags,map.CollisionLayers}.All(p=>p.Length==map.Width*map.Height &&
            Enumerable.Range(0,4).All(y=>Enumerable.Range(0,4).All(x=>p[(51+y)*map.Width+22+x]==p[(59+y)*map.Width+55+x])));

    public static string? Marker(int scene,FieldActorSnapshot actor)
    {
        if(actor.ClassTag!=7 || !actor.IsUsable || actor.IsPartyMember || !actor.ScriptCallsEnabled) return null;
        return (scene,actor.Index) switch
        {
            (248,8) => "Entrance terminal", // Atel0349
            (250,1) => "Conveyor exit door", // Atel0351
            (256,10) => "Southern pod switch", // Atel0355:0911
            (256,11) => "Northern pod switch",
            (256,12) => "Conveyor direction switch",
            (256,13) => "Laser barrier switch",
            (256,14) => "Left wall switch",
            (256,15) => "Middle wall switch",
            (256,16) => "Right wall switch",
            (256,17) => "Charging pod switch",
            (256,19) => "Information terminal", // Calls Robo's terminal routine; actor18 is normally hidden.
            (256,20) => "Southern elevator door",
            (256,21) => "Northern elevator door",
            (256,25) => "Charging pod",
            (256,26) => "Southern receiver pod",
            (256,27) => "Western receiver pod",
            (256,28) => "Northern receiver pod",
            (267,8) => "Rear elevator door", // Atel0358
            (268,8) => "Elevator door", // Atel0359
            (268,10) => "Left figurine pedestal",
            (268,11) => "Right figurine pedestal",
            _ => null,
        };
    }
}
