using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

/// <summary>
/// Reads the visible battle interface without calling game code.
/// <para>Every offset here is justified in docs/battle-interface-native-audit.md sections 12 to 15.
/// Both battle interfaces are views over one block in the save-data canvas and over one shared set
/// of <c>BattleMenu</c> fields, so one reader serves the classic menu (vtable RVA 0x39D884,
/// <c>ClassicBattleMenu</c>) and the touch menu (0x39DC2C, <c>BattleMenu</c>).</para>
/// <para>Ordinary enemy HP is deliberately not read. Only the enemy *name index* the native
/// message path uses (canvas + 0x1A018 + battler*4, from 0x6ECF3) is consulted.</para>
/// </summary>
public sealed class BattleCapture(IReadableMemory memory, Func<int, int, string?>? localizedText = null)
{
    public const uint CanvasGlobalRva = 0x41B4C4;
    public const uint TextManagerGlobalRva = 0x41C3D8;
    public const uint ClassicBattleMenuVtableRva = 0x39D884;
    public const uint TouchBattleMenuVtableRva = 0x39DC2C;

    /// <summary>Item id group bases, indexed by the encoded id's high bits (0x41EAF6).</summary>
    public const uint ItemGroupTableRva = 0x39906C;
    public const int ItemGroupCount = 13;

    /// <summary>
    /// Which panel the battle interface is drawing: 0 base commands, 1 Tech, 2 Item. Written by the
    /// shared battle state machine and mirrored into <see cref="MenuModeOffset"/> by both menus
    /// (0x419F33 for BattleMenu, 0x42002A for ClassicBattleMenu).
    /// </summary>
    public const uint ModeOffset = 0x19E90;

    /// <summary>The dword the classic draw dispatcher at 0x4200E0 actually switches on.</summary>
    public const uint MenuModeOffset = 0x6A8;

    public const int ModeBaseCommand = 0;
    public const int ModeTech = 1;
    public const int ModeItem = 2;

    public const uint BattlerArrayOffset = 0x15BA0;
    public const int BattlerStride = 0x80;

    /// <summary>Battler record fields the HUD formatter at 0x41BE2C reads as little-endian u16.</summary>
    public const int BattlerHpOffset = 3;
    public const int BattlerMaximumHpOffset = 5;
    public const int BattlerMpOffset = 7;
    public const int BattlerMaximumMpOffset = 9;

    public const uint PartyPresenceOffset = 0x19FA0;
    public const uint PartyCharacterIdOffset = 0x1324C;
    /// <summary>Customised character names, from the resolver at 0x414830.</summary>
    public const uint CustomNameOffset = 0x1908;
    public const int CustomNameStride = 24;

    public const uint ActiveSlotOffset = 0x1AD20;
    public const uint MappedBattlerOffset = 0x1AD10;
    public const uint CommandGateOffset = 0x1AD50;
    public const uint BaseCommandRowOffset = 0x19E94;
    /// <summary>
    /// Acting <em>party slot</em> for both lists; 0xFF means the lists draw nothing. This is a
    /// slot, not a saved character id: the tech row table it scales (0x19C8C, 0x14 rows of 8 bytes
    /// per block) ends at 0x19E6C for three blocks, immediately below this field, and the tech
    /// record table it scales (0x1AEE8, 0x14 records of 0x20 bytes per block) ends at exactly
    /// 0x1B668 where the item table starts. Seven blocks overrun both. 0x419289 confirms it from
    /// the other side by placing a second table at 0x1A2CC, which leaves room for only three
    /// counts at 0x1A2C0.
    /// </summary>
    public const uint ActingSlotOffset = 0x19E78;
    public const int NoActingSlot = 0xFF;

    /// <summary>Phase gate: every command and list renderer draws only while this is zero
    /// (0x20599, 0x20B98, 0x1DACC, 0x1E688); the target-hover path runs when it is not.</summary>
    public const uint PhaseGateOffset = 0x19EEC;

    // Combo predicate at 0x414880, whose `this` is canvas + 0x14010.
    public const uint ComboEnabledOffset = 0x19ECC;
    public const uint ComboMaskHighOffset = 0x1A1E0;
    public const uint ComboMaskLowOffset = 0x1A1D4;
    public const uint ComboLockOffset = 0x1A430;
    public const uint ComboPartyCountOffset = 0x1AD24;
    public const uint ComboLeaderOffset = 0x1A42C;

