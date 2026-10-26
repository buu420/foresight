using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class WorldSparkleCaptureTests
{
    private const nuint Data = 0x200000, Table = Data + 0x2EB30, Guard = Data + 0x11341;
    // Event_0000.dat 0F41..0F8B, re-read from the supported resources.bin.
    private const string Script = "010602B00A100A1130892C50015003097A137F38142C54015403097A137F38142C4C014C03097A137F38142C4C015403097A137F38141A4A130D2B24390C2E000000002F0080FFFF313052";
    private static readonly WorldMotionSnapshot Motion = new(1, (uint)Data, 3, 4, 0, 0, 336, 848);

    [Theory]
    [InlineData(0x137D, 0xB6, 336, 848)]
    [InlineData(0x1389, 0xF7, 332, 820)]
    public void ExactLiveSpawnerAndDrawnChildrenSupplyTheirNativePositions(int pc, int attr, int x, int y)
    {
        var memory = Memory().Short(Table + 5 * 0x40 + 7, (ushort)pc)
            .Byte(Table + 5 * 0x40 + 0xF, (byte)attr)
            .Short(Table + 5 * 0x40 + 0x14, (ushort)x).Short(Table + 5 * 0x40 + 0x18, (ushort)y);
        Assert.Equal([new WorldPixelPoint(x, y)], WorldSparkleCapture.Capture(memory, Motion, out var stage));
        Assert.Equal("drawn children", stage);
    }

    [Theory]
    [InlineData(0, 0)] // freed child
    [InlineData(7, 0x137A)] // created, not drawn
    [InlineData(7, 0x138B)] // unrelated program counter
    [InlineData(9, 2)] // foreign code bank
    [InlineData(0xF, 0xB4)] // wrong palette
    [InlineData(0x14, 333)] // not one of this effect's native origins
    [InlineData(0x18, 819)] // below the audited minimum of the rising particle
    public void ForeignUndrawnOrInvalidChildrenStaySilent(int offset, int value)
    {
        var memory = Memory();
        if (offset is 9 or 0xF) memory.Byte(Table + 5 * 0x40 + (nuint)offset, (byte)value);
        else memory.Short(Table + 5 * 0x40 + (nuint)offset, (ushort)value);
        Assert.Empty(WorldSparkleCapture.Capture(memory, Motion, out var stage));
        Assert.Equal("no drawn child", stage);
    }

    [Fact]
    public void SavedFlagWithoutLiveSpawnerNeverCountsAsPresence()
    {
        var memory = Memory().Short(Table + 4 * 0x40, 0).Byte(Data + 0x2FBA8, 0x10);
        Assert.Empty(WorldSparkleCapture.Capture(memory, Motion, out var stage));
        Assert.Equal("no live spawner", stage);
    }

    [Fact]
    public void WrongWorldMissingScriptOrChangedSignatureNeverIdentifiesTheEffect()
    {
        Assert.Empty(WorldSparkleCapture.Capture(Memory(), Motion with { World = 1 }));
        Assert.Empty(WorldSparkleCapture.Capture(new NavigationMemory(), Motion));
        Assert.Empty(WorldSparkleCapture.Capture(Memory().Byte(Guard, 0), Motion));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TornActorsOrScriptInvalidateTheOptionalEffect(bool changeScript)
    {
        var memory = Memory(); var reads = 0;
        memory.BeforeRead = address =>
        {
            if (address != (changeScript ? Guard : Table) || ++reads != 2) return;
            if (changeScript) memory.Byte(Guard, 0);
            else memory.Short(Table + 5 * 0x40 + 0x18, 830);
        };
        Assert.Empty(WorldSparkleCapture.Capture(memory, Motion));
    }

    [Fact]
    public void MultipleDrawnChildrenAreReadWithoutInventingATargetOrMovingAnActor()
    {
        var memory = Memory();
        Actor(memory, 6, 0x1389, 340, 840);
        Assert.Equal([new WorldPixelPoint(336, 848), new(340, 840)], WorldSparkleCapture.Capture(memory, Motion));
        var before = new byte[0x1000]; var after = new byte[0x1000];
        Assert.True(memory.TryRead(Table, before));
        WorldSparkleCapture.Capture(memory, Motion);
        Assert.True(memory.TryRead(Table, after));
        Assert.Equal(before, after);
    }

    private static NavigationMemory Memory() => WithEffect(new NavigationMemory());

    internal static NavigationMemory WithEffect(NavigationMemory memory)
    {
        memory.Add(Guard, Convert.FromHexString(Script)).Add(Table, new byte[0x1000]);
        Actor(memory, 4, 0x1354, 336, 848);
        Actor(memory, 5, 0x137D, 336, 848);
        return memory;
    }

    private static void Actor(NavigationMemory memory, int slot, int pc, int x, int y)
    {
        var address = Table + (nuint)(slot * 0x40);
        memory.Short(address, 0x0F63).Short(address + 7, (ushort)pc).Byte(address + 9, 1)
            .Byte(address + 0xF, 0xB6).Short(address + 0x14, (ushort)x).Short(address + 0x18, (ushort)y);
    }
}
