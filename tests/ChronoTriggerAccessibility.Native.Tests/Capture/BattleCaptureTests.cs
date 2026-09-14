using System.Buffers.Binary;
using System.Text;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

/// <summary>
/// Fixtures are built from the audited native addresses in
/// docs/battle-interface-native-audit.md sections 12 to 15, laid out in a byte-accurate sparse
/// memory so every read the capture performs is a real width-correct read. Expected labels and
/// strides come from the renderers themselves, not from the implementation.
/// </summary>
public sealed class BattleCaptureTests
{
    private const nuint ImageBase = 0x00400000;
    private const nuint Canvas = 0x10000000;
    private const nuint TextManager = 0x20000000;
    private const nuint ClassicMenu = 0x30000000;
    private const nuint TouchMenu = 0x30100000;

    /// <summary>The image table at RVA 0x39906C that 0x41EAF6 adds to an encoded item id.</summary>
    private static readonly int[] ItemGroupBases =
        [0, 0x6F, 0xA1, 0xC8, 0x103, 0x12E, 0x66, 0x4A, 0x2E, 0x22, 2, 0x63, 1];

    [Fact]
    public void RejectsAnObjectThatIsNotOneOfTheTwoAuditedBattleMenus()
    {
        var world = World();
        world.Pointer(0x30900000, ImageBase + 0x3A5D0C); // an nsMenu manager, not a battle menu

        Assert.Null(new BattleCapture(world).Capture(ImageBase, 0x30900000));
        Assert.Null(new BattleCapture(world).Capture(ImageBase, 0));
    }

    [Fact]
    public void ReadsThePartySlotsTheHudItselfFormats()
    {
        var world = World();

        var snapshot = new BattleCapture(world).Capture(ImageBase, ClassicMenu);

        Assert.NotNull(snapshot);
        Assert.Collection(
            snapshot!.Party,
            first =>
            {
                Assert.Equal(0, first.Slot);
                Assert.Equal("Crono", first.Name);
                Assert.Equal(44, first.Hp);
                Assert.Equal(70, first.MaximumHp);
                Assert.Equal(3, first.Mp);
                Assert.Equal(9, first.MaximumMp);
                Assert.Equal(string.Empty, first.Status);
            },
            second =>
            {
                Assert.Equal(1, second.Slot);
                Assert.Equal("Marle", second.Name);
                Assert.Equal(50, second.Hp);
                Assert.Equal(80, second.MaximumHp);
            });
    }

    [Fact]
    public void AbsentPartySlotsAreOmittedRatherThanInvented()
    {
        var world = World();
        world.Int32(Canvas + 0x19FA0 + 4, 0); // slot 1 not present

        var snapshot = new BattleCapture(world).Capture(ImageBase, ClassicMenu);

        Assert.Equal([0], snapshot!.Party.Select(member => member.Slot));
    }

    [Fact]
    public void APresentSlotThatCannotBeReadIsRefusedInsteadOfSilentlyDropped()
    {
        var world = World();
        world.Int32(Canvas + 0x19FA0 + 8, 1); // the HUD says slot 2 is on screen, but nothing backs it

        Assert.Null(new BattleCapture(world).Capture(ImageBase, ClassicMenu));
    }

    [Fact]
    public void ReadsTheBaseCommandTheNativeCursorPointsAtAndSaysWhoseTurnItIs()
    {
        var world = World();

        var snapshot = new BattleCapture(world).Capture(ImageBase, ClassicMenu);

        Assert.Equal("command:0:2:2", snapshot!.FocusIdentity);
        Assert.Equal("Crono: Item", snapshot.FocusText);
    }

