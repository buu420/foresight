using System.Buffers.Binary;
using System.Security.Cryptography;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

/// <summary>Visually reviewed opening and fair actions. Identity only: Root owns the words.</summary>
public enum StorySceneAction
{
    CurtainsOpen,
    MotherHeadsDownstairs,
    BoyGetsOutOfBedAndStretches,
    FairCollision,
    GirlGetsUp,
    BoyGetsUp,
    GirlHops,
}

/// <summary><c>Subject</c> is the visible actor the action is about; for the curtains that is
/// Mother (actor 8), not the invisible controller (actor 10) executing the copy. <c>Partner</c>
/// is the boy for the collision. <c>RepeatDispatch</c> is set when the actor's saved PC (+0x48)
/// already equals this PC, i.e. the run resumed a yielded instruction.</summary>
public sealed record StorySceneActionCandidate(StorySceneAction Action, StoryActionSnapshot Before,
    StoryActorState Subject, StoryActorState? Partner, bool RepeatDispatch);

/// <summary>Matches a dispatch to one of seven reviewed anchors in the installed scripts and
/// proves the native result after the original handler. Evidence:
/// artifacts/research/story-actions-20261009/scene-capture/findings.md.</summary>
public static class StorySceneActionCapture
{
    public const string BedroomScriptSha256 = "7AAC21A8D18D4908F4655D070CDD47ECA95939472EE66625CF12FC713B19C765";
    public const string PlazaRearScriptSha256 = "574098067D910D56A9B30707F3DC837C56990AF788CAE55374A9C90CF4C04307";

    private enum Proof { TileCopyQueued, MoveStarted, StaticFrame, Looping, CountedFinished, AnimationReset }

    private sealed record Anchor(StorySceneAction Action, int Scene, int Script, int Length, string Sha256,
        int Actor, uint Pc, string Bytes, int Subject, int Partner, int MinStory, int MaxStory, Proof Proof);

    // Runtime PCs are file offsets - 1. Story gates mirror the native branches: 18 03 1E / the
    // fn1 curtain path (< 3), Marle's touch function 16 00 06 03 (< 6), the boy's rise ending
    // after she sets 6 (<= 6) and her accepted invitation 16 00 06 04 before 5A 08 (6..7).
    private static readonly Anchor[] Anchors =
    [
        new(StorySceneAction.CurtainsOpen, 2, 324, 2504, BedroomScriptSha256, 10, 0x7C4, "E533323B3C13023B", 8, -1, 0, 2, Proof.TileCopyQueued),
        new(StorySceneAction.MotherHeadsDownstairs, 2, 324, 2504, BedroomScriptSha256, 8, 0x676, "A0170E", 8, -1, 0, 2, Proof.MoveStarted),
        new(StorySceneAction.BoyGetsOutOfBedAndStretches, 2, 324, 2504, BedroomScriptSha256, 1, 0x426, "961808", 1, -1, 0, 2, Proof.MoveStarted),
        new(StorySceneAction.FairCollision, 439, 74, 3010, PlazaRearScriptSha256, 3, 0x6CE, "AC6A", 3, 1, 0, 5, Proof.StaticFrame),
        new(StorySceneAction.GirlGetsUp, 439, 74, 3010, PlazaRearScriptSha256, 3, 0x69B, "B71302", 3, -1, 0, 5, Proof.CountedFinished),
        new(StorySceneAction.BoyGetsUp, 439, 74, 3010, PlazaRearScriptSha256, 1, 0x539, "AE", 1, -1, 0, 6, Proof.AnimationReset),
        new(StorySceneAction.GirlHops, 439, 74, 3010, PlazaRearScriptSha256, 3, 0x63C, "AA0B", 3, -1, 6, 7, Proof.Looping),
    ];

    private const uint ScriptHeader = 0x12000, ScriptActorOffset = 0x1180;

    /// <summary>Cheap filter on a located dispatch: scene, Atel id, executing actor, PC and opcode.</summary>
    public static bool Watches(StoryScriptLocation location) => Find(location) is not null;

