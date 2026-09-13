using ChronoTriggerAccessibility.Mod.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class FootstepSoundTests
{
    [Fact]
    public async Task FivePendingBeatsAreAllRenderedDespiteTheirIntentionalSpacing()
    {
        var output = new Output(5);
        var sound = new FootstepSound(_ => { }, createOutput: () => output);
        for (var i = 0; i < 5; i++) sound.Play();
        await output.Completed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        sound.Stop();
        Assert.Equal(5, output.Accepted);
    }

    [Fact]
    public async Task TemporarilyBusyVoicesRecoverWithoutDisablingFootsteps()
    {
        var output = new Output(1) { BusyAttempts = 2 };
        var sound = new FootstepSound(_ => { }, createOutput: () => output);
        sound.Play();
        await output.Completed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        sound.Stop();
        Assert.Equal(1, output.Accepted);
    }

    [Fact]
    public async Task QueuePressureReportsInterruptedCountingAndCanPlayAgain()
    {
        using var ready = new ManualResetEventSlim();
        var interrupted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new Output(1);
        var sound = new FootstepSound(_ => { }, createOutput: () => { ready.Wait(); return output; },
            timingInterrupted: () => interrupted.TrySetResult());
        try
        {
            for (var i = 0; i < 40; i++) sound.Play();
        }
        finally { ready.Set(); }
        await interrupted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        sound.Play();
        await output.Completed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        sound.Stop();
        Assert.True(output.Accepted > 0);
    }

    private sealed class Output(int expected) : IFootstepOutput
    {
        public int Accepted, BusyAttempts;
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool TryPlay(int variation)
        {
            if (BusyAttempts-- > 0) return false;
            if (Interlocked.Increment(ref Accepted) >= expected) Completed.TrySetResult();
            return true;
        }
        public void Stop() { }
        public void Dispose() { }
    }
}