    [Fact]
    public void BaseCommandUsesTheMappedBattlerIndexNotTheRawPartySlot()
    {
        var world = World();
        world.Int32(Canvas + 0x1AD20, 1);           // active slot 1
        world.Int32(Canvas + 0x1AD10 + 4, 5);       // maps to battler 5
        world.Int32(Canvas + 0x19E94 + 5 * 4, 1);   // whose row is Tech

        var snapshot = new BattleCapture(world).Capture(ImageBase, ClassicMenu);

        Assert.Equal("command:1:1:1", snapshot!.FocusIdentity);
        Assert.Equal("Marle: Tech", snapshot.FocusText);
    }

    [Fact]
    public void TheSecondRowReadsComboWhenTheNativePredicateSelectsIt()
    {
        var world = World();
        world.Int32(Canvas + 0x19E94, 1);       // cursor on row 1
        world.Int32(Canvas + 0x19ECC, 1);       // combos enabled
        world.Int32(Canvas + 0x1A1E0, 0x40);    // slot 0 has a partner mask
        world.Int32(Canvas + 0x1A1D4, 0);
        world.Int32(Canvas + 0x1A430, 0);       // nothing locks the row back to Tech

        var snapshot = new BattleCapture(world).Capture(ImageBase, ClassicMenu);

        Assert.Equal("command:0:1:3", snapshot!.FocusIdentity);
        Assert.Equal("Crono: Combo", snapshot.FocusText);
    }

    [Fact]
    public void ThereIsNoFourthBaseCommandRowToReport()
    {
        var world = World();
        world.Int32(Canvas + 0x19E94, 3); // only rows 0..2 are ever drawn

        Assert.Null(new BattleCapture(world).Capture(ImageBase, ClassicMenu)!.FocusIdentity);
    }

    [Fact]
    public void AnimationPhasesWithNoSelectableCommandHaveNullFocusInsteadOfAGuess()
    {
        var world = World();
        world.Int32(Canvas + 0x1AD50, 1); // the gate the renderer requires to be zero

        var snapshot = new BattleCapture(world).Capture(ImageBase, ClassicMenu);

        Assert.NotNull(snapshot);
        Assert.Null(snapshot!.FocusIdentity);
        Assert.Null(snapshot.FocusText);
        Assert.NotEmpty(snapshot.Party);
    }

    [Fact]
    public void NegativeActiveSlotAbortsFocusExactlyAsTheRendererDoes()
    {
        var world = World();
        world.Int32(Canvas + 0x1AD20, -1);

        Assert.Null(new BattleCapture(world).Capture(ImageBase, ClassicMenu)!.FocusIdentity);
    }

    [Fact]
    public void TheTechListIsReadFromTheCanvasGridCursorRatherThanTheBaseCommandRow()
    {
        var world = World();
        OpenTechList(world, cursor: 1, page: 0, count: 3);
        TechRow(world, character: 0, entry: 1, id: 0x2A, techSlot: 1);
        TechRecord(world, character: 0, techSlot: 1, cost: 8, greyed: false);
        world.Message(0x44, 0x2A, "Cyclone");

        var snapshot = new BattleCapture(world).Capture(ImageBase, ClassicMenu);

        Assert.Equal("tech:0:1:42", snapshot!.FocusIdentity);
        Assert.Equal("Crono: Cyclone, 8 MP", snapshot.FocusText);
    }

    [Fact]
    public void TheTechListPageBaseScrollsTheSelectionPastTheSixVisibleCells()
    {
        var world = World();
        OpenTechList(world, cursor: 3, page: 6, count: 12);
        TechRow(world, character: 0, entry: 9, id: 0x31, techSlot: 9);
        TechRecord(world, character: 0, techSlot: 9, cost: 12, greyed: false);
        world.Message(0x44, 0x31, "Luminaire");

        var snapshot = new BattleCapture(world).Capture(ImageBase, ClassicMenu);

        Assert.Equal("tech:0:9:49", snapshot!.FocusIdentity);
        Assert.Equal("Crono: Luminaire, 12 MP", snapshot.FocusText);
    }

