using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

public sealed record FieldStoryState(int Point, bool MotherIntroducedFriend)
{
    public IReadOnlyDictionary<int, int> Globals { get; init; } = new Dictionary<int, int>();
    public IReadOnlyDictionary<int, int> Locals { get; init; } = new Dictionary<int, int>();
    public IReadOnlyDictionary<int, int> Extended { get; init; } = new Dictionary<int, int>();
    public IReadOnlyDictionary<int, int>? Inventory { get; init; }
    public IReadOnlyList<int>? Party { get; init; }
    public int? Gold { get; init; }
    public int? Global(int index) => Globals.TryGetValue(index, out var value) ? value : null;
    public int? Local(int index) => Locals.TryGetValue(index, out var value) ? value : null;
    public int? Extra(int index) => Extended.TryGetValue(index, out var value) ? value : null;
    public int? ItemCount(int item) => Inventory?.GetValueOrDefault(item, 0);
    public int? PartyContains(int character, bool activeOnly) => Party is { Count: 9 }
        ? Party.Take(activeOnly ? 3 : 9).Contains(character) ? 1 : 0 : null;
    public bool? Flag(int index, int mask) => Global(index) is { } value ? (value & mask) != 0 : null;
}

/// <summary>Optional story context. Opcode 18 reads A+110B0 at 161E80 and
/// opcode 5A writes it at 16257C. Global bytes are expanded to dwords:
/// gbiton reads/writes A+110B0+index*4 at 168BD8/168BF5. Atel_0323
/// sets global 0140 bit 0 after the initial conversation and name prompt.</summary>
public static class FieldStoryCapture
{
    // Only script globals used by the story catalogs. Future: factory security /
    // crane 58, hatch 5C, Arris bridges A4, rat EC, and power-room lock 1D0.
    public static IReadOnlyList<int> ObjectiveGlobalIndices { get; } = Array.AsReadOnly(new[]
        { 0x54, 0x55, 0x56, 0x58, 0x5C, 0xA4, 0xEC, 0xFF, 0x190, 0x1D0 });

    public static FieldStoryState? Capture(IReadableMemory memory, FieldNavigationSnapshot field)
    {
        try
        {
            if (!field.SceneIdCoherent || !Matches() ||
                !Word((nuint)field.ActorBase + 0x110B0u, out var point) ||
                !Word((nuint)field.ActorBase + 0x115B0u, out var flags)) return null;
            var globals = StoryMemoryCapture.StableWords(memory, (nuint)field.ActorBase + 0x110B0u, 512, 4, true);
            // PC local operands index pairs of expanded byte cells. Opcode 12
            // uses the first byte. 6E reads extended cells at A+6798 (169C39).
            var locals = StoryMemoryCapture.StableWords(memory, (nuint)field.ActorBase + 0x118B0u, 256, 8, true);
            // Installed scripts use extended indices through 78 (a word read).
            // Stop before the actor array; its moving coordinates are not flags.
            var extended = StoryMemoryCapture.StableWords(memory, (nuint)field.ActorBase + 0x6798u, 80, 4, false);
            var inventory = StoryMemoryCapture.Inventory(memory, field.ActorBase);
            var party = StoryMemoryCapture.Party(memory, field.ActorBase);
            var gold = StoryMemoryCapture.Gold(memory, field.ActorBase);
            if (!Matches() || !Word((nuint)field.ActorBase + 0x110B0u, out var repeated) || repeated != point ||
                !Word((nuint)field.ActorBase + 0x115B0u, out var repeatedFlags) || flags != repeatedFlags) return null;
            return new(point & 255, (flags & 1) != 0)
            { Globals = globals, Locals = locals, Extended = extended, Inventory = inventory, Party = party, Gold = gold };
        }
        catch { return null; }

        bool Matches() => Word((nuint)field.Engine + 0x40u, out var actors) && (uint)actors == field.ActorBase &&
            Word((nuint)field.FieldState + 0x1010u, out var scene) && scene == field.SceneId;
        bool Word(nuint address, out int value)
        {
            value = 0;
            Span<byte> bytes = stackalloc byte[4];
            if (address == 0 || (ulong)address + 3 > uint.MaxValue || !memory.TryRead(address, bytes)) return false;
            value = BinaryPrimitives.ReadInt32LittleEndian(bytes); return true;
        }

    }
}
