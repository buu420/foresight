using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Core.State;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.Navigation;

public sealed class MovingFloorInteractionTests
{
    [Fact]
    public void AutomaticArrivalKeepsThePlayerInNativeConfirmReach()
    {
        var (controller, frame) = Start(true);
        frame = frame with { Player = new(256, 128, 1), PlayerFacing = NavigationDirection.East };
        var arrived = controller.Update(frame, 16);
        Assert.True(arrived.AutoWalking);
        Assert.True(controller.AllowsConfirmWhileFollowing(frame));
        Assert.Contains(arrived.Speech, s => s.Contains("Press Confirm"));

        // Native sand pushes the player off the final waypoint after arrival.
        frame = frame with { Player = new(256, 192, 1) };
        var drift = controller.Update(frame, 32);
        Assert.True(drift.AutoWalking);
        Assert.NotEqual(NavigationDirection.None, drift.Direction);
    }

    [Fact]
    public void ManualGuidanceKeepsCoachingWithoutGeneratingMovement()
    {
        var (controller, frame) = Start(false);
        var arrived = controller.Update(frame with { Player = new(256,128,1), PlayerFacing = NavigationDirection.East }, 16);
        Assert.True(arrived.Guiding);
        Assert.False(arrived.AutoWalking);
        Assert.Equal(NavigationDirection.None, arrived.Direction);
    }

    [Fact]
    public void AutomaticArrivalKeepsOrdinaryContactTowardTheClosedChest()
    {
        var (controller,frame)=Start(true);
        var arrived=controller.Update(frame with{Player=new(256,128,1),PlayerFacing=NavigationDirection.East},16);
        Assert.Equal(NavigationDirection.East,arrived.Direction);
    }

    [Fact]
    public void AProvenInteractionIsUsedWithoutContinuingToACompetingWaypoint()
    {
        var (controller,frame)=Start(true);
        var ready=controller.Update(frame with{Player=new(192,128,1),PlayerFacing=NavigationDirection.East},16);
        Assert.True(ready.AutoWalking);
        Assert.NotNull(ready.SteeringPoint);
        Assert.Equal(new NavigationPoint(256,128,1),ready.SteeringPoint);
    }

    [Fact]
    public void AReachableStableContactIsPreferredButAnUnreachableOneDoesNotRemoveFallbacks()
    {
        var (_,frame)=Start(true);
        var target=frame.Targets[0] with { PreferredApproachPoints=[new(256,256,1)] };
        var controller=new NavigationController();
        controller.Handle(NavigationCommand.ToggleWalk,frame with{Targets=[target]},1);
        Assert.Contains("goal=(256,256,1)",controller.DiagnosticState);
        target=target with{PreferredApproachPoints=[new(999,999,1)]};
        controller=new();
        Assert.True(controller.Handle(NavigationCommand.ToggleWalk,frame with{Targets=[target]},1).AutoWalking);
        Assert.Contains("goal=(256,128,1)",controller.DiagnosticState);
    }

    [Fact]
    public void ExhaustedOscillatingContactsStopInsteadOfWalkingForever()
    {
        var (_,frame)=Start(true);
        frame=frame with{Targets=[frame.Targets[0] with{ApproachPoints=[new(256,128,1)]}]};
        var controller=new NavigationController();controller.Handle(NavigationCommand.ToggleWalk,frame,1);
        NavigationResult? result=null;
        for(var n=1;n<=20;n++)result=controller.Update(frame with{Player=new(n%2==0?0:64,128,1)},n*100);
        Assert.False(result!.AutoWalking);
    }

    [Fact]
    public void InvoluntarySandMotionDoesNotPreventManualReplanning()
    {
        var (controller,frame)=Start(false);
        frame=frame with{Player=new(256,128,1),PlayerFacing=NavigationDirection.East,MovingFloor=new(NavigationDirection.South,16)};
        controller.Update(frame,16);
        var carried=controller.Update(frame with{Player=new(256,384,1)},1000);
        Assert.True(carried.Guiding);Assert.False(carried.AutoWalking);
        Assert.Equal(NavigationDirection.None,carried.Direction);
        Assert.NotNull(carried.ManualLeg);
        Assert.DoesNotContain(carried.Speech,s=>s.Contains("Stop moving"));
    }

