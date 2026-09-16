using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class FieldStoryCaptureTests
{
    [Fact]
    public void ReadsTheNativeExpandedGlobalBytesAndMasksTheStoryCounter()
    {
        var memory = Memory();
        var state = FieldStoryCapture.Capture(memory, Field());
        Assert.NotNull(state);
        Assert.Equal(3, state.Point);
        Assert.True(state.MotherIntroducedFriend);
    }

    [Theory]
    [InlineData(0x115B0u)]
    [InlineData(0x110B0u)]
    public void MissingStoryDataDoesNotInventProgress(uint offset)
    {
        var memory = Memory(); memory.Words.Remove(0x4000u + offset);
        Assert.Null(FieldStoryCapture.Capture(memory, Field()));
    }

    [Fact]
    public void RejectsAChangedSceneOrActorBase()
    {
        var memory = Memory(); memory.Words[0x1040] = 0x5000;
        Assert.Null(FieldStoryCapture.Capture(memory, Field()));
        memory = Memory(); memory.Words[0x3010] = 2;
        Assert.Null(FieldStoryCapture.Capture(memory, Field()));
        Assert.Null(FieldStoryCapture.Capture(Memory(), Field() with { SceneIdCoherent = false }));
    }

    [Theory]
    [InlineData(0x54, 0x20)]
    [InlineData(0x55, 0x80)]
    [InlineData(0x56, 2)]
    [InlineData(0x58, 0x60)]
    [InlineData(0x5C, 0x20)]
    [InlineData(0xA4, 0x40)]
    [InlineData(0xEC, 0x40)]
    [InlineData(0xFF, 4)]
    [InlineData(0x190, 4)]
    [InlineData(0x1D0, 1)]
    [InlineData(0x60, 0x20)]
    [InlineData(0xA0, 0x10)]
    [InlineData(0x1E0, 0x80)]
    public void ReadsOnlyStableOptionalFlagsAtExpandedDwordOffsets(int index, int mask)
    {
        var memory = Memory();
        var address = (nuint)(0x150B0 + index * 4);
        memory.Words[address] = 0xAB00 | mask;
        var state = FieldStoryCapture.Capture(memory, Field());
        Assert.NotNull(state);
        Assert.True(state.Flag(index, mask));
        Assert.Equal(mask, state.Global(index));
        Assert.Null(state.Flag(0x123, 1));
        memory.Words.Remove(address);
        Assert.Null(FieldStoryCapture.Capture(memory, Field())!.Flag(index, mask));
        memory.Words[address] = mask;
        memory.MutateAddress = address;
        Assert.Null(FieldStoryCapture.Capture(memory, Field())!.Flag(index, mask));
    }

    [Fact]
    public void CapturesRoomLocalsAndBonusQuestCellsWithTheirDifferentStrides()
    {
        var memory = Memory();
        memory.Words[0x4000 + 0x118B0 + 6 * 8] = 0;
        memory.Words[0x4000 + 0x118B0 + 7 * 8] = 0xAB15;
        memory.Words[0x4000 + 0x6798 + 22 * 4] = 0x1234;
        var state = FieldStoryCapture.Capture(memory, Field())!;
        Assert.Equal(0, state.Local(6));
        Assert.Equal(0x15, state.Local(7));
        Assert.Equal(0x1234, state.Extra(22));
        Assert.Null(state.Local(8));
        Assert.Null(state.Extra(23));
    }

    [Fact]
    public void ChangingOptionalStateIsOmittedWithoutInventingZero()
    {
        var memory = Memory();
        memory.MutateAddress = 0x4000 + 0x6798 + 22 * 4;
        memory.Words[memory.MutateAddress] = 4;
        Assert.Null(FieldStoryCapture.Capture(memory, Field())!.Extra(22));
    }

    private static WordsMemory Memory() => new()
    {
        Words = { [0x1040] = 0x4000, [0x3010] = 1, [0x150B0] = 0xAB03, [0x155B0] = 1 }
    };
    private static FieldNavigationSnapshot Field() => new(0x1000, 0x4000, 0x2000, 0x20000,
        0, 1, true, 1, 0, 0, 0, null, []);
    private sealed class WordsMemory : IReadableMemory
    {
        public Dictionary<nuint, int> Words { get; } = [];
        public nuint MutateAddress { get; set; }
        public bool TryRead(nuint address, Span<byte> destination)
        {
            if (destination.Length != 4 || !Words.TryGetValue(address, out var value)) return false;
            BinaryPrimitives.WriteInt32LittleEndian(destination, value);
            if (address == MutateAddress) Words[address] = value ^ 255;
            return true;
        }
    }
}
