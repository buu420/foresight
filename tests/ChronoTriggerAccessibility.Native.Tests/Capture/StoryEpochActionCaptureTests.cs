using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;
using TestMemory = ChronoTriggerAccessibility.Native.Tests.Capture.StorySceneActionCaptureTests.Memory;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

/// <summary>Atel0163 scene243 and c263 assembly frames, reviewed against the original
/// storage-room sequence. Native proof: epoch-seals-0362/ghidra-root 16CB50/17B1B0/16EA90.
/// Literal action numbers keep the initial regression compilable before adding the actions.</summary>
public sealed class StoryEpochActionCaptureTests
{
    private const uint Context = 0x1000, Data = 0x400000, Actors = 0x800000, Field = 0x5000;
    private const uint Camera = Actors + 0x1327C, Nu = Actors + 0x6940 + 14 * 0x154;

    public static TheoryData<uint, int, string> Cues => new()
    {
        { 0x610, 14, "92C0E000AC0A00AC" }, // walks up to the group (native timed movement)
        { 0x5FF, 15, "AC0CAD08AC04BB29" }, // arms raised and mouth open on waking
        { 0x639, 15, "AC0CAD08AC0A0082" }, // same visible stretch after the demonstration
        { 0x61D, 16, "AC0D7C1A750C120D" }, // settles down before naming
        { 0x608, 16, "AC0DAD087C1A0000" }, // settles back down after speaking
    };

    [Theory]
    [MemberData(nameof(Cues))]
    public void AReviewedNuActionRequiresItsExecutedInstructionAndAppliedNativeResult(uint pc, int action, string bytes)
    {
        var memory = Scene();
        var before = Dispatch(memory, pc);
        Assert.Equal(bytes, before.Bytes);
        Assert.True(StorySceneActionCapture.Watches(before.Location));
        Assert.True(StorySceneActionCapture.TryMatch(memory, before, out var candidate));
        Assert.Equal((StorySceneAction)action, candidate.Action);
        Assert.False(StorySceneActionCapture.TryComplete(memory, candidate));
        Applied(memory, before);
        Assert.True(StorySceneActionCapture.TryComplete(memory, candidate));
    }

    [Theory]
    [MemberData(nameof(Cues))]
    public void ChangedScriptOrUnseenOrDifferentCreatureNeverQualifies(uint pc, int action, string bytes)
    {
        _ = action; _ = bytes;
        foreach (var change in new Action<TestMemory>[]
        {
            m => m.Add(Data + 0x12000 + 0x300, [0xFF]),
            m => m.Word(Nu + 0x40, 4),
            m => m.Word(Nu + 0x44, 1),
            m => m.Word(Nu + 0xD0, 0),
            m => m.Word(Nu + 0x40, 0x85),
            m => m.Word(Nu + 0x84, 0x4000),
            m => m.Word(Actors + 0x110B0, 0xAA),
            m => m.Word(Field + 0x1010, 242),
        })
        {
            var memory = Scene(); change(memory);
            Assert.False(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, pc), out _));
        }
    }

    [Theory]
    [MemberData(nameof(Cues))]
    public void CompletionRequiresTheSameSceneOwnerAndActor(uint pc, int action, string bytes)
    {
        _ = action; _ = bytes;
        var memory = Scene(); var before = Dispatch(memory, pc);
        Assert.True(StorySceneActionCapture.TryMatch(memory, before, out var candidate));
        Applied(memory, before); memory.Word(Context + 0x40, Actors + 0x10);
        Assert.False(StorySceneActionCapture.TryComplete(memory, candidate));
    }

    [Fact]
    public void AlreadyRunningMovementDoesNotStartAnotherApproachCue()
    {
        var memory = Scene(); memory.Word(Nu + 0xCC, 1); memory.Word(Nu + 0xC8, 120);
        Assert.False(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, 0x610), out _));
    }

    [Theory]
    [InlineData(0x94, 1u)]         // not the verified straight upward movement
    [InlineData(0xAC, 0u)]         // no movement vector
    [InlineData(0xAC, 16u)]        // moving down instead
    [InlineData(0xC8, 0u)]        // movement ended
    [InlineData(0xCC, 0u)]        // native movement did not start
    public void MovementOperandsWithoutTheNativeUpwardVelocityDoNotDescribeMotion(uint offset, uint value)
    {
        var memory = Scene(); var before = Dispatch(memory, 0x610);
        Assert.True(StorySceneActionCapture.TryMatch(memory, before, out var candidate));
        Applied(memory, before); memory.Word(Nu + offset, value);
        Assert.False(StorySceneActionCapture.TryComplete(memory, candidate));
    }

    [Theory]
    [InlineData(0x5FFu, 0x0Cu)]
    [InlineData(0x61Du, 0x0Du)]
    public void ReapplyingTheCurrentPoseDoesNotDescribeAChange(uint pc, uint frame)
    {
        var memory = Scene(); memory.Word(Nu + 0x50, frame); memory.Word(Nu + 0x74, 3);
        Assert.False(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, pc), out _));
    }

    private static TestMemory Scene()
    {
        var memory = new TestMemory();
        memory.Add(Context, new byte[0xBB8]); memory.Word(Context, Data);
        memory.Word(Context + 0x40, Actors); memory.Word(Context + 0x850, Field);
        memory.Word(Context + 0x854, Camera); memory.Word(Context + 0xBB4, 163);
        memory.Add(Field + 0x1000, new byte[0x1100]); memory.Word(Field + 0x1010, 243);
        memory.Add(Data + 0x12000, InstalledFieldScripts.Atel(163));
        memory.Add(Nu, new byte[0x154]); memory.Word(Nu + 0x40, 5);
        memory.Word(Nu + 0xD0, 1); memory.Word(Nu + 0x48, 0x100);
        memory.Word(Nu + 0x84, 8 * 256); memory.Word(Nu + 0x90, 28 * 256);
        memory.Add(Actors + 0x110B0, new byte[0x800]); memory.Word(Actors + 0x110B0, 0xAB);
        memory.Add(Camera, new byte[0x1C0]); memory.Word(Camera + 0x14, 31);
        memory.Word(Camera + 0x18, 40); memory.Word(Camera + 0x1C, 67);
        return memory;
    }

    private static StoryActionSnapshot Dispatch(TestMemory memory, uint pc)
    {
        memory.Word(Context + 0x24, pc); memory.Word(Field + 0x1180, 28);
        var opcode = pc == 0x610 ? 0x92 : 0xAC;
        Assert.True(StoryActionCapture.TryCapture(memory, Context, opcode, out var before)); return before;
    }

    private static void Applied(TestMemory memory, StoryActionSnapshot before)
    {
        var pc = before.Location.Address;
        memory.Word(Context + 4, 0); memory.Word(Context + 0x24, pc);
        if (before.Location.Opcode == 0x92)
        {
            memory.Word(Context + 0x18, pc); memory.Word(Field + 0x20CC, 0xC0);
            memory.Word(Nu + 0xCC, 1); memory.Word(Nu + 0xC8, 0xE0);
            memory.Word(Nu + 0x124, 0x80); memory.Word(Nu + 0xAC, unchecked((uint)-32));
        }
        else
        {
            memory.Word(Context + 0x18, pc + 2); memory.Word(Nu + 0x50, (uint)before.Operand);
            memory.Word(Nu + 0x74, 3);
        }
    }
}
