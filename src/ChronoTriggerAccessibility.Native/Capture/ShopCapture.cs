using System.Buffers.Binary;
using System.Globalization;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

/// <summary>Read-only Steam shop presentation. Offsets and ownership are audited in
/// docs/shop-native-0325.md against the supported executable and live shop captures.</summary>
public sealed class ShopCapture(IReadableMemory memory)
{
    public const uint SceneVtableRva = 0x3B3C40;
    public const uint ActionVtableRva = 0x3AA414;
    public const uint ItemVtableRva = 0x3AAC6C;
    public const uint CountVtableRva = 0x3AA860;
    private readonly RenderedNodeTextCapture rendered = new(memory);
    private readonly FieldSubmenuCapture menus = new(memory);
    private readonly MsvcStringReader strings = new(memory);
    private readonly LoadedGameTextCapture messages = new(memory);

    private sealed record State(byte Items, byte Quantity, byte Equipment, nuint Page,
        nuint Manager, uint Key, nuint Control, string Row);

    public bool IsScene(nuint image, nuint scene) => image != 0 &&
        Word(scene, out var vt) && vt == image + SceneVtableRva && Visible(scene);

    public FieldSubmenuSnapshot? Capture(nuint image, nuint scene)
    {
        try
        {
            if (!TryState(image, scene, out var state)) return null;
            string? body;
            var identity = $"shop:{state}";
            if (state.Equipment != 0)
            {
                var equipment = menus.Capture(image, state.Page);
                if (equipment is null) return null;
                body = $"{equipment.Title}. {equipment.Text}";
                identity += $":{equipment.FocusIdentity}";
            }
            else if (state.Items == 0)
            {
                var selected = rendered.Read(state.Control);
                body = selected is { Count: 1 } ? selected[0] : null;
            }
            else if (state.Quantity != 0) body = QuantityText(state.Page);
            else body = ItemText(image, scene, state);

            if (body is null) return null;
            if (state.Equipment != 0)
                return TryState(image, scene, out var equipmentAgain) && equipmentAgain == state
                    ? new("Shop", "Shop", identity, body) : null;
            if (!Word(scene + 0x2A4, out var fundsLabel) ||
                !Under(fundsLabel, scene) || rendered.Read(fundsLabel) is not { Count: 1 } funds ||
                !Number(funds[0], out _) || !TryState(image, scene, out var again) || again != state) return null;
            return new("Shop", "Shop", identity, $"{body.TrimEnd('.')}. Funds: {funds[0]} G.");
        }
        catch (Exception) { return null; }
    }

    private bool TryState(nuint image, nuint scene, out State state)
    {
        state = null!;
        if (!IsScene(image, scene) || !Byte(scene + 0x298, out var items) || items > 1 ||
            !Byte(scene + 0x299, out var quantity) || quantity > 1 ||
            !Byte(scene + 0x29A, out var equipment) || equipment > 1) return false;
        if (equipment != 0)
        {
            if (!EquipmentChild(image, scene, out var equipmentPage)) return false;
            state = new(items, quantity, equipment, equipmentPage, 0, 0, 0, "equipment");
            return true;
        }
        var offset = items == 0 ? 0x2B0u : quantity != 0 ? 0x2B4u : 0x2ACu;
        if (!Word(scene + offset, out var page) || page == 0 ||
            !Word(page + 0x16C, out var parent) || parent != scene || !Visible(page) ||
            !Word(page, out var vt)) return false;
        var expected = items == 0 ? ActionVtableRva : quantity != 0 ? CountVtableRva : ItemVtableRva;
        if (vt != image + expected) return false;
        if (items != 0 && quantity == 0 && ItemRow(page, out var emptyMode, out var emptyGroup,
            out _, out var emptyCount, out _, out _, out _) && emptyCount == 0)
        {
            if (!menus.TryReadActiveState(image, page, out var emptyManager, out var emptyKey, out var emptyState) ||
                emptyKey != 0 || !Word(emptyState, out var emptyVt) || emptyVt != image + 0x3ABC2C) return false;
            state = new(items, quantity, equipment, page, emptyManager, emptyKey, 0, $"empty:{emptyMode}:{emptyGroup}");
            return true;
        }
        if (!menus.TryReadOwnedFocus(image, page, out var manager, out var key, out var control)) return false;
        var row = string.Empty;
        if (items != 0 && quantity == 0)
        {
            if (!ItemRow(page, out var mode, out var category, out var cursor, out var count,
                out var encoded, out var price, out var stock) || cursor != key ||
                !Word(page + 0x2F0, out var list) || !Word(list, out var listVt) ||
                listVt != image + FieldSubmenuCapture.MenuListViewVtableRva ||
                !Word(list + 0x280, out var listManager) || listManager != manager || !Under(control, list)) return false;
            row = $"{mode}:{category}:{cursor}:{count}:{encoded}:{price}:{stock}";
        }
        if (quantity != 0)
        {
            if (key > 1 || !Word(page + 0x2D8, out var mode) || mode > 1 ||
                !Word(page + 0x2DC, out var price) || !Word(page + 0x2E8, out var amount)) return false;
            row = $"{mode}:{price}:{amount}";
        }
        state = new(items, quantity, equipment, page, manager, key, control, row);
        return true;
    }

