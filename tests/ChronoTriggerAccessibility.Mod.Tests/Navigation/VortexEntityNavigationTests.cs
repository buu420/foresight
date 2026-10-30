using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class VortexEntityNavigationTests
{
    internal const string PresentPassage = "script-region:26:597:0:3:4:3";
    internal const string StoryId = "story:full:lavos";
    internal const string CastlePassage = "script-terrain:8:0:41:57:43:57";

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TheAntiquityBossApproachHasASpatialStoryRoute(bool automatic)
    {
        var s = Sample("645"); var frame = Build(s.Field, s.Map, s.Story);
        Assert.False(frame.Targets.Single(t => t.Id == StoryId).IsStoryNote);
        Assert.True(Start(frame, StoryId, automatic).Result.Guiding);
    }

    [Theory]
    [InlineData(0,false)] [InlineData(0,true)] [InlineData(2,false)] [InlineData(2,true)]
    public void TheInitialCastleRouteReachesTheNativeOpeningRowBeforeTheBoss(int priorClear,bool automatic)
    {
        var s=Sample("645");var story=s.Story with{Extended=new Dictionary<int,int>(s.Story.Extended){[34]=priorClear}};
        var frame=Build(s.Field,s.Map,story);
        var gate=frame.Targets.Single(t=>t.Id==CastlePassage);
        Assert.Equal(NavigationCategory.Exits,gate.Category);
        Assert.All(gate.ApproachPoints,p=>{Assert.InRange(p.X/256,41,43);Assert.Equal(57,p.Y/256);});
        var objective=frame.Targets.Single(t=>t.Id==StoryId);
        Assert.False(objective.IsStoryNote);
        Assert.DoesNotContain("Shade",objective.Label,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(gate.ApproachPoints,objective.ApproachPoints);
        Assert.True(Start(frame,CastlePassage,automatic).Result.Guiding);
        Assert.True(Start(frame,StoryId,automatic).Result.Guiding);
    }

    [Theory]
    [InlineData("missing")] [InlineData("disabled")] [InlineData("class")] [InlineData("pc")]
    public void CastleSpatialChecksRequireTheirActualNativeWatcher(string variant)
    {
        var s=Sample("645");var actors=s.Field.Actors.Where(a=>variant!="missing" || a.Index!=0).Select(a=>a.Index!=0?a:
            variant=="disabled"?a with{ScriptProcessingEnabled=false}:variant=="pc"?a with{ScriptAddress=0x0168}:
            a with{ClassTag=4}).ToArray();
        var frame=Build(s.Field with{Actors=actors},s.Map,s.Story);
        Assert.DoesNotContain(frame.Targets,t=>t.Id==CastlePassage);
    }

    [Theory]
    [InlineData(false,16)] [InlineData(false,32)] [InlineData(true,16)] [InlineData(true,32)]
    public void CastleGuidanceReachesBothNativeSpatialPredicatesInOrder(bool automatic,int speed)
    {
        var s=Sample("645");var frame=Build(s.Field,s.Map,s.Story);
        frame=Walk(frame,s.Map,StoryId,automatic,speed,p=>p.Y/256==57 && p.X/256 is >=41 and <=43);
        // Native E5 03 20 05 22 29 36 3b at019B: explicitly derived post-copy
        // state, not a live capture. Keep the live map immutable.
        var open=OpenCastle(s.Map);
        var story=s.Story with{Locals=new Dictionary<int,int>(s.Story.Locals){[20]=1}};
        var actor=s.Field.LeadPlayer! with{FineX=frame.Player.X,TileX=frame.Player.X/256,FractionX=frame.Player.X%256,
            FineY=frame.Player.Y,TileY=frame.Player.Y/256,FractionY=frame.Player.Y%256};
        var field=s.Field with{LeadPlayer=actor,Actors=s.Field.Actors.Select(a=>a.Index==actor.Index?actor:a).ToArray()};
        var continued=Build(field,open,story);
        Assert.NotEmpty(continued.Targets.Single(t=>t.Id==CastlePassage).ApproachPoints);
        Assert.False(continued.Targets.Single(t=>t.Id==StoryId).IsStoryNote);
        Walk(continued,open,StoryId,automatic,speed,p=>p.Y/256==51 && p.X/256 is >=35 and <=46);
    }

    internal static FieldMapSnapshot OpenCastle(FieldMapSnapshot map)
    {
        byte[] Copy(byte[] source){var next=(byte[])source.Clone();for(var y=32;y<=34;y++)
            Array.Copy(source,y*map.Width+3,next,(54+y-32)*map.Width+41,3);return next;}
        return map with{CollisionShapes=Copy(map.CollisionShapes),TerrainFlags=Copy(map.TerrainFlags),CollisionLayers=Copy(map.CollisionLayers)};
    }

    [Theory]
    [InlineData(false,16,false)] [InlineData(false,32,false)] [InlineData(true,16,false)] [InlineData(true,32,false)]
    [InlineData(false,16,true)] [InlineData(false,32,true)] [InlineData(true,16,true)] [InlineData(true,32,true)]
    public void TheSameSelectionContinuesAfterTheQueuedCastleCopy(bool automatic,int speed,bool storyTarget)
        => ContinueCastle(automatic,speed,storyTarget,2);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ACopyAlreadyFinishedInTheNextCaptureStillAnnouncesOnce(bool automatic)
        => ContinueCastle(automatic,16,true,0);

    private static void ContinueCastle(bool automatic,int speed,bool storyTarget,int delayFrames)
    {
        var s=Sample("645");var field=s.Field;var map=s.Map;var story=s.Story;
        var source=new FieldNavigationSource(new Empty(),_=>{});
        NavigationFrame Current()=>source.Build(field,map,new(0,0,map.Width*256,map.Height*256),[],story);
        var frame=Current();
        var (controller,result)=Start(frame,storyTarget?StoryId:CastlePassage,automatic);var opened=false;var reached=false;int? queuedAt=null;
        var speech=new List<string>();
        for(var tick=1;tick<6000 && result.Guiding && !reached;tick++){
            var direction=automatic?result.Direction:result.ManualLeg?.Direction??NavigationDirection.None;
            var player=NativeWalkingReplay.Move(map,frame.Player,direction,speed);
            if(queuedAt is null && player.Y/256==57 && player.X/256 is >=41 and <=43){
                queuedAt=tick;story=story with{Locals=new Dictionary<int,int>(story.Locals){[20]=1}};
                field=field with{Actors=field.Actors.Select(a=>a.Index==0?a with{ScriptAddress=0x198,CurrentScriptOpcode=0xE8}:a).ToArray()};
            }
            if(queuedAt is not null){
                // Native75 precedes yielded E8/E5. Cover two pending frames,
                // and a mod capture that sees both changes already complete.
                if(!opened && tick>=queuedAt+delayFrames){map=OpenCastle(map);opened=true;
                    field=field with{Actors=field.Actors.Select(a=>a.Index==0?a with{ScriptAddress=0x17E,CurrentScriptOpcode=0x22}:a).ToArray()};}
                var actor=field.LeadPlayer! with{FineX=player.X,TileX=player.X/256,FractionX=player.X%256,
                    FineY=player.Y,TileY=player.Y/256,FractionY=player.Y%256};
                field=field with{LeadPlayer=actor,Actors=field.Actors.Select(a=>a.Index==actor.Index?actor:a).ToArray()};
                frame=Current();
            }
            frame=frame with{Player=player,PlayerFacing=direction};
            reached=opened && player.Y/256==51 && player.X/256 is >=35 and <=46;
            result=controller.Update(frame,tick*16,!automatic && direction!=NavigationDirection.None);
            speech.AddRange(result.Speech);
        }
        Assert.True(opened);Assert.True(reached,$"Stopped at{frame.Player}");
        Assert.Single(speech,line=>line.Contains("castle passage is open",StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(false,false)] [InlineData(false,true)] [InlineData(true,false)] [InlineData(true,true)]
    public void AQueuedCastleOpeningWaitsAndTimesOutWithoutInventingFloor(bool automatic,bool storyTarget)
    {
        var s=Sample("645");var frame=Build(s.Field,s.Map,s.Story);
        var (controller,result)=Start(frame,storyTarget?StoryId:CastlePassage,automatic);Assert.True(result.Guiding);
        var story=s.Story with{Locals=new Dictionary<int,int>(s.Story.Locals){[20]=1}};
        var field=s.Field with{Actors=s.Field.Actors.Select(a=>a.Index==0?a with{ScriptAddress=0x198,CurrentScriptOpcode=0xE8}:a).ToArray()};
        var pending=Build(field,s.Map,story) with{Player=new(10880,14704,1)};
        result=controller.Update(pending,16);Assert.True(result.Guiding);Assert.Equal(NavigationDirection.None,result.Direction);
        Assert.Null(result.ManualLeg);
        result=controller.Update(pending,48);Assert.True(result.Guiding);
        result=controller.Update(pending,3020);Assert.False(result.Guiding);Assert.False(result.AutoWalking);
        Assert.Contains(result.Speech,line=>line.Contains("passage did not open",StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0x04,s.Map.CollisionShapes[56*s.Map.Width+42]);
    }

    [Fact]
    public void TheOpenedCastleRouteStaysInExitsWithoutDuplicateBonusObjectsOrHiddenEnemies()
    {
        var s=Sample("645");var map=OpenCastle(s.Map);
        var frame=Build(s.Field,map,s.Story with{Locals=new Dictionary<int,int>(s.Story.Locals){[20]=1}}) with{Player=new(10880,14704,1)};
        var gate=Assert.Single(frame.Targets,t=>t.Category==NavigationCategory.Exits);
        Assert.Equal(CastlePassage,gate.Id);Assert.False(gate.PassageOpening);
        Assert.All(gate.ApproachPoints,p=>{Assert.Equal(51,p.Y/256);Assert.InRange(p.X/256,35,46);});
        Assert.Contains("open",gate.Label,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(frame.Targets,t=>t.Category is NavigationCategory.Objects or NavigationCategory.Enemies);
        Assert.Single(frame.Targets,t=>t.Category==NavigationCategory.StoryEvents);
    }

    private static NavigationFrame Walk(NavigationFrame frame,FieldMapSnapshot map,string id,bool automatic,int speed,Func<NavigationPoint,bool> reached)
    {
        var (controller,result)=Start(frame,id,automatic);Assert.True(result.Guiding);
        for(var tick=1;tick<6000 && !reached(frame.Player) && result.Guiding;tick++){
            var direction=automatic?result.Direction:result.ManualLeg?.Direction??NavigationDirection.None;
            frame=frame with{Player=NativeWalkingReplay.Move(map,frame.Player,direction,speed),PlayerFacing=direction};
            result=controller.Update(frame,tick*16,!automatic && direction!=NavigationDirection.None);
            Assert.DoesNotContain(result.Speech,line=>line.Contains("blocked",StringComparison.OrdinalIgnoreCase));
        }
        Assert.True(reached(frame.Player),$"Stopped at{frame.Player}");return frame;
    }

    public static IEnumerable<object[]> PresentCases => new[]{"2808","3021","3356","3372","3462","3578"}
        .SelectMany(key => new[]{new object[]{key,false},new object[]{key,true}});

    [Theory]
    [MemberData(nameof(PresentCases))]
    public void EveryCapturedPresentPoseCanReachItsActualPassage(string key, bool automatic)
    {
        var s=Sample(key);var frame=Build(s.Field,s.Map,s.Story);
        var target=frame.Targets.Single(t=>t.Id==PresentPassage);
        Assert.StartsWith("Vortex passage",target.Label);
        Assert.Contains(target.ApproachPoints,p=>p.X is >=1008 and <=1039 && p.Y/256==3);
        Assert.All(target.ApproachPoints,p=>{Assert.InRange(p.X/256,0,4);Assert.Equal(3,p.Y/256);});
        Assert.True(Start(frame,PresentPassage,automatic).Result.Guiding);
        Assert.False(frame.Targets.Single(t=>t.Id==StoryId).IsStoryNote);
        Assert.True(Start(frame,StoryId,automatic).Result.Guiding);
    }

    [Theory]
    [InlineData(0,"missing")] [InlineData(0,"disabled")]
    [InlineData(26,"missing")] [InlineData(26,"disabled")] [InlineData(26,"class")]
    public void AStoppedOrWrongNativeWatcherCannotOfferThePresentPassage(int index,string variant)
    {
        var s=Sample("3578");
        var actors=s.Field.Actors.Where(a=>variant!="missing" || a.Index!=index).Select(a=>a.Index!=index?a:
            variant=="disabled"?a with{ScriptProcessingEnabled=false}:a with{ClassTag=4}).ToArray();
        var frame=Build(s.Field with{Actors=actors},s.Map,s.Story);
        Assert.DoesNotContain(frame.Targets,t=>t.Id==PresentPassage);
    }

    [Fact]
    public void MissingRandomizedStateDoesNotInventTheNextRoom()
    {
        var s=Sample("3578");var extra=new Dictionary<int,int>(s.Story.Extended);extra.Remove(0x20);
        Assert.DoesNotContain(Build(s.Field,s.Map,s.Story with{Extended=extra}).Targets,t=>t.Id==PresentPassage);
    }

    [Theory]
    [InlineData("2808")] [InlineData("3578")] [InlineData("645")]
    public void EmptyNativeTreasureRecordsDoNotCreateChestTargets(string key)
    {
        // Native TakaraOffsetTbl ranges for638/645 are empty, independently
        // verified in the research receipt. These captures also have no chests.
        var s=Sample(key);
        Assert.DoesNotContain(Build(s.Field,s.Map,s.Story).Targets,t=>t.Id.StartsWith("chest:",StringComparison.Ordinal));
    }

    public static IEnumerable<object[]> PresentWalkingCases => new[]{"2808","3021","3356","3372","3462","3578"}
        .SelectMany(key=>new[]{false,true}.SelectMany(automatic=>new[]{16,32}.Select(speed=>new object[]{key,automatic,speed})));

    [Theory]
    [MemberData(nameof(PresentWalkingCases))]
    public void BothModesReachThePresentNativeTriggerWithoutStoppingAtTheWall(string key,bool automatic,int speed)
    {
        var s=Sample(key);var frame=Build(s.Field,s.Map,s.Story);
        var (controller,result)=Start(frame,PresentPassage,automatic);
        Assert.True(result.Guiding);
        var speech=new List<string>(result.Speech);
        for(var tick=1;tick<6000 && frame.Player.Y/256!=3 && result.Guiding;tick++)
        {
            var direction=automatic?result.Direction:result.ManualLeg?.Direction??NavigationDirection.None;
            frame=frame with{Player=NativeWalkingReplay.Move(s.Map,frame.Player,direction,speed),PlayerFacing=direction};
            result=controller.Update(frame,tick*16,!automatic && direction!=NavigationDirection.None);
            speech.AddRange(result.Speech);
        }
        Assert.Equal(3,frame.Player.Y/256);Assert.InRange(frame.Player.X/256,0,4);
        Assert.DoesNotContain(speech,line=>line.Contains("blocked",StringComparison.OrdinalIgnoreCase));
        var stopped=controller.Update(frame with{CanNavigate=false},96000);
        Assert.False(stopped.AutoWalking);Assert.Equal(NavigationDirection.None,stopped.Direction);
    }

    internal static (FieldNavigationSnapshot Field,FieldMapSnapshot Map,FieldStoryState Story) Sample(string key)
    {
        using var doc=JsonDocument.Parse(typeof(VortexEntityNavigationTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.vortex-entities-0386.json")!);
        var root=doc.RootElement.GetProperty("cases").EnumerateArray().Single(c=>c.GetProperty("key").GetString()==key);
        var actors=root.GetProperty("actors").Deserialize<FieldActorSnapshot[]>()!;
        var map=root.GetProperty("map").Deserialize<FieldMapSnapshot>()!;
        var f=root.GetProperty("field");var s=root.GetProperty("story");
        var lead=actors.Single(a=>a.Index==f.GetProperty("leader").GetInt32());
        var field=new FieldNavigationSnapshot(0x1000,0x4000,0x2000,0x20000,Math.Max(actors.Length,actors.Max(a=>a.Index)+1),
            f.GetProperty("scene").GetInt32(),f.GetProperty("sceneCoherent").GetBoolean(),f.GetProperty("control").GetInt32(),
            f.GetProperty("inputMode").GetInt32(),0,lead.Index,lead,actors)
        {
            ActorCollisionRadius=f.GetProperty("collisionRadius").GetInt32(),LastPartySlotRaw=f.GetProperty("lastPartySlotRaw").GetInt32(),
            ContactActorRaw=f.GetProperty("contactActorRaw").GetInt32(),ConfirmActorRaw=f.GetProperty("confirmActorRaw").GetInt32(),
        };
        var story=new FieldStoryState(s.GetProperty("point").GetInt32(),true){
            Globals=s.GetProperty("globals").Deserialize<Dictionary<int,int>>()!,
            Locals=s.GetProperty("locals").Deserialize<Dictionary<int,int>>()!,Extended=s.GetProperty("extended").Deserialize<Dictionary<int,int>>()!};
        return(field,map,story);
    }
    internal static NavigationFrame Build(FieldNavigationSnapshot f,FieldMapSnapshot m,FieldStoryState s)=>
        new FieldNavigationSource(new Empty(),_=>{}).Build(f,m,new(0,0,m.Width*256,m.ExitHeight*256),[],s);
    internal static (NavigationController Controller,NavigationResult Result) Start(NavigationFrame frame,string id,bool automatic)
    {
        var target=frame.Targets.Single(t=>t.Id==id);var controller=new NavigationController();
        for(var i=0;i<(int)target.Category;i++)controller.Handle(NavigationCommand.NextCategory,frame,0);
        for(var i=0;i<frame.Targets.Count && controller.TargetId!=id;i++)controller.Handle(NavigationCommand.NextTarget,frame,0);
        Assert.Equal(id,controller.TargetId);
        return(controller,controller.Handle(automatic?NavigationCommand.ToggleWalk:NavigationCommand.Guide,frame,1));
    }
    private sealed class Empty:IReadableMemory{public bool TryRead(nuint a,Span<byte>b)=>false;}
}
