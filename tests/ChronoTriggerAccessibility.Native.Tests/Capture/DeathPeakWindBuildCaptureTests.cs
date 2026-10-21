using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

/// <summary>Atel_0052 actor1 loops the Death Peak wind (installed file 0x020A..0x0246; runtime
/// PC = file offset - 1). 16EB10 keeps a waiting actor's PC on its AD. The tester log
/// (2026-10-20 20.12.57) shows actor1 at 0209 (calm), 0228 (build-up) and 0231 (gust).</summary>
public sealed class DeathPeakWindBuildCaptureTests
{
    [Theory]
    [InlineData(0x020B)] [InlineData(0x0210)] [InlineData(0x0212)] [InlineData(0x0217)]
    [InlineData(0x0219)] [InlineData(0x021C)] [InlineData(0x0221)] [InlineData(0x0223)]
    [InlineData(0x0228)] [InlineData(0x022A)] [InlineData(0x022F)]
    public void TheRampBeforeTheGustFlagIsABuildUp(int pc)
    {
        var floor = Capture(new WindMemory(pc, gust: 0));
        Assert.True(floor.WindBuilding);
        Assert.False(floor.WindGusting);
    }

    [Theory]
    [InlineData(0x0231)] [InlineData(0x0233)] [InlineData(0x0238)]
    public void TheGustItselfIsNotABuildUp(int pc)
    {
        var floor = Capture(new WindMemory(pc, gust: 1));
        Assert.False(floor.WindBuilding);
        Assert.True(floor.WindGusting);
    }

    [Theory]
    [InlineData(0x0209)]   // AD 40, the calm wait
    [InlineData(0x023A)] [InlineData(0x023C)] [InlineData(0x0241)] [InlineData(0x0244)]   // easing after 77 0C
    public void CalmAndEasingAreNotABuildUp(int pc)
    {
        var floor = Capture(new WindMemory(pc, gust: 0));
        Assert.False(floor.WindBuilding);
        Assert.False(floor.WindGusting);
    }

    [Theory]
    [InlineData(0x0228, 1)]   // a ramp PC while the gust flag is set
    [InlineData(0x0209, 1)]
    [InlineData(0x0231, 0)]   // the gust wait without the flag
    public void APhaseThatDisagreesWithTheGustFlagIsUnknown(int pc, int gust)
    {
        var floor = Capture(new WindMemory(pc, gust));
        Assert.Null(floor.WindBuilding);
        Assert.Equal(gust == 1, floor.WindGusting);
    }

    [Theory]
    [InlineData(0x0208)]   // fn0's return, before the loop starts
    [InlineData(0x0246)]   // fn1
    [InlineData(0x020A)]   // inside AD 40, not an instruction
    [InlineData(0x0300)]
    public void AnyOtherPcIsUnknownAndKeepsTheGustFlag(int pc)
    {
        var floor = Capture(new WindMemory(pc, gust: 0));
        Assert.Null(floor.WindBuilding);
        Assert.False(floor.WindGusting);
    }

    [Fact]
    public void ADifferentScriptIsUnknown()
    {
        var memory = new WindMemory(0x0228, gust: 0);
        memory.ScriptByte(0x0229, 0xAE);   // the AD 08 operand byte of the installed loop
        Assert.Null(Capture(memory).WindBuilding);
    }

    [Theory]
    [InlineData(0x87)]   // removed
    [InlineData(4)]      // not the invisible controller
    public void AnotherActor1IsUnknown(int classTag)
    {
        var memory = new WindMemory(0x0228, gust: 0);
        memory.SetActor1(0x40, classTag);
        Assert.Null(Capture(memory).WindBuilding);
    }

