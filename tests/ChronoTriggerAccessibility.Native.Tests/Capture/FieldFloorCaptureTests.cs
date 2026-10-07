using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class FieldFloorCaptureTests
{
    [Theory]
    [InlineData(0x0E, -32, 0)]
    [InlineData(0x0B, 16, 0)]
    [InlineData(0x08, 0, -16)]
    [InlineData(0x05, 0, 8)]
    [InlineData(0, 0, 0)]
    public void ReadsTheNativeForceAndDashConfiguration(int flags, int x, int y)
    {
        var memory = new FloorMemory(flags, x, y);
        var floor = Assert.IsType<FieldFloorSnapshot>(FieldFloorCapture.Capture(memory, 0x1000));
        Assert.Equal(231, floor.Scene);
        Assert.Equal(x, floor.ForceX);
        Assert.Equal(y, floor.ForceY);
        Assert.Equal(1, floor.RunMode);
        Assert.Equal(2, floor.RunToggle);
    }

    [Theory]
    [InlineData(0x138, 0x0B)] // flags disagree with cached force
    [InlineData(0x88, 33)] // impossible force magnitude
    [InlineData(0x8C, 16)] // diagonal floor force
    public void InvalidOrInconsistentForceIsUnavailable(int offset, int value)
    {
        var memory = new FloorMemory(0x0E, -32, 0);
        memory.Write(0x14000 + offset, value);
        Assert.Null(FieldFloorCapture.Capture(memory, 0x1000));
    }

    [Fact]
    public void LosingControlDuringTheFloorReadRejectsTheSnapshot()
    {
        var memory = new FloorMemory(0x0B, 16, 0) { LoseControl = true };
        Assert.Null(FieldFloorCapture.Capture(memory, 0x1000));
    }

    [Fact]
    public void UnreadableDashConfigurationDoesNotHideTheFloor()
    {
        var memory = new FloorMemory(0x0B, 16, 0) { RefuseRun = true };
        var floor = Assert.IsType<FieldFloorSnapshot>(FieldFloorCapture.Capture(memory, 0x1000));
        Assert.Equal(16, floor.ForceX);
        Assert.Null(floor.RunMode);
        Assert.Null(floor.RunToggle);
    }

    private sealed class FloorMemory : IReadableMemory
    {
        private readonly byte[] bytes = new byte[0x30000];
        public bool LoseControl { get; init; }
        public bool RefuseRun { get; init; }
        public FloorMemory(int flags, int x, int y)
        {
            Write(0x1000, 0x15000); Write(0x1040, 0x2000);
            Write(0x1850, 0x11000); Write(0x1854, 0x14000); Write(0x1B9C, 0x13000);
            bytes[0x27000] = 3;
            Write(0x1208C, 1); Write(0x120E0, 0); Write(0x121EC, 2);
            Write(0x12010, 231); Write(0x132A0, 231);
            const int actor = 0x2000 + 0x6940 + 0x154;
            Write(actor + 0x40, 0); Write(actor + 0xD0, 1); Write(actor + 0x60, 1);
            Write(actor + 0x84, 1153); Write(actor + 0x80, 4); Write(actor + 0x7C, 129);
            Write(actor + 0x90, 7154); Write(actor + 0x8C, 27); Write(actor + 0x88, 242);
            Write(0x14088, x); Write(0x1408C, y); Write(0x14138, flags);
            Write(0x15FD0, 1); Write(0x15FD4, 2);
        }
        public void Write(int address, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(address, 4), value);
        public bool TryRead(nuint address, Span<byte> destination)
        {
            if (address + (nuint)destination.Length > (nuint)bytes.Length ||
                RefuseRun && address <= 0x15FD4 && address + (nuint)destination.Length > 0x15FD0) return false;
            bytes.AsSpan((int)address, destination.Length).CopyTo(destination);
            if (LoseControl && address >= 0x14088 && address < 0x14140) Write(0x1208C, 0);
            return true;
        }
    }
}
