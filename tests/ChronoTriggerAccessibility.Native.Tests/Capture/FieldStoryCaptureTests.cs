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

    private static WordsMemory Memory() => new()
    {
        Words = { [0x1040] = 0x4000, [0x3010] = 1, [0x150B0] = 0xAB03, [0x155B0] = 1 }
    };
    private static FieldNavigationSnapshot Field() => new(0x1000, 0x4000, 0x2000, 0x20000,
        0, 1, true, 1, 0, 0, 0, null, []);
    private sealed class WordsMemory : IReadableMemory
    {
        public Dictionary<nuint, int> Words { get; } = [];
        public bool TryRead(nuint address, Span<byte> destination)
        {
            if (destination.Length != 4 || !Words.TryGetValue(address, out var value)) return false;
            BinaryPrimitives.WriteInt32LittleEndian(destination, value); return true;
        }
    }
}