    private bool ItemRow(nuint page, out uint mode, out uint category, out uint cursor, out uint count,
        out uint encoded, out uint price, out uint stock)
    {
        mode = category = cursor = count = encoded = price = stock = 0;
        if (!Word(page + 0x2D8, out mode) || mode > 1 || !Word(page + 0x2EC, out category) || category > 3 ||
            !Word(page + 0x2F4, out cursor) || !Word(page + 0x2D4, out var groups) || groups == 0 ||
            !Word(groups + category * 12, out var begin) || !Word(groups + category * 12 + 4, out var end) ||
            end < begin || (end - begin) % 12 != 0 || end - begin > 512 * 12) return false;
        count = (end - begin) / 12;
        return count == 0 || cursor < count && Word(begin + cursor * 12, out encoded) && encoded < 0xD000 &&
            Word(begin + cursor * 12 + 4, out price) && price <= 9999999 &&
            Word(begin + cursor * 12 + 8, out stock) && stock <= 99;
    }

    private string? ItemText(nuint image, nuint scene, State state)
    {
        var page = state.Page;
        if (!ItemRow(page, out var mode, out var category, out var cursor, out var count, out var encoded, out var price, out _)) return null;
        if (count == 0)
        {
            // Exact table at 3AAC48, used by the empty-page builder 2292E0.
            var empty = messages.Get(image, 0x23, (int)(0x72 + category + mode * 4));
            return string.IsNullOrWhiteSpace(empty) ? null : $"{(mode == 0 ? "Buy" : "Sell")}. {empty}";
        }
        if (rendered.Read(state.Control) is not { Count: 2 } row || !Number(row[1], out var displayedPrice) ||
            displayedPrice != price || !Word(page + 0x2E4, out var info) ||
            !Word(info, out var vt) || vt != image + 0x3AB994 || !Under(info, scene) ||
            rendered.Read(info) is not { } details || !Word(page + 0x2CC, out var help) ||
            !Under(help, page) || rendered.Read(help) is not { } helpText) return null;
        var text = $"{(mode == 0 ? "Buy" : "Sell")}. {row[0]}, {row[1]} G, {cursor + 1} of {count}.";
        if (details.Count > 0) text += " " + Join(details) + ".";
        if (helpText.Count > 0) text += " " + Join(helpText).TrimEnd('.') + ".";
        if ((encoded >> 12) < 3)
        {
            var comparisons = Comparisons(image, scene);
            if (comparisons is null) return null;
            if (comparisons.Length > 0) text += " " + comparisons;
        }
        return text;
    }

    private string? QuantityText(nuint page)
    {
        if (!Word(page + 0x2D8, out var mode) || mode > 1 || !Word(page + 0x2E8, out var amount) || amount is 0 or > 99 ||
            !Word(page + 0x2DC, out var price) || price > 9999999 ||
            !Word(page + 0x2CC, out var amountLabel) || !Word(page + 0x2D0, out var totalLabel) ||
            !Under(amountLabel, page) || !Under(totalLabel, page) ||
            rendered.Read(amountLabel) is not { Count: 1 } quantity ||
            rendered.Read(totalLabel) is not { Count: 1 } total ||
            !Number(quantity[0], out var q) || q != amount ||
            !Number(total[0], out var cost) || cost != (long)price * amount ||
            rendered.Read(page) is not { Count: >= 4 } lines || lines[1] != quantity[0] || lines[2] != total[0]) return null;
        var body = $"{(mode == 0 ? "Buy" : "Sell")}. {lines[0]}. Quantity {quantity[0]}. Total {total[0]} {lines[3]}";
        if (lines.Count > 4) body += ". " + Join(lines.Skip(4));
        return body;
    }