    [Fact]
    public void SandOscillationIsNotMistakenForProgressTowardAnInteraction()
    {
        var (controller,frame)=Start(true);var speech=new List<string>();
        for(var n=1;n<=20;n++)speech.AddRange(controller.Update(frame with{Player=new(n%2==0?0:64,128,1)},n*100).Speech);
        Assert.Contains(speech,s=>s.Contains("another approach"));
    }

    [Fact]
    public void ManualReleasePausesDoNotEraseEvidenceOfARepeatedFailedApproach()
    {
        var (controller,frame)=Start(false);var speech=new List<string>();
        frame=frame with{MovingFloor=new(NavigationDirection.South,16),Graph=new DriftGrid()};
        for(var n=1;n<=45;n++)speech.AddRange(controller.Update(frame with{Player=new(n%2==0?0:64,128,1)},n*100,n%2==0).Speech);
        Assert.Contains(speech,s=>s.Contains("another approach"));
    }

    [Fact]
    public void ManualAdviceNamesTheOrdinaryDiagonalThatCountersSidewaysSand()
    {
        var (_,frame)=Start(false);frame=frame with{MovingFloor=new(NavigationDirection.South,16)};
        var result=new NavigationController().Handle(NavigationCommand.Guide,frame,1);
        Assert.Equal(NavigationDirection.None,result.Direction);
        Assert.Equal(NavigationDirection.NorthEast,result.ManualLeg!.Direction);
        Assert.Contains(result.Speech,s=>s.Contains("Run up and right"));
        Assert.Null(result.ManualLeg.ExpectedSteps);
    }

    [Fact]
    public void PassiveDriftReplansAreBoundedAndOrdinaryConveyorsKeepTheirPolicy()
    {
        var (controller,frame)=Start(false);var speech=new List<string>();
        frame=frame with{MovingFloor=new(NavigationDirection.South,16),Graph=new DriftGrid()};
        for(var n=1;n<=150;n++)speech.AddRange(controller.Update(frame with{Player=new(0,128+n*16,1)},n*16).Speech);
        Assert.InRange(speech.Count(s=>s=="Route updated."),0,2);
        var target=frame.Targets[0] with{KeepInteractionReach=false};controller=new();speech=[];
        controller.Handle(NavigationCommand.Guide,frame with{Targets=[target]},1);
        for(var n=1;n<=100;n++)speech.AddRange(controller.Update(frame with{Targets=[target],Player=new(0,128+n*16,1)},n*16).Speech);
        Assert.DoesNotContain("Route updated.",speech);
    }

    [Fact]
    public void NativeManualReachSurvivesDriftOutsideTheSmallArrivalBox()
    {
        var (controller,frame)=Start(false);
        frame=frame with{MovingFloor=new(NavigationDirection.South,16),Player=new(256,128,1),PlayerFacing=NavigationDirection.East};
        controller.Update(frame,1000);
        var drift=controller.Update(frame with{Player=new(256,192,1)},1048);
        Assert.Empty(drift.Speech);Assert.True(drift.Guiding);Assert.Equal(NavigationDirection.None,drift.Direction);
        Assert.Null(drift.ManualLeg);
    }

    [Fact]
    public void RecurringReachWindowsGiveTheArrivalCueTimeToFinish()
    {
        var (controller,frame)=Start(true);var speech=new List<string>();
        frame=frame with{PlayerFacing=NavigationDirection.East};
        for(var n=1;n<=20;n++)speech.AddRange(controller.Update(frame with{Player=new(256,n%2==1?128:352,1)},n*100).Speech);
        Assert.Single(speech,s=>s.Contains("In reach"));
    }

    [Fact]
    public void AnUnchangedQualitativeDriftInstructionDoesNotKeepInterruptingItself()
    {
        var (controller,frame)=Start(false);var speech=new List<string>();
        frame=frame with{MovingFloor=new(NavigationDirection.South,16),Graph=new HorizontalFirst()};
        for(var n=1;n<=150;n++)speech.AddRange(controller.Update(frame with{Player=new(0,128+n*16,1)},n*16).Speech);
        // Initial advice and the first resume after "Stop moving" are useful;
        // later identical idle drift replans must stay quiet.
        Assert.Equal(2,speech.Count(s=>s.StartsWith("Run up and right")));
    }

