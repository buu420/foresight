using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class FieldEnvironmentCaptureTests
{
    private static readonly FieldNavigationSnapshot Field = new(0x1000, 0x4000, 0x2000, 0x20000, 1, 1, true, 1, 0, 0, 0, null, []);

    [Fact]
    public void CameraBoundsFollowNativeEightPixelScrollPhases()
    {
        var m = new Memory();
        m.Word(0x1854, 0x1727C);
        m.Word(0x1728C, 2); m.Word(0x17290, 33);
        m.Word(0x17294, 4); m.Word(0x17298, 31);
        m.Word(0x17424, 3); m.Word(0x17430, 14);
        Assert.True(FieldEnvironmentCapture.TryViewport(m, Field, out var view));
        Assert.Equal(new(19 * 16, 38 * 16, 275 * 16, 262 * 16), view);
        Assert.True(view.Contains(view.Left, view.Top));
        Assert.False(view.Contains(view.Right, view.Top));
        m.Word(0x17290, 1);
        Assert.False(FieldEnvironmentCapture.TryViewport(m, Field, out _));
    }

    [Fact]
    public void TreasuresRequireClosedRenderedChestTilesAndNeverExposeHiddenPickups()
    {
        var m = new Memory();
        m.Word(0x1e44, 0x16000); m.Word(0x1e48, 0x16004);
        m.Word(0x1e50, 2); m.Word(0x1e54, 2);
        Array.Fill(m.Bytes, (byte)128, 0x16000, 4); m.Bytes[0x16003] = 0;
        m.Word(0x4190, 8); m.Word(0x4194, 9);
        m.Word(0x14fd0, 0x16100); m.Word(0x14fdc, 2); m.Bytes[0x16103] = 0xFE;
        var map = new FieldMapSnapshot(2, 2, [0,0,0,1], new byte[4], [1,1,1,1], 1, false, 2, 2, [128,128,128,128]);
        Assert.True(FieldEnvironmentCapture.TryTreasures(m, Field, map, out var chests));
        Assert.Equal(new(8, 384, 384), Assert.Single(chests));
        m.Word(0x150b8, 1);
        Assert.True(FieldEnvironmentCapture.TryTreasures(m, Field, map, out chests)); Assert.Empty(chests);
        m.Word(0x150b8, 0); map.CollisionShapes[3] = 0;
        Assert.True(FieldEnvironmentCapture.TryTreasures(m, Field, map, out chests)); Assert.Empty(chests);
    }

    private sealed class Memory : IReadableMemory
    {
        public byte[] Bytes { get; } = new byte[0x60000];
        public void Word(int address, int value) => BinaryPrimitives.WriteInt32LittleEndian(Bytes.AsSpan(address, 4), value);
        public bool TryRead(nuint address, Span<byte> destination)
        {
            if ((ulong)address + (uint)destination.Length > (ulong)Bytes.Length) return false;
            Bytes.AsSpan((int)address, destination.Length).CopyTo(destination); return true;
        }
    }
}