    private string? Comparisons(nuint image, nuint scene)
    {
        if (!Word(scene + 0x29C, out var manager) || !Word(manager, out var vt) || vt != image + 0x3AB144 ||
            !Under(manager, scene) || !Word(manager + 0x288, out var begin) || !Word(manager + 0x28C, out var end) ||
            end < begin || (end - begin) % 4 != 0 || end - begin > 28 ||
            !Word(image + 0x41B4C4, out var canvas) || canvas == 0) return null;
        var result = new List<string>();
        var ids = new HashSet<uint>();
        for (uint offset = 0; offset < end - begin; offset += 4)
        {
            if (!Word(begin + offset, out var card) || !Word(card, out var cv) || cv != image + 0x3AB700 ||
                !Word(card + 0x298, out var id)) return null;
            if (id == uint.MaxValue) continue;
            if (id >= 7 || !ids.Add(id) || !Under(card, manager) ||
                !strings.TryRead(canvas + 0x1908 + id * 24, out var name, out _) || string.IsNullOrWhiteSpace(name) ||
                !Byte(card + 0x278, out var steam) || steam != 1 ||
                !Word(card + 0x288, out var staticPose) || staticPose == 0 ||
                !Byte(staticPose + 0x1AD, out var disabled)) return null;
            if (disabled != 0) { result.Add($"{name}: cannot equip."); continue; }
            if (!Stat(card, 0x2A4, out var attack) || !Stat(card, 0x2A8, out var defense)) return null;
            result.Add($"{name}: Attack {attack}; Defense {defense}.");
        }
        return string.Join(" ", result);
    }

    private bool EquipmentChild(nuint image, nuint scene, out nuint page)
    {
        page = 0;
        // 2B63C0 adds the newly constructed EquipSteam page directly to the scene;
        // it retains the pointer in its callback, not in a scene member field.
        if (!Word(scene + 0x160, out var begin) || !Word(scene + 0x164, out var end) ||
            end < begin || (end - begin) % 4 != 0 || end - begin > 128 * 4) return false;
        for (uint off = 0; off < end - begin; off += 4)
        {
            if (!Word(begin + off, out var child) || !Word(child, out var vt)) return false;
            if (vt != image + FieldSubmenuCapture.EquipSteamNodeVtableRva) continue;
            if (!Word(child + 0x16C, out var parent) || parent != scene || !Visible(child) || page != 0) return false;
            page = child;
        }
        return page != 0;
    }

    private bool Stat(nuint card, uint offset, out string value)
    {
        value = string.Empty;
        if (!Word(card + offset, out var stat) || !Under(stat, card) || !Word(stat + 0x278, out var original) ||
            rendered.Read(stat) is not { Count: 1 } label || !Number(label[0], out var current)) return false;
        value = label[0] + (current > original ? ", increased" : current < original ? ", decreased" : string.Empty);
        return true;
    }

    private bool Under(nuint node, nuint owner)
    {
        var visited = new HashSet<nuint>();
        for (var i = 0; i < 32 && node != 0 && visited.Add(node); i++)
        {
            if (!Visible(node)) return false;
            if (node == owner) return true;
            if (!Word(node + 0x16C, out var parent)) return false;
            node = parent;
        }
        return false;
    }
    private bool Visible(nuint node) => Byte(node + 0x1AD, out var visible) && visible == 1;
    private static string Join(IEnumerable<string> lines)
    {
        var parts = lines.ToArray();
        var phrases = new List<string>();
        for (var i = 0; i < parts.Length; i++)
            phrases.Add(parts[i].EndsWith(':') && i + 1 < parts.Length
                ? $"{parts[i]} {parts[++i]}" : parts[i].TrimEnd('.'));
        return string.Join(". ", phrases).Trim();
    }
    private static bool Number(string text, out long value) => long.TryParse(text, NumberStyles.AllowThousands,
        CultureInfo.InvariantCulture, out value) && value >= 0;
    private bool Word(nuint address, out uint value)
    {
        value = 0; Span<byte> bytes = stackalloc byte[4];
        if (address == 0 || (ulong)address + 4 > 0x100000000UL || !memory.TryRead(address, bytes)) return false;
        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes); return true;
    }
    private bool Byte(nuint address, out byte value)
    {
        value = 0; Span<byte> bytes = stackalloc byte[1];
        if (address == 0 || (ulong)address >= 0x100000000UL || !memory.TryRead(address, bytes)) return false;
        value = bytes[0]; return true;
    }
}
