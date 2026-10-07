using System.Globalization;
using System.Buffers.Binary;
using System.Text.Json;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

/// <summary>Only bytes read by the capture, retained from read-only process snapshots on
/// 2026-09-14. These retain the actual ASLR base and retail control/Label layout.</summary>
public sealed class FieldSubmenuLiveCaptureTests
{
    private static (NavigationMemory Memory, uint Image, uint Node) Frame(string name, string version = "0319")
    {
        using var stream = typeof(FieldSubmenuLiveCaptureTests).Assembly.GetManifestResourceStream(
            $"ChronoTriggerAccessibility.Native.Tests.Capture.field-submenu-native-{version}.json")!;
        using var json = JsonDocument.Parse(stream);
        var frame = json.RootElement.GetProperty("frames").GetProperty(name);
        var memory = new NavigationMemory();
        foreach (var segment in frame.GetProperty("segments").EnumerateObject())
            memory.Add(uint.Parse(segment.Name.Split(':')[0], NumberStyles.HexNumber),
                Convert.FromHexString(segment.Value.GetString()!));
        return (memory, frame.GetProperty("imageBase").GetUInt32(), frame.GetProperty("node").GetUInt32());
    }

    [Theory]
    [InlineData("inventory_category", "Inventory", "Consumables, category. Empty.")]
    [InlineData("equipment_character", "Equipment", "Crono LV 1. HP 43/70. MP 8/8. EXP 10. Next 10")]
    public void ReplaysTheObservedSilentSelections(string frameName, string title, string expected)
    {
        var (memory, image, node) = Frame(frameName);
        var result = new FieldSubmenuCapture(memory).Capture(image, node);
        Assert.NotNull(result);
        Assert.Equal(title, result.Title);
        Assert.Equal(expected, result.Text);
        Assert.Null(new FieldSubmenuCapture(memory).Capture(0x400000, node));
    }

    [Fact]
    public void AnItemAppearingInTheCapturedCategoryRemovesTheEmptyAnnouncement()
    {
        var (memory, image, node) = Frame("inventory_category");
        memory.Word(0x2764DA0C, 1); // quantity in the sole captured record
        Assert.Equal("Consumables, category", new FieldSubmenuCapture(memory).Capture(image, node)!.Text);
    }

    [Fact]
    public void AChangedCommittedCategoryCannotReuseTheCapturedEmptyList()
    {
        var (memory, image, node) = Frame("inventory_category");
        memory.Word(node + FieldSubmenuCapture.ItemCategoryOffset, 1);
        Assert.Equal("Consumables, category", new FieldSubmenuCapture(memory).Capture(image, node)!.Text);
    }

    [Fact]
    public void ACharacterControlRemovedFromItsMenuCannotSpeakItsOldStats()
    {
        var (memory, image, node) = Frame("equipment_character");
        memory.Word(0x27488670 + FieldSubmenuCapture.NodeParentOffset, 0);
        Assert.Null(new FieldSubmenuCapture(memory).Capture(image, node));
    }

    [Fact]
    public void TheCapturedEmptyItemListReadsWithItsHelpModeEnabled()
    {
        var (memory, image, node) = Frame("inventory_empty_row", "0320");
        var result = new FieldSubmenuCapture(memory).Capture(image, node);
        Assert.NotNull(result);
        Assert.Equal("Inventory", result.Title);
        Assert.Equal("Consumables. Empty.", result.Text);
    }

    [Fact]
    public void PartyReadsItsLockedRosterWhenTheGameHasNoFocusableMember()
    {
        var (memory, image, node) = Frame("party_locked", "0321");
        var result = new FieldSubmenuCapture(memory).Capture(image, node);
        Assert.NotNull(result);
        Assert.Equal("Party", result.Title);
        Assert.Equal("Please select party members. Current party. Crono LV 1. HP 43/70. MP 8/8. Locked. Reserve. Empty. Usable Combos. None.", result.Text);
    }

    [Fact]
    public void PartyCorrelatesAFocusedIconWithTheSeparateCharacterCard()
    {
        var (memory, image, node) = Frame("party_locked", "0321");
        // Synthetic change to the captured layout: unlock Crono and focus his native key zero.
        memory.Word(0x2F13DD98 + 0x2C4, 0);
        memory.Byte(0x29088150 + 5, 0);
        memory.Byte(0x29430F60 + 0x11, 0);
        var result = new FieldSubmenuCapture(memory).Capture(image, node);
        Assert.NotNull(result);
        Assert.Equal("Current party, 1 of 1. Crono LV 1. HP 43/70. MP 8/8. Usable Combos. None.", result.Text);
    }

    [Fact]
    public void PartyDoesNotReadACharacterCardDetachedFromItsRoster()
    {
        var (memory, image, node) = Frame("party_locked", "0321");
        memory.Word(0x1E611348 + 0x16C, 0);
        Assert.Null(new FieldSubmenuCapture(memory).Capture(image, node));
    }

