using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

/// <summary>Who is executing what at a field-script dispatch (1619E0). <c>Actor</c> is
/// <c>[field+0x1180] &gt;&gt; 1</c>; <c>ScriptId</c> is the Atel number at ctx+0xBB4.
/// <c>Camera</c> is 0 when ctx+0x854 is not the actor store's own camera block.</summary>
public readonly record struct StoryScriptLocation(uint Context, uint Data, uint Actors, uint Field, uint Camera,
    int Scene, int ScriptId, int Actor, int ActorCount, uint Address, int Opcode);

/// <summary>Raw actor words. <c>InNativeWindow</c> is +0x20 bit 0x80, which 17A6C0/17A860 keep
/// for a processing window wider than the screen (6 columns either side, 2 rows above and 9
/// below, in 8-pixel units); it is not a visibility test. <c>OnScreen</c> is the actor's
/// anchor inside the native camera bounds while drawn, or null if the camera is unavailable.</summary>
public sealed record StoryActorState(int Index, int ClassTag, int Visual, int DrawMode, int Facing,
    int Animation, int AnimationMode, int StaticFrame, int PlayOnceAnimation, int PlayOnceState,
    int FineX, int FineY, bool InNativeWindow, bool? OnScreen)
{
    /// <summary>17A6C0's own drawn test: class bit 0x80 clear and a positive draw byte.</summary>
    public bool Drawn => (ClassTag & 0x80) == 0 && DrawMode != 0 && (sbyte)DrawMode >= 0;
}

/// <summary>A small dispatch-time snapshot. <c>TextboxState</c> is field+0x10B4 (BB 16FBB0 sets
/// 1 and waits while it is non-zero); <c>ScriptPause</c> is field+0x1088.</summary>
public sealed record StoryActionSnapshot(StoryScriptLocation Location, string Bytes, StoryActorState ActorState,
    int StoryPoint, uint Control, uint ScriptPause, uint TextboxState, int TextboxActor)
{
    public int Operand => Convert.ToByte(Bytes.Substring(2, 2), 16);
    public bool TextboxOpen => TextboxState != 0;
}

public enum StoryAnimationKind { Looping, StaticFrame, PlayOnceStarted, PlayOnceWaiting, PlayOnceFinished }

public readonly record struct StoryAnimationProof(StoryAnimationKind Kind, int Value);

public static class StoryActionCapture
{
    private const uint ScriptHeader = 0x12000, ScriptBytes = 0x12001, StoryPointOffset = 0x110B0;
    private const uint CameraOffset = 0x1327C, ScriptIdOffset = 0xBB4, ScriptActorOffset = 0x1180;
    private const uint ActorRecord = FieldNavigationCapture.ActorStride;
    private const uint FieldBlock = 0x1088, FieldBlockLength = 0x40;

    /// <summary>Owners, scene, script id, executing actor and PC; the script byte at PC must be
    /// the dispatched opcode. Nine reads; call before deciding whether a full capture is needed.</summary>
    public static bool TryLocate(IReadableMemory memory, nuint context, int opcode, out StoryScriptLocation location)
    {
        location = default;
        if (opcode is < 0 or > 0xFF) return false;
        try
        {
            Span<byte> head = stackalloc byte[0x44];
            Span<byte> owners = stackalloc byte[8];
            Span<byte> one = stackalloc byte[1];
            if (!Fits(context, ScriptIdOffset + 4) || !memory.TryRead(context, head) ||
                !memory.TryRead(context + 0x850, owners) ||
                !Word(memory, context + ScriptIdOffset, out var scriptId) || scriptId > 9999) return false;
            var data = Read(head, 0);
            var pc = Read(head, 0x24);
            var actors = Read(head, 0x40);
            var field = Read(owners, 0);
            var camera = Read(owners, 4);
            if (!Fits(data, ScriptBytes + 0x10000 + 8) || pc > 0xFFFF ||
                !Fits(actors, CameraOffset + 0x1C0) || !Fits(field, FieldBlock + FieldBlockLength) ||
                !Fits(field, ScriptActorOffset + 4) ||
                !Word(memory, field + FieldNavigationCapture.FieldStateSceneIdOffset, out var scene) || scene > 0xFFFF ||
                !Word(memory, field + ScriptActorOffset, out var raw) ||
                !memory.TryRead(data + ScriptHeader, one)) return false;
            var count = one[0];
            if (count is 0 or > FieldNavigationCapture.MaximumActors || (raw & 1) != 0 || raw >= count * 2u ||
                !memory.TryRead(data + ScriptBytes + pc, one) || one[0] != opcode) return false;
            location = new((uint)context, data, actors, field, camera == actors + CameraOffset ? camera : 0,
                (int)scene, (int)scriptId, (int)(raw >> 1), count, pc, opcode);
            return true;
        }
        catch (Exception) { return false; }
    }

