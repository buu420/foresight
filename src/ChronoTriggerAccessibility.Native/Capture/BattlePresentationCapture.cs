using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

public sealed record BattleTargetsSnapshot(bool IsTargeting, IReadOnlyList<int> Slots)
{
    /// <summary>
    /// The battlers offered for this selection: 44B770 resets the eleven cells at canvas 1A140 to
    /// FF and 44B9C0 or a sibling builder fills them with present, eligible slots, which the
    /// cursor (44B100/44B160) walks. Read only under the same proven targeting gates as
    /// <see cref="Slots"/>; null when unreadable, malformed or changed between its two reads.
    /// The committed slots stand either way.
    /// </summary>
    public IReadOnlyList<int>? CandidateSlots { get; init; }

    /// <summary>
    /// The enemy whose monster.txt line 44B390 can copy into the battle message label: only its
    /// single-target branch, only a slot above 2, and only while the panel mode at canvas 19E90
    /// (battle data 42F0) is the base command panel, which is an Attack. Null otherwise.
    /// </summary>
    public int? NameLabelSlot { get; init; }
}

/// <summary>Display-side target arrows and applied status effects. See battle-feedback-native-audit.md.</summary>
public sealed class BattlePresentationCapture(IReadableMemory memory)
{
    public BattleTargetsSnapshot? ReadTargets(nuint image, nuint menu)
    {
        if (!Owner(image, menu, out var canvas) || !Word(canvas + 0x19EEC, out var phase)) return null;
        if (phase == 0) return new(false, []);
        if (!Word(canvas + 0x19E78, out var actor) || !Word(canvas + 0x19F08, out var gate)) return null;
        if ((actor & 0x80) != 0 || (gate & 0x80) != 0) return new(true, []);
        if (actor > 2) return null;
        var visible = false;
        for (var i = 0; i < 4; i++)
        {
            if (!Byte(menu + 0x538u + (nuint)(i * 20), out var shown)) return null;
            visible |= shown != 0;
        }
        if (!visible) return new(true, []);
        // The arrows rotate over the committed group. Read that group, not the
        // candidate/hover arrays and not the three arrows drawn on just this frame.
        Span<byte> selected = stackalloc byte[44];
        if (!memory.TryRead(canvas + 0x1ACD4, selected)) return null;
        var slots = new List<int>();
        var single = (BinaryPrimitives.ReadUInt32LittleEndian(selected.Slice(4, 4)) & 0x80) != 0;
        // 4B557/4B55C rotate at 3 and 6; cells beyond that are not drawn as arrows.
        for (var i = 0; i < (single ? 1 : 6); i++)
        {
            var slot = BinaryPrimitives.ReadUInt32LittleEndian(selected.Slice(i * 4, 4));
            if ((slot & 0x80) != 0) continue;
            if (slot > 10 || slots.Contains((int)slot)) return null;
            slots.Add((int)slot);
        }
        // The candidate cells and panel mode only add evidence. Both are read twice inside the
        // gates rechecked below, and an unreadable or moving value withdraws just that evidence.
        Span<byte> offered = stackalloc byte[44];
        Span<byte> offeredAgain = stackalloc byte[44];
        var hasOffered = memory.TryRead(canvas + 0x1A140, offered) &&
            memory.TryRead(canvas + 0x1A140, offeredAgain) && offered.SequenceEqual(offeredAgain);
        var hasMode = Word(canvas + 0x19E90, out var mode) && Word(canvas + 0x19E90, out var modeAgain) &&
            mode == modeAgain;
        Span<byte> again = stackalloc byte[44];
        if (!Owner(image, menu, out var same) || same != canvas ||
            !Word(canvas + 0x19EEC, out var p2) || phase != p2 ||
            !Word(canvas + 0x19E78, out var a2) || actor != a2 ||
            !Word(canvas + 0x19F08, out var g2) || gate != g2 ||
            !memory.TryRead(canvas + 0x1ACD4, again) || !selected.SequenceEqual(again)) return null;
        return new(true, slots.AsReadOnly())
        {
            CandidateSlots = hasOffered ? Candidates(offered) : null,
            NameLabelSlot = single && hasMode && mode == 0 && slots.Count == 1 && slots[0] > 2 ? slots[0] : null,
        };
    }

    /// <summary>Present cells, in cursor order. A repeated or out-of-range slot is not a list 44B9C0 builds.</summary>
    private static IReadOnlyList<int>? Candidates(ReadOnlySpan<byte> cells)
    {
        var slots = new List<int>(11);
        for (var i = 0; i < 11; i++)
        {
            var slot = BinaryPrimitives.ReadUInt32LittleEndian(cells.Slice(i * 4, 4));
            if ((slot & 0x80) != 0) continue;
            if (slot > 10 || slots.Contains((int)slot)) return null;
            slots.Add((int)slot);
        }
        return slots.AsReadOnly();
    }