    [Fact]
    public void GreyedTechRowsAreReportedUnavailableFromTheRecordFlagTheRendererTests()
    {
        var world = World();
        OpenTechList(world, cursor: 1, page: 0, count: 3);
        TechRow(world, character: 0, entry: 1, id: 0x2A, techSlot: 1);
        TechRecord(world, character: 0, techSlot: 1, cost: 8, greyed: true);
        world.Message(0x44, 0x2A, "Cyclone");

        var snapshot = new BattleCapture(world).Capture(ImageBase, ClassicMenu);

        Assert.Equal("Crono: Cyclone, 8 MP, unavailable", snapshot!.FocusText);
    }

    [Fact]
    public void DualAndTripleRowsUseTheBracketedCommandBankLabelsTheRendererDraws()
    {
        var world = World();
        OpenTechList(world, cursor: 0, page: 0, count: 2);
        TechRow(world, character: 0, entry: 0, id: 0xFC, techSlot: 0);
        TechRecord(world, character: 0, techSlot: 0, cost: 0, greyed: false);
        world.Message(0x3B, 0x0D, "Dual Tech");

        var snapshot = new BattleCapture(world).Capture(ImageBase, ClassicMenu);

        Assert.Equal("tech:0:0:252", snapshot!.FocusIdentity);
        Assert.Equal("Crono: [Dual Tech]", snapshot.FocusText);
    }

    [Fact]
    public void RowsTheTechRendererLeavesBlankAreNotReportedAsASelection()
    {
        var world = World();
        OpenTechList(world, cursor: 0, page: 0, count: 2);
        TechRow(world, character: 0, entry: 0, id: 0xFB, techSlot: 0);

        Assert.Null(new BattleCapture(world).Capture(ImageBase, ClassicMenu)!.FocusIdentity);
    }

    [Fact]
    public void ATechCursorBeyondTheNativeRowCountIsRefused()
    {
        var world = World();
        OpenTechList(world, cursor: 4, page: 0, count: 3);
        TechRow(world, character: 0, entry: 4, id: 0x2A, techSlot: 4);

        Assert.Null(new BattleCapture(world).Capture(ImageBase, ClassicMenu)!.FocusIdentity);
    }

    [Fact]
    public void TheItemListDecodesTheGroupedIdAcrossTheTwentyByteEntryStride()
    {
        var world = World();
        OpenItemList(world, cursor: 2, page: 0, count: 4);
        ItemEntry(world, entry: 2, encoded: 0x2005, quantity: 3, greyed: false);
        world.Message(0x1B, 0xA1 + 5, "Mid Ether"); // group 2 base 0xA1 plus the low twelve bits

        var snapshot = new BattleCapture(world).Capture(ImageBase, ClassicMenu);

        Assert.Equal("item:2:8197", snapshot!.FocusIdentity);
        Assert.Equal("Crono: Mid Ether x3", snapshot.FocusText);
    }

    [Fact]
    public void GreyedItemRowsAreReportedUnavailableFromTheFlagByteTheRendererTests()
    {
        var world = World();
        OpenItemList(world, cursor: 0, page: 5, count: 9);
        ItemEntry(world, entry: 5, encoded: 0x0001, quantity: 12, greyed: true);
        world.Message(0x1B, 1, "Tonic");

        var snapshot = new BattleCapture(world).Capture(ImageBase, ClassicMenu);

        Assert.Equal("item:5:1", snapshot!.FocusIdentity);
        Assert.Equal("Crono: Tonic x12, unavailable", snapshot.FocusText);
    }

    [Fact]
    public void AnItemRowTheRendererSkipsIsNotReportedAsASelection()
    {
        var world = World();
        OpenItemList(world, cursor: 1, page: 0, count: 4);
        ItemEntry(world, entry: 1, encoded: 0x0001, quantity: 0, greyed: false); // zero quantity draws nothing

        Assert.Null(new BattleCapture(world).Capture(ImageBase, ClassicMenu)!.FocusIdentity);
    }

