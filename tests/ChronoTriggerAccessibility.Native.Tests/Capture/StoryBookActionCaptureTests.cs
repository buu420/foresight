using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;
using TestMemory = ChronoTriggerAccessibility.Native.Tests.Capture.StorySceneActionCaptureTests.Memory;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

/// <summary>Enhasa (scene 355, Atel_0208) and Kajar (scene 357, Atel_0209) plaza books. Each
/// player Confirm runs one E5 (171C40) that queues the open or closed book tile; the third
/// correct book also queues the passage copy. Script evidence:
/// artifacts/research/zeal-0361/claude/findings.md; native proof:
/// artifacts/research/zeal-0361/claude-native.</summary>
public sealed class StoryBookActionCaptureTests
{
    private const uint Context = 0x1000, Data = 0x400000, Actors = 0x800000, Field = 0x5000;
    private const uint Camera = Actors + 0x1327C;

    /// <summary>Subject: the visible book (actor, class, visual, tile). Camera: 8-pixel columns/rows.</summary>
    public sealed record Cue(StorySceneAction Action, int Scene, int Script, int Actor, uint Pc, string Bytes,
        int Subject, int SubjectClass, int SubjectVisual, int TileX, int TileY, int Left, int Top);

    // Runtime PC = file offset - 1. Both rooms: A, B and C are fixed per actor, never by element.
    public static readonly Cue[] Cues =
    [
        new(StorySceneAction.BookAOpens, 355, 208, 10, 0x674, "E52F1F2F1F101841", 10, 4, 0x73, 16, 24, 16, 28),
        new(StorySceneAction.BookACloses, 355, 208, 10, 0x662, "E52E1F2E1F101841", 10, 4, 0x73, 16, 24, 16, 28),
        new(StorySceneAction.BookBOpens, 355, 208, 11, 0x6ED, "E52F1F2F1F1B0C41", 11, 4, 0x80, 27, 12, 30, 10),
        new(StorySceneAction.BookBCloses, 355, 208, 11, 0x6DB, "E52D1F2D1F1B0C41", 11, 4, 0x80, 27, 12, 30, 10),
        new(StorySceneAction.BookCOpens, 355, 208, 13, 0x746, "E52F1F2F1F060741", 13, 7, 0x00, 6, 7, 0, 0),
        new(StorySceneAction.BookCCloses, 355, 208, 13, 0x731, "E52E1F2E1F060741", 13, 7, 0x00, 6, 7, 0, 0),
        new(StorySceneAction.PassageOpens, 355, 208, 10, 0x6AB, "E5271C281F0C123B", 10, 4, 0x73, 16, 24, 16, 28),
        new(StorySceneAction.BookAOpens, 357, 209, 11, 0x4EE, "E52F2F2F2F0D0F41", 11, 4, 0x73, 13, 15, 16, 12),
        new(StorySceneAction.BookACloses, 357, 209, 11, 0x4DE, "E52D2F2D2F0D0F41", 11, 4, 0x73, 13, 15, 16, 12),
        new(StorySceneAction.BookBOpens, 357, 209, 12, 0x565, "E52F2F2F2F030A41", 12, 4, 0x80, 3, 10, 0, 8),
        new(StorySceneAction.BookBCloses, 357, 209, 12, 0x555, "E52E2F2E2F030A41", 12, 4, 0x80, 3, 10, 0, 8),
        new(StorySceneAction.BookCOpens, 357, 209, 13, 0x5A4, "E52F2F2F2F161841", 13, 4, 0x88, 22, 24, 24, 28),
        new(StorySceneAction.BookCCloses, 357, 209, 13, 0x594, "E52E2F2E2F161841", 13, 4, 0x88, 22, 24, 24, 28),
        new(StorySceneAction.PassageOpens, 357, 209, 11, 0x525, "E5272C282F110B3B", 11, 4, 0x73, 13, 15, 16, 12),
    ];

    public static TheoryData<int> All => new(Enumerable.Range(0, Cues.Length));