    public string? ReadStatus(nuint image, nuint menu, int slot)
    {
        if (slot is < 0 or > 2 || !Owner(image, menu, out var canvas) ||
            !Word(canvas + 0x19FA0u + (nuint)(slot * 4), out var present) || present == 0 ||
            !Word(image + 0x41B4BC, out var script) || script == 0 ||
            !Byte(script + 0x28441u + (nuint)slot, out var visual) ||
            !Word(canvas + 0x1AA30u + (nuint)(slot * 4), out var applied) ||
            (applied & 0xFF) != visual) return null;
        // RVA 4EA70 applies this visual state; 43DF0 draws its animated icon.
        // These are the outputs of the native status-to-visual table at 398680,
        // including palette/pose effects that do not have a sprite every frame.
        var text = visual switch
        {
            2 => "Stop", 3 => "Berserk", 4 => "Confuse", 5 => "Sleep",
            6 => "Barrier", 7 => "Shield", 8 => "Lock", 9 => "Blind",
            12 => "Haste", 13 => "Slow", 14 => "Poison",
            0 or 0x80 or 0x81 or 0xFF => string.Empty,
            _ => null,
        };
        var line = visual switch
        {
            2 => 14, 3 => 23, 4 => 11, 5 => 10, 6 => 21, 7 => 20,
            8 => 13, 9 => 12, 12 => 19, 13 => 9, 14 => 8, _ => -1,
        };
        if (line >= 0) text = StatusText(image, line) ?? text;
        if (!Owner(image, menu, out var same) || same != canvas ||
            !Word(image + 0x41B4BC, out var s2) || script != s2 ||
            !Byte(script + 0x28441u + (nuint)slot, out var v2) || visual != v2 ||
            !Word(canvas + 0x1AA30u + (nuint)(slot * 4), out var applied2) || applied != applied2) return null;
        return text;
    }

    private string? StatusText(nuint image, int line)
    {
        // The game's loaded battle.txt bank contains the localized names for the
        // same effects. The English descriptions above also work before text loads.
        if (!Word(image + 0x41C3D8, out var manager) || manager == 0 ||
            !Word(manager, out var banks) || banks == 0 ||
            !Word(manager + 4, out var banksEnd) || banksEnd < banks + 4 || banksEnd - banks > 1024 ||
            !Word(banks, out var bank) || bank == 0 || !Word(bank, out var lines) || lines == 0 ||
            !Word(bank + 4, out var end) || end < lines || end - lines > 16384 * 24 ||
            (end - lines) % 24 != 0 || (uint)line >= (end - lines) / 24 ||
            !new MsvcStringReader(memory).TryRead(lines + (nuint)(line * 24), out var text, out _) ||
            string.IsNullOrWhiteSpace(text) || text.Contains('<') ||
            !Word(image + 0x41C3D8, out var manager2) || manager2 != manager ||
            !Word(manager, out var banks2) || banks2 != banks ||
            !Word(manager + 4, out var be2) || be2 != banksEnd ||
            !Word(banks, out var bank2) || bank2 != bank ||
            !Word(bank, out var lines2) || lines2 != lines ||
            !Word(bank + 4, out var end2) || end2 != end) return null;
        return text.Trim();
    }

    private bool Owner(nuint image, nuint menu, out nuint canvas)
    {
        canvas = 0;
        if (!Word(menu, out var vtable) ||
            (vtable != image + BattleCapture.ClassicBattleMenuVtableRva &&
             vtable != image + BattleCapture.TouchBattleMenuVtableRva) ||
            !Word(image + BattleCapture.CanvasGlobalRva, out var p) || p == 0 ||
            (ulong)p + 0x1B800 > 0x100000000UL) return false;
        canvas = p; return true;
    }
    private bool Word(nuint address, out uint value)
    {
        value = 0; Span<byte> bytes = stackalloc byte[4];
        if (!Fits(address, 4) || !memory.TryRead(address, bytes)) return false;
        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes); return true;
    }
    private bool Byte(nuint address, out byte value)
    {
        value = 0; Span<byte> bytes = stackalloc byte[1];
        if (!Fits(address, 1) || !memory.TryRead(address, bytes)) return false;
        value = bytes[0]; return true;
    }
    private static bool Fits(nuint address, uint length) => address != 0 && (ulong)address + length <= 0x100000000UL;
}