    public const uint TechCursorOffset = 0x19EA0;
    public const uint TechPageBaseOffset = 0x19EC0;
    public const uint TechCountOffset = 0x1A2C0;
    public const uint TechEntryIdOffset = 0x19C8C;
    public const int TechEntriesPerCharacter = 0x14;
    public const int TechEntryStride = 8;
    /// <summary>Per tech record, 0x20 bytes: +8 flag byte, +0xC the per-slot MP costs (0x420991).</summary>
    public const uint TechRecordOffset = 0x1AEE8;
    public const int TechRecordStride = 0x20;
    public const int TechRecordFlagOffset = 8;
    public const int TechRecordCostOffset = 0x0C;

    public const uint ItemCursorOffset = 0x19EB8;
    public const uint ItemPageBaseOffset = 0x19EBC;
    public const uint ItemCountOffset = 0x1ADB0;
    public const uint ItemIdOffset = 0x1B668;
    public const int ItemEntryStride = 0x14;
    public const int ItemFlagOffset = 8;
    public const int ItemQuantityOffset = 0x0C;

    public const uint TargetCursorOffset = 0x19F0C;
    public const uint TargetCandidateOffset = 0x1A140;
    public const uint TargetRecipientOffset = 0x1ACD4;
    public const uint EnemyNameIndexOffset = 0x1A018;

    public const int MonsterBank = 0x32;
    public const int ItemBank = 0x1B;
    public const int BaseCommandBank = 0x3B;
    public const int TechBank = 0x44;

    // Base command bank rows, from 0x4202B7 / 0x420335 / 0x42039A and the bracketed rows at 0x41E419.
    public const int AttackMessageId = 0;
    public const int TechMessageId = 1;
    public const int ItemMessageId = 2;
    public const int ComboMessageId = 3;
    public const int DualTechMessageId = 0x0D;
    public const int TripleTechMessageId = 0x0E;

    public const int PartySlotCount = 3;
    /// <summary>Playable character ids, which index the customised name table.</summary>
    public const int CharacterIdLimit = 7;
    /// <summary>
    /// Name indices at or above this are markers rather than monsters: 0x430606 dispatches
    /// 0xFC/0xFD/0xFE separately and 0x44B47C uses 0xFF as its "no name" argument.
    /// </summary>
    public const int FirstReservedNameIndex = 0xFC;
    /// <summary>Battler slots: 0..2 party, 3..10 enemies.</summary>
    public const int BattlerLimit = 11;
    public const int FirstEnemyBattler = 3;
    public const int TargetCursorLimit = 0x0B;
    /// <summary>Both lists draw six cells in a two column grid (0x420C3D, 0x420689).</summary>
    public const int VisibleListRows = 6;

    /// <summary>Rows the tech renderer draws as blank instead of as a selectable tech (0x420617).</summary>
    public const int BlankTechEntryId = 0xFB;
    public const int DualTechEntryId = 0xFC;
    public const int TripleTechEntryId = 0xFD;
    public const int EndTechEntryId = 0xFE;
    public const int EmptyTechEntryId = 0xFF;

    private const int MaximumItemEntries = 512;
    private const int MaximumBankCount = 256;
    private const int MaximumLineCount = 16384;

    private readonly MsvcStringReader strings = new(memory);

    /// <summary>Every field that decides what the renderers put on screen this frame.</summary>
    private readonly record struct BattleState(
        nuint Canvas,
        int Mode,
        int MenuMode,
        int Phase,
        int ActiveSlot,
        int ActingSlot,
        int TechCursor,
        int TechPage,
        int ItemCursor,
        int ItemPage,
        int TargetCursor);