    [Theory]
    [MemberData(nameof(All))]
    public void EachPlayerToggleMatchesItsExactInstructionAndCompletesOnlyAfterTheQueuedCopy(int index)
    {
        var cue = Cues[index];
        var memory = Scene(cue);
        var before = Dispatch(memory, cue);
        Assert.True(StorySceneActionCapture.Watches(before.Location));
        Assert.True(StorySceneActionCapture.TryMatch(memory, before, out var candidate));
        Assert.Equal(cue.Action, candidate.Action);
        Assert.Equal(cue.Subject, candidate.Subject.Index);
        // The original has not run yet: an operand is never proof of the visible change.
        Assert.False(StorySceneActionCapture.TryComplete(memory, candidate));
        TileCopy(memory, cue.Pc, cue.Bytes[2..]);
        Assert.True(StorySceneActionCapture.TryComplete(memory, candidate));
    }

    [Theory]
    [MemberData(nameof(All))]
    public void ADifferentQueuedRectangleOrNoCopyRequestIsNotProof(int index)
    {
        var cue = Cues[index];
        var memory = Scene(cue);
        Assert.True(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, cue), out var candidate));
        TileCopy(memory, cue.Pc, "00000000000000");
        Assert.False(StorySceneActionCapture.TryComplete(memory, candidate));
        TileCopy(memory, cue.Pc, cue.Bytes[2..]);
        memory.Word(Actors + 0x1211C, 0);
        Assert.False(StorySceneActionCapture.TryComplete(memory, candidate));
    }

    [Theory]
    [MemberData(nameof(All))]
    public void AnyChangeToTheLoadedScriptOrSceneRejectsTheCue(int index)
    {
        var cue = Cues[index];
        var changed = Scene(cue);
        changed.Add(Data + 0x12000 + 0x300, [0xFF]);
        Assert.False(StorySceneActionCapture.TryMatch(changed, Dispatch(changed, cue), out _));

        var otherScene = Scene(cue with { Scene = cue.Scene == 355 ? 357 : 355 });
        Assert.False(StorySceneActionCapture.TryMatch(otherScene, Dispatch(otherScene, cue), out _));
    }

    [Theory]
    [MemberData(nameof(All))]
    public void TheBookMustBeTheProvenVisibleObjectOnCamera(int index)
    {
        var cue = Cues[index];
        foreach (var change in new Action<TestMemory>[]
                 {
                     m => SetWord(m, cue.Subject, 0xD0, 0),                              // not drawn
                     m => SetWord(m, cue.Subject, 0x40, (uint)cue.SubjectClass | 0x80),  // removed
                     m => SetWord(m, cue.Subject, 0x40, cue.SubjectClass == 7 ? 4u : 7u), // another kind of actor
                     m => SetWord(m, cue.Subject, 0x44, (uint)cue.SubjectVisual + 1),    // another appearance
                     m => Place(m, cue.Subject, cue.TileX + 1, cue.TileY),               // somewhere else
                 })
        {
            var memory = Scene(cue);
            change(memory);
            Assert.False(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, cue), out _));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(11)]
    public void ABookOffCameraIsNotDescribed(int index)
    {
        var cue = Cues[index];
        var memory = Scene(cue with { Left = cue.Left + 64, Top = cue.Top });
        Assert.False(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, cue), out _));
    }

    [Theory]
    [InlineData(6, 0, 30)]    // tiles 0..15 by 15..28: Enhasa passage (12..13, 18..21) in view, book (16,24) not
    [InlineData(13, 32, 12)]  // tiles 16..31 by 6..19: Kajar passage (17..18, 11..14) in view, book (13,15) not
    public void ThePassageIsDescribedOnlyWhenItsOwnTilesAreOnCamera(int index, int left, int top)
    {
        var cue = Cues[index];
        var framed = Scene(cue with { Left = left, Top = top });
        Assert.True(StorySceneActionCapture.TryMatch(framed, Dispatch(framed, cue), out var candidate));
        TileCopy(framed, cue.Pc, cue.Bytes[2..]);
        Assert.True(StorySceneActionCapture.TryComplete(framed, candidate));

        var away = Scene(cue with { Left = left + 80, Top = top + 60 });
        Assert.False(StorySceneActionCapture.TryMatch(away, Dispatch(away, cue), out _));
    }

    [Theory]
    [InlineData(355, 208, 0, 0x4F2, 0xE4)]   // actor 0 reload reapplies an open book (E4 171B70)
    [InlineData(355, 208, 0, 0x519, 0xE4)]   // reload reapplies the opened passage
    [InlineData(355, 208, 10, 0x697, 0xE5)]  // first of the three passage frames
    [InlineData(355, 208, 10, 0x6A1, 0xE5)]  // second passage frame
    [InlineData(357, 209, 0, 0x3B2, 0xE4)]
    [InlineData(357, 209, 0, 0x3D9, 0xE4)]
    [InlineData(357, 209, 11, 0x511, 0xE5)]
    [InlineData(357, 209, 11, 0x51B, 0xE5)]
    public void ReloadReapplyAndEarlierPassageFramesAreNotWatched(int scene, int script, int actor, uint pc, int opcode)
    {
        var cue = Cues[0] with { Scene = scene, Script = script, Actor = actor, Pc = pc };
        var memory = Scene(cue);
        var before = Dispatch(memory, cue, opcode);
        Assert.False(StorySceneActionCapture.Watches(before.Location));
        Assert.False(StorySceneActionCapture.TryMatch(memory, before, out _));
    }

    [Fact]
    public void TheDigestsAreTheInstalledPlazaScripts()
    {
        Assert.Equal(StorySceneActionCapture.EnhasaPlazaScriptSha256,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(InstalledFieldScripts.Atel(208))));
        Assert.Equal(StorySceneActionCapture.KajarPlazaScriptSha256,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(InstalledFieldScripts.Atel(209))));
        Assert.Equal(2796, InstalledFieldScripts.Atel(208).Length);
        Assert.Equal(1861, InstalledFieldScripts.Atel(209).Length);
    }

    [Theory]
    [MemberData(nameof(All))]
    public void TheAnchorBytesAreTheInstalledInstructions(int index)
    {
        var cue = Cues[index];
        var script = InstalledFieldScripts.Atel(cue.Script);
        Assert.Equal(cue.Bytes, Convert.ToHexString(script, (int)cue.Pc + 1, 8));
    }

    private static StoryActionSnapshot Dispatch(TestMemory memory, Cue cue, int opcode = 0xE5)
    {
        memory.Word(Context + 0x24, cue.Pc);
        memory.Word(Field + 0x1180, (uint)cue.Actor * 2);
        Assert.True(StoryActionCapture.TryCapture(memory, Context, opcode, out var before));
        return before;
    }

    private static TestMemory Scene(Cue cue)
    {
        var script = InstalledFieldScripts.Atel(cue.Script);
        var memory = new TestMemory();
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
            Place(memory, actor, 50, 50);
            SetWord(memory, actor, 0x40, 4);
            SetWord(memory, actor, 0xD0, 1);
            SetWord(memory, actor, 0x48, 0x100);
        }
        SetWord(memory, cue.Subject, 0x40, (uint)cue.SubjectClass);
        SetWord(memory, cue.Subject, 0x44, (uint)cue.SubjectVisual);
        Place(memory, cue.Subject, cue.TileX, cue.TileY);
        memory.Add(Actors + 0x110B0, new byte[0x800]);
        memory.Word(Actors + 0x110B0, 0x9F);
        memory.Add(Actors + 0x1211C, new byte[4]);
        memory.Add(Camera, new byte[0x1C0]);
        // A 16 by 14 tile window starting at the cue's 8-pixel column and row.
        memory.Word(Camera + 0x10, (uint)cue.Left);
        memory.Word(Camera + 0x14, (uint)cue.Left + 31);
        memory.Word(Camera + 0x18, (uint)cue.Top);
        memory.Word(Camera + 0x1C, (uint)cue.Top + 27);
        return memory;
    }

    private static uint ActorAddress(int actor) => Actors + 0x6940 + (uint)actor * 0x154;
    private static void SetWord(TestMemory memory, int actor, uint offset, uint value) => memory.Word(ActorAddress(actor) + offset, value);

    private static void Place(TestMemory memory, int actor, int tileX, int tileY)
    {
        SetWord(memory, actor, 0x80, (uint)tileX);
        SetWord(memory, actor, 0x84, (uint)(tileX * 256 + 128));
        SetWord(memory, actor, 0x8C, (uint)tileY);
        SetWord(memory, actor, 0x90, (uint)(tileY * 256 + 255));
    }

    /// <summary>E5 171C40: seven operand bytes to data+0x2E13E, +0x1211C |= 0x20, yield, next = pc+8.</summary>
    private static void TileCopy(TestMemory memory, uint pc, string operands)
    {
        memory.Add(Data + 0x2E13E, Convert.FromHexString(operands));
        memory.Word(Actors + 0x1211C, 0x20);
        memory.Word(Context + 4, 0);
        memory.Word(Context + 0x24, pc);
        memory.Word(Context + 0x18, pc + 8);
    }
}