    public static bool TryCapture(IReadableMemory memory, nuint context, int opcode, out StoryActionSnapshot snapshot)
    {
        snapshot = null!;
        return TryLocate(memory, context, opcode, out var location) && TryCapture(memory, location, out snapshot);
    }

    /// <summary>Instruction bytes, the executing actor, story point and field control/textbox words.</summary>
    public static bool TryCapture(IReadableMemory memory, StoryScriptLocation location, out StoryActionSnapshot snapshot)
    {
        snapshot = null!;
        try
        {
            Span<byte> bytes = stackalloc byte[8];
            Span<byte> block = stackalloc byte[(int)FieldBlockLength];
            if (!memory.TryRead(location.Data + ScriptBytes + location.Address, bytes) || bytes[0] != location.Opcode ||
                !TryReadActor(memory, location, location.Actor, out var actor) ||
                !Word(memory, location.Actors + StoryPointOffset, out var point) ||
                !memory.TryRead(location.Field + FieldBlock, block)) return false;
            var textbox = Read(block, 0x10B4 - FieldBlock);
            var owner = Read(block, 0x10C4 - FieldBlock);
            var textboxActor = textbox != 0 && (owner & 1) == 0 && owner < location.ActorCount * 2u ? (int)(owner >> 1) : -1;
            snapshot = new(location, Convert.ToHexString(bytes), actor, (int)point,
                Read(block, 0x108C - FieldBlock), Read(block, 0), textbox, textboxActor);
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Reads any actor of the located script in one record read.</summary>
    public static bool TryReadActor(IReadableMemory memory, StoryScriptLocation location, int actor, out StoryActorState state)
    {
        state = null!;
        if (actor < 0 || actor >= location.ActorCount) return false;
        try
        {
            var record = new byte[ActorRecord];
            if (!memory.TryRead(location.Actors + FieldNavigationCapture.ActorArrayOffset + (uint)actor * ActorRecord, record))
                return false;
            int Int(uint offset) => (int)Read(record, offset);
            state = new(actor, Int(FieldNavigationCapture.ActorClassTagOffset), Int(FieldNavigationCapture.ActorVisualIndexOffset),
                Int(FieldNavigationCapture.ActorDrawModeOffset), Int(FieldNavigationCapture.ActorFacingOffset),
                Int(0x68), Int(0x74), Int(0x50), Int(0x78), Int(0x128),
                Int(FieldNavigationCapture.ActorFineXOffset), Int(FieldNavigationCapture.ActorFineYOffset),
                (Int(FieldNavigationCapture.ActorActivationBindingOffset) & 0x80) != 0, null);
            if (location.Camera != 0 && TryViewport(memory, location.Camera, out var viewport))
                state = state with { OnScreen = state.Drawn && viewport.Contains(state.FineX, state.FineY) };
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>False once the engine, script data, actor store, field state, scene or Atel id
    /// has changed: drop held descriptions then.</summary>
    public static bool IsSameScript(IReadableMemory memory, StoryScriptLocation location)
    {
        try
        {
            return Word(memory, location.Context, out var data) && data == location.Data &&
                Word(memory, location.Context + 0x40, out var actors) && actors == location.Actors &&
                Word(memory, location.Context + 0x850, out var field) && field == location.Field &&
                Word(memory, location.Context + ScriptIdOffset, out var script) && script == location.ScriptId &&
                Word(memory, field + FieldNavigationCapture.FieldStateSceneIdOffset, out var scene) && scene == location.Scene;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Rearms gesture deduplication only after the native E3 01 handler
    /// changes the same field's control word from scripted to player control.</summary>
    public static bool TryProvePlayerControlRestored(IReadableMemory memory, StoryActionSnapshot before)
    {
        var location = before.Location;
        if (location.Opcode != 0xE3 || before.Operand != 1 || before.Control != 0) return false;
        try
        {
            return IsSameScript(memory, location) &&
                Word(memory, location.Field + ScriptActorOffset, out var actor) && actor == location.Actor * 2u &&
                TryCapture(memory, location, out var after) && after.Bytes == before.Bytes && after.Control == 1;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Call immediately after the original handler returns. Proves the native
    /// AA (16E590), AB (16E7A0) or AC (16EA90) result for the same owners, actor and
    /// instruction bytes; the operand alone never qualifies.</summary>
    public static bool TryProveAnimation(IReadableMemory memory, StoryActionSnapshot before, out StoryAnimationProof proof)
    {
        proof = default;
        var location = before.Location;
        if (location.Opcode is not (0xAA or 0xAB or 0xAC)) return false;
        try
        {
            Span<byte> bytes = stackalloc byte[8];
            Span<byte> head = stackalloc byte[0x28];
            if (!IsSameScript(memory, location) ||
                !Word(memory, location.Field + ScriptActorOffset, out var raw) || raw != location.Actor * 2u ||
                !memory.TryRead(location.Data + ScriptBytes + location.Address, bytes) ||
                Convert.ToHexString(bytes) != before.Bytes || !memory.TryRead(location.Context, head) ||
                !TryReadActor(memory, location, location.Actor, out var actor)) return false;
            var pc = location.Address;
            var advanced = (head[4] & 1) != 0;
            var current = Read(head, 0x24);
            var next = Read(head, 0x18);
            var operand = before.Operand;
            var old = before.ActorState;
            switch (location.Opcode)
            {
                case 0xAA:
                    if (!advanced || current != pc + 1 || next != pc + 2 || actor.Animation != operand ||
                        actor.AnimationMode != 1 || actor.PlayOnceState != 0) return false;
                    proof = new(StoryAnimationKind.Looping, operand);
                    return true;
                case 0xAC:
                    if (advanced || current != pc || next != pc + 2 || actor.StaticFrame != operand ||
                        actor.AnimationMode != 3) return false;
                    proof = new(StoryAnimationKind.StaticFrame, operand);
                    return true;
            }
            // AB jump table 56E904: +0x128 == 0 starts (16E85B), == 1 finishes (16E817),
            // otherwise the same operand waits (16E8C1) and a new one restarts (16E87F).
            if (old.PlayOnceState == 1)
            {
                var looped = old.Animation != 0xFF;
                if (!advanced || current != pc || next != pc + 2 || actor.PlayOnceState != 0 ||
                    actor.AnimationMode != (looped ? 1 : 0) || actor.Animation != (looped ? old.Animation : 0)) return false;
                proof = new(StoryAnimationKind.PlayOnceFinished, old.PlayOnceAnimation);
                return true;
            }
            var waiting = old.PlayOnceState != 0 && old.PlayOnceAnimation == operand;
            if (advanced || current != pc || next != pc || actor.PlayOnceAnimation != operand ||
                actor.PlayOnceState != (waiting ? old.PlayOnceState : 2) || actor.AnimationMode != (waiting ? old.AnimationMode : 2))
                return false;
            proof = new(waiting ? StoryAnimationKind.PlayOnceWaiting : StoryAnimationKind.PlayOnceStarted, operand);
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>The same native camera window as <see cref="FieldEnvironmentCapture.TryViewport"/>.</summary>
    private static bool TryViewport(IReadableMemory memory, uint camera, out FieldViewport viewport)
    {
        viewport = default;
        Span<byte> edges = stackalloc byte[16];
        if (!memory.TryRead(camera + 0x10u, edges) ||
            !Word(memory, camera + 0x1A8u, out var rawPhaseX) || !Word(memory, camera + 0x1B4u, out var rawPhaseY)) return false;
        int left = (int)Read(edges, 0), right = (int)Read(edges, 4), top = (int)Read(edges, 8), bottom = (int)Read(edges, 12);
        int phaseX = (int)rawPhaseX, phaseY = (int)rawPhaseY;
        if (left is < -256 or > 512 || top is < -256 or > 512 || right <= left || right - left > 256 ||
            bottom <= top || bottom - top > 256 || phaseX is < 0 or > 15 || phaseY is < 0 or > 15) return false;
        viewport = new((left * 8 + (phaseX & 7)) * 16, (top * 8 + (phaseY & 7)) * 16,
            ((right + 1) * 8 + (phaseX & 7)) * 16, ((bottom + 1) * 8 + (phaseY & 7)) * 16);
        return true;
    }

    private static uint Read(ReadOnlySpan<byte> bytes, uint offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes[(int)offset..]);

    private static bool Fits(nuint address, uint length) => address != 0 && (ulong)address + length - 1 <= uint.MaxValue;

    private static bool Word(IReadableMemory memory, nuint address, out uint value)
    {
        value = 0;
        Span<byte> bytes = stackalloc byte[4];
        if (!Fits(address, 4) || !memory.TryRead(address, bytes)) return false;
        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        return true;
    }
}