    [Fact]
    public void AnItemCursorBeyondTheNativeItemCountIsRefused()
    {
        var world = World();
        OpenItemList(world, cursor: 3, page: 2, count: 4);
        ItemEntry(world, entry: 5, encoded: 0x0001, quantity: 2, greyed: false);

        Assert.Null(new BattleCapture(world).Capture(ImageBase, ClassicMenu)!.FocusIdentity);
    }

    [Fact]
    public void AStaleRetainedListMenuNeverSteersTheSelection()
    {
        var world = World();
        // A left-over nsBattleListMenu with a focus of its own. Both interfaces select through the
        // canvas, so the base command must still be the answer.
        world.Pointer(ClassicMenu + 0x35C, 0x30200000);
        world.Pointer(0x30200000, ImageBase + 0x3A2AFC);
        world.Pointer(0x30200000 + 0x2A0, 0x30300000);
        world.Pointer(0x30300000, ImageBase + 0x3A5D0C);
        world.Int32(0x30300000 + 0x2C4, 4);

        Assert.Equal("command:0:2:2", new BattleCapture(world).Capture(ImageBase, ClassicMenu)!.FocusIdentity);
    }

    [Fact]
    public void AMenuWhoseModeCopyDisagreesWithTheCanvasClaimsNoPanel()
    {
        var world = World();
        world.Int32(Canvas + 0x19E90, 2);  // the canvas has moved on to the item list
        world.Int32(ClassicMenu + 0x6A8, 0); // but the menu is still drawing base commands

        var snapshot = new BattleCapture(world).Capture(ImageBase, ClassicMenu);

        Assert.NotNull(snapshot);
        Assert.Null(snapshot!.FocusIdentity);
        Assert.NotEmpty(snapshot.Party);
    }

    [Fact]
    public void ReadsTheCommittedTargetAndItsVisibleEnemyName()
    {
        var world = World();
        world.Int32(Canvas + 0x19EEC, 1);           // the phase gate the menus refuse to draw under
        world.Int32(Canvas + 0x19F0C, 2);           // target cursor
        world.Int32(Canvas + 0x1A140 + 2 * 4, 6);   // candidate battler 6
        world.Int32(Canvas + 0x1ACD4, 6);           // committed recipient
        world.Int32(Canvas + 0x1A018 + 6 * 4, 0x0C);
        world.Message(0x32, 0x0C, "Blue Imp");

        var snapshot = new BattleCapture(world).Capture(ImageBase, ClassicMenu);

        Assert.Equal("target:2:6", snapshot!.FocusIdentity);
        Assert.Equal("Blue Imp", snapshot.FocusText);
        Assert.Equal("Blue Imp", snapshot.BattlerNames[6]);
    }

    [Fact]
    public void APartyTargetIsNamedFromTheCharacterNameNotTheMonsterBank()
    {
        var world = World();
        world.Int32(Canvas + 0x19EEC, 1);
        world.Int32(Canvas + 0x19F0C, 0);
        world.Int32(Canvas + 0x1A140, 1);           // battler 1 is a party slot
        world.Int32(Canvas + 0x1ACD4, 1);
        world.Int32(Canvas + 0x1A018 + 4, 0x0C);    // a monster index sharing the slot number
        world.Message(0x32, 0x0C, "Blue Imp");

        var snapshot = new BattleCapture(world).Capture(ImageBase, ClassicMenu);

        Assert.Equal("target:0:1", snapshot!.FocusIdentity);
        Assert.Equal("Marle", snapshot.FocusText);
    }

    [Fact]
    public void SkippedTargetCandidatesNeverBecomeFocus()
    {
        var world = World();
        world.Int32(Canvas + 0x19EEC, 1);
        world.Int32(Canvas + 0x19F0C, 2);
        world.Int32(Canvas + 0x1A140 + 2 * 4, 0x80); // the native skip marker
        world.Int32(Canvas + 0x1ACD4, 6);

        Assert.Null(new BattleCapture(world).Capture(ImageBase, ClassicMenu)!.FocusIdentity);
    }

