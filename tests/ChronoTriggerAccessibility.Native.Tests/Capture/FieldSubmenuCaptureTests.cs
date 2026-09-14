using System.Buffers.Binary;
using System.Text;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

/// <summary>
/// Synthetic sparse-memory fixtures for the audited fields. These exercise capture rules; they
/// are not live menu dumps and do not establish that a retail control contains these test labels.
/// Inventory uses the native focus relationship from 1C5D79: keys 0 through 7 are header controls,
/// and an item row's key is its zero-based index plus 8.
/// </summary>
public sealed class FieldSubmenuCaptureTests
{
    private const nuint ImageBase = 0x00400000;
    private const nuint Node = 0x20000000;
    private const nuint ManagerStack = 0x20100000;
    private const nuint Manager = 0x20200000;
    private const nuint StackEntries = 0x20300000;
    private const nuint Rows = 0x20400000;
    private const nuint ListView = 0x20500000;
    private const nuint FocusMap = 0x20600000;
    private const nuint FocusMapSentinel = 0x20610000;
    private const nuint TextManager = 0x30000000;
    private const nuint LabelGetter = 0x00700000;

    /// <summary>Any class other than MenuListView; the capture only refuses list views.</summary>
    private const nuint CustomButtonVtable = 0x00990000;

    private const nuint EquipChild = 0x21500000;
    private const nuint ChildStack = 0x21600000;
    private const nuint ChildManager = 0x21700000;
    private const nuint ChildStackEntries = 0x21800000;
    private const nuint ChildFocusMap = 0x21900000;
    private const nuint ChildSentinel = 0x21A00000;

    private const int RowFocusKey = 9;
    private const int CategoryFocusKey = 3;
    private const int SlotFocusKey = 4;

    /// <summary>The image table at RVA 0x39906C that 0x1C76A5 adds to an encoded item id.</summary>
    private static readonly int[] ItemGroupBases =
        [0, 0x6F, 0xA1, 0xC8, 0x103, 0x12E, 0x66, 0x4A, 0x2E, 0x22, 2, 0x63, 1];

    [Fact]
    public void EveryPageWithAProvenLayoutIsAdvertisedAsSupported()
    {
        Assert.Equal(
            [
                FieldSubmenuCapture.ClassicItemNodeVtableRva,
                FieldSubmenuCapture.ClassicTechNodeVtableRva,
                FieldSubmenuCapture.ClassicFormationNodeVtableRva,
                FieldSubmenuCapture.EquipSteamNodeVtableRva,
            ],
            FieldSubmenuCapture.SupportedNodeVtableRvas);
    }

    [Fact]
    public void ReadsTheFocusedInventoryRowWithItsLoadedNameAndCount()
    {
        var world = World();

        var snapshot = new FieldSubmenuCapture(world).Capture(ImageBase, Node);

        Assert.NotNull(snapshot);
        Assert.Equal("Inventory", snapshot!.Kind);
        Assert.Equal("Item", snapshot.Title);
        Assert.Equal("inventory:9:1:8197:-1", snapshot.FocusIdentity);
        Assert.Equal("Mid Ether, 7", snapshot.Text);
    }

