using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

/// <summary>
/// Reads the field main-menu submenu pages without calling game code.
/// <para>Every offset is justified in docs/field-submenus-native-audit.md sections 3 to 9. The page
/// is identified from the node's own vtable, so the capture never has to trust which top-menu row
/// the player pressed.</para>
/// <para>The selection is resolved the way the native focus setter at 0x1DD3E0 resolves it: the
/// manager's focus key is looked up in the manager's own focusable map, which yields the
/// <c>FocusableState</c> holding the focused control. Party additionally correlates its icon
/// controls with separate character cards; see docs/party-0321-native-audit.md.</para>
/// </summary>
public sealed partial class FieldSubmenuCapture(IReadableMemory memory)
{
    public const uint ItemGroupTableRva = 0x39906C;
    public const int ItemGroupCount = 13;

    // Node classes, from the MSVC RTTI sweep in artifacts/research/field-submenus-0318/rtti-all.txt.
    public const uint ClassicItemNodeVtableRva = 0x3A383C;
    public const uint ClassicTechNodeVtableRva = 0x3A3C4C;
    public const uint ClassicFormationNodeVtableRva = 0x3A3270;
    public const uint EquipSteamNodeVtableRva = 0x3A84B8;
    public const uint TouchItemNodeVtableRva = 0x3A8D6C;
    public const uint EquipNodeVtableRva = 0x3A7C30;

    /// <summary>nsInput::ManagerStack, allocated by the MenuNodeBase constructor at 0x1DEDE7.</summary>
    public const uint ManagerStackVtableRva = 0x3A5D04;
    /// <summary>nsInput::Manager, constructed at 0x1DCCCB.</summary>
    public const uint ManagerVtableRva = 0x3A5D0C;
    /// <summary>nsMenu::FocusableState, constructed at 0x191345 / 0x191B5E.</summary>
    public const uint FocusableStateVtableRva = 0x3AC3F4;

    /// <summary>MenuNodeBase + 0x2C0 holds the manager stack (0x1DEE02).</summary>
    public const uint ManagerStackOffset = 0x2C0;
    public const uint StackBeginOffset = 4;
    public const uint StackEndOffset = 8;
    /// <summary>Manager + 0x2C4 is the focused key; 0x80000000 is the native "nothing" (0x1DCCF7).</summary>
    public const uint ManagerFocusKeyOffset = 0x2C4;
    public const uint NoManagerFocusKey = 0x80000000;

    /// <summary>
    /// Manager + 0x294 holds a <em>pointer</em> to the focusable map, not the map itself: 0x1DD409
    /// is `mov esi, [manager + 0x294]` and every later access is relative to that value, and the
    /// constructor at 0x1DCCDC initialises the field to a null pointer rather than to an inline
    /// container. The map is an MSVC unordered_map, so map + 4 is the list sentinel and each node
    /// is next, prev, key, value.
    /// </summary>
    public const uint FocusableMapOffset = 0x294;
    public const uint MapSentinelOffset = 4;
    public const uint ListNextOffset = 0;
    public const uint ListKeyOffset = 8;
    public const uint ListValueOffset = 0x0C;
    /// <summary>FocusableState + 0x14 is the control it wraps (0x19134B).</summary>
    public const uint FocusableStateControlOffset = 0x14;

    /// <summary>cocos Node parent, audited in WorldViewportCapture.</summary>
    public const uint NodeParentOffset = 0x16C;

    /// <summary>
    /// A list view is never a selection on the generic path: its subtree is every visible row. It
    /// is refused there. The Inventory row path does not go through the generic reader at all, so
    /// a focused inventory list view is still served from its own row data.
    /// </summary>
    public const uint MenuListViewVtableRva = 0x3A60A0;

    /// <summary>nsMenu::CharaEquipManager, constructed at 0x2063B3.</summary>
    public const uint CharaEquipManagerVtableRva = 0x3A8194;

    /// <summary>
    /// MenuNodeEquipSteam + 0x2F0 is the CharaEquipManager. 0x205C00 is a MenuNodeEquipSteam member
    /// (`mov edi, ecx` at 0x205C2A): it releases the old child, allocates 0x368 bytes, runs the
    /// CharaEquipManager constructor 0x206380, stores the result at 0x205C99 and addChilds it at
    /// 0x205C9F.
    /// </summary>
    public const uint EquipmentManagerOffset = 0x2F0;

