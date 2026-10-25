using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

public enum BekklerMimicAction { Window, RightArm, LeftArm, Surprise, Laugh, Failed, Won }

public sealed record BekklerMimicSnapshot(uint Context, uint Data, uint Actors, uint Field,
    uint Address, int Round, int Visual, BekklerMimicAction Action);

public sealed record BekklerMimicCue(BekklerMimicSnapshot Frame, string? Button);

public static class BekklerMimicCapture
{
    // Atel_0071 / scene 434. Runtime PCs omit the file's actor-count byte.
    // See docs/bekkler-copy-game-0373.md and the retained 20261024 native audit.
    private static readonly byte[] Header = Convert.FromHexString("19200344034403450345034503450345");
    private const uint Locals = 0x118B0;

    public static bool TryCapture(IReadableMemory memory, nuint context, int opcode,
        out BekklerMimicSnapshot snapshot)
    {
        snapshot = null!;
        if (opcode is not (0x4F or 0xAA or 0x77 or 0xEA)) return false;
        try
        {
            if (!Fits(context, 0x854) || !Word(memory, context + 0x24, out var pc) ||
                !Definition(pc, opcode, out var action, out var bytes) ||
                !Word(memory, context, out var data) || !Fits(data, 0x12600) ||
                !Word(memory, context + 0x40, out var actors) || !Fits(actors, Locals + 0x1D * 8 + 4) ||
                !Word(memory, context + 0x850, out var field) || !Fits(field, 0x1184) ||
                !Scope(memory, field, action) || !ScriptMatches(memory, data, pc, bytes) ||
                !Local(memory, actors, 8, out var round) || round is < 1 or > 22 ||
                (action == BekklerMimicAction.Won && round != 22) ||
                !VisibleModel(memory, actors, out var visual)) return false;
            if (action != BekklerMimicAction.Window &&
                (!Local(memory, actors, 0x1D, out var active) || active != 1)) return false;
            if (IsPose(action) && (!Local(memory, actors, 0x11, out var input) || !RoundInput(input))) return false;
            snapshot = new((uint)context, data, actors, field, pc, (int)round, visual, action);
            return true;
        }
        catch (Exception) { return false; }
    }

    public static BekklerMimicCue? Complete(IReadableMemory memory, BekklerMimicSnapshot before,
        nuint imageBase)
    {
        try
        {
            var opcode = IsPose(before.Action) ? 0xAA : before.Action switch
            { BekklerMimicAction.Window => 0x4F, BekklerMimicAction.Failed => 0x77, _ => 0xEA };
            if (!Definition(before.Address, opcode, out var action, out var bytes) || action != before.Action ||
                !Word(memory, before.Context, out var data) || data != before.Data ||
                !Word(memory, before.Context + 0x40u, out var actors) || actors != before.Actors ||
                !Word(memory, before.Context + 0x850u, out var field) || field != before.Field ||
                !Scope(memory, field, action) || !ScriptMatches(memory, data, before.Address, bytes) ||
                !Local(memory, actors, 8, out var round) || round != before.Round ||
                !VisibleModel(memory, actors, out var visual) || visual != before.Visual) return null;
            if (action == BekklerMimicAction.Window)
            {
                return Word(memory, before.Context + 0x18u, out var next) && next == before.Address + 3 &&
                    Local(memory, actors, 0x11, out var input) && input == 0x80 ? new(before, null) : null;
            }
            if (!Word(memory, before.Context + 0x24u, out var pc) ||
                pc != before.Address + (action == BekklerMimicAction.Failed ? 2u : 1u) ||
                !Local(memory, actors, 0x1D, out var active) ||
                active != (action == BekklerMimicAction.Failed ? 0 : 1)) return null;
            if (!IsPose(action))
                return action != BekklerMimicAction.Won ||
                    (round == 22 && Word(memory, before.Context + 0x18u, out var next) && next == before.Address + 2)
                    ? new(before, null) : null;
            var address = Model(actors);
            var animation = action switch
            { BekklerMimicAction.RightArm => 0x1Eu, BekklerMimicAction.LeftArm => 0x1Fu,
                BekklerMimicAction.Surprise => 9u, _ => 0x1Au };
            if (!Word(memory, address + 0x68, out var applied) || applied != animation ||
                !Word(memory, address + 0x74, out var mode) || mode != 1 ||
                !Local(memory, actors, 0x11, out var window) || !RoundInput(window)) return null;
            // Only AFTER the visible animation write may the expected value corroborate it.
            // This is defensive corroboration; normal scheduling has no stale pre-loop
            // pose (budget5, doppel period2). Never read RNG/local07, or use local10
            // to predict/announce the next action.
            if (!Local(memory, actors, 0x10, out var expected) || expected != (uint)action - 1) return null;
            var mask = action switch
            { BekklerMimicAction.RightArm => 0x80u, BekklerMimicAction.LeftArm => 0x40u,
                BekklerMimicAction.Surprise => 8u, _ => 1u };
            return new(before, Button(memory, imageBase, mask));
        }
        catch (Exception) { return null; }
    }

