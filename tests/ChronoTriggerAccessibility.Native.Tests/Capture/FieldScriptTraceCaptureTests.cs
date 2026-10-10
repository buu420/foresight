using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class FieldScriptTraceCaptureTests
{
    [Fact]
    public void CapturesExecutingAddressBeforeTheOpcodeCanAdvanceIt()
    {
        var memory = CreateMemory();

        Assert.True(FieldScriptTraceCapture.TryCapture(memory, 0x1000, out var snapshot));

        Assert.Equal(0x460u, snapshot.Address);
        Assert.Equal(324u, snapshot.ScriptId);
        Assert.Equal(8, snapshot.Actor);
        Assert.Equal(0u, snapshot.ControlBefore);
        Assert.Equal("E301001122334455", snapshot.Bytes);
        Assert.Equal("236004A204D504D6", snapshot.ScriptPrefix);
        memory.Word(0x1024, 0x462);
        Assert.Equal(0x460u, snapshot.Address);
    }

    [Theory]
    [InlineData(0x11u)]
    [InlineData(0x46u)]
    [InlineData(0xFFFFFFFEu)]
    public void KeepsTheTraceLineButMarksAnOddOrOutOfRangeExecutingActorUnknown(uint raw)
    {
        // field+0x1180 holds actor * 2 below the header count (0x23 actors here); the
        // script id at ctx+0xBB4 must never stand in for it.
        var memory = CreateMemory();
        memory.Word(0x6180, raw);
        Assert.True(FieldScriptTraceCapture.TryCapture(memory, 0x1000, out var snapshot));
        Assert.Equal(-1, snapshot.Actor);
        Assert.Equal(324u, snapshot.ScriptId);
    }

    [Fact]
    public void RejectsAFieldStateTooShortForTheExecutingActorWord()
    {
        var memory = CreateMemory();
        memory.Bytes.Remove(0x6180);
        Assert.False(FieldScriptTraceCapture.TryCapture(memory, 0x1000, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0xFFFFFFF0)]
    public void RejectsNullOrOverflowingContext(uint context)
    {
        Assert.False(FieldScriptTraceCapture.TryCapture(CreateMemory(), context, out _));
    }

    [Fact]
    public void RejectsUnreadableScriptBytesAndInvalidFieldPointers()
    {
        var memory = CreateMemory();
        memory.Word(0x1850, 0);
        Assert.False(FieldScriptTraceCapture.TryCapture(memory, 0x1000, out _));
        memory.Word(0x1850, 0x5000);
        memory.Bytes.Remove(0xB2461);
        Assert.False(FieldScriptTraceCapture.TryCapture(memory, 0x1000, out _));
    }

    [Fact]
    public void PostCaptureRejectsAnOwnerChangeAndReadsOnlyTheSameFieldState()
    {
        var memory = CreateMemory();
        Assert.True(FieldScriptTraceCapture.TryCapture(memory, 0x1000, out var before));
        memory.Word(0x608C, 1);
        Assert.True(FieldScriptTraceCapture.TryReadControlAfter(memory, before, out var control));
        Assert.Equal(1u, control);
        memory.Word(0x1850, 0x6000);
        Assert.False(FieldScriptTraceCapture.TryReadControlAfter(memory, before, out _));
    }

    private static Memory CreateMemory()
    {
        var memory = new Memory();
        memory.Word(0x1000, 0xA0000);
        memory.Word(0x1024, 0x460);
        memory.Word(0x1850, 0x5000);
        memory.Word(0x1BB4, 324);
        memory.Word(0x608C, 0);
        memory.Word(0x6180, 8 * 2);
        memory.Add(0xB2000, Convert.FromHexString("236004A204D504D6"));
        memory.Add(0xB2461, Convert.FromHexString("E301001122334455"));
        return memory;
    }

    private sealed class Memory : IReadableMemory
    {
        public Dictionary<nuint, byte> Bytes { get; } = [];
        public void Word(nuint address, uint value)
        {
            Span<byte> data = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(data, value);
            Add(address, data);
        }
        public void Add(nuint address, ReadOnlySpan<byte> data)
        {
            for (var index = 0; index < data.Length; index++) Bytes[address + (nuint)index] = data[index];
        }
        public bool TryRead(nuint address, Span<byte> destination)
        {
            for (var index = 0; index < destination.Length; index++)
                if (!Bytes.TryGetValue(address + (nuint)index, out destination[index])) return false;
            return true;
        }
    }
}
