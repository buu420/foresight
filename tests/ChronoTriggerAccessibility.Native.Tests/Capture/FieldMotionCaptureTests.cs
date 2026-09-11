using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class FieldMotionCaptureTests
{
    [Fact]
    public void ReadsOnlyTheControlledLeadAndPreservesNativeUnits()
    {
        var memory = new MotionMemory();
        var frame = Assert.IsType<FieldMotionSnapshot>(FieldMotionCapture.Capture(memory, 0x1000));
        Assert.Equal(2, frame.Scene);
        Assert.Equal(1, frame.Actor);
        Assert.Equal(6308, frame.FineX);
        Assert.Equal(2303, frame.FineY);
        Assert.Equal(1, memory.ActorReads);
    }

    [Theory]
    [InlineData(0x1208C, 0)] // no player control
    [InlineData(0x120E0, 1)] // script/menu input mode
    [InlineData(0x132A0, 3)] // renderer has another scene
    [InlineData(0x121EC, 0x80)] // no leader
    [InlineData(0x121EC, 3)] // invalid actor encoding
    [InlineData(0x121EC, 6)] // beyond packet actor count
    public void UnsafeControlOrIdentityIsUnavailable(int address, int value)
    {
        var memory = new MotionMemory();
        memory.Write(address, value);
        Assert.Null(FieldMotionCapture.Capture(memory, 0x1000));
    }

    [Theory]
    [InlineData(0x40, 4)] // not a player character
    [InlineData(0xD0, 0)] // hidden
    [InlineData(0x84, 6309)] // fixed point triple disagrees
    [InlineData(0x60, 5)] // invalid facing
    public void InvalidActorBodyIsUnavailable(int offset, int value)
    {
        var memory = new MotionMemory();
        memory.Write(MotionMemory.ActorAddress + offset, value);
        Assert.Null(FieldMotionCapture.Capture(memory, 0x1000));
    }

    [Fact]
    public void RechecksControlAfterReadingActor()
    {
        var memory = new MotionMemory { RemoveControlDuringRead = true };
        Assert.Null(FieldMotionCapture.Capture(memory, 0x1000));
    }

    private sealed class MotionMemory : IReadableMemory
    {
        public const int ActorAddress = 0x2000 + 0x6940 + 0x154;
        private readonly byte[] bytes = new byte[0x30000];
        public int ActorReads { get; private set; }
        public bool RemoveControlDuringRead { get; init; }
        public MotionMemory()
        {
            Write(0x1000, 0x15000); Write(0x1040, 0x2000);
            Write(0x1850, 0x11000); Write(0x1B9C, 0x13000);
            bytes[0x27000] = 3;
            Write(0x1208C, 1); Write(0x120E0, 0); Write(0x121EC, 2);
            Write(0x12010, 2); Write(0x132A0, 2);
            Write(ActorAddress + 0x40, 2); Write(ActorAddress + 0xD0, 1);
            Write(ActorAddress + 0x84, 6308); Write(ActorAddress + 0x80, 6308 >> 8);
            Write(ActorAddress + 0x7C, 6308 & 255);
            Write(ActorAddress + 0x90, 2303); Write(ActorAddress + 0x8C, 2303 >> 8);
            Write(ActorAddress + 0x88, 2303 & 255);
        }
        public void Write(int address, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(address, 4), value);
        public bool TryRead(nuint address, Span<byte> destination)
        {
            if (address + (nuint)destination.Length > (nuint)bytes.Length) return false;
            bytes.AsSpan((int)address, destination.Length).CopyTo(destination);
            if (destination.Length == 0x154)
            {
                Assert.Equal((nuint)ActorAddress, address);
                ActorReads++;
                if (RemoveControlDuringRead) Write(0x1208C, 0);
            }
            return true;
        }
    }
}