    [Fact]
    public void AFocusThatBelongsToTheCategoryButtonsNeverAnnouncesAnInventoryRow()
    {
        var world = World();
        FocusCategoryButton(world);

        var snapshot = new FieldSubmenuCapture(world).Capture(ImageBase, Node);

        // The row cursor still points at Mid Ether, but the player is not on the list.
        Assert.NotNull(snapshot);
        Assert.Equal("Inventory:control:3", snapshot!.FocusIdentity);
        Assert.Equal("Consumables", snapshot.Text);
        Assert.DoesNotContain("Mid Ether", snapshot.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInventoryCategoryIconReadsItsNativeCategoryAndEmptyList()
    {
        var world = EmptyInventory();
        var icon = FocusPanel(world);
        world.Int32(Manager + 0x2C4, 2);
        FocusEntry(world, FocusMapSentinel, 2, FocusMapSentinel + 0x300, icon);

        var snapshot = new FieldSubmenuCapture(world).Capture(ImageBase, Node);
        Assert.NotNull(snapshot);
        Assert.Equal("Consumables, category. Empty.", snapshot.Text);
    }

    [Fact]
    public void ACategoryIconCannotBorrowAnotherCategorysEmptyState()
    {
        var world = EmptyInventory();
        FocusPanel(world); // key 3 is Weapons; the committed category is still Consumables.
        world.Message(0x23, 0x41, "Weapons");
        Assert.Equal("Weapons, category", new FieldSubmenuCapture(world).Capture(ImageBase, Node)!.Text);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TheNativeBlankPlaceholderRowAnnouncesAnEmptyInventory(byte helpMode)
    {
        var world = EmptyInventory();
        world.Byte(Node + 0x2FC, helpMode);
        world.Message(0x1D, 0, "Description for item zero, not for the empty placeholder.");
        world.Int32(Manager + 0x2C4, 8);
        FocusEntry(world, FocusMapSentinel, 8, FocusMapSentinel + 0x100, 0x20A00000);
        Assert.Equal("Consumables. Empty.", new FieldSubmenuCapture(world).Capture(ImageBase, Node)!.Text);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ABlankRowWithinANonemptyInventoryIsNotMistakenForAnEmptyCategory(byte helpMode)
    {
        var world = EmptyInventory();
        world.Byte(Node + 0x2FC, helpMode);
        world.Pointer(Node + 0x2D4, Rows + 24);
        world.Int32(Manager + 0x2C4, 8);
        FocusEntry(world, FocusMapSentinel, 8, FocusMapSentinel + 0x100, 0x20A00000);
        Assert.Null(new FieldSubmenuCapture(world).Capture(ImageBase, Node));
    }

    [Fact]
    public void CharacterCardsKeepAllTheLiveStatFragments()
    {
        var world = World();
        world.Pointer(Node, ImageBase + FieldSubmenuCapture.EquipSteamNodeVtableRva);
        world.Message(0x23, 0x20, "Equipment");
        FocusPanel(world, "Crono", "LV", "1", ":", "HP", "43/", "70", ":",
            "MP", "8/", "8", ":", "EXP", "10", ":", "Next", "10", ":");
        Assert.Equal("Crono LV 1. HP 43/70. MP 8/8. EXP 10. Next 10",
            new FieldSubmenuCapture(world).Capture(ImageBase, Node)!.Text);
    }

    [Fact]
    public void TheHelpLineIsAppendedOnlyWhileTheNativeHelpPanelIsVisible()
    {
        var world = World();
        world.Byte(Node + 0x2FC, 1);
        world.Message(0x1D, 0xA1 + 5, "Restores 30 MP.");

        Assert.Equal("Mid Ether, 7. Restores 30 MP.",
            new FieldSubmenuCapture(world).Capture(ImageBase, Node)!.Text);

        world.Byte(Node + 0x2FC, 0);
        Assert.Equal("Mid Ether, 7", new FieldSubmenuCapture(world).Capture(ImageBase, Node)!.Text);
    }

    [Fact]
    public void TheCursorSelectsTheRowRatherThanAlwaysReadingTheFirst()
    {
        var world = World();
        world.Int32(Node + 0x2F8, 0);
        world.Int32(Manager + 0x2C4, 8);
        FocusEntry(world, FocusMapSentinel, 8, FocusMapSentinel + 0x100, 0x20A00000);

        var snapshot = new FieldSubmenuCapture(world).Capture(ImageBase, Node);

        Assert.Equal("Tonic, 12", snapshot!.Text);
        Assert.Equal("inventory:8:0:1:-1", snapshot.FocusIdentity);
    }

    [Fact]
    public void RowRecordsAreTwelveBytesApartNotFour()
    {
        var world = World();
        world.Int32(Node + 0x2F8, 2);
        world.Int32(Manager + 0x2C4, 10);
        FocusEntry(world, FocusMapSentinel, 10, FocusMapSentinel + 0x100, 0x20A00000);

        // Row 2 only exists at begin + 24. A four byte stride would land inside row 0.
        Assert.Equal("Ether, 3", new FieldSubmenuCapture(world).Capture(ImageBase, Node)!.Text);
    }

    [Fact]
    public void APickedUpRowIsAnnouncedWhileTheCursorSitsOnIt()
    {
        var world = World();
        world.Int32(Node + 0x32C, 1);

        var snapshot = new FieldSubmenuCapture(world).Capture(ImageBase, Node);

        Assert.Equal("Mid Ether, 7, picked up", snapshot!.Text);
        Assert.Equal("inventory:9:1:8197:1", snapshot.FocusIdentity);
    }

    [Fact]
    public void TheRowBeingMovedIsNamedWhileTheCursorLooksForItsSwapPartner()
    {
        var world = World();
        world.Int32(Node + 0x32C, 0);

        Assert.Equal("Mid Ether, 7, moving Tonic",
            new FieldSubmenuCapture(world).Capture(ImageBase, Node)!.Text);
    }

    [Fact]
    public void AGroupedItemIdIsDecodedThroughTheImageGroupTable()
    {
        var world = World();
        world.Int32(Rows + 12, 0x3000 + 9);          // group 3, base 0xC8
        world.Message(0x1B, 0xC8 + 9, "Mid Tonic");

        Assert.Equal("Mid Tonic, 7", new FieldSubmenuCapture(world).Capture(ImageBase, Node)!.Text);
    }

    [Fact]
    public void AnEncodedIdWhoseGroupIsOutsideTheImageTableIsRefused()
    {
        var world = World();
        world.Int32(Rows + 12, 0xD000);              // group 13, one past the table

        Assert.Null(new FieldSubmenuCapture(world).Capture(ImageBase, Node));
    }

    [Fact]
    public void ARowTheNativeRendererSkipsIsNotReportedAsASelection()
    {
        var world = World();
        world.Int32(Rows + 12 + 4, 0);               // zero quantity draws nothing

        Assert.Null(new FieldSubmenuCapture(world).Capture(ImageBase, Node));
    }

    [Fact]
    public void ACursorOutsideTheNativeRowVectorIsRefused()
    {
        var world = World();
        world.Int32(Node + 0x2F8, 3);                // the vector holds exactly three records

        Assert.Null(new FieldSubmenuCapture(world).Capture(ImageBase, Node));
    }

    [Fact]
    public void TheNativeNoSelectionCursorYieldsNoSnapshot()
    {
        var world = World();
        world.Int32(Node + 0x2F8, -1);

        Assert.Null(new FieldSubmenuCapture(world).Capture(ImageBase, Node));
    }

    [Fact]
    public void AnInventoryCursorThatHasNotCaughtUpWithTheFocusKeyIsRetried()
    {
        var world = World();
        world.Int32(Node + 0x2F8, 0); // focus 9 still belongs to row 1
        Assert.Null(new FieldSubmenuCapture(world).Capture(ImageBase, Node));
    }

    [Fact]
    public void AManagerReportingTheNativeNoFocusSentinelYieldsNoSnapshot()
    {
        var world = World();
        world.Int32(Manager + 0x2C4, unchecked((int)0x80000000));

        Assert.Null(new FieldSubmenuCapture(world).Capture(ImageBase, Node));
    }

    [Fact]
    public void AFocusKeyWithNoEntryInTheManagersFocusableMapYieldsNoSnapshot()
    {
        var world = World();
        world.Int32(Manager + 0x2C4, 77);

        Assert.Null(new FieldSubmenuCapture(world).Capture(ImageBase, Node));
    }

    [Fact]
    public void AMapEntryWhoseValueIsNotAFocusableStateIsRefused()
    {
        var world = World();
        world.Pointer(FocusMapSentinel + 0x100, ImageBase + FieldSubmenuCapture.ManagerVtableRva);

        Assert.Null(new FieldSubmenuCapture(world).Capture(ImageBase, Node));
    }

    [Fact]
    public void TheFocusKeyComesFromTheTopOfTheNodesOwnManagerStack()
    {
        var world = World();
        // Push a second manager carrying its own focusable map; the page belongs to it now.
        var second = (nuint)0x20700000;
        var map = (nuint)0x20780000;
        var sentinel = (nuint)0x20800000;
        var control = ControlOf(world, "Robo", 0x20900000);
        world.Pointer(control + 0x16C, Node);
        world.Pointer(second, ImageBase + FieldSubmenuCapture.ManagerVtableRva);
        world.Byte(second + 0x290, 0);
        world.Int32(second + 0x2C4, 21);
        world.Pointer(second + 0x294, map);
        world.Pointer(map + 4, sentinel);
        FocusEntry(world, sentinel, key: 21, state: sentinel + 0x200, control: control);
        world.Pointer(StackEntries + 4, second);
        world.Pointer(ManagerStack + 8, StackEntries + 8);

        var snapshot = new FieldSubmenuCapture(world).Capture(ImageBase, Node);

        Assert.Equal("Inventory:control:15", snapshot!.FocusIdentity);
        Assert.Equal("Robo", snapshot.Text);
    }

    [Fact]
    public void TheFocusableMapIsReachedThroughItsPointerNotReadInline()
    {
        var world = World();
        // Whatever happens to sit at manager + 0x298 is not the map. 0x1DD409 loads the map from
        // manager + 0x294 and only then takes its sentinel, so an inline read lands on the wrong
        // object entirely.
        var decoySentinel = (nuint)0x20C00000;
        var decoy = ControlOf(world, "Decoy", 0x20D00000);
        world.Pointer(decoy + 0x16C, Node);
        world.Pointer(Manager + 0x294 + 4, decoySentinel);
        FocusEntry(world, decoySentinel, RowFocusKey, decoySentinel + 0x200, decoy);

        var snapshot = new FieldSubmenuCapture(world).Capture(ImageBase, Node);

        Assert.Equal("Mid Ether, 7", snapshot!.Text);
    }

    [Fact]
    public void AManagerWithNoFocusableMapAtAllYieldsNoSnapshot()
    {
        var world = World();
        world.Pointer(Manager + 0x294, 0);

        Assert.Null(new FieldSubmenuCapture(world).Capture(ImageBase, Node));
    }

    [Fact]
    public void AFocusedControlOutsideThePageNodeIsRefused()
    {
        var world = World();
        var stray = ControlOf(world, "Somebody else", 0x20E00000);
        world.Pointer(stray + 0x16C, 0x20F00000);     // parented to an unrelated tree
        world.Int32(Manager + 0x2C4, CategoryFocusKey);
        FocusEntry(world, FocusMapSentinel, CategoryFocusKey, FocusMapSentinel + 0x300, stray);

        Assert.Null(new FieldSubmenuCapture(world).Capture(ImageBase, Node));
    }

    [Fact]
    public void AFocusedListViewIsRefusedOnlyOnTheGenericLabelPath()
    {
        var world = World();
        world.Pointer(Node, ImageBase + FieldSubmenuCapture.ClassicTechNodeVtableRva);
        world.Message(0x23, 0x22, "Tech");
        var list = ControlOf(world, "Row one", 0x21000000);
        world.Pointer(list + 0x16C, Node);
        world.Pointer(list, ImageBase + FieldSubmenuCapture.MenuListViewVtableRva);
        world.Int32(Manager + 0x2C4, CategoryFocusKey);
        FocusEntry(world, FocusMapSentinel, CategoryFocusKey, FocusMapSentinel + 0x300, list);

        Assert.Null(new FieldSubmenuCapture(world).Capture(ImageBase, Node));
    }

    [Fact]
    public void AFocusedInventoryListViewStillReportsItsRowFromTheRowData()
    {
        var world = World();
        // The row list itself is what holds focus. Its subtree is every row, so the generic reader
        // would speak the whole page, but the row path reads the cursor's record instead.
        world.Pointer(ListView, ImageBase + FieldSubmenuCapture.MenuListViewVtableRva);
        world.Pointer(ListView + 0x16C, Node);
        FocusEntry(world, FocusMapSentinel, RowFocusKey, FocusMapSentinel + 0x100, ListView);

        Assert.Equal("Mid Ether, 7", new FieldSubmenuCapture(world).Capture(ImageBase, Node)!.Text);
    }

    [Fact]
    public void AControlRenderingFarMoreLinesThanASelectionIsRefused()
    {
        var world = World();
        world.Pointer(Node, ImageBase + FieldSubmenuCapture.ClassicTechNodeVtableRva);
        world.Message(0x23, 0x22, "Tech");
        FocusPanel(world, [.. Enumerable.Range(0, 65).Select(index => $"Row {index}")]);

        Assert.Null(new FieldSubmenuCapture(world).Capture(ImageBase, Node));
    }

    [Fact]
    public void AnEmptyManagerStackYieldsNoSnapshot()
    {
        var world = World();
        world.Pointer(ManagerStack + 8, StackEntries); // end == begin

        Assert.Null(new FieldSubmenuCapture(world).Capture(ImageBase, Node));
    }

    [Fact]
    public void AManagerStackOfAnUnknownClassIsRefusedRatherThanRead()
    {
        var world = World();
        world.Pointer(ManagerStack, ImageBase + FieldSubmenuCapture.ManagerVtableRva);

        Assert.Null(new FieldSubmenuCapture(world).Capture(ImageBase, Node));
    }

    [Fact]
    public void AStackEntryThatIsNotAnInputManagerIsRefused()
    {
        var world = World();
        world.Pointer(Manager, ImageBase + FieldSubmenuCapture.ManagerStackVtableRva);

        Assert.Null(new FieldSubmenuCapture(world).Capture(ImageBase, Node));
    }

    [Theory]
    [InlineData(FieldSubmenuCapture.ClassicTechNodeVtableRva, "Tech", 0x22, "Tech")]
    [InlineData(FieldSubmenuCapture.ClassicFormationNodeVtableRva, "Formation", 0x25, "Party")]
    [InlineData(FieldSubmenuCapture.EquipSteamNodeVtableRva, "Equipment", 0x20, "Equip")]
    public void TheOtherPagesReportTheLabelsRenderedInsideTheirFocusedControl(
        uint vtable, string kind, int captionId, string caption)
    {
        var world = World();
        world.Pointer(Node, ImageBase + vtable);
        world.Message(0x23, captionId, caption);
        FocusPanel(world, "Cyclone", "8 MP", "Cuts through a group.");

        var snapshot = new FieldSubmenuCapture(world).Capture(ImageBase, Node);

        Assert.NotNull(snapshot);
        Assert.Equal(kind, snapshot!.Kind);
        Assert.Equal(caption, snapshot.Title);
        Assert.Equal($"{kind}:control:3", snapshot.FocusIdentity);
        Assert.Equal("Cyclone, 8 MP, Cuts through a group.", snapshot.Text);
    }

    [Fact]
    public void EquipmentReadsTheStatPanelFromItsCharaEquipManagerNotFromThePageNode()
    {
        var world = World();
        var child = Equipment(world);
        var panel = ControlOf(world, ["Power 15 to 22", "Hit 90 to 94"], 0x21100000);
        world.Pointer(panel + 0x16C, child);
        world.Pointer(child + 0x2EC, panel);
        // The page node has its own unrelated field at the same offset. Reading the container from
        // the parent is the wrong owner, so this decoy must never be spoken.
        var decoy = ControlOf(world, ["Wrong owner"], 0x21300000);
        world.Pointer(decoy + 0x16C, Node);
        world.Pointer(Node + 0x2EC, decoy);

        var snapshot = new FieldSubmenuCapture(world).Capture(ImageBase, Node);

        Assert.NotNull(snapshot);
        Assert.Equal("Equipment", snapshot!.Kind);
        Assert.Equal("Weapon, Silver Sword. Power 15 to 22, Hit 90 to 94", snapshot.Text);
        Assert.DoesNotContain("Wrong owner", snapshot.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEquipmentChildOfTheWrongClassIsRefusedForTheStatPanel()
    {
        var world = World();
        var child = Equipment(world);
        var panel = ControlOf(world, ["Power 15 to 22"], 0x21100000);
        world.Pointer(panel + 0x16C, child);
        world.Pointer(child + 0x2EC, panel);
        world.Pointer(child, CustomButtonVtable);   // not a CharaEquipManager

        // A child of the wrong class is not trusted for either the slot cursor or the panel, so the
        // page falls all the way back to the selector on its own stack.
        var snapshot = new FieldSubmenuCapture(world).Capture(ImageBase, Node);
        Assert.Equal("Selector", snapshot!.Text);
        Assert.Equal("Equipment:control:3", snapshot.FocusIdentity);
    }

    [Fact]
    public void AStatPanelThatDoesNotHangOffTheChildIsRefused()
    {
        var world = World();
        var child = Equipment(world);
        var panel = ControlOf(world, ["Power 15 to 22"], 0x21100000);
        world.Pointer(panel + 0x16C, 0x21400000);   // parented somewhere else entirely
        world.Pointer(child + 0x2EC, panel);

        Assert.Equal("Weapon, Silver Sword", new FieldSubmenuCapture(world).Capture(ImageBase, Node)!.Text);
    }

    [Fact]
    public void EquipmentStillReportsTheSlotWhenTheDetailPanelIsNotThere()
    {
        var world = World();
        var child = Equipment(world);
        world.Pointer(child + 0x2EC, 0);

        Assert.Equal("Weapon, Silver Sword", new FieldSubmenuCapture(world).Capture(ImageBase, Node)!.Text);
    }

    [Fact]
    public void AnOversizedEquipmentPanelIsDroppedRatherThanReadOut()
    {
        var world = World();
        var child = Equipment(world);
        var panel = ControlOf(world, [.. Enumerable.Range(0, 33).Select(i => $"Line {i}")], 0x21200000);
        world.Pointer(panel + 0x16C, child);
        world.Pointer(child + 0x2EC, panel);

        Assert.Equal("Weapon, Silver Sword", new FieldSubmenuCapture(world).Capture(ImageBase, Node)!.Text);
    }

    [Fact]
    public void TheEquipmentSlotCursorComesFromTheChildStackNotTheStaleParentStack()
    {
        var world = World();
        var child = Equipment(world);

        var snapshot = new FieldSubmenuCapture(world).Capture(ImageBase, Node);

        // This fixture's parent manager reports the character selector at key 3; the slot the
        // player is on lives on the child's stack at key 4.
        Assert.Equal("Equipment:control:4", snapshot!.FocusIdentity);
        Assert.Equal("Weapon, Silver Sword", snapshot.Text);

        // Remove the child's stack and the page falls back to the parent's selector.
        world.Pointer(child + 0x2C0, 0);
        Assert.Equal("Equipment:control:3",
            new FieldSubmenuCapture(world).Capture(ImageBase, Node)!.FocusIdentity);
    }

    [Fact]
    public void DisabledEquipmentChildReturnsToTheParentSelectorWithoutUsingItsOldCursor()
    {
        var world = World(); Equipment(world);
        world.Byte(ChildManager + 0x290, 1);
        Assert.Equal("Equipment:control:3", new FieldSubmenuCapture(world).Capture(ImageBase, Node)!.FocusIdentity);
    }

    [Fact]
    public void UnreadableActiveEquipmentCursorCannotFallBackToTheParentSelector()
    {
        var world = World(); Equipment(world);
        world.Int32(ChildManager + 0x2C4, 500);
        Assert.Null(new FieldSubmenuCapture(world).Capture(ImageBase, Node));
    }

    [Fact]
    public void AHiddenAncestorMakesItsInventoryRowUnavailable()
    {
        var world = World(); world.Byte(ListView + 0x1AD, 0);
        Assert.Null(new FieldSubmenuCapture(world).Capture(ImageBase, Node));
    }

    [Fact]
    public void OwnsManagerAcceptsBothThePageStackAndTheEquipmentChildStack()
    {
        var world = World();
        var child = Equipment(world);
        var capture = new FieldSubmenuCapture(world);

        Assert.True(capture.OwnsManager(ImageBase, Node, Manager));
        Assert.True(capture.OwnsManager(ImageBase, Node, ChildManager));
        Assert.False(capture.OwnsManager(ImageBase, Node, child));
        Assert.False(capture.OwnsManager(ImageBase, Node, 0));
        Assert.False(capture.OwnsManager(0, Node, Manager));
    }

    [Fact]
    public void AFocusedControlThatRendersNoTextYieldsNoSnapshotRatherThanAnEmptyLine()
    {
        var world = World();
        world.Pointer(Node, ImageBase + FieldSubmenuCapture.ClassicTechNodeVtableRva);
        world.Message(0x23, 0x22, "Tech");
        FocusPanel(world);

        Assert.Null(new FieldSubmenuCapture(world).Capture(ImageBase, Node));
    }

    [Fact]
    public void AnInvisibleFocusedControlContributesNoText()
    {
        var world = World();
        world.Pointer(Node, ImageBase + FieldSubmenuCapture.ClassicFormationNodeVtableRva);
        world.Message(0x23, 0x25, "Party");
        var control = FocusPanel(world, "Crono");
        world.Byte(control + 0x1AD, 0);

        Assert.Null(new FieldSubmenuCapture(world).Capture(ImageBase, Node));
    }

    [Fact]
    public void TheTouchItemNodeIsNotClaimedBecauseItsRowVectorSitsElsewhere()
    {
        var world = World();
        world.Pointer(Node, ImageBase + FieldSubmenuCapture.TouchItemNodeVtableRva);

        Assert.Null(new FieldSubmenuCapture(world).Capture(ImageBase, Node));
    }

    [Fact]
    public void ANodeOfAnUnrelatedClassIsRefused()
    {
        var world = World();
        world.Pointer(Node, ImageBase + FieldSubmenuCapture.EquipNodeVtableRva);

        Assert.Null(new FieldSubmenuCapture(world).Capture(ImageBase, Node));
        Assert.Null(new FieldSubmenuCapture(world).Capture(ImageBase, 0));
        Assert.Null(new FieldSubmenuCapture(world).Capture(0, Node));
    }

    [Fact]
    public void AnUnreadableTextManagerYieldsNoSnapshotRatherThanANamelessRow()
    {
        var world = World();
        world.Pointer(ImageBase + 0x41C3D8, 0);

        Assert.Null(new FieldSubmenuCapture(world).Capture(ImageBase, Node));
    }

    [Fact]
    public void AMissingCaptionYieldsNoSnapshotRatherThanAnEmptyTitle()
    {
        var world = World();
        world.Message(0x23, 0x21, string.Empty);

        Assert.Null(new FieldSubmenuCapture(world).Capture(ImageBase, Node));
    }

    /// <summary>
    /// A classic Inventory page: three rows, the cursor on the second, nothing picked up, the help
    /// panel hidden, and one manager on the node's stack whose focus key 9 resolves through its
    /// focusable map to a control parented to the row list.
    /// </summary>
    private static FixtureMemory World()
    {
        var world = new FixtureMemory();
        world.Pointer(ImageBase + 0x41C3D8, TextManager);
        for (var group = 0; group < ItemGroupBases.Length; group++)
        {
            world.Int32(ImageBase + 0x39906C + (nuint)(group * 4), ItemGroupBases[group]);
        }

        world.Pointer(Node, ImageBase + FieldSubmenuCapture.ClassicItemNodeVtableRva);
        world.Pointer(Node + 0x2DC, Manager);
        world.Pointer(Node + 0x2C0, ManagerStack);
        world.Pointer(ManagerStack, ImageBase + FieldSubmenuCapture.ManagerStackVtableRva);
        world.Pointer(ManagerStack + 4, StackEntries);
        world.Pointer(ManagerStack + 8, StackEntries + 4);
        world.Pointer(StackEntries, Manager);
        world.Pointer(Manager, ImageBase + FieldSubmenuCapture.ManagerVtableRva);
        world.Byte(Manager + 0x290, 0);
        world.Int32(Manager + 0x2C4, RowFocusKey);
        // manager + 0x294 holds a pointer to the map; the sentinel is at map + 4.
        world.Pointer(Manager + 0x294, FocusMap);
        world.Pointer(FocusMap + 4, FocusMapSentinel);

        // The focused control hangs off the row list, which hangs off the page node itself.
        world.Byte(Node + 0x1AD, 1);
        world.Byte(ListView + 0x1AD, 1);
        world.Pointer(Node + 0x2C8, ListView);
        world.Pointer(ListView + 0x16C, Node);
        var rowControl = (nuint)0x20A00000;
        world.Byte(rowControl + 0x1AD, 1);
        world.Pointer(rowControl + 0x16C, ListView);
        world.Pointer(rowControl, CustomButtonVtable);
        FocusEntry(world, FocusMapSentinel, RowFocusKey, FocusMapSentinel + 0x100, rowControl);

        world.Pointer(Node + 0x2D0, Rows);
        world.Pointer(Node + 0x2D4, Rows + 3 * 12);
        world.Int32(Node + 0x2F8, 1);
        world.Byte(Node + 0x2FC, 0);
        world.Int32(Node + 0x32C, -1);

        Row(world, 0, encoded: 0x0001, quantity: 12);
        Row(world, 1, encoded: 0x2005, quantity: 7);   // group 2 base 0xA1 plus the low twelve bits
        Row(world, 2, encoded: 0x0002, quantity: 3);

        world.Message(0x23, 0x21, "Item");
        world.Message(0x1B, 1, "Tonic");
        world.Message(0x1B, 2, "Ether");
        world.Message(0x1B, 0xA1 + 5, "Mid Ether");
        return world;
    }

    private static FixtureMemory EmptyInventory()
    {
        var world = World();
        world.Pointer(Node + 0x2D4, Rows + 12);
        world.Int32(Node + 0x2F8, 0);
        world.Int32(Node + 0x2F0, 0);
        Row(world, 0, encoded: 0, quantity: 0);
        var caption = ControlOf(world, "Consumables", 0x23000000);
        world.Pointer(caption + 0x16C, Node);
        world.Pointer(Node + 0x2F4, caption);
        world.Message(0x23, 0x40, "Consumables");
        return world;
    }

    /// <summary>
    /// Turns the page into Equipment: the node becomes MenuNodeEquipSteam, its CharaEquipManager
    /// child hangs off it with its own manager stack focused on the Weapon slot, and the page
    /// node's own manager still reports this fixture's character selector at key 3.
    /// </summary>
    private static nuint Equipment(FixtureMemory world)
    {
        world.Pointer(Node, ImageBase + FieldSubmenuCapture.EquipSteamNodeVtableRva);
        world.Message(0x23, 0x20, "Equip");
        FocusPanel(world, "Selector");

        world.Pointer(Node + 0x2F0, EquipChild);
        world.Pointer(EquipChild, ImageBase + FieldSubmenuCapture.CharaEquipManagerVtableRva);
        world.Byte(EquipChild + 0x1AD, 1);
        world.Pointer(EquipChild + 0x16C, Node);
        world.Pointer(EquipChild + 0x2C0, ChildStack);
        world.Pointer(ChildStack, ImageBase + FieldSubmenuCapture.ManagerStackVtableRva);
        world.Pointer(ChildStack + 4, ChildStackEntries);
        world.Pointer(ChildStack + 8, ChildStackEntries + 4);
        world.Pointer(ChildStackEntries, ChildManager);
        world.Pointer(ChildManager, ImageBase + FieldSubmenuCapture.ManagerVtableRva);
        world.Byte(ChildManager + 0x290, 0);
        world.Int32(ChildManager + 0x2C4, SlotFocusKey);
        world.Pointer(ChildManager + 0x294, ChildFocusMap);
        world.Pointer(ChildFocusMap + 4, ChildSentinel);

        var slot = ControlOf(world, ["Weapon", "Silver Sword"], 0x21B00000);
        world.Pointer(slot + 0x16C, EquipChild);
        FocusEntry(world, ChildSentinel, SlotFocusKey, ChildSentinel + 0x200, slot);
        return EquipChild;
    }

    /// <summary>Moves the manager's focus onto a control that is not part of the row list.</summary>
    private static void FocusCategoryButton(FixtureMemory world) => FocusPanel(world, "Consumables");

    /// <summary>Points the manager at key 3, whose control is a synthetic panel of labels.</summary>
    private static nuint FocusPanel(FixtureMemory world, params string[] labels)
    {
        var control = ControlOf(world, labels, 0x20B00000);
        world.Pointer(control + 0x16C, Node);
        world.Int32(Manager + 0x2C4, CategoryFocusKey);
        FocusEntry(world, FocusMapSentinel, CategoryFocusKey, FocusMapSentinel + 0x300, control);
        return control;
    }

    private static nuint ControlOf(FixtureMemory world, string label, nuint at) => ControlOf(world, [label], at);

    /// <summary>A cocos control carrying one Label child per line, laid out as the retail engine does.</summary>
    private static nuint ControlOf(FixtureMemory world, string[] labels, nuint at)
    {
        var children = at + 0x10000;
        CocosNode(world, at, parent: 0);
        world.Pointer(at, CustomButtonVtable);   // a control, not a list view
        world.Pointer(at + 0x160, children);
        world.Pointer(at + 0x164, children + (nuint)(labels.Length * 4));
        for (var index = 0; index < labels.Length; index++)
        {
            var child = at + 0x20000 + (nuint)(index * 0x1000);
            world.Pointer(children + (nuint)(index * 4), child);
            CocosNode(world, child, parent: at);
            world.Pointer(child + 0x160, child + 0x800);
            world.Pointer(child + 0x164, child + 0x800);
            world.Pointer(child + 0x278, child + 0x900);
            world.Pointer(child + 0x900 + 8, LabelGetter);
            world.Write(LabelGetter, [0x8D, 0x41, 0x28, 0xC3]);
            world.Name(child + 0x2A0, labels[index]);
        }

        return at;
    }

    private static void CocosNode(FixtureMemory world, nuint node, nuint parent)
    {
        world.Byte(node + 0x1AD, 1);
        world.Pointer(node + 0x16C, parent);
        world.Pointer(node + 0x160, node + 0x700);
        world.Pointer(node + 0x164, node + 0x700);
    }

    /// <summary>
    /// One entry in the manager's focusable map: a list node whose key is the focus key and whose
    /// value is a FocusableState wrapping the control, with the sentinel closing the ring.
    /// </summary>
    private static void FocusEntry(FixtureMemory world, nuint sentinel, int key, nuint state, nuint control)
    {
        var entry = state - 0x80;
        world.Pointer(sentinel, entry);
        world.Pointer(entry, sentinel);
        world.Int32(entry + 8, key);
        world.Pointer(entry + 0x0C, state);
        world.Pointer(state, ImageBase + FieldSubmenuCapture.FocusableStateVtableRva);
        world.Pointer(state + 0x14, control);
    }

    private static void Row(FixtureMemory world, int row, int encoded, int quantity)
    {
        world.Int32(Rows + (nuint)(row * 12), encoded);
        world.Int32(Rows + (nuint)(row * 12) + 4, quantity);
        world.Int32(Rows + (nuint)(row * 12) + 8, 0);
    }

    /// <summary>Sparse byte-accurate memory: every read is served at its real width.</summary>
    private sealed class FixtureMemory : IReadableMemory
    {
        private readonly Dictionary<nuint, byte> bytes = [];
        private readonly Dictionary<int, nuint> banks = [];
        private nuint nextFree = 0x40000000;

        public void Byte(nuint address, byte value) => bytes[address] = value;

        public void Write(nuint address, ReadOnlySpan<byte> value)
        {
            for (var index = 0; index < value.Length; index++) bytes[address + (nuint)index] = value[index];
        }

        public void Int32(nuint address, int value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
            Write(address, buffer);
        }

        public void Pointer(nuint address, nuint value) => Int32(address, unchecked((int)(uint)value));

        public void Name(nuint address, string value) => Write(address, MsvcString(value));

        /// <summary>Adds one line to the loaded TextManager bank vector at msg file <paramref name="bank"/>.</summary>
        public void Message(int bank, int index, string text)
        {
            if (!banks.TryGetValue(bank, out var file))
            {
                if (banks.Count == 0)
                {
                    var table = Allocate(0x100 * 4);
                    Pointer(TextManager, table);
                    Pointer(TextManager + 4, table + 0x100 * 4);
                    for (var slot = 0; slot < 0x100; slot++) Pointer(table + (nuint)(slot * 4), 0);
                }

                var lines = Allocate(0x200 * 24);
                file = Allocate(8);
                Pointer(file, lines);
                Pointer(file + 4, lines + 0x200 * 24);
                for (var line = 0; line < 0x200; line++) Write(lines + (nuint)(line * 24), MsvcString(string.Empty));
                banks[bank] = file;
                Pointer(Read(TextManager) + (nuint)(bank * 4), file);
            }

            Write(Read(file) + (nuint)(index * 24), MsvcString(text));
        }

        public bool TryRead(nuint address, Span<byte> destination)
        {
            if (address == 0 || (ulong)address + (ulong)destination.Length > uint.MaxValue) return false;
            for (var index = 0; index < destination.Length; index++)
            {
                if (!bytes.TryGetValue(address + (nuint)index, out var value)) return false;
                destination[index] = value;
            }

            return true;
        }

        private nuint Read(nuint address)
        {
            Span<byte> buffer = stackalloc byte[4];
            TryRead(address, buffer);
            return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        }

        private nuint Allocate(int size)
        {
            var address = nextFree;
            nextFree += (nuint)(size + 0x40);
            for (var index = 0; index < size; index++) bytes[address + (nuint)index] = 0;
            return address;
        }

        /// <summary>
        /// A real MSVC std::string: short values live inline, anything from sixteen bytes up is
        /// held in a separate buffer exactly as the retail allocator arranges it.
        /// </summary>
        private byte[] MsvcString(string value)
        {
            var encoded = Encoding.UTF8.GetBytes(value);
            var layout = new byte[MsvcStringReader.LayoutSize];
            var capacity = 15;
            if (encoded.Length < 16)
            {
                encoded.CopyTo(layout, 0);
            }
            else
            {
                capacity = encoded.Length;
                var buffer = Allocate(encoded.Length + 1);
                Write(buffer, encoded);
                Byte(buffer + (nuint)encoded.Length, 0);
                BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0), (uint)buffer);
            }

            BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x10), (uint)encoded.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x14), (uint)capacity);
            return layout;
        }
    }
}
