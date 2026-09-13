using ChronoTriggerAccessibility.Core.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.Navigation;

public sealed class CountableFootstepTests
{
    [Theory]
    [InlineData(16, 16)]
    [InlineData(32, 16)]
    [InlineData(16, 33)]
    [InlineData(32, 33)]
    public void SixLocalNavigationStepsProduceSixSoundsAtWalkingAndRunningSpeeds(int speed, int interval)
    {
        var tracker = new FootstepTracker();
        var beats = 0;
        for (var i = 0; i <= 1536 / speed; i++)
            if (tracker.Update(new(1, 2, 3, i * speed, 0), 0x100, i * interval)) beats++;
        Assert.Equal(6, beats);
    }

    [Fact]
    public void StoppingHalfwayThroughAStepDoesNotLoseTheDistanceAlreadyWalked()
    {
        var tracker = new FootstepTracker();
        tracker.Update(new(1, 2, 3, 0, 0), 0x100, 0);
        Assert.False(tracker.Update(new(1, 2, 3, 128, 0), 0, 100));
        Assert.False(tracker.Update(new(1, 2, 3, 128, 0), 0, 200));
        Assert.False(tracker.Update(new(1, 2, 3, 128, 0), 0x100, 300));
        Assert.True(tracker.Update(new(1, 2, 3, 256, 0), 0, 400));
    }

    [Theory]
    [InlineData(128)]
    [InlineData(256)]
    public void BothMapScalesPreserveEveryWholeStepAndTheRemainder(int units)
    {
        var tracker = new FootstepTracker();
        var beats = 0;
        tracker.Update(new(1, 2, 3, 0, 0, units), 0x100, 0);
        for (var x = 32; x <= 5 * units + units / 2; x += 32)
        {
            tracker.Update(new(1, 2, 3, x, 0, units), 0x100, x / 2);
            beats += tracker.Steps;
            Assert.False(tracker.Update(new(1, 2, 3, x, 0, units), 0x100, x / 2));
        }
        Assert.Equal(5, beats);
        Assert.Equal(units / 2.0, tracker.Distance);
    }

    [Fact]
    public void WorldStepFinishesAfterReleaseWithoutGrantingUnlimitedMovement()
    {
        var tracker = new FootstepTracker();
        tracker.Update(new(1, 496, 0, 0, 0, 128), 0x100, 0);
        var beats = 0;
        for (var x = 32; x <= 256; x += 32)
        {
            tracker.Update(new(1, 496, 0, x, 0, 128), 0, x / 2);
            beats += tracker.Steps;
        }
        Assert.Equal(1, beats);
    }
}