    [Fact]
    public void PartyDoesNotTreatAnUnknownManagerStateAsTheLockedRoster()
    {
        var (memory, image, node) = Frame("party_locked", "0321");
        memory.Word(0x29430E80, image + 0x3AC3F4);
        Assert.Null(new FieldSubmenuCapture(memory).Capture(image, node));
    }

    [Fact]
    public void PartyRejectsARebuiltRosterDuringCapture()
    {
        var (memory, image, node) = Frame("party_locked", "0321");
        var reads = 0;
        memory.BeforeRead = address =>
        {
            if (address == node + 0x2CC && ++reads == 2)
                memory.Word(node + 0x2D0, 0x29088150);
        };
        Assert.Null(new FieldSubmenuCapture(memory).Capture(image, node));
    }

    [Fact]
    public void PartyReadsTheReserveCardForKeyTenAndIdentifiesTheMemberBeingMoved()
    {
        var (memory, image, node) = PartyWithSyntheticReserve();
        var capture = new FieldSubmenuCapture(memory);
        Assert.Equal("Reserve, 1 of 1. Lucca. Usable Combos. None.", capture.Capture(image, node)!.Text);
        memory.Word(node + 0x334, 0);
        Assert.Equal("Reserve, 1 of 1. Lucca. Moving Crono. Usable Combos. None.", capture.Capture(image, node)!.Text);
        memory.Word(node + 0x334, 10);
        Assert.Equal("Reserve, 1 of 1. Lucca. Picked up. Usable Combos. None.", capture.Capture(image, node)!.Text);
    }

    [Fact]
    public void PartyReadsRenderedCombosAndDistinguishesIconOnlyGroupsFromUnreadableLabels()
    {
        var (memory, image, node) = Frame("party_locked", "0321");
        const uint comboPanel = 0x1E612788;
        SyntheticLabel(memory, 0x50010000, comboPanel, "Visible combo");
        memory.Word(comboPanel + 0x160, 0x50011000).Word(comboPanel + 0x164, 0x50011004);
        memory.Word(0x50011000, 0x50010000);
        var capture = new FieldSubmenuCapture(memory);
        Assert.EndsWith("Usable Combos. Visible combo.", capture.Capture(image, node)!.Text);
        memory.Word(0x50010000 + 0x2B0, 5000); // recognized Label with invalid string length
        Assert.Null(capture.Capture(image, node));
        // 1BF750 adds group backgrounds and portraits even when its ability loop adds no labels.
        memory.Word(0x50010000 + 0x278, 0); // synthetic non-Label child, with no rendered ability name
        Assert.EndsWith("Usable Combos. None.", capture.Capture(image, node)!.Text);
    }

    [Theory]
    [InlineData(0x294F91C0u + 0x1AD)] // icon hidden
    [InlineData(0x1E611348u + 0x1AD)] // populated card hidden
    [InlineData(0x1E612F20u + 0x1AD)] // card container hidden
    [InlineData(0x1E612788u + 0x1AD)] // combo panel hidden
    public void PartyDoesNotReadHiddenOwnedContent(uint visibilityAddress)
    {
        var (memory, image, node) = Frame("party_locked", "0321");
        memory.Byte(visibilityAddress, 0);
        Assert.Null(new FieldSubmenuCapture(memory).Capture(image, node));
    }

    [Fact]
    public void PartyCannotBorrowAnotherButtonsCardOrParkOnAnUnlockedMember()
    {
        var (memory, image, node) = PartyWithSyntheticReserve();
        memory.Word(0x29430E80 + 0x14, 0x294F91C0); // reserve state borrowing the current icon
        Assert.Null(new FieldSubmenuCapture(memory).Capture(image, node));

        (memory, image, node) = Frame("party_locked", "0321");
        memory.Byte(0x29088150 + 5, 0).Byte(0x29430F60 + 0x11, 0);
        Assert.Null(new FieldSubmenuCapture(memory).Capture(image, node));
    }

    [Fact]
    public void PartyCanReadItsRosterWhenTheComboPreviewHasNoHighlightedMember()
    {
        var (memory, image, node) = Frame("party_locked", "0321");
        memory.Word(node + 0x338, uint.MaxValue); // native combo builder explicitly handles -1
        Assert.Contains("Crono LV 1. HP 43/70. MP 8/8. Locked.", new FieldSubmenuCapture(memory).Capture(image, node)!.Text);
        memory.Word(node + 0x338, uint.MaxValue - 1);
        Assert.Null(new FieldSubmenuCapture(memory).Capture(image, node));
    }

    [Fact]
    public void TheStandalonePartySceneLeadsToThePageItBuiltAndReadsItsRoster()
    {
        var (memory, image, node) = StandaloneParty();
        var capture = new FieldSubmenuCapture(memory);
        Assert.True(capture.TryFindStandaloneFormation(image, StandaloneScene, out var page));
        Assert.Equal((nuint)node, page);
        Assert.Equal("Please select party members. Current party. Crono LV 1. HP 43/70. MP 8/8. Locked. Reserve. Empty. Usable Combos. None.",
            capture.Capture(image, page)!.Text);
    }

