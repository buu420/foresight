using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Short feedback from Atel0355's displayed electrical effect and
/// moving droid. Missing captures revoke current facts without inventing a
/// visual change. This observer never changes native timing or input.</summary>
public sealed class GenoDomeFeedback(Action<string> speak,Func<int,string?>? characterName=null)
{
    private readonly object gate=new();
    private (uint Engine,uint Actors,uint State,uint Script,int Leader)? owner;
    private bool enabled;
    private int epoch;
    private bool? charge;
    private string chargeName="The party leader";
    private DroidState? droid;
    private string? status;
    private readonly Dictionary<int,string> lamps=[];
    private readonly Dictionary<int,bool> pods=[];
    private readonly Dictionary<string,bool> obstacles=[];
    private enum DroidState { Waiting,Following,Stopped }

    public string? Status { get { lock(gate) return status; } }
    public void Enable() { lock(gate) { Reset();enabled=true; } }
    public void Disable() { lock(gate) { enabled=false;Reset(); } }
    public void Reset() { lock(gate) { owner=null;charge=null;droid=null;status=null;chargeName="The party leader";lamps.Clear();pods.Clear();obstacles.Clear();epoch++; } }
    public void Suspend() { lock(gate) status=null; }

    public void Observe(FieldNavigationSnapshot? field,FieldStoryState? story,FieldViewport viewport)=>Observe(field,null,story,viewport);

    public void Observe(FieldNavigationSnapshot? field,FieldMapSnapshot? map,FieldStoryState? story,FieldViewport viewport)
    {
        lock(gate)
        {
            if(!enabled) return;
            status=null;
            if(field is null) return;
            if(!field.SceneIdCoherent || field.SceneId!=256) { Reset();return; }
            if(field.ControlFlag!=1 || field.InputMode!=0 || story is null ||
                field.LeadPlayer is not {IsUsable:true,IsDrawn:true,IsPartyMember:true}) return;
            var context=(field.Engine,field.ActorBase,field.FieldState,field.ScriptData,field.LeadPlayerActorIndex);
            if(owner!=context) { Reset();owner=context; }
            var observedEpoch=epoch;
            var messages=new List<string>();
            bool Visible(FieldActorSnapshot a)=>a.IsUsable && a.IsDrawn && viewport.Contains(a.FineX,a.FineY);
            var effect=field.Actors.FirstOrDefault(a=>a.Index==9 && a.ClassTag==4 && a.VisualIndex==86 && a.IsUsable);
            // 0C21 sets G13C/40, then calls actor9. Its 090A hides the
            // electrical sprite before 090D clears the flag. Require both.
            bool? nextCharge=story.Flag(0x13C,0x40) switch
            {
                true when effect is not null && Visible(effect) => true,
                false when effect is {DrawMode:0} && viewport.Contains(field.LeadPlayer.FineX,field.LeadPlayer.FineY) => false,
                _ => null,
            };
            if(nextCharge==true && charge!=true)
            {
                var name=field.LeadPlayer.VisualIndex is >=0 and <=6?characterName?.Invoke(field.LeadPlayer.VisualIndex):null;
                if(!enabled || epoch!=observedEpoch) return;
                chargeName=string.IsNullOrWhiteSpace(name)?"The party leader":name;
                messages.Add($"{chargeName} is electrically charged.");
            }
            else if(nextCharge==false && charge==true) messages.Add($"{chargeName}'s electrical charge has faded.");
            if(nextCharge is not null) charge=nextCharge;

            var mobile=field.Actors.FirstOrDefault(a=>a.Index==8 && a.ClassTag==4 && a.VisualIndex==170);
            var guard=field.Actors.FirstOrDefault(a=>a.Index==29 && a.ClassTag==4 && a.VisualIndex==170);
            DroidState? nextDroid=null;
            if(mobile is not null && Visible(mobile))
            {
                if(story.Flag(0x13C,0x80)==true && guard is not null && Visible(guard) &&
                    mobile.TileX==7 && mobile.TileY==36 && guard.TileY==34 && Math.Abs(guard.TileX-mobile.TileX)<=3)
                    nextDroid=DroidState.Stopped;
                else if(story.Flag(0x13C,0x80)==false && mobile.ScriptProcessingEnabled && mobile.ScriptCallsEnabled)
                {
                    if(story.Local(12)==1) nextDroid=DroidState.Following;
                    // Its native waiting position is dotxy(1580,0880), not
                    // absence of the follow flag anywhere on the map.
                    else if(story.Local(12)==0 && mobile.FineX==0x1580 && mobile.FineY==0x0880)
                        nextDroid=DroidState.Waiting;
                }
            }
            // After a battle the already-tracked Sentinel visibly flees, then
            // returns to this same known post. Do not leave its following
            // status silent merely because the camera is now elsewhere.
            if(nextDroid is null && droid==DroidState.Following && mobile is {IsUsable:true,IsDrawn:true} &&
                story.Flag(0x13C,0x80)==false && story.Local(12)==0 && mobile.FineX==0x1580 && mobile.FineY==0x0880)
                nextDroid=DroidState.Waiting;
            if(nextDroid is { } next && droid!=next) messages.Add(next==DroidState.Waiting && droid==DroidState.Following
                ? "The mobile Sentinel has returned to its post.":DroidText(next));
            if(nextDroid is not null) droid=nextDroid;
            if(map is not null)
            foreach(var actor in field.Actors)
            {
                if(!actor.IsUsable || GenoDomeTargets.Marker(field.SceneId,actor) is not { } label)continue;
                if(viewport.Contains(actor.FineX,actor.FineY-256) && GenoDomeTargets.SwitchLamp(field.SceneId,actor,map) is { } lamp)
                {
                    if(lamps.TryGetValue(actor.Index,out var prior) && prior!=lamp)messages.Add(label+". "+lamp);
                    lamps[actor.Index]=lamp;
                }
                if(viewport.Contains(actor.FineX,actor.FineY) && GenoDomeTargets.PodOpen(field.SceneId,actor,map,story) is { } open)
                {
                    if(pods.TryGetValue(actor.Index,out var prior) && prior!=open)messages.Add(label+(open?" opened.":" closed."));
                    pods[actor.Index]=open;
                }
            }
            if(map is not null)
            foreach(var obstacle in GenoDomeObstacles.Read(field.SceneId,map,story).Where(o=>o.Visible(viewport)))
            {
                if(obstacles.TryGetValue(obstacle.Id,out var prior) && prior!=obstacle.Open)messages.Add(obstacle.Change);
                obstacles[obstacle.Id]=obstacle.Open;
            }
            status=string.Join(" ",new[]{nextCharge==true?$"{chargeName} is electrically charged.":null,
                nextDroid is { } current?DroidText(current):null}.Where(s=>s is not null));
            if(status.Length==0) status=null;
            if(messages.Count!=0 && enabled && epoch==observedEpoch) speak(string.Join(" ",messages));
        }
    }

    private static string DroidText(DroidState state)=>state switch
    {
        DroidState.Waiting=>"The mobile Sentinel is waiting.",
        DroidState.Following=>"The mobile Sentinel is following you.",
        _=>"The mobile Sentinel has stopped beside the guard Sentinel.",
    };
}
