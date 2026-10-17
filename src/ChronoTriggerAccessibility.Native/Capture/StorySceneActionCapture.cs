using System.Buffers.Binary;
using System.Security.Cryptography;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

/// <summary>Visually reviewed opening and fair actions, and the Zeal plaza books (Enhasa and
/// Kajar). Identity only: Root owns the words. Books A, B and C are fixed native actors per
/// room; their letters carry no element, order or outcome.</summary>
public enum StorySceneAction
{
    CurtainsOpen,
    MotherHeadsDownstairs,
    BoyGetsOutOfBedAndStretches,
    FairCollision,
    GirlGetsUp,
    BoyGetsUp,
    GirlHops,
    BookAOpens,
    BookACloses,
    BookBOpens,
    BookBCloses,
    BookCOpens,
    BookCCloses,
    PassageOpens,
    NuApproachesParty,
    NuRaisesArms,
    NuSettlesDown,
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
    public const string EnhasaPlazaScriptSha256 = "A912C2735A866CE4A2CE0009BE97BCAF5CF7E94C1BF67411CCFD0FA64657C880";
    public const string KajarPlazaScriptSha256 = "DCCC62DA4C8A32EE6ED19E470111921DBA0A3F427194E14BF07CB3CB24578960";
    public const string EpochStorageScriptSha256 = "31EB20EC02C914BE51D48F153C1FAE386757EAC1B0F6F1349F0702BE3A532B65";
    public const string KeepEntranceScriptSha256 = "45383E9FE6637B86C95D4937BE39EB5E9A395BAE48B651B78D210FD80A86B7D1";

    private enum Proof { TileCopyQueued, MoveStarted, StaticFrame, Looping, CountedFinished, AnimationReset, TimedMoveStarted }

    /// <summary>The exact visible object: class tag, visual and tile of the subject actor.</summary>
    private sealed record Identity(int Class, int Visual, int TileX, int TileY);

    /// <summary>A tile rectangle (exclusive right/bottom) that must be inside the native camera.</summary>
    private sealed record Tiles(int Left, int Top, int Right, int Bottom);

    private sealed record Anchor(StorySceneAction Action, int Scene, int Script, int Length, string Sha256,
        int Actor, uint Pc, string Bytes, int Subject, int Partner, int MinStory, int MaxStory, Proof Proof,
        Identity? Book = null, Tiles? Passage = null, int? SubjectClass = null, int? SubjectVisual = null);

    private static Anchor Nu(StorySceneAction action, uint pc, string bytes, Proof proof) =>
        new(action, 243, 163, 2159, EpochStorageScriptSha256, 14, pc, bytes, 14, -1, 0xAB, 0xFF, proof,
            SubjectClass: 5, SubjectVisual: 0);

    private const int EnhasaPlaza = 355, KajarPlaza = 357, EnhasaScript = 208, KajarScript = 209;
    private const int EnhasaLength = 2796, KajarLength = 1861;
    private static readonly Identity EnhasaA = new(4, 0x73, 16, 24), EnhasaB = new(4, 0x80, 27, 12);
    // Enhasa's third closed book is drawn in the map; its Confirm marker (class 7, actor 13) stands on it.
    // The open sprite (actor 12) is hidden or parked at (48,48) until that marker shows it.
    private static readonly Identity EnhasaC = new(7, 0x00, 6, 7);
    private static readonly Identity KajarA = new(4, 0x73, 13, 15), KajarB = new(4, 0x80, 3, 10), KajarC = new(4, 0x88, 22, 24);

