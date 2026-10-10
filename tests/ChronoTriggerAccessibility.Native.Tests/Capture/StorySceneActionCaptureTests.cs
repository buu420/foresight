using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

/// <summary>The seven reviewed opening and fair cues, matched on the installed scripts
/// (Atel_0324 scene 2, Atel_0074 scene 439) loaded verbatim at data+0x12000. Native writes
/// below follow the fresh Ghidra exports in artifacts/research/story-actions-20261009/
/// capture-audit and scene-capture/findings.md.</summary>
public sealed class StorySceneActionCaptureTests
{
    private const uint Context = 0x1000, Data = 0x400000, Actors = 0x800000, Field = 0x5000;
    private const uint Camera = Actors + 0x1327C;
    private const int Crono = 1, Girl = 3, Mother = 8, Curtains = 10;

    public sealed record Cue(StorySceneAction Action, int Scene, int Script, int Actor, uint Pc, int Opcode,
        int Subject, int Story, Action<Memory> Prepare, Action<Memory> Native);

    private static Cue For(StorySceneAction action) => action switch
    {
        StorySceneAction.CurtainsOpen => new(action, 2, 324, Curtains, 0x7C4, 0xE5, Mother, 0, _ => { },
            m => TileCopy(m, 0x7C4, "33323B3C13023B")),
        StorySceneAction.MotherHeadsDownstairs => new(action, 2, 324, Mother, 0x676, 0xA0, Mother, 0,
            m => Place(m, Mother, 0x14, 0x0B), m => Move(m, 0x676, 0x17, 0x0E)),
        StorySceneAction.BoyGetsOutOfBedAndStretches => new(action, 2, 324, Crono, 0x426, 0x96, Crono, 0,
            m => Place(m, Crono, 0x1A, 0x08), m => Move(m, 0x426, 0x18, 0x08)),
        StorySceneAction.FairCollision => new(action, 439, 74, Girl, 0x6CE, 0xAC, Girl, 5,
            m => SetWord(m, Girl, 0x74, 0), m => StaticFrame(m, 0x6CE, Girl, 0x6A)),
        StorySceneAction.GirlGetsUp => new(action, 439, 74, Girl, 0x69B, 0xB7, Girl, 5,
            m => { SetWord(m, Girl, 0x78, 0x13); SetWord(m, Girl, 0x128, 1); SetWord(m, Girl, 0x74, 2); SetWord(m, Girl, 0x68, 0xFF); },
            m => CountedFinish(m, 0x69B, Girl)),
        StorySceneAction.BoyGetsUp => new(action, 439, 74, Crono, 0x539, 0xAE, Crono, 6,
            m => { SetWord(m, Crono, 0x50, 0x45); SetWord(m, Crono, 0x74, 3); SetWord(m, Crono, 0x68, 0xFF); },
            m => Reset(m, 0x539, Crono)),
        StorySceneAction.GirlHops => new(action, 439, 74, Girl, 0x63C, 0xAA, Girl, 6, _ => { },
            m => Looping(m, 0x63C, Girl, 0x0B)),
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    public static TheoryData<StorySceneAction> Actions => new(Enum.GetValues<StorySceneAction>());

    [Theory]
    [MemberData(nameof(Actions))]
    public void MatchesTheExecutedAnchorAndCompletesOnlyAfterTheNativeChange(StorySceneAction action)
    {
        var cue = For(action);
        var memory = Scene(cue);
        var before = Dispatch(memory, cue);

        Assert.True(StorySceneActionCapture.Watches(before.Location));
        Assert.True(StorySceneActionCapture.TryMatch(memory, before, out var candidate));
        Assert.Equal(action, candidate.Action);
        Assert.Equal(cue.Subject, candidate.Subject.Index);
        Assert.False(candidate.RepeatDispatch);

        // The original has not run yet: an operand is never proof of the action.
        Assert.False(StorySceneActionCapture.TryComplete(memory, candidate));
        cue.Native(memory);
        Assert.True(StorySceneActionCapture.TryComplete(memory, candidate));
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public void CompletionRejectsAChangedSceneOwnerOrActor(StorySceneAction action)
    {
        foreach (var change in new Action<Memory>[]
                 {
                     m => m.Word(Field + 0x1010, 3),
                     m => m.Word(Context + 0x40, Actors + 0x10),
                     m => m.Word(Field + 0x1180, 0),
                 })
        {
            var cue = For(action);
            var memory = Scene(cue);
            Assert.True(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, cue), out var candidate));
            cue.Native(memory);
            change(memory);
            Assert.False(StorySceneActionCapture.TryComplete(memory, candidate));
        }
    }