    /// <summary>Pre-native match: the full loaded script equals the installed digest, the
    /// instruction bytes and story gate match, and the subject (and partner) are drawn on screen.</summary>
    public static bool TryMatch(IReadableMemory memory, StoryActionSnapshot before, out StorySceneActionCandidate candidate)
    {
        candidate = null!;
        if (Find(before.Location) is not { } anchor) return false;
        try
        {
            if (!before.Bytes.StartsWith(anchor.Bytes, StringComparison.Ordinal) ||
                before.StoryPoint < anchor.MinStory || before.StoryPoint > anchor.MaxStory ||
                !ScriptMatches(memory, before.Location.Data, anchor) ||
                !Visible(memory, before.Location, anchor.Subject, out var subject)) return false;
            StoryActorState? partner = null;
            if (anchor.Partner >= 0 && !Visible(memory, before.Location, anchor.Partner, out partner)) return false;
            var actor = before.ActorState;
            switch (anchor.Proof)
            {
                case Proof.MoveStarted:
                    // 16CE00 completes at once when the actor already stands on the target tile.
                    var operand = Convert.FromHexString(anchor.Bytes);
                    if (actor.FineX >> 8 == operand[1] && actor.FineY >> 8 == operand[2]) return false;
                    break;
                case Proof.CountedFinished:
                    // B7 jump table 56EA78: only +0x128 == 1 (set by 17BE1F at the last loop end) finishes.
                    if (actor.PlayOnceState != 1 || actor.PlayOnceAnimation != Convert.FromHexString(anchor.Bytes)[1]) return false;
                    break;
            }
            if (!Word(memory, ActorAddress(before.Location, before.Location.Actor) + 0x48, out var saved)) return false;
            candidate = new(anchor.Action, before, subject, partner, saved == before.Location.Address);
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Call immediately after the original handler returns.</summary>
    public static bool TryComplete(IReadableMemory memory, StorySceneActionCandidate candidate)
    {
        var before = candidate.Before;
        var location = before.Location;
        if (Find(location) is not { } anchor || anchor.Action != candidate.Action) return false;
        try
        {
            Span<byte> bytes = stackalloc byte[8];
            Span<byte> head = stackalloc byte[0x28];
            if (!StoryActionCapture.IsSameScript(memory, location) ||
                !Word(memory, location.Field + ScriptActorOffset, out var raw) || raw != location.Actor * 2u ||
                !memory.TryRead(location.Data + ScriptHeader + 1 + location.Address, bytes) ||
                Convert.ToHexString(bytes) != before.Bytes || !memory.TryRead(location.Context, head)) return false;
            var pc = location.Address;
            var advanced = (head[4] & 1) != 0;
            var current = Read(head, 0x24);
            var next = Read(head, 0x18);
            switch (anchor.Proof)
            {
                case Proof.TileCopyQueued:
                {
                    // 171C40 copies the seven operand bytes to data+0x2E13E, requests the copy
                    // (+0x1211C bit 0x20) and yields with the PC already past the instruction.
                    Span<byte> queued = stackalloc byte[7];
                    return !advanced && current == pc && next == pc + 8 &&
                        memory.TryRead(location.Data + 0x2E13Eu, queued) && queued.SequenceEqual(bytes[1..]) &&
                        Word(memory, location.Actors + 0x1211Cu, out var flags) && (flags & 0x20) != 0;
                }
                case Proof.MoveStarted:
                    // 16CE00 stores the target in field+0x20D8/+0x20DC, starts the step and yields.
                    return !advanced && current == pc && next == pc &&
                        Word(memory, location.Field + 0x20D8u, out var x) && x == bytes[1] &&
                        Word(memory, location.Field + 0x20DCu, out var y) && y == bytes[2];
                case Proof.StaticFrame:
                    return StoryActionCapture.TryProveAnimation(memory, before, out var frame) &&
                        frame == new StoryAnimationProof(StoryAnimationKind.StaticFrame, bytes[1]);
                case Proof.Looping:
                    return StoryActionCapture.TryProveAnimation(memory, before, out var loop) &&
                        loop == new StoryAnimationProof(StoryAnimationKind.Looping, bytes[1]);
            }
            if (!advanced || current != pc || !StoryActionCapture.TryReadActor(memory, location, location.Actor, out var actor))
                return false;
            if (anchor.Proof == Proof.AnimationReset)
                // AE 16E720: animation 0, mode 0 (the facing pose), +0x128 cleared.
                return next == pc + 1 && actor.Animation == 0 && actor.AnimationMode == 0 && actor.PlayOnceState == 0;
            // B7 finish 16EA0E: 0xFF returns to animation 0/mode 0 (16EA35), otherwise mode 1 (16EA59).
            var unset = before.ActorState.Animation == 0xFF;
            return next == pc + 3 && actor.PlayOnceState == 0 && actor.AnimationMode == (unset ? 0 : 1) &&
                actor.Animation == (unset ? 0 : before.ActorState.Animation);
        }
        catch (Exception) { return false; }
    }

    private static Anchor? Find(StoryScriptLocation location)
    {
        foreach (var anchor in Anchors)
            if (anchor.Scene == location.Scene && anchor.Script == location.ScriptId && anchor.Actor == location.Actor &&
                anchor.Pc == location.Address && Convert.ToByte(anchor.Bytes[..2], 16) == location.Opcode)
                return anchor;
        return null;
    }

    private static bool ScriptMatches(IReadableMemory memory, uint data, Anchor anchor)
    {
        var script = new byte[anchor.Length];
        return memory.TryRead(data + ScriptHeader, script) && Convert.ToHexString(SHA256.HashData(script)) == anchor.Sha256;
    }

    private static bool Visible(IReadableMemory memory, StoryScriptLocation location, int actor, out StoryActorState state) =>
        StoryActionCapture.TryReadActor(memory, location, actor, out state) && state.Drawn && state.OnScreen == true;

    private static uint ActorAddress(StoryScriptLocation location, int actor) =>
        location.Actors + FieldNavigationCapture.ActorArrayOffset + (uint)actor * FieldNavigationCapture.ActorStride;

    private static uint Read(ReadOnlySpan<byte> bytes, uint offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[(int)offset..]);

    private static bool Word(IReadableMemory memory, nuint address, out uint value)
    {
        value = 0;
        Span<byte> bytes = stackalloc byte[4];
        if (address == 0 || (ulong)address + 3 > uint.MaxValue || !memory.TryRead(address, bytes)) return false;
        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        return true;
    }
}
