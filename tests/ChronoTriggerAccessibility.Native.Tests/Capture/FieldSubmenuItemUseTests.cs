using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed partial class FieldSubmenuCaptureTests
{
    [Fact]
    public void ItemTargetUsesKnockedOutForVisibleHpZeroAndPreservesVisibleMp()
    {
        var world = ItemUseScreen();
        ControlOf(world, ["Crono", ":", "HP", "0/", "70", ":", "MP", "0/", "8"], UseFirstCard);
        world.Pointer(UseFirstCard + 0x16C, UseCards);
        Assert.Equal("Target, 1 of 1. Crono. Knocked out. MP 0/8.",
            new FieldSubmenuCapture(world).Capture(ImageBase, Node)!.Text);
    }
    private const nuint UseManager = 0x25000000, UseMap = 0x25100000, UseSentinel = 0x25101000;
    private const nuint UsePanel = 0x25200000, UseCards = 0x25300000, UseCardVector = 0x25400000;
    private const nuint UseIcons = 0x25500000, UseFirstCard = 0x26000000, UseSecondCard = 0x26100000;

    [Fact]
    public void AnItemUseTargetReadsTheSeparateCharacterCard()
    {
        var world = ItemUseScreen();
        var result = new FieldSubmenuCapture(world).Capture(ImageBase, Node);
        Assert.NotNull(result);
        Assert.Equal("Target, 1 of 1. Crono. HP 13/70. MP 6/8.", result.Text);
        Assert.True(new FieldSubmenuCapture(world).OwnsManager(ImageBase, Node, UseManager));
    }

    [Fact]
    public void AnItemUseTargetStillReadsUpdatedHpAfterTheLastItemIsConsumed()
    {
        var world = ItemUseScreen();
        var capture = new FieldSubmenuCapture(world);
        var before = capture.Capture(ImageBase, Node);
        Row(world, 1, 0x4000, 0);
        // 1C82B0 replaces the complete sheet after applying the item, even at quantity zero.
        const nuint newSheet = 0x28000000, newCards = 0x28100000, newCard = 0x28200000;
        CocosNode(world, newSheet, UsePanel);
        world.Pointer(Node + 0x310, newSheet);
        world.Pointer(newSheet + 0x160, newCards); world.Pointer(newSheet + 0x164, newCards + 12);
        world.Pointer(newCards, newCard);
        ControlOf(world, ["Crono", ":", "HP", "63/", "70", ":", "MP", "6/", "8"], newCard);
        world.Pointer(newCard + 0x16C, newSheet);
        var after = capture.Capture(ImageBase, Node);
        Assert.NotNull(before); Assert.NotNull(after);
        Assert.Equal(before.FocusIdentity, after.FocusIdentity);
        Assert.Equal("Target, 1 of 1. Crono. HP 63/70. MP 6/8.", after.Text);
    }

    [Theory]
    [InlineData(false, 1, "Target, 2 of 2. Lucca. HP 50/80. MP 7/12.")]
    [InlineData(true, 0, "All party members. Crono. HP 13/70. MP 6/8. Lucca. HP 50/80. MP 7/12.")]
    public void ItemUseCorrelatesIndividualAndWholePartyControls(bool all, int key, string expected)
    {
        var world = ItemUseScreen(2, all, key);
        Assert.Equal(expected, new FieldSubmenuCapture(world).Capture(ImageBase, Node)!.Text);
    }

    [Theory]
    [InlineData(0x252001ADu, 0u, true)] // hidden target panel
    [InlineData(0x2600016Cu, 0u, false)] // card detached from its owner
    [InlineData(0x20000324u, 1u, false)] // mirrored cursor has not caught up
    [InlineData(0x25100008u, 2u, false)] // focus map and displayed member counts disagree
    [InlineData(0x25300164u, 0x25400008u, false)] // native sheet always has three card slots
    public void ItemUseDoesNotGuessAcrossInvalidTargetRelationships(uint address, uint value, bool singleByte)
    {
        var world = ItemUseScreen();
        if (singleByte) world.Byte(address, (byte)value); else world.Int32(address, (int)value);
        Assert.Null(new FieldSubmenuCapture(world).Capture(ImageBase, Node));
    }

    [Fact]
    public void ItemUseRejectsACardReplacedDuringCapture()
    {
        var world = ItemUseScreen();
        var reads = 0;
        world.BeforeRead = address =>
        {
            if (address == UseCardVector && ++reads == 2)
                world.Pointer(UseCardVector, UseSecondCard);
        };
        Assert.Null(new FieldSubmenuCapture(world).Capture(ImageBase, Node));
    }

    // Synthetic layout from 1C7710/23BC40/23CD80: the sheet always has three cards,
    // an icon vector counts populated members, and sibling blank controls own focus.
    private static FixtureMemory ItemUseScreen(int members = 1, bool all = false, int key = 0)
    {
        var world = World();
        CocosNode(world, UsePanel, Node); CocosNode(world, UseCards, UsePanel);
        world.Pointer(Node + 0x308, UsePanel); world.Pointer(Node + 0x310, UseCards);
        world.Pointer(Node + 0x314, UseManager); world.Int32(Node + 0x324, key);
        world.Pointer(UseCards + 0x160, UseCardVector); world.Pointer(UseCards + 0x164, UseCardVector + 12);
        world.Pointer(Node + 0x318, UseIcons); world.Pointer(Node + 0x31C, UseIcons + (nuint)(members * 4));
        world.Pointer(ManagerStack + 8, StackEntries + 8); world.Pointer(StackEntries + 4, UseManager);
        world.Byte(Manager + 0x290, 1);
        world.Pointer(UseManager, ImageBase + FieldSubmenuCapture.ManagerVtableRva);
        world.Byte(UseManager + 0x290, 0); world.Int32(UseManager + 0x2C4, key);
        world.Pointer(UseManager + 0x294, UseMap); world.Pointer(UseMap + 4, UseSentinel);
        var controls = all ? 1 : members;
        world.Int32(UseMap + 8, controls);
        for (var i = controls - 1; i >= 0; i--)
        {
            var control = (nuint)0x27000000 + (nuint)(i * 0x100000);
            ControlOf(world, [], control); world.Pointer(control + 0x16C, UsePanel);
            world.Pointer(control, ImageBase + 0x3A4364);
            var state = UseSentinel + 0x100 + (nuint)(i * 0x100);
            FocusEntry(world, UseSentinel, i, state, control);
            if (i + 1 < controls) world.Pointer(state - 0x80, state + 0x80);
        }
        for (var i = 0; i < 3; i++)
        {
            var card = UseFirstCard + (nuint)(i * 0x100000);
            string[] lines = i switch
            {
                0 => ["Crono", ":", "HP", "13/", "70", ":", "MP", "6/", "8"],
                1 => ["Lucca", ":", "HP", "50/", "80", ":", "MP", "7/", "12"],
                _ => ["Unused card must not be read"],
            };
            ControlOf(world, lines, card); world.Pointer(card + 0x16C, UseCards);
            world.Pointer(UseCardVector + (nuint)(i * 4), card);
            if (i < members)
            {
                var icon = UseIcons + 0x1000 + (nuint)(i * 0x1000);
                CocosNode(world, icon, UsePanel); world.Pointer(UseIcons + (nuint)(i * 4), icon);
            }
        }
        return world;
    }
}
