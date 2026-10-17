using System.Security.Cryptography;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;
using static ChronoTriggerAccessibility.Native.Tests.Capture.StoryBookActionCaptureTests;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class KeepDoorStoryActionTests
{
    private static readonly Cue Door = new(StorySceneAction.PassageOpens, 165, 246, 14, 0x69E,
        "E50427072B061A3B", 14, 7, 0, 7, 29, 0, 52);

    [Fact]
    public void TheFinalOpeningFrameDescribesOnlyTheNativeQueuedChange()
    {
        var memory = Scene(Door);
        var before = Dispatch(memory, Door);
        Assert.True(StorySceneActionCapture.Watches(before.Location));
        Assert.True(StorySceneActionCapture.TryMatch(memory, before, out var candidate));
        Assert.Equal(StorySceneAction.PassageOpens, candidate.Action);
        Assert.False(StorySceneActionCapture.TryComplete(memory, candidate));
        TileCopy(memory, Door.Pc, Door.Bytes[2..]);
        Assert.True(StorySceneActionCapture.TryComplete(memory, candidate));
    }

    [Theory]
    [InlineData("camera")] [InlineData("script")] [InlineData("marker")]
    public void UnseenOrUnverifiedOpeningIsNotDescribed(string variant)
    {
        var memory = Scene(Door);
        Assert.True(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, Door), out _));
        if (variant == "camera") memory.Word(0x800000 + 0x1327C + 0x10, 80);
        if (variant == "script") memory.Add(0x400000 + 0x12000 + 0x300, [0xFF]);
        if (variant == "marker") memory.Word(0x800000 + 0x6940 + 14 * 0x154 + 0x40, 4);
        Assert.False(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, Door), out _));
    }

    [Fact]
    public void AnotherQueuedRectangleCannotCompleteThisDoorCue()
    {
        var memory = Scene(Door);
        Assert.True(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, Door), out var candidate));
        TileCopy(memory, Door.Pc, "0027032B061A3B");
        Assert.False(StorySceneActionCapture.TryComplete(memory, candidate));
    }

    [Theory]
    [InlineData(0x690, 0xE5)] // Earlier animation frame, raw0691.
    [InlineData(0x39B, 0xE4)] // Reload reapplies open tiles rather than opening the door.
    public void EarlierFramesAndReloadDoNotRepeatTheDescription(uint pc, int opcode)
    {
        var memory = Scene(Door);
        Assert.False(StorySceneActionCapture.Watches(Dispatch(memory, Door with { Actor = opcode == 0xE4 ? 0 : 14, Pc = pc }, opcode).Location));
    }

    [Fact]
    public void InstalledScriptAndFinalFrameMatchTheAuditedBytes()
    {
        var script = InstalledFieldScripts.Atel(246);
        Assert.Equal(2626, script.Length);
        Assert.Equal("45383E9FE6637B86C95D4937BE39EB5E9A395BAE48B651B78D210FD80A86B7D1", Convert.ToHexString(SHA256.HashData(script)));
        Assert.Equal(Door.Bytes, Convert.ToHexString(script, (int)Door.Pc + 1, 8));
    }
}
