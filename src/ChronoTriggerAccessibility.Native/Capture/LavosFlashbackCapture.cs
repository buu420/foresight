using System.Buffers.Binary;
using System.Security.Cryptography;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

/// <summary><c>Outside</c>: a located, renderer-coherent dispatch in a scene that is not a Lavos
/// flashback; it only proves a scene/owner change. <c>Flashback</c>: actor 0's first visible pause
/// in a pinned flashback script with its presentation proven.</summary>
public enum LavosFlashbackKind { Outside, Flashback }

/// <summary>The nine earlier bosses whose original sprites the shell's flashback scenes draw.</summary>
public enum LavosFlashbackSubject { DragonTank, Guardian, Heckran, Zombor, MasaAndMune, Nizbel, Magus, BlackTyrannoAndAzala, GigaGaia }

/// <summary>Owners are the dispatcher context and its data/actor/field words; <c>Subject</c> is
/// null for <c>Outside</c>. No form index, global or battle identifier is carried.</summary>
public sealed record LavosFlashbackFrame(LavosFlashbackKind Kind, uint Context, uint Data, uint Actors, uint Field,
    int Scene, int ScriptId, LavosFlashbackSubject? Subject);

/// <summary>Native name sources: monster.txt (bank 0x32) indices, and the current character name
/// to prefer for Magus (the Extras bestiary itself names this boss &lt;NAME_MAG&gt;).</summary>
public sealed record LavosFlashbackLabel(IReadOnlyList<int> Monsters, int? Character);

/// <summary>A pinned installed flashback script. <c>AnchorPc</c> is a runtime PC (file offset - 1).</summary>
public sealed record LavosFlashbackScript(LavosFlashbackSubject Subject, int Scene, int ScriptId, int Length, string Sha256,
    int ActorCount, uint AnchorPc);

/// <summary>An actor the flashback loads. Enemies (op 83, class 5/6) must carry the loaded flag;
/// <c>Drawn</c> actors must be drawn at the anchor, the others may still be hidden (their art is
/// in the scene map, or a later 90/7C shows them).</summary>
public sealed record LavosFlashbackPresence(int Actor, int Visual, bool PlayerCharacter, bool Drawn);

/// <summary>
/// The Lavos shell's nine "Lavos Past Mode" scenes (346-350, 456-459): Atel_0286 actor 8 fn5
/// changes to one after "Lavos changes attack modes!", the scene draws the ORIGINAL boss with op 83
/// (or Magus with op 81 06), then returns to scene 467 for the battle. Observe BEFORE the original
/// instruction: StoryActionCapture.TryLocate requires the live byte at PC to be the dispatched
/// opcode. The anchor is actor 0's first AD 1E, which follows F0 F1 (fade in, 1719A0) and F2
/// (waits until actors+0x12120 bit 0x02 clears, 163485), so the scene is fully visible.
/// Evidence: claude-native-ui-audit.md section 1 and the companion JSON; tester log actor facts
/// desert-lavos-0377 lines 6976-8274.
/// </summary>
public static class LavosFlashbackCapture
{
    private const uint ScriptHeader = 0x12000, ScriptBytes = 0x12001, FadeFlagsOffset = 0x12120;
    private const byte FadeInProgress = 0x02, PauseOpcode = 0xAD, PauseTicks = 0x1E;

