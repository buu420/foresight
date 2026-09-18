using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class MenuModeTests
{
    [Fact] public void TheCapturedHelmSlotReadsBronzeHelmInsteadOfThePassiveHideCapPreview()
    {
        var (memory, image, node) = Frame("equipment_slot");
        var result = new FieldSubmenuCapture(memory).Capture(image, node);
        Assert.StartsWith("Equipped Helm: Bronze Helm.", result!.Text);
        Assert.Contains("Current stats: Attack 12. Defense 22", result.Text);
        Assert.Contains("Item details: Defense:8", result.Text);
        Assert.DoesNotContain("Hide Cap", result.Text);
    }

    [Fact] public void AnAnimatedFooterDoesNotChangeTheEquipmentAnnouncement()
    {
        var (memory, image, node) = Frame("equipment_slot");
        var bar = Word(memory, Word(memory, node + 0x2F0) + 0x2F0);
        var label = Word(memory, Word(memory, bar + 0x2F0));
        var capture = new FieldSubmenuCapture(memory);
        var expected = capture.Capture(image, node);
        foreach (var prefix in new[] { "D", "Defen", "Defense:", "Defense:8" })
        {
            memory.String(label + 0x2A0, prefix);
            Assert.Equal(expected, capture.Capture(image, node));
        }
    }

    [Fact] public void AnEmptyEquippedSlotIsNamedFromItsNativeEmptyItemId()
    {
        var (memory, image, node) = Frame("equipment_slot");
        var child = Word(memory, node + 0x2F0);
        memory.Word(child + 0x328 + 12, 0x2000);
        memory.String(Word(memory, child + 0x32C + 12) + 0x2A0, "");
        Assert.StartsWith("Equipped Helm: None.", new FieldSubmenuCapture(memory).Capture(image, node)!.Text);
    }

    [Fact] public void ATemporarilyBlankLabelCannotDeclareWornGearUnequipped()
    {
        var (memory, image, node) = Frame("equipment_slot");
        var child = Word(memory, node + 0x2F0);
        memory.String(Word(memory, child + 0x32C + 12) + 0x2A0, "");
        Assert.Null(new FieldSubmenuCapture(memory).Capture(image, node));
    }

    [Fact] public void PickupInformationReadsTheItemUsableCharactersAndItsStat()
    {
        var (memory, image, node) = Frame("inventory_information");
        var result = new FieldSubmenuCapture(memory).Capture(image, node);
        Assert.NotNull(result);
        Assert.StartsWith("Item information. Hide Cap, quantity 2.", result.Text);
        Assert.Contains("Usable by:", result.Text);
        Assert.Contains("Crono", result.Text);
        Assert.Contains("Marle", result.Text);
        Assert.Contains("Lucca", result.Text);
        Assert.Contains("Defense", result.Text);
        Assert.Contains("3", result.Text);
        Assert.Contains("A lightweight leather cap.", result.Text);
        Assert.Contains("Confirm or Cancel to close.", result.Text);
        Assert.DoesNotContain("Target", result.Text);
    }

    [Theory]
    [InlineData(0x290, 1)]
    [InlineData(0x2C4, 1)]
    public void AnInactiveOrDifferentPopupStateCannotReadOldItemInformation(uint offset, uint value)
    {
        var (memory, image, node) = Frame("inventory_information");
        var manager = Word(memory, node + 0x304);
        if (offset == 0x290) memory.Byte(manager + offset, (byte)value);
        else memory.Word(manager + offset, value);
        Assert.Null(new FieldSubmenuCapture(memory).Capture(image, node));
    }

    [Fact] public void ClosingThePopupDuringCaptureCannotSpeakItAfterReturningToTheList()
    {
        var (memory, image, node) = Frame("inventory_information"); var reads = 0;
        memory.BeforeRead = address => {
            if (address == node + 0x300 && ++reads == 3) memory.Word(node + 0x300, 0);
        };
        Assert.Null(new FieldSubmenuCapture(memory).Capture(image, node));
    }

    [Fact] public void ADifferentActiveManagerCannotBorrowTheInformationPanel()
    {
        var (memory, image, node) = Frame("inventory_information");
        memory.Word(node + 0x304, 0);
        Assert.Null(new FieldSubmenuCapture(memory).Capture(image, node));
    }

    [Fact] public void AnInformationPanelDetachedFromItsInventoryCannotSpeak()
    {
        var (memory, image, node) = Frame("inventory_information");
        memory.Word(Word(memory, node + 0x300) + 0x16C, 0);
        Assert.Null(new FieldSubmenuCapture(memory).Capture(image, node));
    }

    [Fact] public void AnItemChangeDuringInformationCaptureCannotMixTwoItems()
    {
        var (memory, image, node) = Frame("inventory_information");
        var rows = Word(memory, node + 0x2D0); var reads = 0;
        memory.BeforeRead = address => {
            if (address == rows + 12 && ++reads == 2) memory.Word(rows + 12, 0x2002);
        };
        Assert.Null(new FieldSubmenuCapture(memory).Capture(image, node));
    }

    private static (NavigationMemory Memory, uint Image, uint Node) Frame(string name)
    {
        using var stream = typeof(MenuModeTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Native.Tests.Capture.menu-modes-native-0327.json")!;
        using var json = JsonDocument.Parse(stream); var frame = json.RootElement.GetProperty("frames").GetProperty(name);
        var memory = new NavigationMemory();
        foreach (var segment in frame.GetProperty("segments").EnumerateObject())
            memory.Add(uint.Parse(segment.Name.Split(':')[0], NumberStyles.HexNumber), Convert.FromHexString(segment.Value.GetString()!));
        return (memory, frame.GetProperty("imageBase").GetUInt32(), frame.GetProperty("node").GetUInt32());
    }

    private static uint Word(NavigationMemory memory, uint address)
    {
        Span<byte> data = stackalloc byte[4]; Assert.True(memory.TryRead(address, data));
        return BinaryPrimitives.ReadUInt32LittleEndian(data);
    }
}
