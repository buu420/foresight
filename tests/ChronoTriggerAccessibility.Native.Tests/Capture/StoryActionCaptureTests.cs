using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

/// <summary>Dispatcher-time story capture. Native evidence (fresh read-only Ghidra exports in
/// artifacts/research/story-actions-20261009/capture-audit): AA 16E590, AB 16E7A0 (jump table
/// 56E904), AC 16EA90, BB 16FBB0, camera window 17A6C0/17A860, animation end 17BE1F.</summary>
public sealed class StoryActionCaptureTests
{
    private const uint Context = 0x1000, Data = 0xA0000, Actors = 0x200000, Field = 0x5000;
    private const uint Camera = Actors + 0x1327C;
    private const int Crono = 1, Mother = 8;

    [Fact]
    public void LocatesTheExecutingActorRatherThanTheScriptId()
    {
        var memory = Bedroom(0x42B, "AB21AA2000000000", Crono);

        Assert.True(StoryActionCapture.TryLocate(memory, Context, 0xAB, out var location));

        Assert.Equal((Context, Data, Actors, Field, Camera), (location.Context, location.Data, location.Actors, location.Field, location.Camera));
        Assert.Equal((2, 324, Crono, 0x23, 0x42Bu, 0xAB), (location.Scene, location.ScriptId, location.Actor, location.ActorCount, location.Address, location.Opcode));
    }

    [Fact]
    public void RejectsADispatchWhoseScriptByteIsNotTheDispatchedOpcode()
    {
        var memory = Bedroom(0x42B, "AB21AA2000000000", Crono);
        Assert.False(StoryActionCapture.TryLocate(memory, Context, 0xAA, out _));
        Assert.False(StoryActionCapture.TryLocate(memory, Context, 0x1AB, out _));
    }

    [Theory]
    [InlineData(3u)]            // odd: not actor * 2
    [InlineData(0x23u * 2)]     // first index past the header's 0x23 actors
    [InlineData(0xFFFFFFFEu)]
    public void RejectsAnExecutingActorOutsideTheScriptHeader(uint raw)
    {
        var memory = Bedroom(0x42B, "AB21AA2000000000", Crono);
        memory.Word(Field + 0x1180, raw);
        Assert.False(StoryActionCapture.TryLocate(memory, Context, 0xAB, out _));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(65u)]
    public void RejectsAnActorCountOutsideTheAuditedBounds(uint count)
    {
        var memory = Bedroom(0x42B, "AB21AA2000000000", Crono);
        memory.Add(Data + 0x12000, [(byte)count]);
        Assert.False(StoryActionCapture.TryLocate(memory, Context, 0xAB, out _));
    }

    [Fact]
    public void RejectsNullOverflowingOrUnreadableOwners()
    {
        Assert.False(StoryActionCapture.TryLocate(Bedroom(0x42B, "AB21AA2000000000", Crono), 0, 0xAB, out _));
        Assert.False(StoryActionCapture.TryLocate(Bedroom(0x42B, "AB21AA2000000000", Crono), 0xFFFFFFF0, 0xAB, out _));
        foreach (var owner in new uint[] { 0, 0x40, 0x850 })
        {
            var memory = Bedroom(0x42B, "AB21AA2000000000", Crono);
            memory.Word(Context + owner, 0);
            Assert.False(StoryActionCapture.TryLocate(memory, Context, 0xAB, out _));
        }
        var outOfRange = Bedroom(0x42B, "AB21AA2000000000", Crono);
        outOfRange.Word(Context + 0x24, 0x10000);
        Assert.False(StoryActionCapture.TryLocate(outOfRange, Context, 0xAB, out _));
    }

    [Fact]
    public void CapturesTheActorsPresentationStoryPointControlAndTextbox()
    {
        var memory = Bedroom(0x676, "A0170E9100000000", Mother);
        SetActor(memory, Mother, visual: 0x17, facing: 2, animation: 3, mode: 1, frame: 5, fineX: 0x1080, fineY: 0x0C80);
        memory.Word(Actors + 0x110B0, 2);
        memory.Word(Field + 0x108C, 0);
        memory.Word(Field + 0x1088, 0);
        memory.Word(Field + 0x10B4, 1);
        memory.Word(Field + 0x10C4, Mother * 2);

        Assert.True(StoryActionCapture.TryCapture(memory, Context, 0xA0, out var snapshot));

        Assert.Equal(Mother, snapshot.Location.Actor);
        Assert.Equal("A0170E9100000000", snapshot.Bytes);
        Assert.Equal(0x17, snapshot.Operand);
        var actor = snapshot.ActorState;
        Assert.Equal((Mother, 4, 0x17, 1, 2), (actor.Index, actor.ClassTag, actor.Visual, actor.DrawMode, actor.Facing));
        Assert.Equal((3, 1, 5), (actor.Animation, actor.AnimationMode, actor.StaticFrame));
        Assert.Equal((0x1080, 0x0C80), (actor.FineX, actor.FineY));
        Assert.True(actor.Drawn);
        Assert.Equal(2, snapshot.StoryPoint);
        Assert.Equal((0u, 0u), (snapshot.Control, snapshot.ScriptPause));
        Assert.True(snapshot.TextboxOpen);
        Assert.Equal(Mother, snapshot.TextboxActor);
    }