    public static IReadOnlyList<LavosFlashbackScript> Scripts { get; } =
    [
        new(LavosFlashbackSubject.DragonTank, 347, 311, 257, "79980F61656B64A41CF84800C86D5EA8F20150C59AC5717C94014A6AD8301294", 4, 0xA4),
        new(LavosFlashbackSubject.Guardian, 346, 310, 221, "D0110B6C4DB38FE5E9384CBF4E50AAC03697A40D65DA269CB7176052DF5C0EAC", 3, 0x84),
        new(LavosFlashbackSubject.Heckran, 348, 312, 160, "B2947F577BB23A2E99FFC4E9A14A6AEA53704E23A0727B59D80264A1B2DCAB8B", 2, 0x64),
        new(LavosFlashbackSubject.Zombor, 349, 313, 215, "728A5E0ECA0948DA4A88B864149E932E9E08D9CEA88A8AB788BF320B8B975D5C", 3, 0x84),
        new(LavosFlashbackSubject.MasaAndMune, 350, 314, 157, "2C3CA061C8EA601543E574ECBC51A28711E33D22992046364DFFB401D773FF43", 2, 0x64),
        new(LavosFlashbackSubject.Nizbel, 456, 315, 159, "E62032ED532635992C34662918720EB2C5941AF7FEBBA2C343FCC57CC39E0A09", 2, 0x64),
        new(LavosFlashbackSubject.Magus, 457, 316, 175, "3EE11B89B0552A3C07995CEC97A3C4B9F7F24030B88EB46368C03009E91F78F3", 2, 0x64),
        new(LavosFlashbackSubject.BlackTyrannoAndAzala, 458, 317, 223, "2ED4AE625C176EE240302BAD8D6556CA69D438ED10E998042C0B367716B28439", 3, 0x84),
        new(LavosFlashbackSubject.GigaGaia, 459, 318, 246, "51406F8CBBAC4EE9FFE74E123954206611C04880E34B74BE822F75728A307240", 4, 0xA4),
    ];

    // Visuals are the installed op 83 / 81 operands (verified per actor by the overlay tests).
    // Guardian's body and Giga Gaia's head are scene map art; their actors are loaded hidden.
    private static readonly Dictionary<LavosFlashbackSubject, LavosFlashbackPresence[]> Presences = new()
    {
        [LavosFlashbackSubject.DragonTank] = [new(1, 0x93, false, true), new(2, 0xAD, false, true), new(3, 0x94, false, true)],
        [LavosFlashbackSubject.Guardian] = [new(1, 0xCF, false, false), new(2, 0xCF, false, false)],
        [LavosFlashbackSubject.Heckran] = [new(1, 0xA9, false, true)],
        [LavosFlashbackSubject.Zombor] = [new(1, 0xB4, false, true), new(2, 0xB3, false, true)],
        [LavosFlashbackSubject.MasaAndMune] = [new(1, 0x99, false, true)],
        [LavosFlashbackSubject.Nizbel] = [new(1, 0x9B, false, true)],
        [LavosFlashbackSubject.Magus] = [new(1, 0x06, true, true)],
        [LavosFlashbackSubject.BlackTyrannoAndAzala] = [new(1, 0xBC, false, true), new(2, 0x9A, false, true)],
        [LavosFlashbackSubject.GigaGaia] = [new(1, 0xD1, false, false), new(2, 0xD2, false, true), new(3, 0xD3, false, true)],
    };

    // monster.txt indices of the original bosses (en: Dragon Tank, Guardian, Heckran, Zombor, Masa & Mune,
    // Nizbel, Magus, Black Tyranno + Azala, Giga Gaia). 0xD4 and 0xD1 name the map art.
    private static readonly Dictionary<LavosFlashbackSubject, LavosFlashbackLabel> Labels = new()
    {
        [LavosFlashbackSubject.DragonTank] = new([0x93], null),
        [LavosFlashbackSubject.Guardian] = new([0xD4], null),
        [LavosFlashbackSubject.Heckran] = new([0xA9], null),
        [LavosFlashbackSubject.Zombor] = new([0xB4], null),
        [LavosFlashbackSubject.MasaAndMune] = new([0x99], null),
        [LavosFlashbackSubject.Nizbel] = new([0x9B], null),
        [LavosFlashbackSubject.Magus] = new([0xF9], 6),
        [LavosFlashbackSubject.BlackTyrannoAndAzala] = new([0xBC, 0x9A], null),
        [LavosFlashbackSubject.GigaGaia] = new([0xD1], null),
    };

    public static LavosFlashbackLabel Label(LavosFlashbackSubject subject) => Labels[subject];