    [Fact]
    public void EveryNameableBattlerIsListedSoPopupsNeverFallBackToASlotNumber()
    {
        var world = World();
        world.Int32(Canvas + 0x1A018 + 3 * 4, 0x0C);
        world.Int32(Canvas + 0x1A018 + 4 * 4, 0x1A);
        world.Message(0x32, 0x0C, "Blue Imp");
        world.Message(0x32, 0x1A, "Roly");

        var names = new BattleCapture(world).Capture(ImageBase, ClassicMenu)!.BattlerNames;

        Assert.Equal("Crono", names[0]);
        Assert.Equal("Marle", names[1]);
        Assert.Equal("Blue Imp", names[3]);
        Assert.Equal("Roly", names[4]);
        Assert.False(names.ContainsKey(2));
    }

    [Fact]
    public void IdenticalEnemiesKeepTheIdenticalNameTheGameShowsWithoutAnInventedSuffix()
    {
        var world = World();
        foreach (var battler in (int[])[3, 5, 7])
        {
            world.Int32(Canvas + 0x1A018 + (nuint)(battler * 4), 0x0C);
        }

        world.Message(0x32, 0x0C, "Nu");

        var names = new BattleCapture(world).Capture(ImageBase, ClassicMenu)!.BattlerNames;

        // A sighted player sees three sprites all labelled Nu. Lettering them would both invent a
        // label and corrupt the single real Nu when an unused slot carries a stale index.
        Assert.Equal("Nu", names[3]);
        Assert.Equal("Nu", names[5]);
        Assert.Equal("Nu", names[7]);
    }

    [Fact]
    public void ReservedNameIndicesAreNotTurnedIntoMonsterNames()
    {
        var world = World();
        world.Int32(Canvas + 0x1A018 + 3 * 4, 0xFF); // the native "no name" marker
        world.Int32(Canvas + 0x1A018 + 4 * 4, 0xFC);
        world.Message(0x32, 0xFF, "not a monster");
        world.Message(0x32, 0xFC, "not a monster either");

        var names = new BattleCapture(world).Capture(ImageBase, ClassicMenu)!.BattlerNames;

        Assert.False(names.ContainsKey(3));
        Assert.False(names.ContainsKey(4));
    }

    [Fact]
    public void TheActingFieldIsAPartySlotSoACharacterWhoseIdDiffersStillGetsItsTechList()
    {
        var world = World();
        world.Int32(Canvas + 0x1324C + 4, 2);           // Lucca, character id 2, sitting in slot 1
        world.Name(Canvas + 0x1908 + 2 * 24, "Lucca");
        OpenTechList(world, cursor: 0, page: 0, count: 2, slot: 1);
        TechRow(world, character: 1, entry: 0, id: 0x3D, techSlot: 0);
        TechRecord(world, character: 1, techSlot: 0, cost: 6, greyed: false);
        world.Message(0x44, 0x3D, "Flame Toss");

        var snapshot = new BattleCapture(world).Capture(ImageBase, ClassicMenu);

        Assert.Equal("tech:1:0:61", snapshot!.FocusIdentity);
        Assert.Equal("Lucca: Flame Toss, 6 MP", snapshot.FocusText);
    }

    [Fact]
    public void TheItemListAlsoNamesTheActingSlotRatherThanAMatchingCharacterId()
    {
        var world = World();
        world.Int32(Canvas + 0x1324C + 4, 2);
        world.Name(Canvas + 0x1908 + 2 * 24, "Lucca");
        OpenItemList(world, cursor: 0, page: 0, count: 1, slot: 1);
        ItemEntry(world, entry: 0, encoded: 0x0001, quantity: 6, greyed: false);
        world.Message(0x1B, 1, "Tonic");

        Assert.Equal("Lucca: Tonic x6", new BattleCapture(world).Capture(ImageBase, ClassicMenu)!.FocusText);
    }