    [Fact]
    public void ClosedTextboxHasNoOwnerEvenIfTheLastOwnerWordRemains()
    {
        var memory = Bedroom(0x676, "A0170E9100000000", Mother);
        memory.Word(Field + 0x10B4, 0);
        memory.Word(Field + 0x10C4, Mother * 2);
        Assert.True(StoryActionCapture.TryCapture(memory, Context, 0xA0, out var snapshot));
        Assert.False(snapshot.TextboxOpen);
        Assert.Equal(-1, snapshot.TextboxActor);
    }

    [Fact]
    public void OnScreenUsesTheNativeCameraBoundsNotTheWiderProcessingWindowFlag()
    {
        // Camera columns 0..31 x rows 0..27 in 8-pixel units: fine 0..0x1000 by 0..0xE00.
        var memory = Bedroom(0x676, "A0170E9100000000", Mother);
        SetCamera(memory, 0, 31, 0, 27);
        SetActor(memory, Mother, fineX: 0x0800, fineY: 0x0600, window: true);
        Assert.True(Capture(memory).ActorState.OnScreen);

        // 17A6C0 keeps +0x20 bit 0x80 six 8-pixel columns past the right edge.
        SetActor(memory, Mother, fineX: 0x1100, fineY: 0x0600, window: true);
        var outside = Capture(memory).ActorState;
        Assert.True(outside.InNativeWindow);
        Assert.False(outside.OnScreen);

        SetActor(memory, Mother, fineX: 0x0800, fineY: 0x0600, draw: 0, window: true);
        Assert.False(Capture(memory).ActorState.OnScreen);
        SetActor(memory, Mother, fineX: 0x0800, fineY: 0x0600, classTag: 0x84, window: true);
        Assert.False(Capture(memory).ActorState.OnScreen);
    }

    [Fact]
    public void OnScreenIsUnknownWhenTheCameraIsNotTheActorStoresOwn()
    {
        var memory = Bedroom(0x676, "A0170E9100000000", Mother);
        memory.Word(Context + 0x854, Camera + 4);
        var snapshot = Capture(memory);
        Assert.Null(snapshot.ActorState.OnScreen);
        Assert.Equal(0u, snapshot.Location.Camera);
    }

    [Fact]
    public void AaIsProvenOnlyAfterTheNativeLoopIsApplied()
    {
        var memory = Bedroom(0x433, "AA20E30100000000", Crono);
        SetActor(memory, Crono, animation: 7, mode: 3);
        Assert.True(StoryActionCapture.TryCapture(memory, Context, 0xAA, out var before));

        // The original has not run: the operand alone is speculative.
        Assert.False(StoryActionCapture.TryProveAnimation(memory, before, out _));

        ApplyAa(memory, 0x433, Crono, 0x20);
        Assert.True(StoryActionCapture.TryProveAnimation(memory, before, out var proof));
        Assert.Equal(new StoryAnimationProof(StoryAnimationKind.Looping, 0x20), proof);
    }

    [Fact]
    public void AaProofRejectsAnotherActorOwnerOrAnimation()
    {
        foreach (var corrupt in new Action<Memory>[]
                 {
                     memory => memory.Word(Field + 0x1180, Mother * 2),
                     memory => memory.Word(Context + 0x40, Actors + 0x1000),
                     memory => memory.Word(Context + 0xBB4, 325),
                     memory => memory.Word(ActorAddress(Crono) + 0x68, 0x21),
                     memory => memory.Word(Context + 0x24, 0x433),
                     memory => memory.Add(Data + 0x12001 + 0x434, [0x21]),
                 })
        {
            var memory = Bedroom(0x433, "AA20E30100000000", Crono);
            Assert.True(StoryActionCapture.TryCapture(memory, Context, 0xAA, out var before));
            ApplyAa(memory, 0x433, Crono, 0x20);
            corrupt(memory);
            Assert.False(StoryActionCapture.TryProveAnimation(memory, before, out _));
        }
    }