    public static IReadOnlyList<LavosFlashbackPresence> Presence(LavosFlashbackSubject subject) => Presences[subject];

    /// <summary>Null means unknown: unreadable, incoherent, or a non-anchor instruction inside a
    /// flashback scene (which must neither announce nor rearm).</summary>
    public static LavosFlashbackFrame? Capture(IReadableMemory memory, nuint context, int opcode)
    {
        try
        {
            if (memory is null || !StoryActionCapture.TryLocate(memory, context, opcode, out var at) ||
                !RenderedScene(memory, context, out var rendered) || rendered != at.Scene) return null;
            // Called before every dispatch: a plain loop, no per-call closure.
            LavosFlashbackScript? script = null;
            foreach (var candidate in Scripts)
                if (candidate.Scene == at.Scene) { script = candidate; break; }
            if (script is null)
                return StoryActionCapture.IsSameScript(memory, at)
                    ? new(LavosFlashbackKind.Outside, at.Context, at.Data, at.Actors, at.Field, at.Scene, at.ScriptId, null)
                    : null;
            if (at.ScriptId != script.ScriptId || at.ActorCount != script.ActorCount || at.Actor != 0 ||
                at.Address != script.AnchorPc || opcode != PauseOpcode) return null;
            Span<byte> pause = stackalloc byte[2];
            Span<byte> fade = stackalloc byte[1];
            if (!memory.TryRead(at.Data + ScriptBytes + at.Address, pause) || pause[0] != PauseOpcode || pause[1] != PauseTicks ||
                !memory.TryRead(at.Actors + FadeFlagsOffset, fade) || (fade[0] & FadeInProgress) != 0 ||
                !DigestMatches(memory, at.Data, script)) return null;
            foreach (var presence in Presences[script.Subject])
                if (!Presented(memory, at, presence)) return null;
            // The owners are reread after the proof: a changed store or scene is never reported.
            if (!StoryActionCapture.IsSameScript(memory, at) ||
                !RenderedScene(memory, context, out var renderedAgain) || renderedAgain != at.Scene) return null;
            return new(LavosFlashbackKind.Flashback, at.Context, at.Data, at.Actors, at.Field, at.Scene, at.ScriptId, script.Subject);
        }
        catch (Exception) { return null; }
    }

    private static bool Presented(IReadableMemory memory, StoryScriptLocation at, LavosFlashbackPresence presence)
    {
        if (!StoryActionCapture.TryReadActor(memory, at, presence.Actor, out var actor) || actor.Visual != presence.Visual) return false;
        if (presence.PlayerCharacter)
        {
            // 81 06: class 3 (PC out of party), visual = character 6. Its loader (1604C0) is not
            // audited for the +0xD8 flag, so the drawn state is the proof.
            if (actor.ClassTag != 3) return false;
        }
        else
        {
            var record = at.Actors + FieldNavigationCapture.ActorArrayOffset + (uint)presence.Actor * FieldNavigationCapture.ActorStride;
            if (actor.ClassTag is not (5 or 6) ||
                !Word(memory, record + FieldNavigationCapture.ActorLoadedFlagOffset, out var loaded) || loaded != 1) return false;
        }
        return !presence.Drawn || (actor.Drawn && actor.DrawMode == FieldNavigationCapture.DrawModeDrawn);
    }

    private static bool RenderedScene(IReadableMemory memory, nuint context, out uint scene)
    {
        scene = 0;
        return Word(memory, context + FieldNavigationCapture.EngineRendererPointerOffset, out var renderer) &&
            Fits(renderer, FieldNavigationCapture.RendererSceneIdOffset + 4) &&
            Word(memory, renderer + FieldNavigationCapture.RendererSceneIdOffset, out scene);
    }

    private static bool DigestMatches(IReadableMemory memory, uint data, LavosFlashbackScript script)
    {
        var bytes = new byte[script.Length];
        return memory.TryRead(data + ScriptHeader, bytes) && Convert.ToHexString(SHA256.HashData(bytes)) == script.Sha256;
    }

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