    [Fact]
    public void AnActingSlotOutsideThePartyIsRefusedRatherThanIndexedPastTheTechTables()
    {
        var world = World();
        OpenTechList(world, cursor: 0, page: 0, count: 2);
        world.Int32(Canvas + 0x19E78, 4); // beyond the three blocks the tech tables hold

        Assert.Null(new BattleCapture(world).Capture(ImageBase, ClassicMenu)!.FocusIdentity);
    }

    [Fact]
    public void TheTouchMenuProducesTheSameFocusAsTheClassicMenu()
    {
        var world = World();

        var classic = new BattleCapture(world).Capture(ImageBase, ClassicMenu);
        var touch = new BattleCapture(world).Capture(ImageBase, TouchMenu);

        Assert.Equal(classic!.FocusIdentity, touch!.FocusIdentity);
        Assert.Equal(classic.FocusText, touch.FocusText);
    }

    [Fact]
    public void AnUnreadableCanvasYieldsNoSnapshotAtAll()
    {
        var world = World();
        world.Pointer(ImageBase + 0x41B4C4, 0);

        Assert.Null(new BattleCapture(world).Capture(ImageBase, ClassicMenu));
    }

    [Fact]
    public void TheDefaultConstructionResolvesTextThroughTheLoadedTextManager()
    {
        var world = World();

        // No callback supplied: the capture must reach msg/sfc_btl.txt itself.
        Assert.Equal("Crono: Item", new BattleCapture(world).Capture(ImageBase, ClassicMenu)!.FocusText);
    }

    [Fact]
    public void AnUnreadableTextManagerLeavesFocusTextNullButKeepsIdentity()
    {
        var world = World();
        world.Pointer(ImageBase + 0x41C3D8, 0);

        var snapshot = new BattleCapture(world).Capture(ImageBase, ClassicMenu);

        Assert.Equal("command:0:2:2", snapshot!.FocusIdentity);
        Assert.Null(snapshot.FocusText);
    }

    [Fact]
    public void AnInjectedLocalizerOverridesTheLoadedText()
    {
        var world = World();

        var snapshot = new BattleCapture(world, (bank, index) => bank == 0x3B && index == 2 ? "Object" : null)
            .Capture(ImageBase, ClassicMenu);

        Assert.Equal("Crono: Object", snapshot!.FocusText);
    }

    /// <summary>A battle in the base-command phase with Crono and Marle present.</summary>
    private static FixtureMemory World()
    {
        var world = new FixtureMemory();
        world.Pointer(ImageBase + 0x41B4C4, Canvas);
        world.Pointer(ImageBase + 0x41C3D8, TextManager);
        world.Pointer(ClassicMenu, ImageBase + 0x39D884);
        world.Pointer(TouchMenu, ImageBase + 0x39DC2C);
        for (var group = 0; group < ItemGroupBases.Length; group++)
        {
            world.Int32(ImageBase + 0x39906C + (nuint)(group * 4), ItemGroupBases[group]);
        }

        world.Int32(Canvas + 0x19FA0, 1);
        world.Int32(Canvas + 0x19FA0 + 4, 1);
        world.Int32(Canvas + 0x19FA0 + 8, 0);
        world.Int32(Canvas + 0x1324C, 0);
        world.Int32(Canvas + 0x1324C + 4, 1);
        world.Int32(Canvas + 0x1324C + 8, 0xFF);
        world.Name(Canvas + 0x1908, "Crono");
        world.Name(Canvas + 0x1908 + 24, "Marle");
        world.UInt16(Canvas + 0x15BA3, 44);
        world.UInt16(Canvas + 0x15BA5, 70);
        world.UInt16(Canvas + 0x15BA7, 3);
        world.UInt16(Canvas + 0x15BA9, 9);
        world.UInt16(Canvas + 0x15BA3 + 0x80, 50);
        world.UInt16(Canvas + 0x15BA5 + 0x80, 80);
        world.UInt16(Canvas + 0x15BA7 + 0x80, 12);
        world.UInt16(Canvas + 0x15BA9 + 0x80, 20);

        SetMode(world, 0);
        world.Int32(Canvas + 0x1AD20, 0);
        world.Int32(Canvas + 0x1AD10, 0);
        world.Int32(Canvas + 0x1AD50, 0);
        world.Int32(Canvas + 0x19E94, 2);
        world.Int32(Canvas + 0x19EEC, 0);
        world.Int32(Canvas + 0x19E78, 0);
        for (var slot = 0; slot < 3; slot++)
        {
            world.Int32(Canvas + 0x19EA0 + (nuint)(slot * 4), 0);
            world.Int32(Canvas + 0x19EC0 + (nuint)(slot * 4), 0);
        }

        world.Int32(Canvas + 0x19EB8, 0);
        world.Int32(Canvas + 0x19EBC, 0);
        world.Int32(Canvas + 0x19F0C, 0);
        world.Int32(Canvas + 0x1A140, 0x80);
        world.Int32(Canvas + 0x1ACD4, 0);

        world.Message(0x3B, 0, "Attack");
        world.Message(0x3B, 1, "Tech");
        world.Message(0x3B, 2, "Item");
        world.Message(0x3B, 3, "Combo");
        return world;
    }

