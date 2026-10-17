using System.Security.Cryptography;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;
using TestMemory = ChronoTriggerAccessibility.Native.Tests.Capture.StorySceneActionCaptureTests.Memory;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

/// <summary>The Ocean Palace anchors on the installed Atel_0220/0397/0223/0230, loaded verbatim at
/// data+0x12000. Native post-states follow the exported handlers: AA 16E590 (animation, mode 1,
/// advance to pc+1/pc+2), AC 16EA90 (any operand byte, including 0xF0, mode 3, yield, next pc+2) and
/// 92 16CB50 with 17B1B0 (angle at field+0x20CC, +0xCC = 1, +0xC8 = steps, yield at pc).
/// Evidence: artifacts/research/ocean-palace-0367/claude-actions/findings.md.</summary>
public sealed class OceanPalaceStoryActionCaptureTests
{
    private const uint Context = 0x1000, Data = 0x400000, Actors = 0x800000, Field = 0x5000;
    private const uint Camera = Actors + 0x1327C;

    public enum Kind { Looping, StaticFrame, TimedMove }

    public sealed record Cue(StorySceneAction Action, int Scene, int Script, int Actor, uint Pc, string Bytes,
        int MinStory, int MaxStory, int Class, int Visual, Kind Kind)
    {
        public int Opcode => Convert.ToByte(Bytes[..2], 16);
        public byte Operand => Convert.FromHexString(Bytes)[1];
    }

    public static readonly Cue[] All =
    [
        new(StorySceneAction.MuneSpinsAndVanishes, 404, 220, 9, 0x3A1, "92C004171D0F1B17", 0xBD, 0xC2, 5, 0x98, Kind.TimedMove),
        new(StorySceneAction.MasaSpinsAndVanishes, 409, 397, 33, 0xAFC, "92C004A601A603A6", 0xC3, 0xC5, 5, 0x97, Kind.TimedMove),
        new(StorySceneAction.CronoRaisesHandsToMachine, 415, 223, 1, 0x49A, "AA221103FC058940", 0xC9, 0xC9, 0, 0, Kind.Looping),
        new(StorySceneAction.KnifeRises, 415, 223, 9, 0x70C, "92C00CC19900C19A", 0xC9, 0xC9, 4, 0x67, Kind.TimedMove),
        new(StorySceneAction.KnifeShootsIntoMachine, 415, 223, 9, 0x720, "92C020AD0888400F", 0xC9, 0xC9, 4, 0x67, Kind.TimedMove),
        new(StorySceneAction.SwordAppearsInMachine, 415, 223, 10, 0x753, "AC069CC00288400F", 0xC9, 0xC9, 4, 0x8B, Kind.StaticFrame),
        new(StorySceneAction.SchalaHoldsOutHands, 415, 223, 12, 0x9A3, "AA06AD08040E28AD", 0xCB, 0xCB, 4, 9, Kind.Looping),
        new(StorySceneAction.PartyGoneSchalaKneels, 415, 223, 12, 0x9DA, "AA05AD10BBD6000C", 0xCB, 0xCB, 4, 9, Kind.Looping),
        new(StorySceneAction.MagusStandsWhereProphetWas, 423, 230, 8, 0xBE0, "AA00AD0CBBA3000F", 0xC9, 0xCA, 3, 6, Kind.Looping),
        new(StorySceneAction.SchalaKnockedToKnees, 423, 230, 19, 0xF0A, "AA057D24BBAB0000", 0xC9, 0xCA, 4, 9, Kind.Looping),
        new(StorySceneAction.MagusThrownDown, 423, 230, 8, 0xB5F, "AA088938922A10F1", 0xC9, 0xCA, 3, 6, Kind.Looping),
        new(StorySceneAction.CronoRaisesArmsBeforeLavos, 423, 230, 1, 0x7A0, "AA220FAD040D009C", 0xC9, 0xCA, 0, 0, Kind.Looping),
        new(StorySceneAction.CronoCrumbles, 423, 230, 1, 0x7D2, "ACF09C4002ACF19C", 0xC9, 0xCA, 0, 0, Kind.StaticFrame),
    ];

    public static TheoryData<StorySceneAction> Actions => new(All.Select(cue => cue.Action));
    public static TheoryData<StorySceneAction> Frames => new(All.Where(c => c.Kind == Kind.StaticFrame).Select(c => c.Action));
    public static TheoryData<StorySceneAction> Moves => new(All.Where(c => c.Kind == Kind.TimedMove).Select(c => c.Action));
    public static TheoryData<StorySceneAction> Loops => new(All.Where(c => c.Kind == Kind.Looping).Select(c => c.Action));

    private static Cue For(StorySceneAction action) => All.Single(cue => cue.Action == action);

