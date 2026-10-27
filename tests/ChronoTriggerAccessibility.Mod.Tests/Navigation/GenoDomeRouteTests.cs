using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class GenoDomeRouteTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TheLoggedExitRouteStartsInBothModes(bool automatic)
    {
        var frame=Live(2646).Frame;var result=VortexRoomGeometryTests.Start(frame,"exit:2",automatic);
        Assert.True(result.Guiding);Assert.Equal(automatic,result.AutoWalking);
        Assert.True(frame.CompensateMovingFloor);Assert.NotNull(frame.CanTraverseFloorStep);
        if(automatic)Assert.NotNull(result.SteeringPoint);
        else Assert.NotNull(result.ManualLeg);
    }

    [Theory]
    [InlineData(0,1,0x808u)] [InlineData(0,2,0x800u)]
    [InlineData(1,2,0x800u)] [InlineData(2,0,0x808u)]
    public void TheCapturedCornerUsesAProvenShortWalkingStep(int mode,int toggle,uint expected)
    {
        var sample=Live(2646);var graph=new FieldNavigationGraph(sample.Map,new FieldActorCollisionRules(sample.Field));
        var frame=sample.Frame with{CompensateMovingFloor=true,CanTraverseFloorStep=graph.CanTraverseFloorStep};
        // Original x86 175E90/178FF0 on these exact captured planes:
        // Up32 remains (9906,10886); Up16 reaches (9906,10870).
        var result=new NavigationResult([],NavigationDirection.North,true,true){SteeringPoint=new(9906,10864,1)};
        var pad=FieldFloorMovement.Adjust(frame,result,new(0x1000,0x4000,256,4,0,0,mode,toggle));
        Assert.Null(pad.StopReason);Assert.Equal(expected,pad.Pad);
    }

    [Fact]
    public void AnUnselectableGaitKeepsNativeMovementInsteadOfInventingARestriction()
    {
        var sample=Live(2646);var graph=new FieldNavigationGraph(sample.Map,new FieldActorCollisionRules(sample.Field));
        var result=new NavigationResult([],NavigationDirection.North,true,true){SteeringPoint=new(9906,10864,1)};
        var pad=FieldFloorMovement.Adjust(sample.Frame with{CompensateMovingFloor=true,CanTraverseFloorStep=graph.CanTraverseFloorStep},
            result,new(0x1000,0x4000,256,4,0,0,1,1));
        Assert.Equal(0x800u,pad.Pad);Assert.Null(pad.StopReason);
    }

    [Fact]
    public void GenoAutomaticSteeringCanSelectWalkingWhileThePlayerHoldsDash()
    {
        var sample=Live(2646);var frame=sample.Frame with{Targets=sample.Frame.Targets.Where(t=>t.Id=="exit:2").ToArray()};
        var keys=new HashSet<int>();long now=0;var said=new List<string>();
        var floor=new FieldFloorSnapshot(0x1000,0x4000,256,4,0,0,1,2);
        var runtime=new FieldNavigationRuntime(_=>frame,new(keys.Contains,()=>true),()=>true,()=>now,said.Add,_=>{},
            adjustFieldInput:(f,r)=>FieldFloorMovement.Adjust(f,r,floor),fieldFloor:()=>floor);
        runtime.Enable();runtime.OnInput(0x1000,0);now=16;keys.Add('O');runtime.OnInput(0x1000,0);
        now=32;keys.Clear();runtime.OnInput(0x1000,0);now=48;keys.Add('P');
        Assert.Equal(0x800u,runtime.OnInput(0x1000,8));
        keys.Clear();now=64;Assert.Equal(0x108u,runtime.OnInput(0x1000,0x108)); // Native movement still cancels.
    }

    [Theory]
    [InlineData(10)] [InlineData(14)] [InlineData(15)] [InlineData(16)] [InlineData(17)] [InlineData(19)] [InlineData(20)]
    public void ReachableControlsUseTheirActualCapturedContact(int slot)
    {
        foreach(var automatic in new[]{false,true})
        {
            var sample=Live(2179);var result=VortexRoomGeometryTests.Start(sample.Frame,$"landmark:{slot}",automatic);
            Assert.True(result.Guiding,string.Join(" ",result.Speech));
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void StartingTheDroidDoesNotRequireAlreadyMeetingTheGuard(bool automatic)
    {
        var frame=Escort(false);var target=Assert.Single(frame.Targets,t=>t.Id=="actor:8:4:170");
        Assert.NotEmpty(target.ApproachPoints);Assert.NotNull(target.ContactPosition);
        Assert.Null(target.ConfirmFacings);
        Assert.True(VortexRoomGeometryTests.Start(frame,target.Id,automatic).Guiding);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LeadingTheDroidEndsAtTheNativeMeetingRow(bool automatic)
    {
        var frame=Escort(true);var target=Assert.Single(frame.Targets,t=>t.Id=="actor:29:4:170");
        Assert.NotEmpty(target.ApproachPoints);
        Assert.All(target.ApproachPoints,p=>{Assert.True(p.X/256<10);Assert.Equal(36,p.Y/256);});
        Assert.Null(target.ConfirmFacings);Assert.Contains("Wait",target.ArrivalInstruction!);
        Assert.True(VortexRoomGeometryTests.Start(frame,target.Id,automatic).Guiding);
        var following=Assert.Single(frame.Targets,t=>t.Id=="actor:8:4:170");
        Assert.Contains("following",following.GuidanceRestriction!);
    }

    [Theory]
    [InlineData(25)] [InlineData(26)] [InlineData(27)] [InlineData(28)]
    public void VisiblyClosedPodsGiveTheirRestrictionInsteadOfAnUnexplainedRouteFailure(int slot)
    {
        var target=Assert.Single(Live(2646).Frame.Targets,t=>t.Id==$"landmark:{slot}");
        Assert.Contains("closed",target.GuidanceRestriction!);
        Assert.Contains("closed",target.Instruction!);
    }

    [Fact]
    public void TheActuallyOpenedSouthernReceiverRemainsReachableWithoutInventingACharge()
    {
        var frame=Live(2179).Frame;var target=Assert.Single(frame.Targets,t=>t.Id=="landmark:26");
        Assert.Null(target.GuidanceRestriction);Assert.NotEmpty(target.ApproachPoints);
        Assert.Contains("open",target.Instruction!);Assert.Contains("charge",target.Instruction!);
        foreach(var automatic in new[]{false,true})Assert.True(VortexRoomGeometryTests.Start(frame,target.Id,automatic).Guiding);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TheOpenChargerUsesItsNarrowNativeFrontContact(bool automatic)
    {
        var frame=Charger(false);var target=Assert.Single(frame.Targets,t=>t.Id=="landmark:25");
        Assert.Contains(new NavigationPoint(6400,6656,1),target.ApproachPoints);
        Assert.NotNull(target.ContactPosition);Assert.Null(target.ConfirmFacings);
        Assert.True(VortexRoomGeometryTests.Start(frame,target.Id,automatic).Guiding);
    }

    [Fact]
    public void AnAcceptedChargeStopsPushingBeforeTheDelayedAuraAppears()
    {
        var target=Assert.Single(Charger(true).Targets,t=>t.Id=="landmark:25");
        Assert.Contains("Step away",target.GuidanceRestriction!);
        Assert.DoesNotContain("charged",target.GuidanceRestriction!,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheOpenedConveyorDoorRemainsIdentifiableAndDoesNotRequestAnotherConfirm()
    {
        var leader=GenoDomeNavigationTests.Marker(4,23,56) with{ClassTag=0,VisualIndex=3,LoadedFlag=1,IsPartyMember=true};
        var marker=GenoDomeNavigationTests.Marker(1,23,54);
        var field=new FieldNavigationSnapshot(0x1000,0x4000,0x2000,0x20000,9,250,true,1,0,0,4,leader,[leader,marker]);
        var exits=Enumerable.Repeat((byte)128,4096).ToArray();exits[53*64+23]=0;
        var map=new FieldMapSnapshot(64,64,new byte[4096],new byte[4096],Enumerable.Repeat((byte)1,4096).ToArray(),1,false,64,64,exits);
        var story=GenoDomeFeedbackTests.Story() with{Locals=new Dictionary<int,int>{{6,1}}};
        var frame=new FieldNavigationSource(new Empty(),_=>{}).Build(field,map,new(0,0,16384,16384),[],story);
        var target=Assert.Single(frame.Targets,t=>t.Id=="landmark:1");
        Assert.Contains("open",target.Instruction!);Assert.Contains("Continue up",target.ArrivalInstruction!);
        Assert.Contains("Continue up",target.ArrivalAnnouncement!);
        Assert.Null(target.ConfirmFacings);Assert.NotEmpty(target.ApproachPoints);
    }

    internal static NavigationFrame Charger(bool accepted)
    {
        var sample=Live(2179);var map=sample.Map;
        // Derived open charger: Atel0355:0B2E E5 3B 37 3C 3A 18 17 3B.
        foreach(var plane in new[]{map.CollisionShapes,map.TerrainFlags,map.CollisionLayers})
            for(var y=0;y<4;y++)for(var x=0;x<2;x++)plane[(23+y)*map.Width+24+x]=plane[(55+y)*map.Width+59+x];
        var globals=sample.Story.Globals.ToDictionary();globals[0x13D]=0xD0;globals[0x13C]=accepted?0x41:1;
        var field=sample.Field;
        if(accepted)
        {
            var leader=field.LeadPlayer! with{FineX=6400,TileX=25,FractionX=0,FineY=6688,TileY=26,FractionY=32};
            field=field with{LeadPlayer=leader,Actors=field.Actors.Select(a=>a.Index==leader.Index?leader:a).ToArray()};
        }
        return new FieldNavigationSource(new Empty(),_=>{}).Build(field,map,new(0,0,16384,16384),[],sample.Story with{Globals=globals});
    }

    internal static NavigationFrame Escort(bool following)
    {
        var sample=Live(2179);var map=sample.Map;
        // Derived state, not a second live capture: Atel0355:0C87's actual
        // E5 3C 3C 3E 3E 15 0F 3B opens the northern receiver door.
        foreach(var plane in new[]{map.CollisionShapes,map.TerrainFlags,map.CollisionLayers})
            for(var y=0;y<3;y++)for(var x=0;x<3;x++)plane[(15+y)*map.Width+21+x]=plane[(60+y)*map.Width+60+x];
        var globals=sample.Story.Globals.ToDictionary();globals[0x13E]=2;
        var locals=sample.Story.Locals.ToDictionary();locals[12]=following?1:0;
        var story=sample.Story with{Globals=globals,Locals=locals};
        return new FieldNavigationSource(new Empty(),_=>{}).Build(sample.Field,map,new(0,0,16384,16384),[],story);
    }

    internal static (NavigationFrame Frame,FieldNavigationSnapshot Field,FieldMapSnapshot Map,FieldStoryState Story) Live(int line)
    {
        using var doc=JsonDocument.Parse(typeof(GenoDomeRouteTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.geno-dome-live-0379.json")!);
        var root=doc.RootElement.GetProperty("captures").EnumerateArray().Single(e=>e.GetProperty("provenance").GetProperty("line").GetInt32()==line);
        var map=root.GetProperty("map").Deserialize<FieldMapSnapshot>()!;
        var actors=root.GetProperty("actors").Deserialize<FieldActorSnapshot[]>()!;
        var f=root.GetProperty("field");var s=root.GetProperty("story");var lead=actors.Single(a=>a.Index==f.GetProperty("leader").GetInt32());
        var field=new FieldNavigationSnapshot(0x1000,0x4000,0x2000,0x20000,51,256,true,1,0,0,lead.Index,lead,actors)
        {ActorCollisionRadius=f.GetProperty("collisionRadius").GetInt32(),LastPartySlotRaw=f.GetProperty("lastPartySlotRaw").GetInt32(),
            ContactActorRaw=f.GetProperty("contactActorRaw").GetInt32(),ConfirmActorRaw=f.GetProperty("confirmActorRaw").GetInt32()};
        var story=new FieldStoryState(s.GetProperty("point").GetInt32(),true)
        {Globals=s.GetProperty("globals").Deserialize<Dictionary<int,int>>()!,Locals=s.GetProperty("locals").Deserialize<Dictionary<int,int>>()!,
            Extended=s.GetProperty("extended").Deserialize<Dictionary<int,int>>()!,Party=[3,5,0,130,4,131,2,6,1]};
        var treasures=root.GetProperty("treasures").EnumerateArray().Select(t=>new FieldTreasure(t.GetProperty("index").GetInt32(),
            t.GetProperty("x").GetInt32(),t.GetProperty("y").GetInt32()){IsChest=t.GetProperty("chest").GetBoolean()}).ToArray();
        var frame=new FieldNavigationSource(new Empty(),_=>{}).Build(field,map,new(0,0,16384,16384),treasures,story);
        return(frame,field,map,story);
    }
    private sealed class Empty:IReadableMemory{public bool TryRead(nuint address,Span<byte> bytes)=>false;}
}