    /// <summary>
    /// The detail container lives on the <em>child</em>, not on the page node. The panel builder is
    /// 0x2066A0, which is CharaEquipManager vtable slot 158 (0x3A8194 + 158*4 = 0x3A840C, inside
    /// its own table because the next vtable starts at 0x3A84B8). It keeps `this` in [ebp-0x94]
    /// (0x2066CD), creates the container and stores it on itself at 0x2069BC
    /// (`mov [edi + 0x2EC], eax`), then adds its panels to it at 0x206F97 and 0x2070BD and passes
    /// the second to 0x209410, which fills it with the seven stat captions from bank 0x23 lines
    /// 0x33 to 0x39.
    /// <para>MenuNodeEquipSteam has an unrelated field at its own +0x2EC (written at 0x20565F), so
    /// reading the container from the page node is the wrong owner and yields a different object.
    /// </para>
    /// </summary>
    public const uint EquipmentDetailPanelOffset = 0x2EC;

    // ClassicMenuNodeItem fields.
    /// <summary>The scroll view owning the row controls (0x1C5048); 0x1C5890 scrolls to a row.</summary>
    public const uint ItemListViewOffset = 0x2C8;
    public const uint ItemManagerOffset = 0x2DC;
    public const uint ItemCategoryOffset = 0x2F0;
    public const uint ItemCategoryLabelOffset = 0x2F4;
    public const int FirstItemCategoryKey = 2;
    public const int ItemCategoryCount = 6;
    public const int FirstItemCategoryCaption = 0x40;
    /// <summary>Row vector begin; records are 12 bytes (0x1C75EE, 0x1C6627 divides by 12).</summary>
    public const uint ItemRowsBeginOffset = 0x2D0;
    public const uint ItemRowsEndOffset = 0x2D4;
    public const int ItemRowStride = 12;
    /// <summary>Encoded item id, pushed straight into the help updater at 0x1C75F7.</summary>
    public const int ItemRowEncodedIdOffset = 0;
    /// <summary>Quantity; the item-use handler refuses nonpositive quantities (0x1C7FCD).</summary>
    public const int ItemRowQuantityOffset = 4;
    /// <summary>Cursor row, -1 when nothing is selected (0x1C75E8, cleared at 0x1C5C6A).</summary>
    public const uint ItemCursorOffset = 0x2F8;
    /// <summary>Help mode, set for nonnegative encoded IDs at 0x1C76D1 and cleared at 0x1C7691.
    /// This includes the empty placeholder's ID zero and does not prove nonempty help text.</summary>
    public const uint ItemHelpVisibleOffset = 0x2FC;
    /// <summary>Row picked up for reordering, -1 when none (0x1C5B94, reset at 0x1C75B8).</summary>
    public const uint ItemHeldRowOffset = 0x32C;

    public const int ItemNameBank = 0x1B;
    public const int ItemHelpBank = 0x1D;
    /// <summary>The page captions every node loads for itself from bank 0x23.</summary>
    public const int MenuCaptionBank = 0x23;
    public const int EquipmentCaptionMessageId = 0x20;
    public const int InventoryCaptionMessageId = 0x21;
    public const int TechCaptionMessageId = 0x22;
    public const int FormationCaptionMessageId = 0x25;

    public const string InventoryKind = "Inventory";
    public const string TechKind = "Tech";
    public const string FormationKind = "Formation";
    public const string EquipmentKind = "Equipment";

    private const int MaximumRows = 512;
    private const int MaximumManagerDepth = 64;
    private const int MaximumFocusableEntries = 512;
    private const int MaximumAncestorDepth = 64;
    /// <summary>A selected character card has 18 Label fragments, including colon separators.
    /// This bounds fragments within the independently selected control, not menu rows.</summary>
    private const int MaximumRenderedLines = 64;
    /// <summary>The equipment detail panel is a panel by design, so it gets its own bound.</summary>
    private const int MaximumPanelLines = 32;

    private readonly LoadedGameTextCapture text = new(memory);
    private readonly RenderedNodeTextCapture rendered = new(memory);

