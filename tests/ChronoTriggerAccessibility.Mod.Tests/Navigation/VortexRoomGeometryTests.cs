using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class VortexRoomGeometryTests
{
    [Fact]
    public void TheCapturedDestinationZeroRecordIsNotOfferedAsAnExit()
    {
        Assert.DoesNotContain(Hall(false).Targets,t=>t.Id=="exit:0");
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TheOpenedHallUsesTheNarrowStandingBandOfTheRealWarp(bool automatic)
    {
        var frame=Hall(true);
        var target=frame.Targets.Single(t=>t.Id=="script-region:0:599:6:5:255:5");
        var result=Start(frame,target.Id,automatic);
        Assert.True(result.Guiding,string.Join(" ",result.Speech));
        Assert.Equal(automatic,result.AutoWalking);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TheClosedHallHasAReachableNativeOpeningContact(bool automatic)
    {
        var frame=Hall(false);
        var result=Start(frame,"landmark:21",automatic);
        Assert.True(result.Guiding,string.Join(" ",result.Speech));
        Assert.Equal(automatic,result.AutoWalking);
    }

    [Theory]
    [InlineData("disabled")] [InlineData("wrong-class")] [InlineData("missing-guard")]
    [InlineData("handled")] [InlineData("missing-marker")]
    public void UnavailableHallContactsCannotInventTheMissingFloor(string variant)
    {
        var frame=Hall(false,variant);
        var target=frame.Targets.SingleOrDefault(t=>t.Id=="script-region:0:599:6:5:255:5");
        if(target is not null)
            Assert.Null(NavigationPathfinder.Search(frame.Graph,frame.Player,target.ApproachPoints).Route);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SelectingTheWallItselfContinuesNorthUntilTheNativeContactRuns(bool automatic)
    {
        var frame=Hall(false);var target=frame.Targets.Single(t=>t.Id=="landmark:21");
        var route=NavigationPathfinder.Search(frame.Graph,frame.Player,target.ApproachPoints).Route!;
        var controller=new NavigationController();frame=frame with{Targets=[target]};
        for(var i=0;i<(int)target.Category;i++)controller.Handle(NavigationCommand.NextCategory,frame,0);
        controller.Handle(automatic?NavigationCommand.ToggleWalk:NavigationCommand.Guide,frame,1);
        var result=controller.Update(frame with{Player=route[^1]},100);
        Assert.True(result.Guiding);
        if(automatic)Assert.Equal(NavigationDirection.North,result.Direction);
        Assert.Contains(result.Speech,s=>s.Contains("Continue up",StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TheClosedHallAlsoStagesItsForwardExitAndStopsAtNativeControlLoss(bool automatic)
    {
        var frame=Hall(false);
        const string id="script-region:0:599:6:5:255:5";
        var target=frame.Targets.Single(t=>t.Id==id);
        var path=NavigationPathfinder.Search(frame.Graph,frame.Player,target.ApproachPoints);
        Assert.NotNull(path.Route);
        Assert.Equal("landmark:21",path.IntermediateId);
        Assert.NotNull(path.IntermediateContact);
        var controller=new NavigationController();frame=frame with{Targets=[target]};
        for(var i=0;i<(int)target.Category;i++)controller.Handle(NavigationCommand.NextCategory,frame,0);
        Assert.True(controller.Handle(automatic?NavigationCommand.ToggleWalk:NavigationCommand.Guide,frame,1).Guiding);
        var result=controller.Update(frame with{Player=path.Route[^1]},100);
        Assert.True(result.Guiding);
        if(automatic)Assert.Equal(NavigationDirection.North,result.Direction);
        var stopped=controller.Update(frame with{CanNavigate=false},120);
        Assert.False(stopped.AutoWalking);Assert.Equal(NavigationDirection.None,stopped.Direction);
    }

    [Theory]
    [InlineData(599,"4",false)] [InlineData(599,"4",true)]
    [InlineData(643,"console-closed",false)] [InlineData(643,"console-closed",true)]
    [InlineData(643,"console-open",false)] [InlineData(643,"console-open",true)]
    public void OtherNativeNarrowDoorsAndTheirStoryRoutesWork(int scene,string state,bool automatic)
    {
        var (frame,_)=Room(scene,state);
        var id=scene==599?"script-region:0:644:5:5:255:5":"script-region:9:642:17:2:18:2";
        Assert.True(Start(frame,id,automatic).Guiding);
        var story=frame.Targets.Single(t=>t.Category==NavigationCategory.StoryEvents && !t.IsStoryNote);
        Assert.True(Start(frame,story.Id,automatic).Guiding);
        Assert.DoesNotContain(frame.Targets,t=>t.Id=="exit:0");
    }

    [Theory]
    [InlineData(0,false)] [InlineData(0,true)] [InlineData(1,false)] [InlineData(1,true)]
    [InlineData(2,false)] [InlineData(2,true)] [InlineData(3,false)] [InlineData(3,true)]
    public void TheQuizStoryTargetIsTheNextVisibleChildWithoutAnAnswer(int stage,bool automatic)
    {
        var (frame,_)=Room(599,stage.ToString());
        var child=frame.Targets.Single(t=>t.ActorSlot==8+stage && t.Category==NavigationCategory.People &&
            t.Id.StartsWith("actor:",StringComparison.Ordinal));
        var story=frame.Targets.Single(t=>t.Category==NavigationCategory.StoryEvents);
        Assert.Contains(child.ApproachPoints,p=>story.ApproachPoints.Contains(p));
        Assert.True(Start(frame,story.Id,automatic).Guiding);
        Assert.DoesNotContain("answer",(story.Label+story.Instruction).ToLowerInvariant());
        Assert.DoesNotContain("correct",(story.Label+story.Instruction).ToLowerInvariant());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TheNextCavePassageHasAReachableContactInsteadOfTwoWallCentres(bool automatic)
    {
        var (frame,_)=Room(642,"initial");
        var passage=Assert.Single(frame.Targets,t=>t.Id=="landmark:14" || t.Id=="landmark:15");
        var path=NavigationPathfinder.Search(frame.Graph,frame.Player,passage.ApproachPoints);
        Assert.NotNull(path.Route);
        Assert.True(Start(frame,passage.Id,automatic).Guiding);
        Assert.NotNull(passage.ContactAt(path.Route[^1]));
    }

    [Fact]
    public void TheConsoleHasAUsefulLabelWithoutItsPrivateSequence()
    {
        var (frame,_)=Room(643,"console-closed");
        var console=frame.Targets.Single(t=>t.Id=="landmark:8");
        Assert.Equal("Door console",console.Label);
        Assert.True(Start(frame,console.Id,false).Guiding);
    }

    [Fact]
    public void InvisibleAmbushChecksAreNotEnemyDestinations()
    {
        var frame=Hall(false);
        Assert.DoesNotContain(frame.Targets,t=>t.Id is "actor-encounter:21" or "script-encounter:0:128:6:34:255:34");
    }

    internal static (NavigationFrame Frame,FieldStoryState Story) Room(int scene,string state)
    {
        using var doc=JsonDocument.Parse(typeof(VortexRoomGeometryTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.vortex-native-rooms.json")!);
        var root=doc.RootElement.EnumerateArray().Single(e=>e.GetProperty("Scene").GetInt32()==scene &&
            e.GetProperty("State").ToString()==state);
        var map=root.GetProperty("Map").Deserialize<FieldMapSnapshot>()!;
        var actors=root.GetProperty("Actors").Deserialize<FieldActorSnapshot[]>()!;
        var f=root.GetProperty("Field");var s=root.GetProperty("Story");
        Dictionary<int,int> Cells(string name)=>s.TryGetProperty(name,out var v)?v.Deserialize<Dictionary<int,int>>()!:[];
        var leader=actors.Single(a=>a.Index==f.GetProperty("leader").GetInt32());
        var field=new FieldNavigationSnapshot(0x1000,0x4000,0x2000,0x20000,actors.Length,scene,true,
            f.GetProperty("control").GetInt32(),f.GetProperty("inputMode").GetInt32(),0,leader.Index,leader,actors)
        {
            ActorCollisionRadius=f.GetProperty("collisionRadius").GetInt32(),
            LastPartySlotRaw=f.GetProperty("lastPartySlotRaw").GetInt32(),ContactActorRaw=128,ConfirmActorRaw=128,
        };
        var story=new FieldStoryState(s.GetProperty("point").GetInt32(),true){Globals=Cells("globals"),Locals=Cells("locals"),Extended=Cells("extended")};
        var v=root.GetProperty("Viewport").Deserialize<int[]>()!;
        return(new FieldNavigationSource(new Empty(),_=>{}).Build(field,map,new(v[0],v[1],v[2],v[3]),[],story),story);
    }

    internal static NavigationResult Start(NavigationFrame frame,string id,bool automatic)
    {
        var target=frame.Targets.Single(t=>t.Id==id);
        frame=frame with{Targets=[target]};
        var controller=new NavigationController();
        for(var i=0;i<5 && controller.TargetId!=id;i++)controller.Handle(NavigationCommand.NextCategory,frame,0);
        Assert.Equal(id,controller.TargetId);
        return controller.Handle(automatic?NavigationCommand.ToggleWalk:NavigationCommand.Guide,frame,1);
    }

    internal static NavigationFrame Hall(bool opened,string? variant=null)
    {
        using var doc=JsonDocument.Parse(typeof(VortexRoomGeometryTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.vortex-hall-live-0377.json")!);
        var root=doc.RootElement;
        var map=root.GetProperty("map").Deserialize<FieldMapSnapshot>()!;
        var actors=root.GetProperty("actors").Deserialize<FieldActorSnapshot[]>()!;
        var f=root.GetProperty("field");var s=root.GetProperty("story");
        var globals=s.GetProperty("globals").Deserialize<Dictionary<int,int>>()!;
        var locals=s.GetProperty("locals").Deserialize<Dictionary<int,int>>()!;
        var extra=s.GetProperty("extended").Deserialize<Dictionary<int,int>>()!;
        if(variant=="disabled")actors=actors.Select(a=>a.Index==21?a with{ScriptCallsEnabled=false}:a).ToArray();
        if(variant=="wrong-class")actors=actors.Select(a=>a.Index==21?a with{ClassTag=4}:a).ToArray();
        if(variant=="missing-marker")actors=actors.Where(a=>a.Index!=21).ToArray();
        if(variant=="missing-guard")locals.Remove(0x64);
        if(variant=="handled")locals[0x64]=1;
        if(opened)
        {
            // Native Atel0600 actor21 E5 12 3C 13 3F 07 03 FB.
            // Source18..19,60..63 ->7,3 on all three planes. Not a live post-copy capture.
            foreach(var plane in new[]{map.CollisionShapes,map.TerrainFlags,map.CollisionLayers})
                for(var y=0;y<4;y++)for(var x=0;x<2;x++)
                    plane[(3+y)*map.Width+7+x]=plane[(60+y)*map.Width+18+x];
            locals[0x64]=1;
            actors=actors.Select(a=>a.Index==21?a with{FineY=1536,TileY=6,FractionY=0,ActivationBinding=128}:a).ToArray();
        }
        var leader=actors.Single(a=>a.Index==f.GetProperty("leader").GetInt32());
        if(opened)
        {
            leader=leader with{FineX=1939,TileX=7,FractionX=147,FineY=1918,TileY=7,FractionY=126};
            actors=actors.Select(a=>a.Index==leader.Index?leader:a).ToArray();
        }
        var field=new FieldNavigationSnapshot(0x1000,0x4000,0x2000,0x20000,actors.Length,600,true,
            f.GetProperty("control").GetInt32(),f.GetProperty("inputMode").GetInt32(),0,leader.Index,leader,actors)
        {
            ActorCollisionRadius=f.GetProperty("collisionRadius").GetInt32(),
            LastPartySlotRaw=f.GetProperty("lastPartySlotRaw").GetInt32(),
            ContactActorRaw=f.GetProperty("contactActorRaw").GetInt32(),ConfirmActorRaw=f.GetProperty("confirmActorRaw").GetInt32(),
        };
        var story=new FieldStoryState(s.GetProperty("point").GetInt32(),true){Globals=globals,Locals=locals,Extended=extra};
        return new FieldNavigationSource(new Empty(),_=>{}).Build(field,map,new(0,0,map.Width*256,map.Height*256),[],story);
    }
    private sealed class Empty:IReadableMemory{public bool TryRead(nuint a,Span<byte>b)=>false;}
}