    [Fact]
    public void AbStartsWaitsAndFinishesAPlayOnceAnimation()
    {
        var memory = Bedroom(0x42B, "AB21AA2000000000", Crono);
        SetActor(memory, Crono, animation: 0, mode: 1);

        // First dispatch (+0x128 == 0): 16E85B stores +0x78, mode 2, state 2, and yields at the same PC.
        Assert.True(StoryActionCapture.TryCapture(memory, Context, 0xAB, out var first));
        Assert.False(StoryActionCapture.TryProveAnimation(memory, first, out _));
        ApplyAbStart(memory, 0x42B, Crono, 0x21);
        Assert.True(StoryActionCapture.TryProveAnimation(memory, first, out var started));
        Assert.Equal(new StoryAnimationProof(StoryAnimationKind.PlayOnceStarted, 0x21), started);

        // Re-dispatch while playing: 16E8C1 only yields again.
        Assert.True(StoryActionCapture.TryCapture(memory, Context, 0xAB, out var waiting));
        Yield(memory, 0x42B);
        Assert.True(StoryActionCapture.TryProveAnimation(memory, waiting, out var waited));
        Assert.Equal(new StoryAnimationProof(StoryAnimationKind.PlayOnceWaiting, 0x21), waited);

        // 17BE1F decrements +0x128 to 1 at the sequence end; 16E817 then restores mode 1 and advances.
        memory.Word(ActorAddress(Crono) + 0x128, 1);
        Assert.True(StoryActionCapture.TryCapture(memory, Context, 0xAB, out var ending));
        memory.Word(ActorAddress(Crono) + 0x128, 0);
        memory.Word(ActorAddress(Crono) + 0x74, 1);
        memory.Word(Context + 0x18, 0x42B + 2);
        memory.Word(Context + 4, 1);
        Assert.True(StoryActionCapture.TryProveAnimation(memory, ending, out var finished));
        Assert.Equal(new StoryAnimationProof(StoryAnimationKind.PlayOnceFinished, 0x21), finished);
    }

    [Fact]
    public void AbWithANewOperandWhilePlayingRestartsRatherThanWaits()
    {
        var memory = Bedroom(0x42B, "AB22AA2000000000", Crono);
        SetActor(memory, Crono, mode: 2);
        memory.Word(ActorAddress(Crono) + 0x78, 0x21);
        memory.Word(ActorAddress(Crono) + 0x128, 2);
        Assert.True(StoryActionCapture.TryCapture(memory, Context, 0xAB, out var before));

        Yield(memory, 0x42B);
        Assert.False(StoryActionCapture.TryProveAnimation(memory, before, out _));
        ApplyAbStart(memory, 0x42B, Crono, 0x22);
        Assert.True(StoryActionCapture.TryProveAnimation(memory, before, out var proof));
        Assert.Equal(new StoryAnimationProof(StoryAnimationKind.PlayOnceStarted, 0x22), proof);
    }

    [Fact]
    public void AbFinishIsNotProvenWhileTheStateIsStillPlaying()
    {
        var memory = Bedroom(0x42B, "AB21AA2000000000", Crono);
        SetActor(memory, Crono, mode: 2);
        memory.Word(ActorAddress(Crono) + 0x78, 0x21);
        memory.Word(ActorAddress(Crono) + 0x128, 1);
        Assert.True(StoryActionCapture.TryCapture(memory, Context, 0xAB, out var before));
        memory.Word(Context + 0x18, 0x42B + 2);
        memory.Word(Context + 4, 1);
        Assert.False(StoryActionCapture.TryProveAnimation(memory, before, out _));
    }

