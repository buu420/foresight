using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class FieldMapCaptureTests
{
    private static readonly FieldNavigationSnapshot Field = new(0x1000, 0x4000, 0x2000, 0x20000,
        1, 0, true, 1, 0, 0, 0, null, []);

    [Fact]
    public void CapturesLivePlanesLayerAndOnlyActiveExitCells()
    {
        var memory = Valid();
        Assert.True(FieldMapCapture.TryCapture(memory, Field, out var map, out var error), error);
        Assert.Equal(2, map.Width);
        Assert.Equal(2, map.Height);
        Assert.Equal(1, map.PlayerLayer);
        Assert.False(map.TransitionPending);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, map.CollisionShapes);
        Assert.Equal(new byte[] { 1, 1, 1, 1 }, map.CollisionLayers);
        Assert.Equal(new byte[] { 128, 128, 0, 128 }, map.ExitCells);
    }

    [Theory]
    [InlineData("oversize")]
    [InlineData("short")]
    [InlineData("unreadable")]
    [InlineData("different-plane")]
    [InlineData("scene")]
    [InlineData("bad-exit")]
    [InlineData("bad-layer")]
    public void RejectsMalformedOrIncoherentStateWithoutReadingUnboundedMemory(string cause)
    {
        var memory = Valid();
        switch (cause)
        {
            case "oversize": memory.Word(0x15018, 0x7fffffff); break;
            case "short": memory.Word(0x15010, 0x16003); break;
            case "unreadable": memory.Unreadable = 0x16000; break;
            case "different-plane": memory.Word(0x1502c, 1); break;
            case "scene": memory.Word(0x3010, 1); break;
            case "bad-exit": memory.Bytes[0x16302] = 1; break;
            case "bad-layer": memory.Bytes[0x4e155] = 8; break;
        }
        Assert.False(FieldMapCapture.TryCapture(memory, Field, out _, out var error));
        Assert.NotEmpty(error);
        Assert.InRange(memory.LargestRead, 0, 65536);
    }

    [Fact]
    public void ReportsTransitionFlagWithoutTreatingItAsAnError()
    {
        var memory = Valid();
        memory.Word(0x306c, 0x80);
        Assert.True(FieldMapCapture.TryCapture(memory, Field, out var map, out _));
        Assert.True(map.TransitionPending);
    }

    [Fact]
    public void ReadsTheCurrentScenesExpandedDestinationRecords()
    {
        var memory = Valid();
        memory.Word(0x1e5c, 0x16408);
        memory.Word(0x16400, 1); memory.Word(0x16404, 2);
        memory.Word(0x1e68, 0x16538);
        memory.Word(0x16510, 43);
        memory.Word(0x1652c, 0x8000 | 654);
        Assert.True(FieldMapCapture.TryCapture(memory, Field, out var map, out var error), error);
        Assert.Equal(654, Assert.Single(map.ExitDestinations!).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnreadableOrChangingDestinationsDoNotDestroyTheCollisionMap(bool changing)
    {
        var memory = Valid();
        if (changing) memory.ChangeOnSecondRead = 0x16500;
        else memory.Unreadable = 0x16500;
        Assert.True(FieldMapCapture.TryCapture(memory, Field, out var map, out var error), error);
        Assert.Null(map.ExitDestinations);
        Assert.Equal(new byte[] { 128, 128, 0, 128 }, map.ExitCells);
    }

    private static Memory Valid()
    {
        var m = new Memory();
        for (var i = 0; i < 3; i++)
        {
            var address = 0x1500c + 20 * i;
            var data = 0x16000 + i * 256;
            m.Word(address, data); m.Word(address + 4, data + 4); m.Word(address + 8, data + 4);
            m.Word(address + 12, 2); m.Word(address + 16, 2);
        }
        m.Bytes[0x16000] = 1; m.Bytes[0x16001] = 2; m.Bytes[0x16002] = 3; m.Bytes[0x16003] = 4;
        Array.Fill(m.Bytes, (byte)1, 0x16200, 4);
        m.Bytes[0x4e155] = 1;
        m.Word(0x1e58, 0x16400); m.Word(0x1e5c, 0x16404); m.Word(0x16400, 0);
        m.Word(0x1e64, 0x16500); m.Word(0x1e68, 0x1651c);
        m.Word(0x1e70, 0x16300); m.Word(0x1e74, 0x16304); m.Word(0x1e78, 0x16304);
        m.Word(0x1e7c, 2); m.Word(0x1e80, 2);
        Array.Fill(m.Bytes, (byte)128, 0x16300, 4); m.Bytes[0x16302] = 0;
        return m;
    }

    private sealed class Memory : IReadableMemory
    {
        public byte[] Bytes { get; } = new byte[0x60000];
        public nuint Unreadable { get; set; }
        public nuint ChangeOnSecondRead { get; set; }
        private int destinationReads;
        public int LargestRead { get; private set; }
        public void Word(int address, int value) => BinaryPrimitives.WriteInt32LittleEndian(Bytes.AsSpan(address, 4), value);
        public bool TryRead(nuint address, Span<byte> destination)
        {
            LargestRead = Math.Max(LargestRead, destination.Length);
            if (address == Unreadable || (ulong)address + (uint)destination.Length > (ulong)Bytes.Length) return false;
            Bytes.AsSpan((int)address, destination.Length).CopyTo(destination);
            if (address == ChangeOnSecondRead && ++destinationReads == 2) destination[^1] ^= 1;
            return true;
        }
    }
}
