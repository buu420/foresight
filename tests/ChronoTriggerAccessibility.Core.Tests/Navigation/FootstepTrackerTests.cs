using ChronoTriggerAccessibility.Core.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.Navigation;

public sealed class FootstepTrackerTests
{
    private static FootstepFrame Frame(int x = 0, int y = 0) => new(1, 2, 3, x, y);

    [Fact]
    public void HeldMovementWithoutDisplacementIsSilent()
    {
        var tracker = new FootstepTracker();
        for (var time = 0; time < 2000; time += 16)
            Assert.False(tracker.Update(Frame(), 0x100, time));
    }

    [Fact]
    public void ActualWalkingProducesDistanceSpacedSteps()
    {
        var tracker = new FootstepTracker();
        var count = 0;
        for (var i = 0; i <= 96; i++)
            if (tracker.Update(Frame(i * 16), 0x100, i * 33)) count++;
        Assert.Equal(4, count);
    }

    [Fact]
    public void RunningProducesTheSameNumberForTheSameDistance()
    {
        var tracker = new FootstepTracker();
        var count = 0;
        for (var i = 0; i <= 48; i++)
            if (tracker.Update(Frame(i * 32), 0x100, i * 33)) count++;
        Assert.Equal(4, count);
    }

    [Fact]
    public void ScriptedMotionWithoutAcceptedMovementIsSilent()
    {
        var tracker = new FootstepTracker();
        for (var i = 0; i <= 50; i++)
            Assert.False(tracker.Update(Frame(i * 32), 0, i * 33));
    }

    [Fact]
    public void DisplacementIsAttributedToPreviousInputIncludingTheReleaseTick()
    {
        var tracker = new FootstepTracker();
        tracker.Update(Frame(), 0x100, 0);
        for (var i = 1; i < 12; i++) Assert.False(tracker.Update(Frame(i * 32), 0x100, i * 33));
        Assert.True(tracker.Update(Frame(384), 0, 396));
        Assert.False(tracker.Update(Frame(416), 0, 429));
    }

    [Theory]
    [InlineData(1)] // area
    [InlineData(2)] // leader
    [InlineData(3)] // engine/actor base
    [InlineData(4)] // input gap
    [InlineData(5)] // teleport
    [InlineData(6)] // unavailable
    public void TransitionsDiscardPartialStrideAndNeverEmitABurst(int kind)
    {
        var tracker = new FootstepTracker();
        tracker.Update(Frame(), 0x100, 0);
        for (var i = 1; i <= 11; i++) tracker.Update(Frame(i * 32), 0x100, i * 16);
        FootstepFrame? changed = kind switch
        {
            1 => Frame(384) with { Scene = 5 },
            2 => Frame(384) with { Actor = 4 },
            3 => Frame(384) with { Context = 2 },
            5 => Frame(2000),
            6 => null,
            _ => Frame(384),
        };
        Assert.False(tracker.Update(changed, 0x100, kind == 4 ? 1000 : 192));
    }

    [Fact]
    public void ResetDropsMotionAfterFocusOrMenuInterruption()
    {
        var tracker = new FootstepTracker();
        tracker.Update(Frame(), 0x100, 0);
        tracker.Update(Frame(256), 0x100, 100);
        tracker.Reset();
        Assert.False(tracker.Update(Frame(384), 0x100, 116));
    }
}