    public BattleSnapshot? Capture(nuint imageBase, nuint menu)
    {
        try
        {
            if (imageBase == 0 || !IsBattleMenu(imageBase, menu) ||
                !TryReadState(imageBase, menu, out var state))
            {
                return null;
            }

            var party = ReadParty(state.Canvas);
            if (party is null)
            {
                // A slot the game says is on screen could not be read coherently. Reporting an
                // empty party here would silently hide a member the player can see.
                return null;
            }

            var names = ReadBattlerNames(imageBase, state.Canvas);
            var focus = ReadFocus(imageBase, state, names);

            // Nothing that selects a panel, a row, or a target may have moved while we read, and
            // the object must still be a battle menu, or the snapshot is a blend of two frames.
            if (!IsBattleMenu(imageBase, menu) ||
                !TryReadState(imageBase, menu, out var recheck) || recheck != state)
            {
                return null;
            }

            return new BattleSnapshot(
                party,
                focus.Identity,
                focus.Text,
                new ReadOnlyDictionary<int, string>(names));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private bool IsBattleMenu(nuint imageBase, nuint menu) =>
        menu != 0 && Pointer(menu, out var vtable) &&
        (vtable == imageBase + ClassicBattleMenuVtableRva || vtable == imageBase + TouchBattleMenuVtableRva);

    private bool TryReadState(nuint imageBase, nuint menu, out BattleState state)
    {
        state = default;
        if (!Pointer(imageBase + CanvasGlobalRva, out var canvas) ||
            !Int32(canvas + ModeOffset, out var mode) ||
            !Int32(menu + MenuModeOffset, out var menuMode) ||
            !Int32(canvas + PhaseGateOffset, out var phase) ||
            !Int32(canvas + ActiveSlotOffset, out var activeSlot) ||
            !Int32(canvas + ActingSlotOffset, out var acting) ||
            !Int32(canvas + TargetCursorOffset, out var targetCursor))
        {
            return false;
        }

        var slot = acting is >= 0 and < PartySlotCount ? acting : -1;
        var techCursor = -1;
        var techPage = -1;
        if (slot >= 0)
        {
            var offset = (nuint)(slot * 4);
            if (!Int32(canvas + TechCursorOffset + offset, out techCursor) ||
                !Int32(canvas + TechPageBaseOffset + offset, out techPage))
            {
                return false;
            }
        }

        if (!Int32(canvas + ItemCursorOffset, out var itemCursor) ||
            !Int32(canvas + ItemPageBaseOffset, out var itemPage))
        {
            return false;
        }

        state = new BattleState(
            canvas, mode, menuMode, phase, activeSlot, acting,
            techCursor, techPage, itemCursor, itemPage, targetCursor);
        return true;
    }

    private IReadOnlyList<BattlePartyMemberSnapshot>? ReadParty(nuint canvas)
    {
        var party = new List<BattlePartyMemberSnapshot>(PartySlotCount);
        for (var slot = 0; slot < PartySlotCount; slot++)
        {
            if (!Int32(canvas + PartyPresenceOffset + (nuint)(slot * 4), out var present))
            {
                return null;
            }

            if (present == 0)
            {
                continue;
            }

            if (!TryReadCharacterName(canvas, slot, out var name))
            {
                return null;
            }

            var record = canvas + BattlerArrayOffset + (nuint)(slot * BattlerStride);
            if (!UInt16(record + BattlerHpOffset, out var hp) ||
                !UInt16(record + BattlerMaximumHpOffset, out var maximumHp) ||
                !UInt16(record + BattlerMpOffset, out var mp) ||
                !UInt16(record + BattlerMaximumMpOffset, out var maximumMp))
            {
                return null;
            }

            // BattleSession decorates this HUD-only snapshot with the separately
            // audited applied visual status from BattlePresentationCapture.
            party.Add(new BattlePartyMemberSnapshot(
                slot, name, hp, maximumHp, mp, maximumMp, string.Empty));
        }

        return new ReadOnlyCollection<BattlePartyMemberSnapshot>(party);
    }

    private bool TryReadCharacterName(nuint canvas, int slot, out string name)
    {
        name = string.Empty;
        if (!Int32(canvas + PartyCharacterIdOffset + (nuint)(slot * 4), out var character) ||
            character is < 0 or >= CharacterIdLimit ||
            !strings.TryReadName(canvas + CustomNameOffset + (nuint)(character * CustomNameStride),
                out var read, out _) ||
            string.IsNullOrWhiteSpace(read))
        {
            return false;
        }

        name = read;
        return true;
    }

    /// <summary>
    /// A lookup table keyed by battler slot, for consumers that already hold a slot number - a
    /// damage or miss popup, or a committed target. Party slots use their customised name; enemy
    /// slots use the same name index the native popup path uses. It is deliberately <em>not</em> a
    /// roster: nothing here proves which enemy slots are occupied, so an unused slot may carry a
    /// stale name and the map must never be read out as a list. This capture preserves native
    /// names; the presentation layer adds stable slot identities to actual targets and hits.
    /// </summary>
    private Dictionary<int, string> ReadBattlerNames(nuint imageBase, nuint canvas)
    {
        var names = new Dictionary<int, string>();
        for (var slot = 0; slot < PartySlotCount; slot++)
        {
            if (Int32(canvas + PartyPresenceOffset + (nuint)(slot * 4), out var present) && present != 0 &&
                TryReadCharacterName(canvas, slot, out var name))
            {
                names[slot] = name;
            }
        }

        for (var battler = FirstEnemyBattler; battler < BattlerLimit; battler++)
        {
            if (!Int32(canvas + EnemyNameIndexOffset + (nuint)(battler * 4), out var index) ||
                index is < 0 or >= FirstReservedNameIndex)
            {
                continue;
            }

            var name = Text(imageBase, MonsterBank, index);
            if (name is not null)
            {
                names[battler] = name;
            }
        }

        return names;
    }

    private (string? Identity, string? Text) ReadFocus(
        nuint imageBase, in BattleState state, IReadOnlyDictionary<int, string> names)
    {
        if (state.Phase != 0)
        {
            return TryReadTargetFocus(state, names, out var target) ? target : (null, null);
        }

        // The classic dispatcher at 0x4200E0 switches on the menu's copy of the mode; the touch
        // menu keeps the same field. Disagreeing copies mean the frame is mid-transition, so no
        // panel is claimed rather than the wrong one.
        if (state.Mode != state.MenuMode)
        {
            return (null, null);
        }

        return state.Mode switch
        {
            ModeBaseCommand => TryReadBaseCommandFocus(imageBase, state, out var command)
                ? command
                : (null, null),
            ModeTech => TryReadTechFocus(imageBase, state, out var tech) ? tech : (null, null),
            ModeItem => TryReadItemFocus(imageBase, state, out var item) ? item : (null, null),
            _ => (null, null),
        };
    }

    /// <summary>
    /// Base command row, proved from the mode 0 renderer at 0x420202 and its cursor at 0x4204C2.
    /// Only three rows exist; row 1 is Tech or Combo according to the native predicate.
    /// </summary>
    private bool TryReadBaseCommandFocus(
        nuint imageBase, in BattleState state, out (string? Identity, string? Text) focus)
    {
        focus = (null, null);
        var canvas = state.Canvas;
        var slot = state.ActiveSlot;
        if (!Int32(canvas + CommandGateOffset, out var gate) || gate != 0 ||
            IsNativeNegativeByte(slot) || slot is < 0 or >= PartySlotCount ||
            !Int32(canvas + MappedBattlerOffset + (nuint)(slot * 4), out var mapped) ||
            IsNativeNegativeByte(mapped) || mapped is < 0 or >= BattlerLimit ||
            !Int32(canvas + BaseCommandRowOffset + (nuint)(mapped * 4), out var row) ||
            row is < 0 or >= PartySlotCount ||
            !TryReadCharacterName(canvas, slot, out var character))
        {
            return false;
        }

        var message = row switch
        {
            0 => AttackMessageId,
            1 => UsesCombo(canvas, slot) ? ComboMessageId : TechMessageId,
            _ => ItemMessageId,
        };

        focus = ($"command:{slot}:{row}:{message}",
            Compose(character, Text(imageBase, BaseCommandBank, message)));
        return true;
    }

    /// <summary>Row 1's label, replicating the native predicate at 0x414880 field for field.</summary>
    private bool UsesCombo(nuint canvas, int slot)
    {
        var offset = (nuint)(slot * 4);
        if (!Int32(canvas + ComboEnabledOffset, out var enabled) || enabled == 0 ||
            !Int32(canvas + ComboMaskHighOffset + offset, out var high) ||
            !Int32(canvas + ComboMaskLowOffset + offset, out var low) || (high | low) == 0 ||
            !Int32(canvas + ComboLockOffset, out var locked))
        {
            return false;
        }

        if (locked == 0)
        {
            return true;
        }

        if (!Int32(canvas + ComboPartyCountOffset, out var count))
        {
            return false;
        }

        if (count == PartySlotCount)
        {
            return true;
        }

        return Int32(canvas + ComboLeaderOffset, out var leader) &&
            leader is >= 0 and < PartySlotCount && leader != slot &&
            Int32(canvas + MappedBattlerOffset + (nuint)(leader * 4), out var mapped) &&
            IsNativeNegativeByte(mapped);
    }

    /// <summary>
    /// Tech list. Both interfaces select through the same canvas state: the six cell grid cursor at
    /// 0x19EA0, the page base at 0x19EC0 and the per character row count at 0x1A2C0 (0x41DA0B), and
    /// both read the row itself at 0x19C8C (0x4205FF, 0x41E3EE).
    /// </summary>
    private bool TryReadTechFocus(
        nuint imageBase, in BattleState state, out (string? Identity, string? Text) focus)
    {
        focus = (null, null);
        var canvas = state.Canvas;
        var slot = state.ActingSlot;
        if (slot is < 0 or >= PartySlotCount ||
            state.TechCursor is < 0 or >= VisibleListRows || state.TechPage < 0 ||
            !Int32(canvas + TechCountOffset + (nuint)(slot * 4), out var count) ||
            count is < 1 or > TechEntriesPerCharacter)
        {
            return false;
        }

        var entry = state.TechPage + state.TechCursor;
        if (entry >= count)
        {
            return false;
        }

        var record = (nuint)(slot * TechEntriesPerCharacter + entry);
        if (!Int32(canvas + TechEntryIdOffset + record * TechEntryStride, out var id) ||
            !Int32(canvas + TechEntryIdOffset + record * TechEntryStride + 4, out var techSlot) ||
            id is < 0 or > 0xFF)
        {
            return false;
        }

        string? label;
        switch (id)
        {
            case DualTechEntryId:
                label = Bracket(Text(imageBase, BaseCommandBank, DualTechMessageId));
                break;
            case TripleTechEntryId:
                label = Bracket(Text(imageBase, BaseCommandBank, TripleTechMessageId));
                break;
            case BlankTechEntryId:
            case EndTechEntryId:
            case EmptyTechEntryId:
                // The renderer draws nothing on these rows, so there is no selection to report.
                return false;
            default:
                label = Text(imageBase, TechBank, id);
                break;
        }

        if (!TryReadCharacterName(canvas, slot, out var owner))
        {
            return false;
        }

        var text = Compose(owner, label);
        if (text is not null && TryReadTechDetail(canvas, slot, techSlot, out var cost, out var usable))
        {
            if (cost is not null)
            {
                text = $"{text}, {cost} MP";
            }

            if (!usable)
            {
                text = $"{text}, unavailable";
            }
        }

        focus = ($"tech:{slot}:{entry}:{id}", text);
        return true;
    }

    /// <summary>
    /// The tech's own record: the greyed flag the row renderer tests at 0x41E59D and the per party
    /// slot MP costs the info panel prints at 0x420991. Costs are signed bytes; a negative one
    /// means the panel leaves that participant blank.
    /// </summary>
    private bool TryReadTechDetail(nuint canvas, int slot, int techSlot, out string? cost, out bool usable)
    {
        cost = null;
        usable = true;
        if (techSlot is < 0 or >= TechEntriesPerCharacter)
        {
            return false;
        }

        var record = canvas + TechRecordOffset +
            (nuint)((slot * TechEntriesPerCharacter + techSlot) * TechRecordStride);
        if (!Byte(record + TechRecordFlagOffset, out var flag))
        {
            return false;
        }

        usable = (flag & 0x80) == 0;

        var costs = new List<string>(PartySlotCount);
        for (var participant = 0; participant < PartySlotCount; participant++)
        {
            if (!Int32(record + (nuint)(TechRecordCostOffset + participant * 4), out var value))
            {
                return false;
            }

            var amount = value & 0xFF;
            if (amount is > 0 and < 0x80)
            {
                costs.Add(amount.ToString(CultureInfo.InvariantCulture));
            }
        }

        cost = costs.Count == 0 ? null : string.Join(" and ", costs);
        return true;
    }

    /// <summary>
    /// Item list. The row renderer at 0x41EA70 reads a 0x14 byte entry: the encoded id at 0x1B668,
    /// a flag byte at 0x1B670 whose 0x80 bit greys the row, and the quantity at 0x1B674. The id is
    /// split at 0x41EAEA into a low twelve bit index plus a group base from the image table at RVA
    /// 0x39906C, and the sum is the bank 0x1B line the native resolver at 0xB7CE0 loads.
    /// </summary>
    private bool TryReadItemFocus(
        nuint imageBase, in BattleState state, out (string? Identity, string? Text) focus)
    {
        focus = (null, null);
        var canvas = state.Canvas;
        if (state.ActingSlot is < 0 or >= PartySlotCount ||
            state.ItemCursor is < 0 or >= VisibleListRows || state.ItemPage < 0 ||
            !Int32(canvas + ItemCountOffset, out var count) || count is < 0 or > MaximumItemEntries)
        {
            return false;
        }

        if (count == 0)
        {
            // The native renderer still visits six cells when the list is empty. Its row
            // renderer skips zero quantities/ids (RVA 1EAB3/1EACE); do not describe stale
            // rendered rows or a retained page as an empty list. See battle-lists-0322-fix.md.
            Span<byte> rows = stackalloc byte[VisibleListRows * ItemEntryStride];
            if (state.ItemCursor != 0 || state.ItemPage != 0 ||
                !memory.TryRead(canvas + ItemIdOffset, rows)) return false;
            for (var i = 0; i < VisibleListRows; i++)
            {
                var row = rows.Slice(i * ItemEntryStride, ItemEntryStride);
                if (BinaryPrimitives.ReadInt32LittleEndian(row) != 0 &&
                    BinaryPrimitives.ReadInt32LittleEndian(row.Slice(ItemQuantityOffset)) != 0)
                    return false;
            }
            if (!TryReadCharacterName(canvas, state.ActingSlot, out var character)) return false;
            var label = Compose(character, Text(imageBase, BaseCommandBank, ItemMessageId));
            Span<byte> again = stackalloc byte[VisibleListRows * ItemEntryStride];
            if (!Int32(canvas + ItemCountOffset, out var countAgain) || countAgain != 0 ||
                !memory.TryRead(canvas + ItemIdOffset, again) || !rows.SequenceEqual(again)) return false;
            focus = ($"item:empty:{state.ActingSlot}", label is null ? null : $"{label}. Empty.");
            return true;
        }

        var entry = state.ItemPage + state.ItemCursor;
        if (entry >= count)
        {
            return false;
        }

        var record = canvas + ItemIdOffset + (nuint)(entry * ItemEntryStride);
        if (!Int32(record, out var encoded) || encoded <= 0 ||
            !Int32(record + ItemQuantityOffset, out var quantity) || quantity <= 0 ||
            !Byte(record + ItemFlagOffset, out var flag) ||
            !TryResolveItemName(imageBase, encoded, out var name) ||
            !TryReadCharacterName(canvas, state.ActingSlot, out var owner))
        {
            return false;
        }

        var text = Compose(owner, name);
        if (text is not null)
        {
            text = $"{text} x{quantity.ToString(CultureInfo.InvariantCulture)}";
            if ((flag & 0x80) != 0)
            {
                text = $"{text}, unavailable";
            }
        }

        focus = ($"item:{entry}:{encoded}", text);
        return true;
    }

    private bool TryResolveItemName(nuint imageBase, int encoded, out string? name)
    {
        name = null;
        var group = encoded >> 12;
        if (group is < 0 or >= ItemGroupCount ||
            !Int32(imageBase + ItemGroupTableRva + (nuint)(group * 4), out var start) || start < 0)
        {
            return false;
        }

        name = Text(imageBase, ItemBank, start + (encoded & 0xFFF));
        return true;
    }

    /// <summary>
    /// Target cursor, proved from the cursor movement pair at 0x4B100/0x4B160: the cursor wraps at
    /// 11, entries whose sign bit is set are skipped, and the committed battler is stored at
    /// canvas + 0x1ACD4. Group membership is owned elsewhere, so only the single committed
    /// recipient is reported. Reached only while the phase gate is non-zero.
    /// </summary>
    private bool TryReadTargetFocus(
        in BattleState state, IReadOnlyDictionary<int, string> names, out (string? Identity, string? Text) focus)
    {
        focus = (null, null);
        var canvas = state.Canvas;
        var cursor = state.TargetCursor;
        if (cursor is < 0 or >= TargetCursorLimit ||
            !Int32(canvas + TargetCandidateOffset + (nuint)(cursor * 4), out var candidate) ||
            IsNativeNegativeByte(candidate) || candidate is < 0 or >= BattlerLimit ||
            !Int32(canvas + TargetRecipientOffset, out var recipient) || recipient != candidate)
        {
            return false;
        }

        focus = ($"target:{cursor}:{recipient}",
            names.TryGetValue(recipient, out var name) ? name : null);
        return true;
    }

    private static string? Compose(string character, string? label) =>
        label is null ? null : $"{character}: {label}";

    private static string? Bracket(string? label) => label is null ? null : $"[{label}]";

    /// <summary>
    /// Resolves one loaded message. The default path walks the TextManager the game already
    /// populated, exactly as the native resolver at 0x1B9060 indexes it: a vector of bank
    /// pointers, each a vector of 24-byte MSVC strings.
    /// </summary>
    private string? Text(nuint imageBase, int bank, int index)
    {
        if (localizedText is not null)
        {
            var injected = localizedText(bank, index);
            if (!string.IsNullOrWhiteSpace(injected))
            {
                return injected;
            }
        }

        if (bank < 0 || index < 0 || !Pointer(imageBase + TextManagerGlobalRva, out var manager) ||
            !Vector(manager, 4, MaximumBankCount, out var banks, out var bankCount) || bank >= bankCount ||
            !Pointer(banks + (nuint)(bank * 4), out var file) ||
            !Vector(file, MsvcStringReader.LayoutSize, MaximumLineCount, out var lines, out var lineCount) ||
            index >= lineCount ||
            !strings.TryRead(lines + (nuint)(index * MsvcStringReader.LayoutSize), out var text, out _) ||
            string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Both vectors are re-read: a reload between the bounds check and the string read would
        // otherwise hand back a line from a bank that no longer exists.
        if (!Pointer(imageBase + TextManagerGlobalRva, out var recheck) || recheck != manager ||
            !Vector(manager, 4, MaximumBankCount, out var banksAgain, out var bankCountAgain) ||
            banksAgain != banks || bankCountAgain != bankCount ||
            !Pointer(banks + (nuint)(bank * 4), out var fileAgain) || fileAgain != file ||
            !Vector(file, MsvcStringReader.LayoutSize, MaximumLineCount, out var linesAgain, out var lineCountAgain) ||
            linesAgain != lines || lineCountAgain != lineCount)
        {
            return null;
        }

        return text;
    }

    private bool Vector(nuint address, int stride, int maximum, out nuint begin, out int count)
    {
        count = 0;
        if (!Pointer(address, out begin) || !Pointer(address + 4, out var end) || end < begin ||
            (end - begin) % (nuint)stride != 0 || (end - begin) / (nuint)stride > (nuint)maximum)
        {
            return false;
        }

        count = (int)((end - begin) / (nuint)stride);
        return true;
    }

    /// <summary>The renderers test these fields as signed bytes, so 0x80 marks "none".</summary>
    private static bool IsNativeNegativeByte(int value) => (value & 0x80) != 0;

    private bool Pointer(nuint address, out nuint value)
    {
        value = 0;
        if (!Int32(address, out var raw))
        {
            return false;
        }

        value = (nuint)(uint)raw;
        return value != 0;
    }

    private bool Int32(nuint address, out int value)
    {
        value = 0;
        Span<byte> bytes = stackalloc byte[4];
        if (address == 0 || (ulong)address + 4 > uint.MaxValue || !memory.TryRead(address, bytes))
        {
            return false;
        }

        value = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        return true;
    }

    private bool UInt16(nuint address, out int value)
    {
        value = 0;
        Span<byte> bytes = stackalloc byte[2];
        if (address == 0 || (ulong)address + 2 > uint.MaxValue || !memory.TryRead(address, bytes))
        {
            return false;
        }

        value = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
        return true;
    }

    private bool Byte(nuint address, out byte value)
    {
        value = 0;
        Span<byte> bytes = stackalloc byte[1];
        if (address == 0 || (ulong)address + 1 > uint.MaxValue || !memory.TryRead(address, bytes))
        {
            return false;
        }

        value = bytes[0];
        return true;
    }
}