    /// <summary>
    /// The node classes this capture will read. Classes that are identified but whose field layout
    /// is not yet proven are deliberately absent rather than listed and guessed at.
    /// </summary>
    public static IReadOnlyList<uint> SupportedNodeVtableRvas { get; } =
        new ReadOnlyCollection<uint>([
            ClassicItemNodeVtableRva,
            ClassicTechNodeVtableRva,
            ClassicFormationNodeVtableRva,
            EquipSteamNodeVtableRva,
        ]);

    /// <summary>Everything that decides what the page is presenting this frame.</summary>
    private readonly record struct PageState(string Kind, nuint Manager, uint FocusKey, nuint Control);

    public FieldSubmenuSnapshot? Capture(nuint imageBase, nuint node)
    {
        try
        {
            if (imageBase != 0 && node != 0 && Pointer(node, out var vtable) &&
                vtable == imageBase + ClassicFormationNodeVtableRva)
                return CaptureFormation(imageBase, node);

            if (imageBase == 0 || !TryReadPageState(imageBase, node, out var state))
            {
                return null;
            }

            var caption = text.Get(imageBase, MenuCaptionBank, CaptionMessageId(state.Kind));
            if (string.IsNullOrWhiteSpace(caption))
            {
                return null;
            }

            string? rowKey = null;
            FieldSubmenuSnapshot? snapshot;
            if (state.Kind == InventoryKind && Pointer(node + 0x314, out var itemUseManager) &&
                itemUseManager == state.Manager)
                snapshot = CaptureItemUseTarget(imageBase, node, caption, state);
            else
                snapshot = state.Kind == InventoryKind && IsUnderItemList(node, state.Control)
                    ? CaptureInventoryRow(imageBase, node, caption, state, out rowKey)
                    : CaptureFocusedControl(imageBase, node, caption, state);

            // Nothing that selects the page, the control or the row may move while we read.
            if (snapshot is null || !TryReadPageState(imageBase, node, out var recheck) || recheck != state ||
                (rowKey is not null && (!TryReadRowKey(node, out var rowAgain) || rowAgain != rowKey)))
            {
                return null;
            }

            return snapshot;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private FieldSubmenuSnapshot? CaptureFocusedControl(
        nuint imageBase, nuint node, string caption, in PageState state)
    {
        // Only the generic reader has to refuse a container. The Inventory row path never reaches
        // here, so a focused inventory list view is still served from its own row data.
        if (!Pointer(state.Control, out var controlVtable) ||
            controlVtable == imageBase + MenuListViewVtableRva)
        {
            return null;
        }

        var lines = rendered.Read(state.Control);
        if (lines is null || lines.Count > MaximumRenderedLines)
        {
            return null;
        }

        // Retail Inventory category controls contain sprites. Their names live in the native
        // category table and the separate heading, not in those controls' child labels.
        if (lines.Count == 0)
            return state.Kind == InventoryKind ? CaptureInventoryCategory(imageBase, node, caption, state) : null;

        var body = JoinRenderedLines(lines);
        if (state.Kind == EquipmentKind && TryReadEquipmentDetail(imageBase, node, out var detail))
        {
            body = $"{body}. {detail}";
        }

        return string.IsNullOrWhiteSpace(body)
            ? null
            : new FieldSubmenuSnapshot(
                state.Kind,
                caption,
                $"{state.Kind}:control:{state.FocusKey.ToString("X", CultureInfo.InvariantCulture)}",
                body);
    }

    /// <summary>
    /// The CharaEquipManager the Equipment page owns, validated by class and by really hanging off
    /// the page node. Everything the page renders for the selected character lives on this child.
    /// </summary>
    private bool TryReadEquipmentChild(nuint imageBase, nuint node, out nuint child) =>
        Pointer(node + EquipmentManagerOffset, out child) &&
        Pointer(child, out var childVtable) && childVtable == imageBase + CharaEquipManagerVtableRva &&
        HasAncestor(child, node);

    /// <summary>
    /// The Equipment page's own detail panel. It is a panel rather than a selection, so it is read
    /// separately from the focused slot and under its own bound; the player sees both at once.
    /// </summary>
    private bool TryReadEquipmentDetail(nuint imageBase, nuint node, out string detail)
    {
        detail = string.Empty;
        if (!TryReadEquipmentChild(imageBase, node, out var child) ||
            !Pointer(child + EquipmentDetailPanelOffset, out var panel) ||
            !HasAncestor(panel, child))
        {
            return false;
        }

        var lines = rendered.Read(panel);
        if (lines is null || lines.Count == 0 || lines.Count > MaximumPanelLines ||
            !TryReadEquipmentChild(imageBase, node, out var childAgain) || childAgain != child ||
            !Pointer(child + EquipmentDetailPanelOffset, out var panelAgain) || panelAgain != panel ||
            !HasAncestor(panel, child))
        {
            return false;
        }

        detail = JoinRenderedLines(lines);
        return !string.IsNullOrWhiteSpace(detail);
    }

    private static string JoinRenderedLines(IReadOnlyList<string> lines)
    {
        if (!lines.Contains(":")) return string.Join(", ", lines);
        var clauses = new List<string>();
        var words = new List<string>();
        foreach (var line in lines.Append(":"))
        {
            if (line != ":") { words.Add(line); continue; }
            if (words.Count > 0) clauses.Add(string.Join(" ", words).Replace("/ ", "/", StringComparison.Ordinal));
            words.Clear();
        }
        return string.Join(". ", clauses);
    }

    private FieldSubmenuSnapshot? CaptureInventoryCategory(
        nuint imageBase, nuint node, string caption, in PageState state)
    {
        if (state.FocusKey < FirstItemCategoryKey || state.FocusKey >= FirstItemCategoryKey + ItemCategoryCount ||
            !Pointer(node + ItemManagerOffset, out var manager) || manager != state.Manager) return null;
        var category = (int)state.FocusKey - FirstItemCategoryKey;
        var name = text.Get(imageBase, MenuCaptionBank, FirstItemCategoryCaption + category);
        if (string.IsNullOrWhiteSpace(name)) return null;
        var body = $"{name}, category";
        if (TryReadEmptyInventory(imageBase, node, out var emptyCategory, out _) && emptyCategory == category)
            body += ". Empty.";
        if (!Pointer(node + ItemManagerOffset, out var again) || again != manager) return null;
        return new(InventoryKind, caption, $"inventory:category:{category}", body);
    }

    private bool TryReadEmptyInventory(nuint imageBase, nuint node, out int category, out string name)
    {
        category = -1;
        name = string.Empty;
        // 1C31A2 pads an empty category to one blank record. A blank row in a larger vector
        // does not establish emptiness. The visible heading must agree with the committed category.
        if (!Pointer(node + ItemRowsBeginOffset, out var begin) ||
            !Pointer(node + ItemRowsEndOffset, out var end) || end != begin + ItemRowStride ||
            !Int32(begin + ItemRowQuantityOffset, out var quantity) || quantity != 0 ||
            !Int32(node + ItemCategoryOffset, out category) || category is < 0 or >= ItemCategoryCount ||
            !Pointer(node + ItemCategoryLabelOffset, out var label) || !HasAncestor(label, node)) return false;
        var expected = text.Get(imageBase, MenuCaptionBank, FirstItemCategoryCaption + category);
        var heading = rendered.Read(label);
        if (expected is null || heading is null || heading.Count != 1 || heading[0] != expected ||
            !Pointer(node + ItemRowsBeginOffset, out var b2) || b2 != begin ||
            !Pointer(node + ItemRowsEndOffset, out var e2) || e2 != end ||
            !Int32(begin + ItemRowQuantityOffset, out var q2) || q2 != quantity ||
            !Int32(node + ItemCategoryOffset, out var c2) || c2 != category ||
            !Pointer(node + ItemCategoryLabelOffset, out var l2) || l2 != label || !HasAncestor(label, node)) return false;
        name = expected;
        return true;
    }

    private static int CaptionMessageId(string kind) => kind switch
    {
        InventoryKind => InventoryCaptionMessageId,
        TechKind => TechCaptionMessageId,
        FormationKind => FormationCaptionMessageId,
        _ => EquipmentCaptionMessageId,
    };

    private bool TryReadPageState(nuint imageBase, nuint node, out PageState state)
    {
        state = default;
        if (node == 0 || !Pointer(node, out var vtable) || !TryReadKind(imageBase, vtable, out var kind) ||
            !TryReadFocus(imageBase, node, kind, out var manager, out var focusKey, out var control))
        {
            return false;
        }

        state = new PageState(kind, manager, focusKey, control);
        return true;
    }

    private static bool TryReadKind(nuint imageBase, nuint vtable, out string kind)
    {
        kind = string.Empty;
        if (vtable == imageBase + ClassicItemNodeVtableRva)
        {
            kind = InventoryKind;
        }
        else if (vtable == imageBase + ClassicTechNodeVtableRva)
        {
            kind = TechKind;
        }
        else if (vtable == imageBase + ClassicFormationNodeVtableRva)
        {
            kind = FormationKind;
        }
        else if (vtable == imageBase + EquipSteamNodeVtableRva)
        {
            kind = EquipmentKind;
        }

        return kind.Length != 0;
    }

    /// <summary>
    /// The active manager is the top of the node's own stack (MenuNodeBase gives every node a
    /// ManagerStack at +0x2C0, 0x1DEE02). Its focus key is then resolved to a control exactly as
    /// the native focus setter does at 0x1DD409: look the key up in the manager's focusable map at
    /// +0x294 and take the FocusableState's control at +0x14.
    /// </summary>
    private bool TryReadFocus(
        nuint imageBase, nuint node, string kind, out nuint manager, out uint focusKey, out nuint control)
    {
        manager = 0;
        focusKey = NoManagerFocusKey;
        control = 0;
        // The child may retain a cursor while disabled. Its active flag, not the existence of
        // that old cursor, decides whether slot/item selection owns this page.
        if (kind == EquipmentKind && TryReadEquipmentChild(imageBase, node, out var child) &&
            TryReadTopManager(imageBase, child, out var childManager))
        {
            if (!Byte(childManager + 0x290, out var disabled)) return false;
            if (disabled == 0)
                return TryReadStackFocus(imageBase, child, out manager, out focusKey, out control) &&
                    HasAncestor(control, child);
        }

        if (!TryReadStackFocus(imageBase, node, out manager, out focusKey, out control) ||
            !HasAncestor(control, node))
        {
            manager = 0;
            focusKey = NoManagerFocusKey;
            control = 0;
            return false;
        }

        return true;
    }

    /// <summary>
    /// The active manager is the top of the given owner's stack. MenuNodeBase gives every node a
    /// ManagerStack at +0x2C0 (0x1DEE02), and the manager on top carries the focused key at +0x2C4
    /// (0x1DD02C), which is then resolved to a control exactly as the native focus setter does at
    /// 0x1DD409.
    /// </summary>
    private bool TryReadStackFocus(
        nuint imageBase, nuint owner, out nuint manager, out uint focusKey, out nuint control)
    {
        manager = 0;
        focusKey = NoManagerFocusKey;
        control = 0;
        if (!TryReadTopManager(imageBase, owner, out manager) ||
            !Byte(manager + 0x290, out var disabled) || disabled != 0 ||
            !UInt32(manager + ManagerFocusKeyOffset, out var key) || key == NoManagerFocusKey ||
            !TryFindFocusableControl(imageBase, manager, key, out control))
        {
            manager = 0;
            control = 0;
            return false;
        }

        focusKey = key;
        return true;
    }

    private bool TryReadTopManager(nuint imageBase, nuint owner, out nuint manager)
    {
        manager = 0;
        return Pointer(owner + ManagerStackOffset, out var stack) &&
            Pointer(stack, out var stackVtable) && stackVtable == imageBase + ManagerStackVtableRva &&
            Pointer(stack + StackBeginOffset, out var begin) &&
            Pointer(stack + StackEndOffset, out var end) && end > begin &&
            (end - begin) % 4 == 0 && (end - begin) / 4 <= MaximumManagerDepth &&
            Pointer(end - 4, out manager) &&
            Pointer(manager, out var managerVtable) && managerVtable == imageBase + ManagerVtableRva;
    }

    /// <summary>
    /// Whether <paramref name="manager"/> is the one currently driving <paramref name="node"/>: the
    /// top of the page node's own stack, or for Equipment the top of its CharaEquipManager's stack,
    /// which is where the slot cursor lives.
    /// </summary>
    public bool OwnsManager(nuint imageBase, nuint node, nuint manager)
    {
        try
        {
            if (imageBase == 0 || node == 0 || manager == 0 ||
                !Pointer(node, out var vtable) || !TryReadKind(imageBase, vtable, out var kind))
            {
                return false;
            }

            if (TryReadTopManager(imageBase, node, out var top) && top == manager)
            {
                return true;
            }

            return kind == EquipmentKind && TryReadEquipmentChild(imageBase, node, out var child) &&
                TryReadTopManager(imageBase, child, out var childTop) && childTop == manager;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Every page: the focused control has to belong to this node's own tree. Without it a manager
    /// left on the stack by something else would hand back an unrelated control and the page would
    /// read out whatever that control happens to say.
    /// </summary>
    private bool IsUnderNode(nuint node, nuint control) => HasAncestor(control, node);

    private bool HasAncestor(nuint start, nuint ancestor)
    {
        var current = start;
        for (var depth = 0; depth < MaximumAncestorDepth; depth++)
        {
            if (!Byte(current + 0x1AD, out var visible) || visible == 0) return false;
            if (current == ancestor)
            {
                return true;
            }

            if (!Pointer(current + NodeParentOffset, out current))
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Walks the manager's focusable map. The native side hashes the key into a bucket; a linear
    /// walk of the same list reaches the identical entry without reimplementing the hash, and it is
    /// bounded so a corrupt list cannot spin.
    /// </summary>
    private bool TryFindFocusableControl(nuint imageBase, nuint manager, uint key, out nuint control)
    {
        control = 0;
        return TryFindManagerState(manager, key, out var state) &&
            Pointer(state, out var stateVtable) && stateVtable == imageBase + FocusableStateVtableRva &&
            Pointer(state + FocusableStateControlOffset, out control);
    }

    private bool TryFindManagerState(nuint manager, uint key, out nuint state)
    {
        state = 0;
        if (!Pointer(manager + FocusableMapOffset, out var map) ||
            !Pointer(map + MapSentinelOffset, out var sentinel))
        {
            return false;
        }

        var node = sentinel;
        for (var visited = 0; visited < MaximumFocusableEntries; visited++)
        {
            if (!Pointer(node + ListNextOffset, out node) || node == sentinel)
            {
                return false;
            }

            if (!UInt32(node + ListKeyOffset, out var entryKey) || entryKey != key)
            {
                continue;
            }

            return Pointer(node + ListValueOffset, out state);
        }

        return false;
    }

    /// <summary>
    /// Whether the focused control really belongs to the inventory row list rather than to the
    /// category buttons or a character panel that share the page. Row controls are added to this
    /// scroll view at 0x1C5048; 0x1C1B60 creates the picked-up row's overlay sprite.
    /// </summary>
    private bool IsUnderItemList(nuint node, nuint control)
    {
        if (!Pointer(node + ItemListViewOffset, out var listView))
        {
            return false;
        }

        return HasAncestor(control, listView);
    }

    /// <summary>
    /// Classic Inventory. The node keeps its rows in a plain vector of 12-byte records at
    /// +0x2D0/+0x2D4: the encoded item id the help updater consumes at 0x1C75F7 and the quantity
    /// the item-use handler requires to be positive at 0x1C7FCD. The cursor is +0x2F8 and the row the
    /// player has picked up to reorder is +0x32C.
    /// </summary>
    private FieldSubmenuSnapshot? CaptureInventoryRow(
        nuint imageBase, nuint node, string caption, in PageState state, out string? rowKey)
    {
        rowKey = null;
        if (!TryReadRowKey(node, out var key) ||
            !Pointer(node + ItemRowsBeginOffset, out var begin) ||
            !Pointer(node + ItemRowsEndOffset, out var end) || end < begin ||
            (end - begin) % ItemRowStride != 0 || (end - begin) / ItemRowStride > MaximumRows)
        {
            return null;
        }

        var count = (int)((end - begin) / ItemRowStride);
        if (count == 0 || !Int32(node + ItemCursorOffset, out var cursor) ||
            cursor < 0 || cursor >= count ||
            state.FocusKey != (uint)(cursor + 8) ||
            !Pointer(node + ItemManagerOffset, out var itemManager) || itemManager != state.Manager ||
            !Int32(node + ItemHeldRowOffset, out var held) || held >= count ||
            !Byte(node + ItemHelpVisibleOffset, out var helpVisible))
        {
            return null;
        }

        // The retail game enables help mode even on the blank placeholder. Its quantity and
        // matching category heading establish emptiness; it has no item description to read.
        if (count == 1 && cursor == 0 && held == -1 &&
            TryReadEmptyInventory(imageBase, node, out var category, out var categoryName))
        {
            rowKey = key;
            return new(InventoryKind, caption, $"inventory:empty:{category}", $"{categoryName}. Empty.");
        }
        if (!TryReadRow(imageBase, begin, cursor, out var name, out var quantity, out var encoded)) return null;

        var body = $"{name}, {quantity.ToString(CultureInfo.InvariantCulture)}";
        if (held >= 0 && held != cursor &&
            TryReadRow(imageBase, begin, held, out var heldName, out _, out _))
        {
            // The picked-up row stays lit on screen while the cursor moves to its swap partner.
            body = $"{body}, moving {heldName}";
        }
        else if (held == cursor)
        {
            body = $"{body}, picked up";
        }

        if (helpVisible != 0)
        {
            var help = text.Get(imageBase, ItemHelpBank, DecodeItemMessageId(imageBase, encoded));
            if (!string.IsNullOrWhiteSpace(help))
            {
                body = $"{body}. {help}";
            }
        }

        rowKey = key;
        return new FieldSubmenuSnapshot(
            InventoryKind,
            caption,
            $"inventory:{state.FocusKey.ToString("X", CultureInfo.InvariantCulture)}:{cursor}:{encoded}:{held}",
            body);
    }

    /// <summary>
    /// The row vector, cursor and held row together. The focus key does not change when the vector
    /// is rebuilt underneath a stationary cursor, so these are re-read on their own.
    /// </summary>
    private bool TryReadRowKey(nuint node, out string key)
    {
        key = string.Empty;
        if (!Pointer(node + ItemManagerOffset, out var manager) ||
            !Pointer(node + ItemRowsBeginOffset, out var begin) ||
            !Pointer(node + ItemRowsEndOffset, out var end) ||
            !Int32(node + ItemCursorOffset, out var cursor) ||
            !Int32(node + ItemHeldRowOffset, out var held) ||
            !Byte(node + ItemHelpVisibleOffset, out var helpVisible))
        {
            return false;
        }

        key = $"{manager}:{begin}:{end}:{cursor}:{held}:{helpVisible}";
        if (cursor < 0)
        {
            return true;
        }

        var record = begin + (nuint)(cursor * ItemRowStride);
        if (!Int32(record + ItemRowEncodedIdOffset, out var encoded) ||
            !Int32(record + ItemRowQuantityOffset, out var quantity))
        {
            return false;
        }

        key = $"{key}:{encoded}:{quantity}";
        return true;
    }

    private bool TryReadRow(
        nuint imageBase, nuint begin, int row, out string name, out int quantity, out int encoded)
    {
        name = string.Empty;
        quantity = 0;
        encoded = 0;
        var record = begin + (nuint)(row * ItemRowStride);
        if (!Int32(record + ItemRowEncodedIdOffset, out encoded) || encoded < 0 ||
            !Int32(record + ItemRowQuantityOffset, out quantity) || quantity <= 0)
        {
            return false;
        }

        var resolved = text.Get(imageBase, ItemNameBank, DecodeItemMessageId(imageBase, encoded));
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return false;
        }

        name = resolved;
        return true;
    }

    /// <summary>
    /// The encoded id splits into a low twelve bit index plus a group base from the image table at
    /// RVA 0x39906C, identically in the field item node (0x1C769C) and the battle item list
    /// (0x41EAEA). Returns -1 when the group is out of range.
    /// </summary>
    private int DecodeItemMessageId(nuint imageBase, int encoded)
    {
        var group = encoded >> 12;
        if (group is < 0 or >= ItemGroupCount ||
            !Int32(imageBase + ItemGroupTableRva + (nuint)(group * 4), out var start) || start < 0)
        {
            return -1;
        }

        return start + (encoded & 0xFFF);
    }

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

    private bool UInt32(nuint address, out uint value)
    {
        value = 0;
        Span<byte> bytes = stackalloc byte[4];
        if (address == 0 || (ulong)address + 4 > uint.MaxValue || !memory.TryRead(address, bytes))
        {
            return false;
        }

        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
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