    [Theory]
    [InlineData(StorySceneAction.CurtainsOpen, 3)]           // the player's later Confirm at the window
    [InlineData(StorySceneAction.MotherHeadsDownstairs, 3)]
    [InlineData(StorySceneAction.BoyGetsOutOfBedAndStretches, 3)]
    [InlineData(StorySceneAction.FairCollision, 6)]
    [InlineData(StorySceneAction.FairCollision, 0x4A)]       // trial replays of the same functions
    [InlineData(StorySceneAction.GirlGetsUp, 6)]
    [InlineData(StorySceneAction.BoyGetsUp, 7)]
    [InlineData(StorySceneAction.BoyGetsUp, 0x4A)]
    [InlineData(StorySceneAction.GirlHops, 5)]
    [InlineData(StorySceneAction.GirlHops, 8)]
    public void StoryGatesExcludeOtherVisitsOfTheSameInstructions(StorySceneAction action, int story)
    {
        var cue = For(action) with { Story = story };
        var memory = Scene(cue);
        Assert.False(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, cue), out _));
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public void AnyChangeToTheLoadedScriptRejectsTheCue(StorySceneAction action)
    {
        var cue = For(action);
        var memory = Scene(cue);
        // A byte far from the anchor: the full installed script must match, not just the instruction.
        memory.Add(Data + 0x12000 + 0x300, [0xFF]);
        Assert.False(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, cue), out _));
    }

    [Fact]
    public void TheCurtainControllerIsInvisibleSoTheVisibleMotherGatesTheCurtainCue()
    {
        var cue = For(StorySceneAction.CurtainsOpen);
        var memory = Scene(cue);
        SetWord(memory, Curtains, 0xD0, 0);
        Assert.True(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, cue), out var candidate));
        Assert.Equal(Mother, candidate.Subject.Index);

        SetWord(memory, Mother, 0xD0, 0);
        Assert.False(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, cue), out _));
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public void AHiddenOrOffscreenSubjectIsNotDescribed(StorySceneAction action)
    {
        var cue = For(action);
        var hidden = Scene(cue);
        SetWord(hidden, cue.Subject, 0xD0, 0);
        Assert.False(StorySceneActionCapture.TryMatch(hidden, Dispatch(hidden, cue), out _));

        var away = Scene(cue);
        SetWord(away, cue.Subject, 0x84, 0x3000);
        SetWord(away, cue.Subject, 0x80, 0x30);
        Assert.False(StorySceneActionCapture.TryMatch(away, Dispatch(away, cue), out _));
    }

    [Fact]
    public void TheCollisionNeedsTheBoyVisibleToo()
    {
        var cue = For(StorySceneAction.FairCollision);
        var memory = Scene(cue);
        Assert.True(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, cue), out var candidate));
        Assert.Equal(Crono, candidate.Partner?.Index);

        SetWord(memory, Crono, 0x84, 0x3000);
        Assert.False(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, cue), out _));
    }

    [Theory]
    [InlineData(0)]   // B7 starting: anim 0x13 begins, she is still seated
    [InlineData(3)]   // looping: 17BE1F counts +0x128 down at each sequence end
    [InlineData(2)]
    public void TheGirlRisesOnlyWhenTheCountedAnimationFinishes(int state)
    {
        var cue = For(StorySceneAction.GirlGetsUp);
        var memory = Scene(cue);
        SetWord(memory, Girl, 0x128, (uint)state);
        Assert.False(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, cue), out _));
    }

    [Fact]
    public void GirlRiseCompletionRequiresTheNativeReturnToHerStandingPose()
    {
        var cue = For(StorySceneAction.GirlGetsUp);
        var memory = Scene(cue);
        Assert.True(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, cue), out var candidate));
        CountedFinish(memory, 0x69B, Girl);
        SetWord(memory, Girl, 0x74, 1);   // 16EA59 is only taken when +0x68 was not 0xFF
        Assert.False(StorySceneActionCapture.TryComplete(memory, candidate));
    }

    [Theory]
    [InlineData(StorySceneAction.MotherHeadsDownstairs, Mother, 0x17, 0x0E)]
    [InlineData(StorySceneAction.BoyGetsOutOfBedAndStretches, Crono, 0x18, 0x08)]
    public void AMoveAlreadyAtItsTargetIsNotDescribed(StorySceneAction action, int actor, int x, int y)
    {
        var cue = For(action);
        var memory = Scene(cue);
        Place(memory, actor, x, y);
        Assert.False(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, cue), out _));
    }

    [Fact]
    public void MoveCompletionRequiresTheNativeTargetAndAYield()
    {
        var cue = For(StorySceneAction.MotherHeadsDownstairs);
        var memory = Scene(cue);
        Assert.True(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, cue), out var candidate));
        Move(memory, 0x676, 0x17, 0x0F);
        Assert.False(StorySceneActionCapture.TryComplete(memory, candidate));
        Move(memory, 0x676, 0x17, 0x0E);
        memory.Word(Context + 4, 1);   // arrived and continued: no movement was started
        Assert.False(StorySceneActionCapture.TryComplete(memory, candidate));
    }

    [Fact]
    public void ARepeatedBlockingMoveIsMarkedFromTheSavedActorPc()
    {
        var cue = For(StorySceneAction.MotherHeadsDownstairs);
        var memory = Scene(cue);
        SetWord(memory, Mother, 0x48, 0x676);
        Assert.True(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, cue), out var candidate));
        Assert.True(candidate.RepeatDispatch);
    }

    [Fact]
    public void TileCopyCompletionRequiresTheQueuedRectangle()
    {
        var cue = For(StorySceneAction.CurtainsOpen);
        var memory = Scene(cue);
        Assert.True(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, cue), out var candidate));
        TileCopy(memory, 0x7C4, "13121B1C13023C");   // the closing rectangle
        Assert.False(StorySceneActionCapture.TryComplete(memory, candidate));
        TileCopy(memory, 0x7C4, "33323B3C13023B");
        memory.Word(Actors + 0x1211C, 0);
        Assert.False(StorySceneActionCapture.TryComplete(memory, candidate));
    }

    [Theory]
    [InlineData(2, 324, Curtains, 0x7CC, 0xE5)]   // second curtain copy of the same opening
    [InlineData(2, 324, Curtains, 0x7A9, 0xE5)]   // closing branch
    [InlineData(2, 324, Curtains, 0x7DE, 0xE5)]   // global 0x15C variant: no reviewed visual
    [InlineData(2, 324, Mother, 0x691, 0xA0)]     // Mother's story >= 3 branch
    [InlineData(2, 324, Crono, 0x456, 0x96)]      // the boy's fn6 variant
    [InlineData(439, 74, Crono, 0x4DD, 0xAA)]     // a trial replay pose
    [InlineData(439, 74, Girl, 0x6D0, 0x9C)]      // the girl's knock-back after the anchored impact
    public void OtherInstructionsOfTheSameActorsAreNotWatched(int scene, int script, int actor, uint pc, int opcode)
    {
        var cue = For(StorySceneAction.CurtainsOpen) with { Scene = scene, Script = script, Actor = actor, Pc = pc, Opcode = opcode };
        var memory = Scene(cue);
        var before = Dispatch(memory, cue);
        Assert.False(StorySceneActionCapture.Watches(before.Location));
        Assert.False(StorySceneActionCapture.TryMatch(memory, before, out _));
    }

    [Fact]
    public void TheDigestsAreTheInstalledScripts()
    {
        Assert.Equal(StorySceneActionCapture.BedroomScriptSha256,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(InstalledFieldScripts.Atel(324))));
        Assert.Equal(StorySceneActionCapture.PlazaRearScriptSha256,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(InstalledFieldScripts.Atel(74))));
    }

    private static StoryActionSnapshot Dispatch(Memory memory, Cue cue)
    {
        memory.Word(Context + 0x24, cue.Pc);
        memory.Word(Field + 0x1180, (uint)cue.Actor * 2);
        Assert.True(StoryActionCapture.TryCapture(memory, Context, cue.Opcode, out var before));
        return before;
    }

    private static Memory Scene(Cue cue)
    {
        var script = InstalledFieldScripts.Atel(cue.Script);
        var memory = new Memory();
        memory.Add(Context, new byte[0xBB8]);
        memory.Word(Context, Data);
        memory.Word(Context + 0x40, Actors);
        memory.Word(Context + 0x850, Field);
        memory.Word(Context + 0x854, Camera);
        memory.Word(Context + 0xBB4, (uint)cue.Script);
        memory.Add(Field + 0x1000, new byte[0x1100]);
        memory.Word(Field + 0x1010, (uint)cue.Scene);
        memory.Add(Data + 0x12000, script);
        memory.Add(Data + 0x2E13E, new byte[8]);
        for (var actor = 0; actor < script[0]; actor++)
        {
            memory.Add(ActorAddress(actor), new byte[0x154]);
            Place(memory, actor, 14 + actor % 8, 4 + actor % 6);
            SetWord(memory, actor, 0x40, actor <= 2 ? 0u : 4u);
            SetWord(memory, actor, 0xD0, 1);
            SetWord(memory, actor, 0x48, 0x100);
        }
        memory.Add(Actors + 0x110B0, new byte[0x800]);
        memory.Word(Actors + 0x110B0, (uint)cue.Story);
        memory.Add(Actors + 0x1211C, new byte[4]);
        memory.Add(Camera, new byte[0x1C0]);
        // 8-pixel columns 24..55 and rows 0..27: tiles 12..27 by 0..13.
        memory.Word(Camera + 0x10, 24);
        memory.Word(Camera + 0x14, 55);
        memory.Word(Camera + 0x1C, 27);
        cue.Prepare(memory);
        return memory;
    }

    private static uint ActorAddress(int actor) => Actors + 0x6940 + (uint)actor * 0x154;
    private static void SetWord(Memory memory, int actor, uint offset, uint value) => memory.Word(ActorAddress(actor) + offset, value);

    private static void Place(Memory memory, int actor, int tileX, int tileY)
    {
        SetWord(memory, actor, 0x80, (uint)tileX);
        SetWord(memory, actor, 0x84, (uint)(tileX * 256 + 128));
        SetWord(memory, actor, 0x8C, (uint)tileY);
        SetWord(memory, actor, 0x90, (uint)(tileY * 256 + 255));
    }

    /// <summary>E5 171C40: seven operand bytes to data+0x2E13E, +0x1211C |= 0x20, yield at pc+8.</summary>
    private static void TileCopy(Memory memory, uint pc, string operands)
    {
        memory.Add(Data + 0x2E13E, Convert.FromHexString(operands));
        memory.Word(Actors + 0x1211C, 0x20);
        Yield(memory, pc, pc + 8);
    }

    /// <summary>A0/96 via 16CE00: the target is written to field+0x20D8/0x20DC and the actor yields at the same PC.</summary>
    private static void Move(Memory memory, uint pc, int x, int y)
    {
        memory.Word(Field + 0x20D8, (uint)x);
        memory.Word(Field + 0x20DC, (uint)y);
        Yield(memory, pc, pc);
    }

    /// <summary>AC 16EA90.</summary>
    private static void StaticFrame(Memory memory, uint pc, int actor, uint frame)
    {
        SetWord(memory, actor, 0x50, frame);
        SetWord(memory, actor, 0x74, 3);
        Yield(memory, pc, pc + 2);
    }

    /// <summary>AA 16E590.</summary>
    private static void Looping(Memory memory, uint pc, int actor, uint animation)
    {
        SetWord(memory, actor, 0x68, animation);
        SetWord(memory, actor, 0x74, 1);
        SetWord(memory, actor, 0x128, 0);
        memory.Word(Context + 4, 1);
        memory.Word(Context + 0x24, pc + 1);
        memory.Word(Context + 0x18, pc + 2);
    }

    /// <summary>B7 16EA0E with +0x68 == 0xFF: clears it and returns to mode 0, advancing three bytes.</summary>
    private static void CountedFinish(Memory memory, uint pc, int actor)
    {
        SetWord(memory, actor, 0x128, 0);
        SetWord(memory, actor, 0x68, 0);
        SetWord(memory, actor, 0x74, 0);
        memory.Word(Context + 4, 1);
        memory.Word(Context + 0x24, pc);
        memory.Word(Context + 0x18, pc + 3);
    }

    /// <summary>AE 16E720.</summary>
    private static void Reset(Memory memory, uint pc, int actor)
    {
        SetWord(memory, actor, 0x68, 0);
        SetWord(memory, actor, 0x74, 0);
        SetWord(memory, actor, 0x128, 0);
        memory.Word(Context + 4, 1);
        memory.Word(Context + 0x24, pc);
        memory.Word(Context + 0x18, pc + 1);
    }

    private static void Yield(Memory memory, uint pc, uint next)
    {
        memory.Word(Context + 4, 0);
        memory.Word(Context + 0x24, pc);
        memory.Word(Context + 0x18, next);
    }

    public sealed class Memory : IReadableMemory
    {
        private readonly Dictionary<nuint, byte> bytes = [];
        public void Word(nuint address, uint value)
        {
            Span<byte> data = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(data, value);
            Add(address, data);
        }
        public void Add(nuint address, ReadOnlySpan<byte> data)
        {
            for (var index = 0; index < data.Length; index++) bytes[address + (nuint)index] = data[index];
        }
        public bool TryRead(nuint address, Span<byte> destination)
        {
            for (var index = 0; index < destination.Length; index++)
                if (!bytes.TryGetValue(address + (nuint)index, out destination[index])) return false;
            return true;
        }
    }
}