    [Fact]
    public void TheDigestsLengthsAndAnchorWindowsAreTheInstalledScripts()
    {
        foreach (var (script, length, sha256) in new[]
                 {
                     (220, 1173, StorySceneActionCapture.OceanEntranceScriptSha256),
                     (397, 2861, StorySceneActionCapture.GreatStairScriptSha256),
                     (223, 3650, StorySceneActionCapture.MammonMachineScriptSha256),
                     (230, 4852, StorySceneActionCapture.LavosChamberScriptSha256),
                 })
        {
            var bytes = InstalledFieldScripts.Atel(script);
            Assert.Equal(length, bytes.Length);
            Assert.Equal(sha256, Convert.ToHexString(SHA256.HashData(bytes)));
        }
        foreach (var cue in All)
            Assert.Equal(cue.Bytes, Convert.ToHexString(InstalledFieldScripts.Atel(cue.Script), (int)cue.Pc + 1, 8));
        Assert.Equal(34, InstalledFieldScripts.Atel(397)[0]);   // Masa is actor 33 of 34
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public void AnOceanActionMatchesItsExecutedAnchorAndCompletesOnlyAfterTheNativeResult(StorySceneAction action)
    {
        var cue = For(action);
        var memory = Scene(cue);
        var before = Dispatch(memory, cue);
        Assert.Equal(cue.Bytes, before.Bytes);
        Assert.True(StorySceneActionCapture.Watches(before.Location));
        Assert.True(StorySceneActionCapture.TryMatch(memory, before, out var candidate));
        Assert.Equal(action, candidate.Action);
        Assert.Equal(cue.Actor, candidate.Subject.Index);
        Assert.Null(candidate.Partner);

        // The operand alone never proves the change: the original handler has not run yet.
        Assert.False(StorySceneActionCapture.TryComplete(memory, candidate));
        Native(memory, cue);
        Assert.True(StorySceneActionCapture.TryComplete(memory, candidate));
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public void CompletionRejectsAChangedSceneOwnerExecutingActorOrHiddenSubject(StorySceneAction action)
    {
        foreach (var change in new Action<TestMemory, Cue>[]
                 {
                     (m, _) => m.Word(Field + 0x1010, 3),
                     (m, _) => m.Word(Context + 0x40, Actors + 0x10),
                     (m, _) => m.Word(Field + 0x1180, 0),
                     (m, c) => SetWord(m, c.Actor, 0xD0, 0),
                     (m, c) => SetWord(m, c.Actor, 0x84, 0x3000),
                 })
        {
            var cue = For(action);
            var memory = Scene(cue);
            Assert.True(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, cue), out var candidate));
            Native(memory, cue);
            change(memory, cue);
            Assert.False(StorySceneActionCapture.TryComplete(memory, candidate));
        }
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public void StoryPointsOutsideTheNativeBranchAreRejected(StorySceneAction action)
    {
        var cue = For(action);
        foreach (var story in new[] { cue.MinStory - 1, cue.MaxStory + 1, 0x100 })
        {
            var memory = Scene(cue, story);
            Assert.False(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, cue), out _));
        }
        foreach (var story in new[] { cue.MinStory, cue.MaxStory })
        {
            var memory = Scene(cue, story);
            Assert.True(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, cue), out _));
        }
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public void AnyChangeToTheLoadedScriptRejectsTheAction(StorySceneAction action)
    {
        var cue = For(action);
        var memory = Scene(cue);
        // A byte far from the anchor window: the whole installed script must match.
        var offset = cue.Pc is > 0x2F0 and < 0x310 ? 0x200u : 0x300u;
        memory.Add(Data + 0x12000 + offset, [(byte)(InstalledFieldScripts.Atel(cue.Script)[offset] ^ 0xFF)]);
        Assert.False(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, cue), out _));
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public void AHiddenOffscreenOrDifferentSubjectIsNotDescribed(StorySceneAction action)
    {
        var cue = For(action);
        foreach (var change in new Action<TestMemory>[]
                 {
                     m => SetWord(m, cue.Actor, 0xD0, 0),                      // not drawn
                     m => SetWord(m, cue.Actor, 0x40, (uint)cue.Class | 0x80), // class hidden bit
                     m => SetWord(m, cue.Actor, 0x40, (uint)cue.Class + 1),    // another kind of actor
                     m => SetWord(m, cue.Actor, 0x44, (uint)cue.Visual + 1),   // another graphic
                     m => { SetWord(m, cue.Actor, 0x84, 0x3000); SetWord(m, cue.Actor, 0x80, 0x30); },
                     m => m.Word(Context + 0x854, 0),                          // no native camera
                 })
        {
            var memory = Scene(cue);
            change(memory);
            Assert.False(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, cue), out _));
        }
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public void OtherScenesScriptsActorsPcsAndOpcodesAreNotWatched(StorySceneAction action)
    {
        var cue = For(action);
        var memory = Scene(cue);
        var location = Dispatch(memory, cue).Location;
        Assert.True(StorySceneActionCapture.Watches(location));
        Assert.False(StorySceneActionCapture.Watches(location with { Scene = cue.Scene + 1 }));
        Assert.False(StorySceneActionCapture.Watches(location with { ScriptId = cue.Script + 1 }));
        Assert.False(StorySceneActionCapture.Watches(location with { Actor = cue.Actor + 1 }));
        Assert.False(StorySceneActionCapture.Watches(location with { Address = cue.Pc + 1 }));
        Assert.False(StorySceneActionCapture.Watches(location with { Opcode = cue.Opcode ^ 0x01 }));
    }

    [Theory]
    [MemberData(nameof(Frames))]
    public void AStaticFrameAlreadyShownIsNotDescribedAgain(StorySceneAction action)
    {
        var cue = For(action);
        var memory = Scene(cue);
        SetWord(memory, cue.Actor, 0x50, cue.Operand);
        SetWord(memory, cue.Actor, 0x74, 3);
        Assert.False(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, cue), out _));
    }

    [Theory]
    [MemberData(nameof(Frames))]
    public void StaticFrameCompletionRequiresTheExactAppliedFrameModeAndYield(StorySceneAction action)
    {
        var cue = For(action);
        foreach (var change in new Action<TestMemory>[]
                 {
                     m => SetWord(m, cue.Actor, 0x50, (uint)cue.Operand ^ 1),   // another frame
                     m => SetWord(m, cue.Actor, 0x74, 1),                       // not static mode
                     m => m.Word(Context + 4, 1),                                // advanced instead of yielding
                     m => m.Word(Context + 0x18, cue.Pc + 3),                    // not the AC continuation
                 })
        {
            var memory = Scene(cue);
            Assert.True(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, cue), out var candidate));
            Native(memory, cue);
            change(memory);
            Assert.False(StorySceneActionCapture.TryComplete(memory, candidate));
        }
    }

    [Fact]
    public void TheHighF0SilhouetteFrameIsOnlyProvenByTheHandlersStoredByte()
    {
        var cue = For(StorySceneAction.CronoCrumbles);
        Assert.Equal(0xF0, cue.Operand);
        var memory = Scene(cue);
        Assert.True(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, cue), out var candidate));
        Native(memory, cue);
        Assert.True(StorySceneActionCapture.TryComplete(memory, candidate));
        // 16EA90 stores the full byte: a truncated or later silhouette frame is not this proof.
        foreach (var frame in new uint[] { 0x70, 0xF1, 0xF2, 0x1F0 })
        {
            SetWord(memory, cue.Actor, 0x50, frame);
            Assert.False(StorySceneActionCapture.TryComplete(memory, candidate));
        }
    }

    [Theory]
    [MemberData(nameof(Moves))]
    public void AMoveAlreadyRunningIsNotDescribedAgain(StorySceneAction action)
    {
        var cue = For(action);
        var memory = Scene(cue);
        SetWord(memory, cue.Actor, 0xCC, 1);
        SetWord(memory, cue.Actor, 0xC8, 3);
        Assert.False(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, cue), out _));
    }

    [Theory]
    [MemberData(nameof(Moves))]
    public void MoveCompletionRequiresTheNativeUpwardStart(StorySceneAction action)
    {
        var cue = For(action);
        foreach (var change in new Action<TestMemory>[]
                 {
                     m => SetWord(m, cue.Actor, 0xCC, 0),                        // movement not started
                     m => SetWord(m, cue.Actor, 0xC8, 0),                        // no remaining steps
                     m => m.Word(Field + 0x20CC, 0x40),                          // another angle
                     m => SetWord(m, cue.Actor, 0x94, 1),                        // sideways component
                     m => SetWord(m, cue.Actor, 0xAC, 16),                       // moving down
                     m => SetWord(m, cue.Actor, 0xAC, 0),                        // no vector
                     m => m.Word(Context + 4, 1),                                // completed and advanced
                 })
        {
            var memory = Scene(cue);
            Assert.True(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, cue), out var candidate));
            Native(memory, cue);
            change(memory);
            Assert.False(StorySceneActionCapture.TryComplete(memory, candidate));
        }
    }

    [Theory]
    [MemberData(nameof(Loops))]
    public void LoopingCompletionRequiresTheAppliedAnimationAndAdvance(StorySceneAction action)
    {
        var cue = For(action);
        foreach (var change in new Action<TestMemory>[]
                 {
                     m => SetWord(m, cue.Actor, 0x68, (uint)cue.Operand ^ 1),   // another animation
                     m => SetWord(m, cue.Actor, 0x74, 2),                       // play-once mode
                     m => SetWord(m, cue.Actor, 0x128, 1),                      // counted animation pending
                     m => m.Word(Context + 4, 0),                                // did not advance
                     m => m.Word(Context + 0x24, cue.Pc),                        // not the AA continuation
                 })
        {
            var memory = Scene(cue);
            Assert.True(StorySceneActionCapture.TryMatch(memory, Dispatch(memory, cue), out var candidate));
            Native(memory, cue);
            change(memory);
            Assert.False(StorySceneActionCapture.TryComplete(memory, candidate));
        }
    }

    [Fact]
    public void OnlyCronosTwoAnchoredArmsPosesSupersedeTheGenericGesture()
    {
        foreach (var cue in All)
        {
            var memory = Scene(cue);
            var location = Dispatch(memory, cue).Location;
            Assert.Equal(cue.Action is StorySceneAction.CronoRaisesHandsToMachine or StorySceneAction.CronoRaisesArmsBeforeLavos,
                StorySceneActionCapture.SupersedesGesture(location));
            Assert.False(StorySceneActionCapture.SupersedesGesture(location with { Address = cue.Pc + 1 }));
        }
        // Earlier generic-overlapping anchors keep their existing fallback.
        Assert.False(StorySceneActionCapture.SupersedesGesture(new(Context, Data, Actors, Field, Camera, 439, 74, 3, 33, 0x63C, 0xAA)));
    }

    private static StoryActionSnapshot Dispatch(TestMemory memory, Cue cue)
    {
        memory.Word(Context + 0x24, cue.Pc);
        memory.Word(Field + 0x1180, (uint)cue.Actor * 2);
        Assert.True(StoryActionCapture.TryCapture(memory, Context, cue.Opcode, out var before));
        return before;
    }

    private static TestMemory Scene(Cue cue, int? story = null)
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
        for (var actor = 0; actor < script[0]; actor++)
        {
            memory.Add(ActorAddress(actor), new byte[0x154]);
            Place(memory, actor, 14 + actor % 8, 4 + actor % 6);
            SetWord(memory, actor, 0x40, 4);
            SetWord(memory, actor, 0x44, 0x22);
            SetWord(memory, actor, 0xD0, 1);
            SetWord(memory, actor, 0x48, 0x100);
        }
        SetWord(memory, cue.Actor, 0x40, (uint)cue.Class);
        SetWord(memory, cue.Actor, 0x44, (uint)cue.Visual);
        memory.Add(Actors + 0x110B0, new byte[0x800]);
        memory.Word(Actors + 0x110B0, (uint)(story ?? cue.MinStory));
        memory.Add(Camera, new byte[0x1C0]);
        // 8-pixel columns 24..55 and rows 0..27: tiles 12..27 by 0..13.
        memory.Word(Camera + 0x10, 24);
        memory.Word(Camera + 0x14, 55);
        memory.Word(Camera + 0x1C, 27);
        return memory;
    }

    /// <summary>The exported handler's state just after the original returns.</summary>
    private static void Native(TestMemory memory, Cue cue)
    {
        var bytes = Convert.FromHexString(cue.Bytes);
        switch (cue.Kind)
        {
            case Kind.Looping:   // AA 16E590
                SetWord(memory, cue.Actor, 0x68, bytes[1]);
                SetWord(memory, cue.Actor, 0x74, 1);
                SetWord(memory, cue.Actor, 0x128, 0);
                memory.Word(Context + 4, 1);
                memory.Word(Context + 0x24, cue.Pc + 1);
                memory.Word(Context + 0x18, cue.Pc + 2);
                break;
            case Kind.StaticFrame:   // AC 16EA90
                SetWord(memory, cue.Actor, 0x50, bytes[1]);
                SetWord(memory, cue.Actor, 0x74, 3);
                memory.Word(Context + 4, 0);
                memory.Word(Context + 0x24, cue.Pc);
                memory.Word(Context + 0x18, cue.Pc + 2);
                break;
            case Kind.TimedMove:   // 92 16CB50 + 17B1B0
                memory.Word(Field + 0x20CC, bytes[1]);
                SetWord(memory, cue.Actor, 0xCC, 1);
                SetWord(memory, cue.Actor, 0xC8, bytes[2]);
                SetWord(memory, cue.Actor, 0x124, 0x80);
                SetWord(memory, cue.Actor, 0x94, 0);
                SetWord(memory, cue.Actor, 0xAC, unchecked((uint)-32));
                memory.Word(Context + 4, 0);
                memory.Word(Context + 0x24, cue.Pc);
                memory.Word(Context + 0x18, cue.Pc);
                break;
        }
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
}
