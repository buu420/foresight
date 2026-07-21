using Xunit;

namespace ChronoTriggerAccessibility.Prism.Tests;

public sealed class PrismOutputTests
{
    [Fact]
    public void SessionInitializesCreatesOutputsThenFreesBeforeShutdown()
    {
        var native = new RecordingPrismNative();

        using (var session = new PrismSession(native))
        {
            session.Output(new PrismOutput("Title screen", Interrupt: true));
            Assert.Equal(["init", "create-best", "output:Title screen:1"], native.Calls);
        }

        Assert.Equal(["init", "create-best", "output:Title screen:1", "free", "shutdown"], native.Calls);
    }

    [Fact]
    public void SessionSurfacesNativeOutputFailure()
    {
        var native = new RecordingPrismNative { OutputResult = PrismError.SpeakFailure };
        using var session = new PrismSession(native);

        var exception = Assert.Throws<PrismException>(() => session.Output(new PrismOutput("Unable to save.")));

        Assert.Equal(PrismError.SpeakFailure, exception.Error);
        Assert.Equal("simulated Prism failure", exception.Message);
    }

    [Fact]
    public async Task SessionSerializesConcurrentOutputCalls()
    {
        var native = new RecordingPrismNative { OutputDelay = TimeSpan.FromMilliseconds(10) };
        using var session = new PrismSession(native);

        await Task.WhenAll(Enumerable.Range(0, 12).Select(index => Task.Run(() => session.Output(new PrismOutput($"Item {index}")))));

        Assert.Equal(1, native.MaximumConcurrentOutputs);
        Assert.Equal(12, native.OutputCount);
    }

    private sealed class RecordingPrismNative : IPrismNative
    {
        private int activeOutputs;
        private int maximumConcurrentOutputs;
        private int outputCount;

        public List<string> Calls { get; } = [];
        public PrismError OutputResult { get; init; } = PrismError.Ok;
        public TimeSpan OutputDelay { get; init; }
        public int MaximumConcurrentOutputs => maximumConcurrentOutputs;
        public int OutputCount => outputCount;

        public IntPtr Init(IntPtr configuration)
        {
            Calls.Add("init");
            return (IntPtr)1;
        }

        public IntPtr CreateBest(IntPtr context)
        {
            Calls.Add("create-best");
            return (IntPtr)2;
        }

        public PrismError Output(IntPtr backend, string text, bool interrupt)
        {
            var active = Interlocked.Increment(ref activeOutputs);
            maximumConcurrentOutputs = Math.Max(maximumConcurrentOutputs, active);
            Calls.Add($"output:{text}:{(interrupt ? 1 : 0)}");
            if (OutputDelay > TimeSpan.Zero)
            {
                Thread.Sleep(OutputDelay);
            }

            Interlocked.Decrement(ref activeOutputs);
            Interlocked.Increment(ref outputCount);
            return OutputResult;
        }

        public void Free(IntPtr backend) => Calls.Add("free");

        public void Shutdown(IntPtr context) => Calls.Add("shutdown");

        public string ErrorString(PrismError error) => "simulated Prism failure";
    }
}