    [Fact]
    public void AbFinishFromAnUnsetPoseReturnsToModeZeroRatherThanLooping()
    {
        // 16E8A9 stores 0xFF when the previous mode was 0; 16E8D7 then clears it and mode.
        var memory = Bedroom(0x42B, "AB21AA2000000000", Crono);
        SetActor(memory, Crono, animation: 0xFF, mode: 2);
        memory.Word(ActorAddress(Crono) + 0x78, 0x21);
        memory.Word(ActorAddress(Crono) + 0x128, 1);
        Assert.True(StoryActionCapture.TryCapture(memory, Context, 0xAB, out var before));
        memory.Word(ActorAddress(Crono) + 0x128, 0);
        memory.Word(Context + 0x18, 0x42B + 2);
        memory.Word(Context + 4, 1);
        memory.Word(ActorAddress(Crono) + 0x74, 1);
        Assert.False(StoryActionCapture.TryProveAnimation(memory, before, out _));
        memory.Word(ActorAddress(Crono) + 0x74, 0);
        memory.Word(ActorAddress(Crono) + 0x68, 0);
        Assert.True(StoryActionCapture.TryProveAnimation(memory, before, out var proof));
        Assert.Equal(new StoryAnimationProof(StoryAnimationKind.PlayOnceFinished, 0x21), proof);
    }

    [Fact]
    public void AcIsProvenAsAHeldStaticFrame()
    {
        var memory = Bedroom(0x500, "AC02000000000000", Mother);
        SetActor(memory, Mother, mode: 1, animation: 4);
        Assert.True(StoryActionCapture.TryCapture(memory, Context, 0xAC, out var before));
        Assert.False(StoryActionCapture.TryProveAnimation(memory, before, out _));

        var actor = ActorAddress(Mother);
        memory.Word(actor + 0x50, 2);
        memory.Word(actor + 0x74, 3);
        memory.Word(Context + 0x18, 0x502);
        memory.Word(Context + 4, 0);
        Assert.True(StoryActionCapture.TryProveAnimation(memory, before, out var proof));
        Assert.Equal(new StoryAnimationProof(StoryAnimationKind.StaticFrame, 2), proof);
    }

    [Fact]
    public void OtherOpcodesHaveNoAnimationProof()
    {
        var memory = Bedroom(0x676, "A0170E9100000000", Mother);
        Assert.True(StoryActionCapture.TryCapture(memory, Context, 0xA0, out var before));
        Assert.False(StoryActionCapture.TryProveAnimation(memory, before, out _));
    }

    [Fact]
    public void ReadsAnotherActorOfTheSameScriptWithinTheHeaderCount()
    {
        var memory = Bedroom(0x7C4, "E533323B3C13023B", 10);
        SetActor(memory, Crono, visual: 0, fineX: 0x900, fineY: 0x700);
        Assert.True(StoryActionCapture.TryLocate(memory, Context, 0xE5, out var location));

        Assert.True(StoryActionCapture.TryReadActor(memory, location, Crono, out var crono));
        Assert.Equal((Crono, 0x900, 0x700), (crono.Index, crono.FineX, crono.FineY));
        Assert.False(StoryActionCapture.TryReadActor(memory, location, 0x23, out _));
        Assert.False(StoryActionCapture.TryReadActor(memory, location, -1, out _));
    }

    [Fact]
    public void SameScriptDetectsASceneScriptOrEngineChange()
    {
        var memory = Bedroom(0x676, "A0170E9100000000", Mother);
        Assert.True(StoryActionCapture.TryLocate(memory, Context, 0xA0, out var location));
        Assert.True(StoryActionCapture.IsSameScript(memory, location));
        foreach (var change in new Action<Memory>[]
                 {
                     m => m.Word(Field + 0x1010, 3),
                     m => m.Word(Context + 0xBB4, 325),
                     m => m.Word(Context, Data + 0x40000),
                     m => m.Word(Context + 0x850, Field + 0x4000),
                 })
        {
            var changed = Bedroom(0x676, "A0170E9100000000", Mother);
            change(changed);
            Assert.False(StoryActionCapture.IsSameScript(changed, location));
        }
    }

