using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class FrozenCliffsMovementTests
{
    [Fact]
    public void TheSlopeRoutesUseNativeSidewaysDescentInputs()
    {
        var frame = FrozenCliffsNavigationTests.Sample(604).Frame;
        Assert.True(frame.CompensateMovingFloor);
        Assert.Contains(frame.Graph.Neighbours(frame.Player), p => p.X > frame.Player.X && p.Y > frame.Player.Y &&
            frame.Graph.InputDirection(frame.Player, p) == NavigationDirection.NorthEast);
        Assert.DoesNotContain(frame.Graph.Neighbours(frame.Player), p => p.Y < frame.Player.Y);
    }

    [Theory]
    [InlineData(0, 1, false)] [InlineData(1, 2, true)] [InlineData(2, 0, false)]
    public void RunningUsesTheCurrentNativeDashMode(int mode, int toggle, bool dash)
    {
        var frame = FrozenCliffsNavigationTests.Sample(604).Frame;
        var result = new NavigationResult([], NavigationDirection.NorthEast, true, true);
        var adjusted = FieldFloorMovement.Adjust(frame, result, new(0x1000, 0x4000, 604, 3, 0, 32, mode, toggle));
        Assert.Null(adjusted.StopReason);
        Assert.Equal(0x900u | (dash ? 8u : 0u), adjusted.Pad);
        Assert.True(adjusted.OverrideDash);
    }

    [Theory]
    [InlineData("missing")] [InlineData("run")] [InlineData("toggle")] [InlineData("owner")]
    public void UnknownOrForeignNativeMovementStateCannotDriveTheSlide(string variant)
    {
        var frame = FrozenCliffsNavigationTests.Sample(604).Frame;
        FieldFloorSnapshot? floor = variant == "missing" ? null : new(variant == "owner" ? 0x2000u : 0x1000u,
            0x4000, 604, 3, 0, 32, variant == "run" ? null : 0, variant == "toggle" ? null : 1);
        var adjusted = FieldFloorMovement.Adjust(frame, new([], NavigationDirection.NorthEast, true, true), floor);
        Assert.Equal(0u, adjusted.Pad);
        Assert.NotNull(adjusted.StopReason);
    }

    [Fact]
    public void AWalkingToggleExplainsWhyRunningIsRequired()
    {
        var frame = FrozenCliffsNavigationTests.Sample(604).Frame;
        var adjusted = FieldFloorMovement.Adjust(frame, new([], NavigationDirection.NorthEast, true, true),
            new(0x1000, 0x4000, 604, 3, 0, 32, 0, 2));
        Assert.Equal(0u, adjusted.Pad);
        Assert.Contains("running", adjusted.StopReason ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OtherScenesRetainTheirExistingMovementHandling()
    {
        var frame = FrozenCliffsNavigationTests.Sample(604).Frame with { Scene = "00001000:660" };
        var adjusted = FieldFloorMovement.Adjust(frame, new([], NavigationDirection.NorthEast, true, true), null);
        Assert.Equal(0x900u, adjusted.Pad);
        Assert.Null(adjusted.StopReason);
    }

    [Fact]
    public void ManualRecoveryCanReplanWhileUpIsHoldingTheSlideStill()
    {
        var frame = FrozenCliffsNavigationTests.Sample(604).Frame;
        var target = frame.Targets.Single(t => t.Id == "script-region:9:605:33:23:33:23");
        var controller = FrozenCliffsNavigationTests.Select(frame, target.Id);
        Assert.True(controller.Handle(NavigationCommand.Guide, frame, 1).Guiding);
        // Logged/derived native replay pose on a south32 tile. Holding Up
        // keeps this position still; releasing it would continue the slide.
        var held = frame with { Player = new(8231, 4434, 1), PlayerFacing = NavigationDirection.North };
        var result = controller.Update(held, 16, manualInput: true);
        Assert.Contains(result.Speech, line => line.Contains("Hold Up", StringComparison.Ordinal));
        for (var tick = 2; tick <= 18; tick++) result = controller.Update(held, tick * 16, manualInput: true);
        Assert.NotNull(result.ManualLeg);
    }

    [Fact]
    public void RepeatingManualRecoveryKeepsTheSlideInstruction()
    {
        var frame=FrozenCliffsNavigationTests.Sample(604).Frame;
        var target=frame.Targets.Single(t=>t.Label=="Middle cave C");
        var controller=FrozenCliffsNavigationTests.Select(frame,target.Id);
        Assert.True(controller.Handle(NavigationCommand.Guide,frame,1).Guiding);
        frame=frame with{Player=new(8231,4434,1),PlayerFacing=NavigationDirection.North};
        controller.Update(frame,16,true);
        var repeat=controller.Handle(NavigationCommand.Repeat,frame,32);
        Assert.Contains(repeat.Speech,s=>s.Contains("Hold Up",StringComparison.Ordinal));
        Assert.DoesNotContain(repeat.Speech,s=>s.Contains("Stop moving",StringComparison.Ordinal));
    }

    [Fact]
    public void CaveApproachesLeaveRoomForAnActualNorthwardFinish()
    {
        var caves = FrozenCliffsNavigationTests.Caves(FrozenCliffsNavigationTests.Sample(604).Frame);
        Assert.Equal(6, caves.Length);
        Assert.All(caves, cave => Assert.All(cave.ApproachPoints, p => Assert.InRange(p.Y % 256, 160, 223)));
    }
}