    public static bool IsDemonstration(StoryScriptLocation location) =>
        location is { Scene: 434, ScriptId: 71, Actor: 9, ActorCount: 25, Opcode: 0xAA,
            Address: 0x53F or 0x546 or 0x54D or 0x554 };

    private static bool IsPose(BekklerMimicAction action) =>
        action is >= BekklerMimicAction.RightArm and <= BekklerMimicAction.Laugh;
    // The native input poller may accept a held press before the doppel draws.
    // The demonstration stays visible after that press and must still be described.
    // Runtime separately requires this owner's verified window for the current round.
    private static bool RoundInput(uint value) => value <= 3 || value == 0x80;
    private static bool Scope(IReadableMemory memory, uint field, BekklerMimicAction action) =>
        Word(memory, field + 0x1010u, out var scene) && scene == 434 &&
        Word(memory, field + 0x1180u, out var actor) && actor == (IsPose(action) ? 18u : 0u);
    private static uint Model(uint actors) => actors + FieldNavigationCapture.ActorArrayOffset + 9 * FieldNavigationCapture.ActorStride;
    private static bool VisibleModel(IReadableMemory memory, uint actors, out int visual)
    {
        visual = -1; var address = Model(actors);
        if (!Word(memory, address + FieldNavigationCapture.ActorClassTagOffset, out var type) || type > 3 ||
            !Word(memory, address + FieldNavigationCapture.ActorVisualIndexOffset, out var index) || index > 6 ||
            !Word(memory, address + FieldNavigationCapture.ActorDrawModeOffset, out var draw) ||
            draw != FieldNavigationCapture.DrawModeDrawn) return false;
        visual = (int)index; return true;
    }
    private static bool Local(IReadableMemory memory, uint actors, uint index, out uint value) =>
        Word(memory, actors + Locals + index * 8, out value);
    private static string? Button(IReadableMemory memory, nuint imageBase, uint mask)
    {
        // FUN_00597330's live <BTN_*> rule, shared by the existing settings capture.
        if (!Fits(imageBase, 0x41B4C8) || !Word(memory, imageBase + 0x41B4C4, out var config) ||
            !Fits(config, 0x6904)) return null;
        for (uint i = 0; i < 4; i++)
        {
            if (!Word(memory, config + 0x68F4 + i * 4, out var binding)) return null;
            if (binding == mask) return new[] { "A", "B", "Y", "X" }[i];
        }
        return null;
    }
    private static bool Definition(uint pc, int opcode, out BekklerMimicAction action, out string bytes)
    {
        var definition = (pc, opcode) switch
        {
            (0x382, 0x4F) => (BekklerMimicAction.Window, "4F8011751DE80202"),
            (0x53F, 0xAA) => (BekklerMimicAction.RightArm, "AA1E10161A0105AA"),
            (0x546, 0xAA) => (BekklerMimicAction.LeftArm, "AA1F100F1A0205AA"),
            (0x54D, 0xAA) => (BekklerMimicAction.Surprise, "AA0910081A0305AA"),
            (0x554, 0xAA) => (BekklerMimicAction.Laugh, "AA1A100100001211"),
            (0x3C4, 0x77) => (BekklerMimicAction.Failed, "771DEA25AD10121C"),
            (0x3AE, 0xEA) => (BekklerMimicAction.Won, "EA1CBB4E00041437"),
            _ => (default(BekklerMimicAction), ""),
        };
        (action, bytes) = definition; return bytes.Length != 0;
    }
    private static bool ScriptMatches(IReadableMemory memory, uint data, uint pc, string expected)
    {
        Span<byte> header = stackalloc byte[16]; Span<byte> instruction = stackalloc byte[8];
        return memory.TryRead(data + 0x12000u, header) && header.SequenceEqual(Header) &&
            memory.TryRead(data + 0x12001u + pc, instruction) && Convert.ToHexString(instruction) == expected;
    }
    private static bool Fits(nuint address, uint length) => address != 0 && (ulong)address + length - 1 <= uint.MaxValue;
    private static bool Word(IReadableMemory memory, nuint address, out uint value)
    {
        value = 0; Span<byte> bytes = stackalloc byte[4];
        if (!Fits(address, 4) || !memory.TryRead(address, bytes)) return false;
        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes); return true;
    }
}