    private static void SetMode(FixtureMemory world, int mode)
    {
        world.Int32(Canvas + 0x19E90, mode);
        world.Int32(ClassicMenu + 0x6A8, mode);
        world.Int32(TouchMenu + 0x6A8, mode);
    }

    private static void OpenTechList(FixtureMemory world, int cursor, int page, int count, int slot = 0)
    {
        SetMode(world, 1);
        world.Int32(Canvas + 0x19E78, slot);
        world.Int32(Canvas + 0x19EA0 + (nuint)(slot * 4), cursor);
        world.Int32(Canvas + 0x19EC0 + (nuint)(slot * 4), page);
        world.Int32(Canvas + 0x1A2C0 + (nuint)(slot * 4), count);
    }

    private static void TechRow(FixtureMemory world, int character, int entry, int id, int techSlot)
    {
        var record = Canvas + 0x19C8C + (nuint)((character * 0x14 + entry) * 8);
        world.Int32(record, id);
        world.Int32(record + 4, techSlot);
    }

    private static void TechRecord(FixtureMemory world, int character, int techSlot, int cost, bool greyed)
    {
        var record = Canvas + 0x1AEE8 + (nuint)((character * 0x14 + techSlot) * 0x20);
        world.Byte(record + 8, (byte)(greyed ? 0x80 : 0));
        world.Int32(record + 0x0C, cost);
        world.Int32(record + 0x10, -1);
        world.Int32(record + 0x14, -1);
    }

    private static void OpenItemList(FixtureMemory world, int cursor, int page, int count, int slot = 0)
    {
        SetMode(world, 2);
        world.Int32(Canvas + 0x19E78, slot);
        world.Int32(Canvas + 0x19EB8, cursor);
        world.Int32(Canvas + 0x19EBC, page);
        world.Int32(Canvas + 0x1ADB0, count);
    }

    private static void ItemEntry(FixtureMemory world, int entry, int encoded, int quantity, bool greyed)
    {
        var record = Canvas + 0x1B668 + (nuint)(entry * 0x14);
        world.Int32(record, encoded);
        world.Byte(record + 8, (byte)(greyed ? 0x80 : 0));
        world.Int32(record + 0x0C, quantity);
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

        public void UInt16(nuint address, ushort value)
        {
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
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

        private static byte[] MsvcString(string value)
        {
            var encoded = Encoding.UTF8.GetBytes(value);
            var layout = new byte[MsvcStringReader.LayoutSize];
            encoded.CopyTo(layout, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x10), (uint)encoded.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x14), 15);
            return layout;
        }
    }
}
