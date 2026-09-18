using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class EquipmentCandidateTests
{
    [Fact] public void ReadsTheWeaponListWithCharacterAndSlotManagersDisabled()
    {
        var (memory, image, node) = Frame();
        var result = new FieldSubmenuCapture(memory).Capture(image, node);
        Assert.NotNull(result);
        Assert.Contains("Bronze Blade", result.Text);
        Assert.Contains("Quantity 1", result.Text);
        Assert.Contains("1 of 1", result.Text);
    }

    [Fact] public void ReadsWeaponAttributesSeparatelyFromTheCharacterPreview()
    {
        var (memory, image, node) = Frame();
        var result = new FieldSubmenuCapture(memory).Capture(image, node);
        Assert.Contains("Item details: Attack:7", result!.Text);
        Assert.Contains("Attack 10, increased from 8", result.Text);
    }

    [Fact] public void TheNativeCandidateCursorMustAgreeWithItsInputManager()
    {
        var (memory, image, node) = Frame();
        memory.Word(Word(memory, node + 0x2F0) + 0x35C, 1);
        Assert.Null(new FieldSubmenuCapture(memory).Capture(image, node));
    }

    [Fact] public void ItemListCallbacksBelongToTheEquipmentPage()
    {
        var (memory, image, node) = Frame();
        Assert.True(new FieldSubmenuCapture(memory).OwnsManager(image, node, CandidateManager(memory, node)));
    }

    [Fact] public void PairsThePreviewStatsAndAnnouncesTheVisibleIncrease()
    {
        var (memory, image, node) = Frame();
        var result = new FieldSubmenuCapture(memory).Capture(image, node);
        Assert.NotNull(result);
        Assert.Contains("Attack 10, increased from 8", result.Text);
        Assert.Contains("Defense 16", result.Text);
        Assert.Contains("Strength 5", result.Text);
        Assert.Contains("Speed 13", result.Text);
        Assert.Contains("Magic Defense 2", result.Text);
        Assert.Contains("Maximum HP 70", result.Text);
    }

    [Fact] public void DoesNotConfusePreviewWithTheEquippedBaseline()
    {
        var (memory, image, node) = Frame();
        var stat = Stat(memory, node, 7);
        memory.String(Word(memory, stat + 0x280) + 0x2A0, "6");
        var result = new FieldSubmenuCapture(memory).Capture(image, node);
        Assert.Contains("Attack 6, decreased from 8", result!.Text);
    }

    [Fact] public void AnUnrelatedStatCannotSilenceTheSelectedItem()
    {
        var (memory, image, node) = Frame();
        memory.Word(Stat(memory, node, 7) + 0x16C, 0);
        var result = new FieldSubmenuCapture(memory).Capture(image, node);
        Assert.Contains("Bronze Blade", result!.Text);
        Assert.DoesNotContain("Attack 10", result.Text);
    }

    [Fact] public void MaximumStatMarkersRemainAccessible()
    {
        var (memory, image, node) = Frame();
        var stat = Stat(memory, node, 2);
        memory.String(Word(memory, stat + 0x280) + 0x2A0, "\u2605\u2605");
        Assert.Contains("Speed maximum 16, increased from 13", new FieldSubmenuCapture(memory).Capture(image, node)!.Text);
    }

    [Fact] public void CandidateReadingAlsoWorksInsideTheShopsEquipPage()
    {
        var (memory, image, node) = Frame();
        const uint scene = 0x61000000, children = 0x61010000;
        memory.Word(scene, image + ShopCapture.SceneVtableRva).Byte(scene + 0x1AD, 1)
            .Byte(scene + 0x298, 0).Byte(scene + 0x299, 0).Byte(scene + 0x29A, 1)
            .Word(scene + 0x160, children).Word(scene + 0x164, children + 4).Word(children, node)
            .Word(node + 0x16C, scene);
        var result = new ShopCapture(memory).Capture(image, scene);
        Assert.Contains("Equipment. Bronze Blade", result!.Text);
        Assert.Contains("Attack 10, increased from 8", result.Text);
    }

    [Fact] public void ARowQuantityThatDisagreesWithTheDisplayedControlIsRejected()
    {
        var (memory, image, node) = Frame();
        memory.Word(Word(memory, Word(memory, node + 0x2F0) + 0x31C) + 4, 2);
        Assert.Null(new FieldSubmenuCapture(memory).Capture(image, node));
    }

    [Fact] public void ASlotChangeDuringCaptureIsRejected()
    {
        var (memory, image, node) = Frame();
        var child = Word(memory, node + 0x2F0); var reads = 0;
        memory.BeforeRead = a => { if (a == child + 0x304 && ++reads == 2) memory.Word(child + 0x304, 1); };
        Assert.Null(new FieldSubmenuCapture(memory).Capture(image, node));
    }

    [Fact] public void ADetachedCandidateListCannotSpeak()
    {
        var (memory, image, node) = Frame();
        var list = Word(memory, Word(memory, node + 0x2F0) + 0x30C);
        memory.Word(list + 0x16C, 0);
        Assert.Null(new FieldSubmenuCapture(memory).Capture(image, node));
    }

    [Fact] public void ADisabledCandidateListCannotReuseItsCursor()
    {
        var (memory, image, node) = Frame();
        memory.Byte(CandidateManager(memory, node) + 0x290, 1);
        Assert.Null(new FieldSubmenuCapture(memory).Capture(image, node));
    }

    [Fact] public void CancellingTheCandidateListReturnsToTheEquippedSlot()
    {
        var (memory, image, node) = Frame();
        var child = Word(memory, node + 0x2F0);
        memory.Byte(CandidateManager(memory, node) + 0x290, 1);
        memory.Byte(Word(memory, Word(memory, child + 0x308) + 0x280) + 0x290, 0);
        var result = new FieldSubmenuCapture(memory).Capture(image, node);
        Assert.StartsWith("Wooden Sword", result!.Text);
        Assert.DoesNotContain("Bronze Blade", result.Text);
    }

    [Fact] public void AChangedFocusDuringCaptureIsRejected()
    {
        var (memory, image, node) = Frame();
        var manager = CandidateManager(memory, node); var reads = 0;
        memory.BeforeRead = a => { if (a == manager + 0x2C4 && ++reads == 2) memory.Word(manager + 0x2C4, 1); };
        Assert.Null(new FieldSubmenuCapture(memory).Capture(image, node));
    }

    internal static (NavigationMemory Memory, uint Image, uint Node) Frame()
    {
        using var stream = typeof(EquipmentCandidateTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Native.Tests.Capture.equipment-native-0326.json")!;
        using var json = JsonDocument.Parse(stream); var frame = json.RootElement;
        var memory = new NavigationMemory();
        foreach (var segment in frame.GetProperty("segments").EnumerateObject())
            memory.Add(uint.Parse(segment.Name.Split(':')[0], NumberStyles.HexNumber), Convert.FromHexString(segment.Value.GetString()!));
        return (memory, frame.GetProperty("imageBase").GetUInt32(), frame.GetProperty("node").GetUInt32());
    }
    private static uint CandidateManager(NavigationMemory m, uint node) => Word(m, Word(m, Word(m, node + 0x2F0) + 0x30C) + 0x280);
    private static uint Stat(NavigationMemory m, uint node, uint index) => Word(m, Word(m, Word(m, node + 0x2F0) + 0x310) + index * 4);
    private static uint Word(NavigationMemory m, uint address)
    {
        Span<byte> bytes = stackalloc byte[4]; Assert.True(m.TryRead(address, bytes));
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }
}