    [Theory]
    [InlineData("touch scene")]
    [InlineData("foreign parent")]
    [InlineData("hidden page")]
    [InlineData("second page")]
    public void AStandalonePartySceneMustOwnExactlyOneVisibleClassicPage(string mutation)
    {
        var (memory, image, node) = StandaloneParty();
        switch (mutation)
        {
            case "touch scene": memory.Word(StandaloneScene, image + 0x3B5764); break; // FormationScene, MenuNodeFormation
            case "foreign parent": memory.Word(node + 0x16C, Backdrop); break;
            case "hidden page": memory.Byte(node + 0x1AD, 0); break;
            case "second page":
                memory.Word(StandaloneScene + 0x164, SceneChildren + 12).Word(SceneChildren + 8, 0x51003000)
                    .Word(0x51003000, image + FieldSubmenuCapture.ClassicFormationNodeVtableRva)
                    .Word(0x51003000 + 0x16C, StandaloneScene).Byte(0x51003000 + 0x1AD, 1);
                break;
        }
        Assert.False(new FieldSubmenuCapture(memory).TryFindStandaloneFormation(image, StandaloneScene, out var page));
        Assert.Equal((nuint)0, page);
    }

    // FormationSteamScene::init (2A49A0) adds a backdrop, then the page built by 1BE850(0), to ECX.
    // The captured field-menu page is re-parented under a synthetic scene of that class.
    private const uint StandaloneScene = 0x51000000, Backdrop = 0x51001000, SceneChildren = 0x51002000;

    private static (NavigationMemory Memory, uint Image, uint Node) StandaloneParty()
    {
        var (memory, image, node) = Frame("party_locked", "0321");
        memory.Word(StandaloneScene, image + 0x3B0A48).Byte(StandaloneScene + 0x1AD, 1)
            .Word(StandaloneScene + 0x160, SceneChildren).Word(StandaloneScene + 0x164, SceneChildren + 8)
            .Word(SceneChildren, Backdrop).Word(SceneChildren + 4, node)
            .Word(Backdrop, image + 0x3A5D04).Word(Backdrop + 0x16C, StandaloneScene).Byte(Backdrop + 0x1AD, 1)
            .Word(node + 0x16C, StandaloneScene);
        return (memory, image, node);
    }

    // Synthetic reserve topology using the actual captured manager map and Label protocol.
    // This tests key/card correlation and swap speech; it is not a live multi-member capture.
    private static (NavigationMemory Memory, uint Image, uint Node) PartyWithSyntheticReserve()
    {
        var (memory, image, node) = Frame("party_locked", "0321");
        memory.Byte(0x29088150 + 5, 0).Byte(0x29430F60 + 0x11, 0);
        memory.Word(0x2F13DD98 + 0x2C4, 10).Word(node + 0x338, 10);
        memory.Word(node + 0x2D8, 0x50000000).Word(node + 0x2DC, 0x50000008);
        memory.Word(0x50000000, 1).Word(0x50000004, 0); // ID 1, reserve, unlocked
        memory.Word(node + 0x2F0, 0x50001000).Word(node + 0x2F4, 0x50001004);
        memory.Word(0x50001000, 0x50003000);
        memory.Word(node + 0x30C, 0x50002000).Word(node + 0x310, 0x50002004);
        memory.Word(0x50002000, 0x50004000);
        memory.Word(0x50003000, image + 0x3A4364).Word(0x50003000 + 0x16C, 0x1E610928).Byte(0x50003000 + 0x1AD, 1);
        SyntheticLabel(memory, 0x50004000, 0x1E610928, "Lucca");
        var sentinel = ReadWord(memory, ReadWord(memory, 0x2F13DD98 + 0x294) + 4);
        var entry = ReadWord(memory, sentinel);
        while (ReadWord(memory, entry + 8) != 999) entry = ReadWord(memory, entry);
        memory.Word(entry + 8, 10);
        memory.Word(0x29430E80, image + 0x3AC3F4).Byte(0x29430E80 + 0x11, 0).Word(0x29430E80 + 0x14, 0x50003000);
        return (memory, image, node);
    }

    private static void SyntheticLabel(NavigationMemory memory, uint label, uint parent, string value)
    {
        memory.Byte(label + 0x1AD, 1).Word(label + 0x16C, parent)
            .Word(label + 0x160, 0).Word(label + 0x164, 0)
            .Word(label + 0x278, ReadWord(memory, 0x1E5C6040 + 0x278)).String(label + 0x2A0, value);
    }

    private static uint ReadWord(NavigationMemory memory, uint address)
    {
        Span<byte> data = stackalloc byte[4];
        Assert.True(memory.TryRead(address, data));
        return BinaryPrimitives.ReadUInt32LittleEndian(data);
    }
}