    [Fact]
    public void AStoppedWindLoopIsUnknown()
    {
        var memory = new WindMemory(0x0228, gust: 0);
        memory.SetActor1(0x30, 0x80);
        Assert.Null(Capture(memory).WindBuilding);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AnUnreadablePcOrScriptIsUnknown(bool refusePc, bool refuseScript)
    {
        var memory = new WindMemory(0x0228, gust: 0) { RefusePc = refusePc, RefuseScript = refuseScript };
        var floor = Capture(memory);
        Assert.Null(floor.WindBuilding);
        Assert.False(floor.WindGusting);
    }

    [Theory]
    [InlineData(0x0228, 0x022A)]   // still ramping, but the sample moved
    [InlineData(0x022F, 0x0231)]   // the gust began during the read
    public void APcThatChangesDuringTheReadIsUnknown(int first, int second)
    {
        var memory = new WindMemory(first, gust: 0) { PcAfterFirstRead = second };
        Assert.Null(Capture(memory).WindBuilding);
    }

    [Fact]
    public void OtherScenesReportNoWind()
    {
        var memory = new WindMemory(0x0228, gust: 0);
        memory.Scene(245);
        var floor = Capture(memory);
        Assert.Null(floor.WindBuilding);
        Assert.Null(floor.WindGusting);
    }

    [Fact]
    public void TheExistingControlGateStillRejectsTheWholeSample()
    {
        var memory = new WindMemory(0x0228, gust: 0);
        memory.Control(0);
        Assert.Null(FieldFloorCapture.Capture(memory, 0x1000));
    }

    private static FieldFloorSnapshot Capture(WindMemory memory) =>
        FieldFloorCapture.Capture(memory, 0x1000) ?? throw new Xunit.Sdk.XunitException("floor capture failed");

    private sealed class WindMemory : IReadableMemory
    {
        // Installed Atel_0052.dat (SHA-256 9162AF03...) bytes 0x020A..0x0246.
        private static readonly byte[] Loop = Convert.FromHexString(
            "AD40E600F80400AD04E600FC0400AD04EB08FFE600F80400AD08E600E00400AD08E600A00400750CAD3C" +
            "E600C00400770CAD04E600E00400EB082011" + "3C");
        private const int Engine = 0x1000, Actors = 0x2000, State = 0x11000, Renderer = 0x13000,
            Motion = 0x14000, Script = 0x15000, Leader = 6;
        private const int Actor1 = Actors + 0x6940 + 0x154, Wind = Actors + 0x118B0 + 12 * 8;
        private readonly byte[] bytes = new byte[0x30000];
        public bool RefusePc { get; init; }
        public bool RefuseScript { get; init; }
        public int? PcAfterFirstRead { get; set; }

        public WindMemory(int pc, int gust)
        {
            Write(Engine, Script); Write(Engine + 0x40, Actors); Write(Engine + 0x850, State);
            Write(Engine + 0x854, Motion); Write(Engine + 0xB9C, Renderer);
            bytes[Script + 0x12000] = 11;
            Loop.CopyTo(bytes, Script + 0x12001 + 0x0209);
            Write(State + 0x108C, 1); Write(State + 0x10E0, 0); Write(State + 0x11EC, Leader * 2);
            Write(State + 0x1010, 244); Write(Renderer + 0x2A0, 244);
            var leader = Actors + 0x6940 + Leader * 0x154;
            Write(leader + 0x40, 0); Write(leader + 0xD0, 1); Write(leader + 0x60, 1);
            Write(leader + 0x84, 14080); Write(leader + 0x80, 55); Write(leader + 0x7C, 0);
            Write(leader + 0x90, 15103); Write(leader + 0x8C, 58); Write(leader + 0x88, 255);
            Write(Motion + 0x88, 0); Write(Motion + 0x8C, 8); Write(Motion + 0x138, 5);
            Write(Actors + 0x13FD0, 0); Write(Actors + 0x13FD4, 1);
            Write(Actor1 + 0x40, 7); Write(Actor1 + 0xD0, 1); Write(Actor1 + 0x30, 0); Write(Actor1 + 0x48, pc);
            Write(Wind, gust);
        }

        public void ScriptByte(int pc, byte value) => bytes[Script + 0x12001 + pc] = value;
        public void SetActor1(int offset, int value) => Write(Actor1 + offset, value);
        public void Scene(int scene) { Write(State + 0x1010, scene); Write(Renderer + 0x2A0, scene); }
        public void Control(int value) => Write(State + 0x108C, value);

        private void Write(int address, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(address, 4), value);

        public bool TryRead(nuint address, Span<byte> destination)
        {
            var start = (long)address;
            var end = start + destination.Length;
            if (RefusePc && start <= Actor1 + 0x48 && end > Actor1 + 0x48) return false;
            if (RefuseScript && start < Script + 0x12001 + 0x0246 && end > Script + 0x12001 + 0x0209) return false;
            if (end > bytes.Length) return false;
            bytes.AsSpan((int)start, destination.Length).CopyTo(destination);
            if (PcAfterFirstRead is { } next && start == Actor1 + 0x48)
            {
                PcAfterFirstRead = null;
                Write(Actor1 + 0x48, next);
                if (next == 0x0231) Write(Wind, 1);
            }
            return true;
        }
    }
}
