using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class WorldNavigationCaptureTests
{
    private const nuint Image = 0x400000, Context = 0x100000, Data = 0x200000,
        Renderer = 0x300000, Actors = 0x500000, Lead = 0x400;

    [Fact]
    public void WorldMotionUsesItsOwnContextAndPixelCoordinates()
    {
        var motion = WorldNavigationCapture.Motion(Memory(), Image, Context, out var stage);
        Assert.NotNull(motion);
        Assert.Equal("ready", stage);
        Assert.Equal(0, motion.World);
        Assert.Equal(400, motion.PixelX);
        Assert.Equal(304, motion.PixelY);
        Assert.Equal(0, motion.Actor);
    }

    [Theory]
    [InlineData(0x20980, 0x80)]
    [InlineData(0x2E27C, 3)]
    [InlineData(0x2E27E, 1)]
    [InlineData(0x2E280, 1)]
    public void UnavailablePlayerControlNeverSuppliesMotion(int offset, int value)
    {
        var memory = Memory().Byte(Data + (nuint)offset, (byte)value);
        Assert.Null(WorldNavigationCapture.Motion(memory, Image, Context, out _));
    }

    [Fact]
    public void StaleWorldRendererOrSharedDataCannotBeReadAsActiveWorld()
    {
        var memory = Memory().Word(Renderer + 0x290, 1);
        Assert.Null(WorldNavigationCapture.Motion(memory, Image, Context, out _));
        memory.Word(Renderer + 0x290, 0).Word(Image + 0x41B4BC, 0x210000);
        Assert.Null(WorldNavigationCapture.Motion(memory, Image, Context, out _));
    }

    [Fact]
    public void ATransitionDuringTheReadInvalidatesTheWholeSample()
    {
        var memory = Memory();
        memory.BeforeRead = address =>
        {
            if (address != Data + 0x2E283) return;
            memory.Word(Renderer + 0x290, 1);
        };
        Assert.Null(WorldNavigationCapture.Motion(memory, Image, Context, out _));
    }

    [Fact]
    public void TransientScriptActorScratchDoesNotReplaceTheLiveWorldPlayer()
    {
        var memory = Memory().Short(Data + 0x2E04E, 4016);
        var motion = WorldNavigationCapture.Motion(memory, Image, Context, out _);
        Assert.NotNull(motion);
        Assert.Equal(400, motion.PixelX);
        Assert.Equal(304, motion.PixelY);
    }

    [Fact]
    public void FullCaptureUsesNativeEntranceContactsAndLiveViewport()
    {
        var snapshot = WorldNavigationCapture.Capture(FullMemory(), Image, Context, out var stage);
        Assert.NotNull(snapshot);
        Assert.Equal("ready", stage);
        Assert.Equal(106, snapshot.EraMessageIndex);
        Assert.Equal(3, snapshot.StoryPoint);
        var entrance = Assert.Single(snapshot.Entrances);
        Assert.True(entrance.Available);
        Assert.Equal(12, entrance.Destination);
        Assert.Equal([new(400, 304), new(408, 304), new(400, 312), new WorldPixelPoint(408, 312)], entrance.ContactPoints);
        Assert.True(snapshot.Viewport.Contains(400 * 16, 304 * 16));
        Assert.False(snapshot.Viewport.Contains(800 * 16, 304 * 16));
        Assert.InRange(snapshot.Viewport.Left, 3880, 3900);
        Assert.InRange(snapshot.Viewport.Right, 8890, 8920);
    }

    [Fact]
    public void CameraWrapKeepsOnlyTheVisibleCopiesOfEntrances()
    {
        var snapshot = WorldNavigationCapture.Capture(FullMemory().Word(Renderer + 0x2606C, 0), Image, Context, out _);
        Assert.NotNull(snapshot);
        Assert.True(snapshot.IsVisible(1530 * 16, 304 * 16));
        Assert.False(snapshot.IsVisible(800 * 16, 304 * 16));
    }

    [Theory]
    [InlineData(0, 603, 205, 603, 205)]
    [InlineData(1, 603, 205, 603, 205)]
    [InlineData(2, 607, 205, 611, 205)]
    public void WalkingPhaseUsesTheLivePlayerTaskAndItsCommittedEndpoint(int state, int x, int y, int endX, int endY)
    {
        const nuint actor = 0xB30;
        var memory = FullMemory().Short(Data + 0x2E04E, (ushort)actor)
            .Short(Data + 0x2E000 + actor, 0x3404).Byte(Data + 0x2E002 + actor, (byte)state)
            .Short(Data + 0x2E014 + actor, (ushort)x).Short(Data + 0x2E018 + actor, (ushort)y)
            .Short(Data + 0x2E02A + actor, 611).Short(Data + 0x2E02C + actor, 205)
            .Short(Data + 0x2E283, (ushort)x).Short(Data + 0x2E285, (ushort)y);
        Assert.Equal(new WorldPixelPoint(endX, endY), WorldNavigationCapture.Capture(memory, Image, Context, out _)?.WalkingEndpoint);
        memory.Short(Data + 0x2E000 + actor, 0x42DD);
        Assert.Null(WorldNavigationCapture.Capture(memory, Image, Context, out _)?.WalkingEndpoint);
        Assert.Equal(x, WorldNavigationCapture.Motion(memory, Image, Context, out _)?.PixelX);
    }

    [Theory]
    [InlineData(0, 3, 106)]
    [InlineData(1, 12, 111)]
    [InlineData(2, 53, 111)]
    [InlineData(2, 54, 108)]
    public void EraNamesFollowTheGamesOwnKnowledgeGate(int world, int progress, int expected)
    {
        var memory = FullMemory().Word(Context + 0x3324, (uint)world).Word(Renderer + 0x290, (uint)world)
            .Short(Data + 0x2E100, (ushort)(496 + world)).Word(Actors + 0x110B0, (uint)progress);
        Assert.Equal(expected, WorldNavigationCapture.Capture(memory, Image, Context, out _)?.EraMessageIndex);
        if (world == 1)
        {
            memory.Byte(Data + 0x2FBAD, 1);
            Assert.Equal(107, WorldNavigationCapture.Capture(memory, Image, Context, out _)?.EraMessageIndex);
        }
    }

    [Fact]
    public void TornExitListOrHiddenParentCannotPublishTargets()
    {
        var memory = FullMemory();
        memory.BeforeRead = address => { if (address == Data + 0x25E00) memory.Byte(Data + 0x2FB38, 2); };
        Assert.Null(WorldNavigationCapture.Capture(memory, Image, Context, out _));
        memory = FullMemory().Byte(Renderer + 0x1AD, 0);
        Assert.Null(WorldNavigationCapture.Capture(memory, Image, Context, out _));
    }

    private static NavigationMemory FullMemory()
    {
        const nuint dll = 0x10000000, director = 0x800000, view = 0x810000, vtable = 0x820000,
            node = 0x830000, children = 0x840000;
        var memory = Memory().Add(Data + 0x23800, new byte[6144]).Add(Data + 0x25000, new byte[512])
            .Byte(Data + 0x2FB38, 1).Add(Data + 0x25E00, [153, 19, 6, 12, 0, 0, 52, 28])
            .Word(Actors + 0x110B0, 3).Byte(Data + 0x2FBAD, 0)
            .Word(Image + 0x385BBC, (uint)dll + 0x19529E).Word(Image + 0x385B40, (uint)dll + 0x195375)
            .Add(dll + 0x195375, [0x55, 0x8B, 0xEC, 0x8B, 0x49, 0x78, 0x85, 0xC9])
            .Add(dll + 0x24C138, [0x55, 0x8B, 0xEC, 0x83, 0x79, 0x58, 0x01])
            .Word(dll + 0x87D47C, (uint)director).Word(director + 0x78, (uint)view)
            .Word(view, (uint)vtable).Word(vtable + 0x44, (uint)dll + 0x24C138).Word(view + 0x58, 1)
            .Word(Renderer + 0x160, (uint)children).Word(Renderer + 0x164, (uint)children + 4).Word(children, (uint)node);
        Float(view + 0x18, 3834); Float(view + 0x1C, 2087); Float(view + 0x20, 587.8677368f); Float(view + 0x24, 320);
        Float(view + 0x50, 6.52187538f); Float(view + 0x54, 6.52187538f);
        memory.Add(node, new byte[0x1B0]).String(node + 0x178, "worldmap").Word(node + 0x16C, (uint)Renderer).Byte(node + 0x1AD, 1);
        // Scene anchors cancel because ignoreAnchorPointForPosition is enabled.
        memory.Add(Renderer + 0x20, new byte[0x140]).Word(Renderer + 0x16C, 0).Byte(Renderer + 0x1AD, 1).Byte(Renderer + 0x1AE, 1);
        Float(node + 0x38, 1.875f); Float(node + 0x3C, 1.6666666269f); Float(node + 0x44, 53.9338684f);
        Float(Renderer + 0x38, 1); Float(Renderer + 0x3C, 1); Float(Renderer + 0x64, 293.9338684f); Float(Renderer + 0x68, 160);
        Float(Renderer + 0x2606C, -272); Float(Renderer + 0x26070, 384);
        return memory;
        void Float(nuint address, float value) => memory.Word(address, BitConverter.SingleToUInt32Bits(value));
    }

    private static NavigationMemory Memory() => new NavigationMemory()
        .Word(Image + 0x41B4BC, (uint)Data).Word(Image + 0x41B4C4, (uint)Actors)
        .Word(Context, (uint)Data).Word(Context + 0x40, (uint)Actors)
        .Word(Context + 0x1E68, (uint)Renderer).Word(Context + 0x3324, 0).Word(Renderer + 0x290, 0)
        .Short(Data + 0x2E100, 496).Short(Data + 0x2E04E, (ushort)Lead)
        .Byte(Data + 0x20980, 0).Byte(Data + 0x2E27C, 1).Byte(Data + 0x2E27E, 0).Byte(Data + 0x2E280, 0)
        .Byte(Data + 0x2E000 + Lead + 2, 1)
        .Short(Data + 0x2E000 + Lead + 0x14, 400).Short(Data + 0x2E000 + Lead + 0x18, 304)
        .Short(Data + 0x2E283, 400).Short(Data + 0x2E285, 304);
}
