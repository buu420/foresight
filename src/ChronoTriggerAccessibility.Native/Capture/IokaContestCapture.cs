using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

public enum IokaContestAction { Started, PlayerDrinks, AylaDrinks, Finished }

public sealed record IokaContestSnapshot(uint Context, uint Data, uint Actors, uint Field,
    uint Address, int Actor, int Active, IokaContestAction Action);

public static class IokaContestCapture
{
    // Supported PC Atel_0371: scene 280, 23 actors. PCs omit the leading actor-count byte.
    // The installed script and visual frames are audited in docs/ioka-contest-0352.md.
    private static readonly byte[] Header = Convert.FromHexString("17E002F3022403250325032503250325");
    private const uint LocalActive = 0x118B0 + 0x0E * 8;

    public static bool TryCapture(IReadableMemory memory, nuint context, int opcode,
        out IokaContestSnapshot snapshot)
    {
        snapshot = null!;
        // Most dispatcher calls do not perform even one memory read.
        if (opcode is not (0x75 or 0x77 or 0xAA)) return false;
        try
        {
            if (!Fits(context, 0x854) || !Word(memory, context + 0x24, out var pc) ||
                !Definition(pc, opcode, out var actor, out var action, out var bytes) ||
                !Word(memory, context, out var data) || !Fits(data, 0x12AAC) ||
                !Word(memory, context + 0x40, out var actors) || !Fits(actors, LocalActive + 4) ||
                !Word(memory, context + 0x850, out var field) || !Fits(field, 0x1184) ||
                !Word(memory, field + 0x1010u, out var scene) || scene != 280 ||
                !Word(memory, field + 0x1180u, out var currentActor) || currentActor != actor * 2 ||
                !ScriptMatches(memory, data, pc, bytes) ||
                !Word(memory, actors + LocalActive, out var active) ||
                active != (action == IokaContestAction.Started ? 0 : 1) ||
                !VisibleCharacter(memory, actors, actor)) return false;
            snapshot = new((uint)context, data, actors, field, pc, actor, (int)active, action);
            return true;
        }
        catch (Exception) { return false; }
    }

    public static bool TryComplete(IReadableMemory memory, IokaContestSnapshot before)
    {
        try
        {
            var animation = before.Action is IokaContestAction.PlayerDrinks or IokaContestAction.AylaDrinks;
            var opcode = animation ? 0xAA : before.Action == IokaContestAction.Started ? 0x75 : 0x77;
            if (!Definition(before.Address, opcode, out var actor, out var action, out var bytes) ||
                actor != before.Actor || action != before.Action ||
                !Word(memory, before.Context, out var data) || data != before.Data ||
                !Word(memory, before.Context + 0x40u, out var actors) || actors != before.Actors ||
                !Word(memory, before.Context + 0x850u, out var field) || field != before.Field ||
                !Word(memory, field + 0x1010u, out var scene) || scene != 280 ||
                !Word(memory, field + 0x1180u, out var currentActor) || currentActor != actor * 2 ||
                !Word(memory, before.Context + 0x24u, out var pc) ||
                pc != before.Address + (animation ? 1u : 2u) ||
                !ScriptMatches(memory, data, before.Address, bytes) ||
                !Word(memory, actors + LocalActive, out var active) ||
                active != (action == IokaContestAction.Finished ? 0 : 1) ||
                !VisibleCharacter(memory, actors, actor)) return false;
            if (!animation) return true;
            var address = actors + FieldNavigationCapture.ActorArrayOffset + (uint)actor * FieldNavigationCapture.ActorStride;
            return Word(memory, address + 0x68u, out var index) && index == (actor == 2 ? 0x3Bu : 0x50u) &&
                Word(memory, address + 0x74u, out var mode) && mode == 1;
        }
        catch (Exception) { return false; }
    }

    private static bool Definition(uint pc, int opcode, out int actor, out IokaContestAction action,
        out string bytes)
    {
        (actor, action, bytes) = (pc, opcode) switch
        {
            (0x7BD, 0x75) => (6, IokaContestAction.Started, "750E771B771C0404"),
            (0x3E8, 0xAA) => (2, IokaContestAction.PlayerDrinks, "AA3B00032832AA3A"),
            (0x7CF, 0xAA) => (6, IokaContestAction.AylaDrinks, "AA50121BAF031B71"),
            (0x7EA, 0xAA) => (6, IokaContestAction.AylaDrinks, "AA50771C111E032A"),
            (0x7F5, 0x77) => (6, IokaContestAction.Finished, "770E120F00000311"),
            _ => (-1, default, ""),
        };
        return actor >= 0;
    }

    private static bool VisibleCharacter(IReadableMemory memory, uint actors, int actor)
    {
        var address = actors + FieldNavigationCapture.ActorArrayOffset + (uint)actor * FieldNavigationCapture.ActorStride;
        return Word(memory, address + FieldNavigationCapture.ActorClassTagOffset, out var type) && type <= 3 &&
            Word(memory, address + FieldNavigationCapture.ActorVisualIndexOffset, out var visual) && visual == (actor == 2 ? 0 : 5) &&
            Word(memory, address + FieldNavigationCapture.ActorDrawModeOffset, out var draw) && draw == FieldNavigationCapture.DrawModeDrawn;
    }

    private static bool ScriptMatches(IReadableMemory memory, uint data, uint pc, string expected)
    {
        Span<byte> header = stackalloc byte[16];
        Span<byte> instruction = stackalloc byte[8];
        return memory.TryRead(data + 0x12000u, header) && header.SequenceEqual(Header) &&
            memory.TryRead(data + 0x12001u + pc, instruction) &&
            Convert.ToHexString(instruction) == expected;
    }

    private static bool Fits(nuint address, uint length) => address != 0 && (ulong)address + length - 1 <= uint.MaxValue;
    private static bool Word(IReadableMemory memory, nuint address, out uint value)
    {
        value = 0;
        Span<byte> bytes = stackalloc byte[4];
        if (!Fits(address, 4) || !memory.TryRead(address, bytes)) return false;
        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes); return true;
    }
}
