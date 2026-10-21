using System.Buffers.Binary;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Bootstrap;

public sealed class RenderTextureFailureTests
{
    private static readonly RenderTextureState State = new(1.5f, 0x3FC00000, false, null);
    private static readonly TextureMemorySnapshot Snapshot = new(100, 200, 300, 400, 500, 600, null);

    [Theory]
    [InlineData(0x12345678)]
    [InlineData(-1)]
    public void SuccessfulNativeCreationIsForwardedExactlyOnceWithoutCapturingOrLogging(int pointer)
    {
        var calls = 0;
        var observer = new RenderTextureFailureObserver(() => throw new Exception("Unexpected state read"),
            () => throw new Exception("Unexpected memory scan"), _ => throw new Exception("Unexpected log"));
        Assert.Equal((nint)pointer, observer.Create(-20, 1234, (width, height) =>
        {
            calls++;
            Assert.Equal(-20, width);
            Assert.Equal(1234, height);
            return pointer;
        }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void NullIsReturnedUnchangedAfterTheExactRequestAndIndependentCountersAreRecorded()
    {
        var calls = 0;
        RenderTextureFailure? failure = null;
        var observer = new RenderTextureFailureObserver(() => { Assert.Equal(1, calls); return State; },
            () => Snapshot, value => failure = value);
        Assert.Equal(0, observer.Create(1216, 800, (_, _) => { calls++; return 0; }));
        Assert.Equal(1, calls);
        Assert.NotNull(failure);
        Assert.Equal(1216, failure.Width);
        Assert.Equal(800, failure.Height);
        Assert.Same(State, failure.State);
        Assert.Same(Snapshot, failure.Memory);
        Assert.Equal(2048u, failure.Estimate.PixelWidth);
        Assert.Equal(2048u, failure.Estimate.PixelHeight);
        Assert.Equal(16777216ul, failure.Estimate.RgbaBytes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FailedCaptureRetainsTheOtherEvidenceAndStillReports(bool stateFails)
    {
        RenderTextureFailure? failure = null;
        var observer = new RenderTextureFailureObserver(
            () => stateFails ? throw new IOException("read") : State,
            () => stateFails ? Snapshot : throw new IOException("counters"), value => failure = value);
        Assert.Equal(0, observer.Create(100, 200, (_, _) => 0));
        Assert.NotNull(failure);
        if (stateFails)
        {
            Assert.Same(Snapshot, failure.Memory);
            Assert.Null(failure.State.ContentScaleFactor);
            Assert.NotNull(failure.State.Error);
        }
        else
        {
            Assert.Same(State, failure.State);
            Assert.Null(failure.Memory.PrivateBytes);
            Assert.NotNull(failure.Memory.Error);
        }
    }

    [Fact]
    public void LoggingFailureCannotEscapeOrRetryTheNativeCall()
    {
        var calls = 0;
        var observer = new RenderTextureFailureObserver(() => State, () => Snapshot,
            _ => throw new IOException("disk full"));
        Assert.Equal(0, observer.Create(100, 200, (_, _) => { calls++; return 0; }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void AnOriginalExceptionIsNotTurnedIntoANullTextureOrRetried()
    {
        var calls = 0;
        var observer = new RenderTextureFailureObserver(() => throw new Exception("capture"),
            () => throw new Exception("capture"), _ => throw new Exception("log"));
        var error = new InvalidOperationException("native wrapper");
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => observer.Create(1, 2,
            (_, _) => { calls++; throw error; })));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ExistingSingletonScalarsAreReadWithoutCallingOrCreatingNativeObjects()
    {
        var memory = new Memory();
        memory.Word(0x10000000 + RenderTextureStateCapture.DirectorPointerRva, 0x20000000);
        memory.Word(0x10000000 + RenderTextureStateCapture.ConfigurationPointerRva, 0x30000000);
        memory.Word(0x20000000 + RenderTextureStateCapture.DirectorScaleOffset, 0x3FC00000);
        memory.Byte(0x30000000 + RenderTextureStateCapture.ConfigurationNpotOffset, 1);
        var state = RenderTextureStateCapture.Capture(memory, 0x10000000);
        Assert.Equal(1.5f, state.ContentScaleFactor);
        Assert.Equal(0x3FC00000u, state.ContentScaleBits);
        Assert.True(state.SupportsNpot);
        Assert.Null(state.Error);
    }

    [Fact]
    public void AnAbsentDirectorDoesNotEraseAnIndependentlyReadableNpotFlag()
    {
        var memory = new Memory();
        memory.Word(0x10000000 + RenderTextureStateCapture.DirectorPointerRva, 0);
        memory.Word(0x10000000 + RenderTextureStateCapture.ConfigurationPointerRva, 0x30000000);
        memory.Byte(0x30000000 + RenderTextureStateCapture.ConfigurationNpotOffset, 0);
        var state = RenderTextureStateCapture.Capture(memory, 0x10000000);
        Assert.Null(state.ContentScaleFactor);
        Assert.False(state.SupportsNpot);
        Assert.NotNull(state.Error);
        Assert.DoesNotContain((nuint)RenderTextureStateCapture.DirectorScaleOffset, memory.Reads);
    }

    [Fact]
    public void ReadErrorsAndAddressOverflowRemainUnknown()
    {
        var state = RenderTextureStateCapture.Capture(new Memory(), 0xFFFFF000);
        Assert.Null(state.ContentScaleFactor);
        Assert.Null(state.SupportsNpot);
        Assert.NotNull(state.Error);
        state = RenderTextureStateCapture.Capture(new ThrowingMemory(), 0x10000000);
        Assert.Null(state.ContentScaleFactor);
        Assert.NotNull(state.Error);
    }

    [Theory]
    [InlineData(true, 1824u, 1200u, 8755200ul)]
    [InlineData(false, 2048u, 2048u, 16777216ul)]
    public void EstimateUsesNativeFloatScalingTruncationAndOptionalPowerOfTwoPadding(bool npot,
        uint pixelWidth, uint pixelHeight, ulong bytes)
    {
        var result = RenderTextureStateCapture.Estimate(1216, 800, State with { SupportsNpot = npot });
        Assert.Equal(pixelWidth, result.PixelWidth);
        Assert.Equal(pixelHeight, result.PixelHeight);
        Assert.Equal(bytes, result.RgbaBytes);
        Assert.Equal((uint)bytes, result.NativeByteCount32);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-1f)]
    [InlineData(0f)]
    [InlineData(float.MaxValue)]
    public void InvalidOrUnrepresentableScaleDoesNotFabricateAnAllocation(float scale)
    {
        var result = RenderTextureStateCapture.Estimate(1216, 800, State with { ContentScaleFactor = scale });
        Assert.Null(result.RgbaBytes);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void MissingNpotProofKeepsTheAllocationUnknown()
    {
        var result = RenderTextureStateCapture.Estimate(1216, 800, State with { SupportsNpot = null });
        Assert.Null(result.RgbaBytes);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void OversizedByteCountShowsBothWideEstimateAndNative32BitWrap()
    {
        var result = RenderTextureStateCapture.Estimate(65536, 65536, new(1f, 0x3F800000, true, null));
        Assert.Equal(17179869184ul, result.RgbaBytes);
        Assert.Equal(0u, result.NativeByteCount32);
        Assert.NotNull(result.Error);
    }

    private sealed class ThrowingMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination) => throw new IOException("unreadable");
    }

    private sealed class Memory : IReadableMemory
    {
        private readonly Dictionary<nuint, byte> data = [];
        public List<nuint> Reads { get; } = [];
        public void Word(nuint address, uint value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            for (var i = 0; i < bytes.Length; i++) data[address + (nuint)i] = bytes[i];
        }
        public void Byte(nuint address, byte value) => data[address] = value;
        public bool TryRead(nuint address, Span<byte> destination)
        {
            Reads.Add(address);
            for (var i = 0; i < destination.Length; i++)
                if (!data.TryGetValue(address + (nuint)i, out destination[i])) return false;
            return true;
        }
    }
}
