using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class GenoDomeReviewRegressionTests
{
    [Theory]
    [InlineData(0,1)] [InlineData(0,2)] [InlineData(1,1)] [InlineData(1,2)]
    public void ARealNativeWallSlideKeepsTheControllersWestInput(int mode,int toggle)
    {
        var frame=GenoDomeRouteTests.Live(2179).Frame with{Player=new(5239,8257,1)};
        var result=new NavigationResult([],NavigationDirection.West,true,true){SteeringPoint=new(5200,8304,1)};
        var pad=FieldFloorMovement.Adjust(frame,result,new(0x1000,0x4000,256,4,0,0,mode,toggle));
        Assert.Null(pad.StopReason);Assert.Equal(0x200u,pad.Pad);
    }

    [Theory]
    [InlineData(2179,"landmark:17",6080,6912)]
    [InlineData(2179,"landmark:19",7232,6912)]
    [InlineData(2646,"landmark:10",7360,11264)]
    public void TheFinalNativeFacingTurnStillReachesTheGame(int line,string id,int x,int y)
    {
        var sample=GenoDomeRouteTests.Live(line);
        var target=Assert.Single(sample.Frame.Targets,t=>t.Id==id);
        var goal=Assert.Single(target.ApproachPoints,p=>p.X==x && p.Y==y);
        var frame=sample.Frame with{Targets=[target],Player=goal,PlayerFacing=NavigationDirection.South};
        var result=VortexRoomGeometryTests.Start(frame,id,true);
        Assert.True(result.AutoWalking);Assert.NotEqual(NavigationDirection.None,result.Direction);
        var pad=FieldFloorMovement.Adjust(frame,result,new(0x1000,0x4000,256,4,0,0,0,1));
        Assert.Null(pad.StopReason);Assert.Equal(FieldNavigationRuntime.DirectionBits(result.Direction),pad.Pad);
    }

    [Fact]
    public void TheElevatorDoorStillReceivesItsNativeTouchPush()
    {
        var frame=GenoDomeRouteTests.Live(2646).Frame with{Player=new(10120,10864,1)};
        var result=VortexRoomGeometryTests.Start(frame,"exit:3",true);
        Assert.True(result.AutoWalking);Assert.Equal(NavigationDirection.North,result.Direction);
        var pad=FieldFloorMovement.Adjust(frame,result,new(0x1000,0x4000,256,4,0,0,0,1));
        Assert.Null(pad.StopReason);Assert.Equal(0x800u,pad.Pad);
    }

    [Theory]
    [InlineData(0)] [InlineData(2)]
    public void WalkingAgainstTheConveyorKeepsTheCorrectRunAdvice(int mode)
    {
        var frame=GenoDomeRouteTests.Live(2646).Frame with{Player=new(8832,7808,1),MovingFloor=new(NavigationDirection.South,16)};
        var result=new NavigationResult([],NavigationDirection.North,true,true){SteeringPoint=new(8832,7296,1)};
        var pad=FieldFloorMovement.Adjust(frame,result,new(0x1000,0x4000,256,4,0,16,mode,2));
        Assert.Contains("running is required",pad.StopReason!);
        Assert.DoesNotContain("Select walking",pad.StopReason!);
    }

    [Fact]
    public void OptionalGaitCaptureDoesNotDisableAnOrdinaryRoute()
    {
        var frame=GenoDomeRouteTests.Live(2646).Frame;
        var result=new NavigationResult([],NavigationDirection.North,true,true){SteeringPoint=new(9906,10864,1)};
        var pad=FieldFloorMovement.Adjust(frame,result,null);
        Assert.Null(pad.StopReason);Assert.Equal(0x800u,pad.Pad);
    }

    [Theory]
    [InlineData(0,"Crono")] [InlineData(3,"R-66")] [InlineData(6,"Magus")]
    public void TheAuraNamesItsVisibleLeader(int character,string name)
    {
        var said=new List<string>();var requested=new List<int>();
        var feedback=new GenoDomeFeedback(said.Add,id=>{requested.Add(id);return id==character?name:"Wrong character";});
        feedback.Enable();var field=GenoDomeFeedbackTests.Field(charged:true);
        var leader=field.LeadPlayer! with{VisualIndex=character};
        field=field with{LeadPlayer=leader,Actors=field.Actors.Select(a=>a.Index==leader.Index?leader:a).ToArray()};
        feedback.Observe(field,GenoDomeFeedbackTests.Story(charge:true),new(0,0,16384,16384));
        Assert.Equal(character,Assert.Single(requested));Assert.Equal(name+" is electrically charged.",Assert.Single(said));
    }

    [Fact]
    public void AnUnreadableLeaderNameStaysNeutral()
    {
        var said=new List<string>();var feedback=new GenoDomeFeedback(said.Add,_=>null);feedback.Enable();
        feedback.Observe(GenoDomeFeedbackTests.Field(charged:true),GenoDomeFeedbackTests.Story(charge:true),new(0,0,16384,16384));
        Assert.Equal("The party leader is electrically charged.",Assert.Single(said));
    }

    [Fact]
    public void TheGenoBeltUsesTheGamesConveyorName()
    {
        var said=new List<string>();var feedback=new FieldFloorFeedback(said.Add,_=>{});
        feedback.Observe(new(0x1000,0x4000,256,4,0,16,0,1));
        Assert.Contains("Conveyor",Assert.Single(said));
        feedback.Observe(new(0x1000,0x4000,256,4,0,0,0,1));
        Assert.Equal("Off the conveyor.",said.Last());
    }
}