    private static Anchor Book(StorySceneAction action, int scene, int actor, uint pc, string bytes, Identity book) =>
        scene == EnhasaPlaza
            ? new(action, scene, EnhasaScript, EnhasaLength, EnhasaPlazaScriptSha256, actor, pc, bytes, actor, -1, 0, 0xFF, Proof.TileCopyQueued, book)
            : new(action, scene, KajarScript, KajarLength, KajarPlazaScriptSha256, actor, pc, bytes, actor, -1, 0, 0xFF, Proof.TileCopyQueued, book);

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
        // Zeal plaza books. Each player Confirm runs exactly one E5 copy_tiles_wait: the open
        // (2F) or closed book tile, graphics only (flags 41). Actor 0's reload reapply uses E4
        // and is not watched. The third correct book's last E5 frame (flags 3B, with collision)
        // completes the passage; its earlier two frames and the counter are not watched.
        Book(StorySceneAction.BookAOpens, EnhasaPlaza, 10, 0x674, "E52F1F2F1F101841", EnhasaA),
        Book(StorySceneAction.BookACloses, EnhasaPlaza, 10, 0x662, "E52E1F2E1F101841", EnhasaA),
        Book(StorySceneAction.BookBOpens, EnhasaPlaza, 11, 0x6ED, "E52F1F2F1F1B0C41", EnhasaB),
        Book(StorySceneAction.BookBCloses, EnhasaPlaza, 11, 0x6DB, "E52D1F2D1F1B0C41", EnhasaB),
        Book(StorySceneAction.BookCOpens, EnhasaPlaza, 13, 0x746, "E52F1F2F1F060741", EnhasaC),
        Book(StorySceneAction.BookCCloses, EnhasaPlaza, 13, 0x731, "E52E1F2E1F060741", EnhasaC),
        Book(StorySceneAction.PassageOpens, EnhasaPlaza, 10, 0x6AB, "E5271C281F0C123B", EnhasaA) with { Passage = new(12, 18, 14, 22) },
        Book(StorySceneAction.BookAOpens, KajarPlaza, 11, 0x4EE, "E52F2F2F2F0D0F41", KajarA),
        Book(StorySceneAction.BookACloses, KajarPlaza, 11, 0x4DE, "E52D2F2D2F0D0F41", KajarA),
        Book(StorySceneAction.BookBOpens, KajarPlaza, 12, 0x565, "E52F2F2F2F030A41", KajarB),
        Book(StorySceneAction.BookBCloses, KajarPlaza, 12, 0x555, "E52E2F2E2F030A41", KajarB),
        Book(StorySceneAction.BookCOpens, KajarPlaza, 13, 0x5A4, "E52F2F2F2F161841", KajarC),
        Book(StorySceneAction.BookCCloses, KajarPlaza, 13, 0x594, "E52E2F2E2F161841", KajarC),
        Book(StorySceneAction.PassageOpens, KajarPlaza, 11, 0x525, "E5272C282F110B3B", KajarA) with { Passage = new(17, 11, 19, 15) },
        // Keep entrance: final opening frame only, never the earlier animation
        // or actor0's E4 reload. The visible doorway supplies the camera proof.
        new(StorySceneAction.PassageOpens, 165, 246, 2626, KeepEntranceScriptSha256,
            14, 0x69E, "E50427072B061A3B", 14, -1, 0, 0xFF, Proof.TileCopyQueued,
            Book: new(7, 0, 7, 29), Passage: new(6, 26, 10, 31)),
        // Exact Nu (load_enemy 0 -> c263) dispatches in the storage-room introduction
        // and the later player interaction. Ordinary load-time pose reapplication is absent.
        Nu(StorySceneAction.NuApproachesParty, 0x610, "92C0E0", Proof.TimedMoveStarted),
        Nu(StorySceneAction.NuRaisesArms, 0x5FF, "AC0C", Proof.StaticFrame),
        Nu(StorySceneAction.NuRaisesArms, 0x639, "AC0C", Proof.StaticFrame),
        Nu(StorySceneAction.NuSettlesDown, 0x61D, "AC0D", Proof.StaticFrame),
        Nu(StorySceneAction.NuSettlesDown, 0x608, "AC0D", Proof.StaticFrame),
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
                !Subject(memory, before.Location, anchor, out var subject)) return false;
            StoryActorState? partner = null;
            if (anchor.Partner >= 0 && !Visible(memory, before.Location, anchor.Partner, out partner)) return false;
            var actor = before.ActorState;
            switch (anchor.Proof)
            {
                case Proof.TimedMoveStarted:
                    if (!Word(memory, ActorAddress(before.Location, before.Location.Actor) + 0xCC, out var moving) || moving != 0)
                        return false;
                    break;
                case Proof.StaticFrame when anchor.SubjectClass.HasValue:
                    if (actor.AnimationMode == 3 && actor.StaticFrame == Convert.FromHexString(anchor.Bytes)[1]) return false;
                    break;
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
            if (anchor.SubjectClass.HasValue && !Subject(memory, location, anchor, out _)) return false;
            var pc = location.Address;
            var advanced = (head[4] & 1) != 0;
            var current = Read(head, 0x24);
            var next = Read(head, 0x18);
            switch (anchor.Proof)
            {
                case Proof.TimedMoveStarted:
                {
                    // 16CB50 initializes a timed move and yields at this PC; 17B1B0
                    // derives the actual velocity from its angle and the actor's speed.
                    // The requested angle alone does not prove the Nu started moving.
                    var address = ActorAddress(location, location.Actor);
                    return !advanced && current == pc && next == pc &&
                        Word(memory, address + 0xCC, out var started) && started == 1 &&
                        Word(memory, address + 0xC8, out var remaining) && remaining == bytes[2] &&
                        Word(memory, location.Field + 0x20CC, out var angle) && angle == bytes[1] &&
                        Word(memory, address + 0x94, out var xVelocity) && xVelocity == 0 &&
                        Word(memory, address + 0xAC, out var yVelocity) && (int)yVelocity < 0;
                }
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

    /// <summary>The subject must be the proven visible object (exact class, visual and tile).
    /// A passage is judged by its own tiles on camera, not by the book that completed it.</summary>
    private static bool Subject(IReadableMemory memory, StoryScriptLocation location, Anchor anchor, out StoryActorState state)
    {
        if (anchor.SubjectClass is { } expectedClass)
            return Visible(memory, location, anchor.Subject, out state) && state.ClassTag == expectedClass &&
                state.Visual == anchor.SubjectVisual;
        if (anchor.Book is not { } book) return Visible(memory, location, anchor.Subject, out state);
        if (!StoryActionCapture.TryReadActor(memory, location, anchor.Subject, out state) ||
            state.ClassTag != book.Class || state.Visual != book.Visual ||
            state.FineX >> 8 != book.TileX || state.FineY >> 8 != book.TileY || !state.Drawn) return false;
        if (anchor.Passage is not { } tiles) return state.OnScreen == true;
        var field = new FieldNavigationSnapshot(location.Context, location.Actors, location.Field, location.Data,
            location.ActorCount, location.Scene, true, 0, 0, location.Actor, -1, null, []);
        return location.Camera != 0 && FieldEnvironmentCapture.TryViewport(memory, field, out var viewport) &&
            viewport.Left <= tiles.Left * 256 && viewport.Top <= tiles.Top * 256 &&
            viewport.Right >= tiles.Right * 256 && viewport.Bottom >= tiles.Bottom * 256;
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