    [Fact]
    public void RestoredControlRequiresTheOriginalNativeFieldTransition()
    {
        var memory = Bedroom(0x500, "E301000000000000", Mother);
        Assert.True(StoryActionCapture.TryCapture(memory, Context, 0xE3, out var before));
        Assert.False(StoryActionCapture.TryProvePlayerControlRestored(memory, before));
        memory.Word(Field + 0x108C, 1);
        Assert.True(StoryActionCapture.TryProvePlayerControlRestored(memory, before));
        Assert.False(StoryActionCapture.TryProvePlayerControlRestored(memory, before with { Control = 1 }));
        Assert.False(StoryActionCapture.TryProvePlayerControlRestored(memory, before with { Bytes = "E300000000000000" }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AChangedOwnerActorOrInstructionCannotRearmDescriptions(int change)
    {
        var memory = Bedroom(0x500, "E301000000000000", Mother);
        Assert.True(StoryActionCapture.TryCapture(memory, Context, 0xE3, out var before));
        memory.Word(Field + 0x108C, 1);
        switch (change)
        {
            case 0: memory.Word(Context + 0xBB4, 325); break;
            case 1: memory.Word(Field + 0x1010, 3); break;
            case 2: memory.Word(Field + 0x1180, 2); break;
            case 3: memory.Add(Data + 0x12001 + 0x500, [0xE3, 0]); break;
        }
        Assert.False(StoryActionCapture.TryProvePlayerControlRestored(memory, before));
    }

    private static StoryActionSnapshot Capture(Memory memory)
    {
        Assert.True(StoryActionCapture.TryCapture(memory, Context, 0xA0, out var snapshot));
        return snapshot;
    }

    private static uint ActorAddress(int actor) => Actors + 0x6940 + (uint)actor * 0x154;

    private static void ApplyAa(Memory memory, uint pc, int actor, uint animation)
    {
        var address = ActorAddress(actor);
        memory.Word(address + 0x68, animation);
        memory.Word(address + 0x74, 1);
        memory.Word(address + 0x64, 0);
        memory.Word(address + 0x128, 0);
        memory.Word(address + 0x6C, 0);
        memory.Word(Context + 4, 1);
        memory.Word(Context + 0x24, pc + 1);
        memory.Word(Context + 0x18, pc + 2);
    }

    private static void ApplyAbStart(Memory memory, uint pc, int actor, uint animation)
    {
        var address = ActorAddress(actor);
        memory.Word(address + 0x78, animation);
        memory.Word(address + 0x74, 2);
        memory.Word(address + 0x128, 2);
        Yield(memory, pc);
    }

    private static void Yield(Memory memory, uint pc)
    {
        memory.Word(Context + 4, 0);
        memory.Word(Context + 0x24, pc);
        memory.Word(Context + 0x18, pc);
    }

    private static void SetCamera(Memory memory, int left, int right, int top, int bottom)
    {
        memory.Word(Camera + 0x10, (uint)left);
        memory.Word(Camera + 0x14, (uint)right);
        memory.Word(Camera + 0x18, (uint)top);
        memory.Word(Camera + 0x1C, (uint)bottom);
        memory.Word(Camera + 0x1A8, 0);
        memory.Word(Camera + 0x1B4, 0);
    }

    private static void SetActor(Memory memory, int actor, int classTag = 4, int visual = 0x17, int draw = 1,
        int facing = 0, int animation = 0, int mode = 0, int frame = 0, int fineX = 0x800, int fineY = 0x600,
        bool window = true)
    {
        var address = ActorAddress(actor);
        memory.Word(address + 0x40, (uint)classTag);
        memory.Word(address + 0x44, (uint)visual);
        memory.Word(address + 0xD0, (uint)draw);
        memory.Word(address + 0x60, (uint)facing);
        memory.Word(address + 0x68, (uint)animation);
        memory.Word(address + 0x74, (uint)mode);
        memory.Word(address + 0x50, (uint)frame);
        memory.Word(address + 0x84, (uint)fineX);
        memory.Word(address + 0x90, (uint)fineY);
        memory.Word(address + 0x20, window ? 0x80u : 0);
    }

    /// <summary>Bedroom Atel_0324 shape: 0x23 actors, scene 2, script 324.</summary>
    private static Memory Bedroom(uint pc, string bytes, int actor)
    {
        var memory = new Memory();
        memory.Add(Context, new byte[0xBB8]);
        memory.Word(Context, Data);
        memory.Word(Context + 0x24, pc);
        memory.Word(Context + 0x40, Actors);
        memory.Word(Context + 0x850, Field);
        memory.Word(Context + 0x854, Camera);
        memory.Word(Context + 0xBB4, 324);
        memory.Add(Field + 0x1000, new byte[0x200]);
        memory.Word(Field + 0x1010, 2);
        memory.Word(Field + 0x1180, (uint)actor * 2);
        memory.Add(Data + 0x12000, [0x23]);
        memory.Add(Data + 0x12001 + pc, Convert.FromHexString(bytes));
        for (var index = 0; index < 0x23; index++) memory.Add(ActorAddress(index), new byte[0x154]);
        memory.Word(Actors + 0x110B0, 0);
        memory.Add(Camera, new byte[0x1C0]);
        SetCamera(memory, 0, 31, 0, 27);
        SetActor(memory, actor);
        return memory;
    }

    private sealed class Memory : IReadableMemory
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