    [Fact]
    public void OrdinaryFloorRoutesKeepTheSameQuietManualDriftPolicyWithoutAnInteractionHold()
    {
        var (_,frame)=Start(false);
        var target=frame.Targets[0] with{KeepInteractionReach=false,ConfirmFacings=null,ArrivalAnnouncement=null};
        frame=frame with{Targets=[target],CompensateMovingFloor=true,
            MovingFloor=new(NavigationDirection.South,16),Graph=new HorizontalFirst()};
        var controller=new NavigationController();var speech=new List<string>();
        controller.Handle(NavigationCommand.Guide,frame,1);
        for(var n=1;n<=150;n++)speech.AddRange(controller.Update(frame with{Player=new(0,128+n*16,1)},n*16).Speech);
        Assert.True(controller.IsActive);
        Assert.InRange(speech.Count(s=>s=="Route updated."),0,2);
        Assert.InRange(speech.Count(s=>s.StartsWith("Run up and right")),0,2);
        Assert.False(controller.IsHolding);
    }

    [Fact]
    public void UnavailableTargetOrChangedSceneRevokesInteractionAuthority()
    {
        var (controller, frame) = Start(true);
        Assert.True(controller.AllowsConfirmWhileFollowing(frame));
        Assert.False(controller.AllowsConfirmWhileFollowing(frame with { Scene = "other" }));
        Assert.False(controller.Update(frame with { Targets = [] }, 16).AutoWalking);
    }

    [Fact]
    public void ABlockedSandApproachCanTryAnotherProvenNativeContact()
    {
        var (controller, frame) = Start(true);
        controller.Update(frame, 500);
        controller.Update(frame, 1000);
        var changed = controller.Update(frame, 1600);
        Assert.True(changed.AutoWalking);
        Assert.Contains("goal=(256,256,1)", controller.DiagnosticState);
    }

    [Fact]
    public void BackgroundSandSpeechDoesNotInterruptGuidanceAndExplicitStatusStillDoes()
    {
        var state = new AccessibilityState();
        var background = Assert.Single(state.Apply(new NavigationAnnouncement("Sand flows west.") { Interrupt = false }));
        Assert.False(background.Interrupt);
        Assert.True(Assert.Single(state.Apply(new NavigationAnnouncement("Sand flows west."))).Interrupt);
    }

    private static (NavigationController, NavigationFrame) Start(bool auto)
    {
        var goal = new NavigationPoint(256,128,1);
        var target = new NavigationTarget("chest", "Treasure chest", NavigationCategory.People, goal,
            [goal,new(256,256,1)],true,true)
        {
            KeepInteractionReach = true,
            ConfirmFacings = p => Math.Abs(p.X-goal.X)<=80 && p.Y is >= 48 and <= 336 ? [NavigationDirection.East] : [],
            ArrivalAnnouncement = "In reach. Press Confirm to open the chest."
        };
        var frame = new NavigationFrame("sand",true,new(0,128,1),[target],new Grid(),256) { PlayerFacing=NavigationDirection.East };
        var controller = new NavigationController();
        controller.Handle(auto ? NavigationCommand.ToggleWalk : NavigationCommand.Guide,frame,1);
        return (controller,frame);
    }
    private sealed class Grid:INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint p)
        {
            if(p.X<512)yield return p with{X=p.X+64};if(p.X>0)yield return p with{X=p.X-64};
            if(p.Y<512)yield return p with{Y=p.Y+64};if(p.Y>0)yield return p with{Y=p.Y-64};
        }
    }
    private sealed class DriftGrid:INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint p)
        {
            if(p.X<512)yield return p with{X=(p.X/64+1)*64};if(p.X>0)yield return p with{X=(p.X-1)/64*64};
            if(p.Y<4096)yield return p with{Y=(p.Y/64+1)*64};if(p.Y>0)yield return p with{Y=(p.Y-1)/64*64};
        }
    }
    private sealed class HorizontalFirst:INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint p)
        {
            if(p.X<256)yield return p with{X=p.X+64};
            else {if(p.Y>0)yield return p with{Y=(p.Y-1)/64*64};if(p.Y<4096)yield return p with{Y=(p.Y/64+1)*64};}
        }
    }
}
