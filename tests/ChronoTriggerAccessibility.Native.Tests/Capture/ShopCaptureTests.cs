using System.Globalization;
using System.Text.Json;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class ShopCaptureTests
{
    [Theory]
    [InlineData("action", "Buy", "Funds: 460 G")]
    [InlineData("buy", "Buy. Padded Vest, 300 G", "Stock: 0")]
    [InlineData("quantity", "Buy. Bronze Blade. Quantity 1. Total 350 G", "Attack: 7")]
    [InlineData("sell", "Sell.", "Funds: 460 G")]
    public void ReplaysTheUsersSilentShop(string frame, string expected, string detail)
    {
        var (memory, image, node) = Frame(frame);
        var result = new ShopCapture(memory).Capture(image, node);
        Assert.NotNull(result);
        Assert.Equal("Shop", result.Title);
        Assert.Contains(expected, result.Text);
        Assert.Contains(detail, result.Text);
        Assert.Null(new ShopCapture(memory).Capture(0x400000, node));
    }

    [Theory]
    [InlineData("action", 0x2B0u)]
    [InlineData("buy", 0x2ACu)]
    [InlineData("quantity", 0x2B4u)]
    public void RejectsDetachedActivePage(string frame, uint offset)
    {
        var (memory, image, node) = Frame(frame);
        memory.Word(Word(memory, node + offset) + 0x16C, 0);
        Assert.Null(new ShopCapture(memory).Capture(image, node));
    }

    [Fact] public void IgnoresStaleQuantityPointerAfterReturningToList()
    {
        var (memory, image, node) = Frame("buy");
        memory.Word(node + 0x2B4, 0xDEADBEEF);
        Assert.Contains("Padded Vest", new ShopCapture(memory).Capture(image, node)!.Text);
    }

    [Fact] public void RejectsSelectionRebuiltDuringCapture()
    {
        var (memory, image, node) = Frame("buy");
        var seen = 0;
        memory.BeforeRead = address => { if (address == node + 0x298 && ++seen == 2) memory.Byte(node + 0x298, 0); };
        Assert.Null(new ShopCapture(memory).Capture(image, node));
    }

    [Fact] public void RejectsUnknownSceneRatherThanReadingAnUnrelatedMenu()
    {
        var (memory, image, node) = Frame("buy");
        memory.Word(node, image + FieldSubmenuCapture.ClassicItemNodeVtableRva);
        Assert.Null(new ShopCapture(memory).Capture(image, node));
    }

    [Fact] public void NamesTheCharactersAndTheVisibleEquipmentImprovement()
    {
        var (memory, image, node) = Frame("buy");
        var result = new ShopCapture(memory).Capture(image, node)!;
        Assert.Contains("Crono: Attack 8; Defense 21, increased.", result.Text);
        Assert.Contains("Lucca: Attack 8; Defense 19.", result.Text);
        // Native Steam's static pose is used when this character cannot equip the item.
        memory.Byte(0x1D9AEB08 + 0x1AD, 1);
        Assert.Contains("Crono: cannot equip.", new ShopCapture(memory).Capture(image, node)!.Text);
    }

    [Fact] public void DoesNotCombineOneRowsPriceWithAnotherRowsFocus()
    {
        var (memory, image, node) = Frame("buy");
        var page = Word(memory, node + 0x2AC);
        memory.Word(page + 0x2F4, 1);
        Assert.Null(new ShopCapture(memory).Capture(image, node));
    }

    [Fact] public void DoesNotReadTheDisabledListUnderAQuantityScreen()
    {
        var (memory, image, node) = Frame("quantity");
        var page = Word(memory, node + 0x2B4);
        var stack = Word(memory, page + 0x2C0);
        var manager = Word(memory, Word(memory, stack + 8) - 4);
        memory.Byte(manager + 0x290, 1);
        Assert.Null(new ShopCapture(memory).Capture(image, node));
    }

    [Fact] public void RejectsAnOutdatedQuantityTotal()
    {
        var (memory, image, node) = Frame("quantity");
        memory.Word(Word(memory, node + 0x2B4) + 0x2E8, 2);
        Assert.Null(new ShopCapture(memory).Capture(image, node));
    }

    [Fact] public void EmptySellListUsesItsNativeEmptyStateAndCaption()
    {
        var (memory, image, node) = Frame("sell");
        var page = Word(memory, node + 0x2AC);
        var groups = Word(memory, page + 0x2D4);
        memory.Word(groups + 4, Word(memory, groups));
        var stack = Word(memory, page + 0x2C0);
        var manager = Word(memory, Word(memory, stack + 8) - 4);
        var sentinel = Word(memory, Word(memory, manager + 0x294) + 4);
        var entry = Word(memory, sentinel);
        while (Word(memory, entry + 8) != 0) entry = Word(memory, entry);
        var state = Word(memory, entry + 12);
        memory.Word(state, image + 0x3ABC2C);
        memory.Word(manager + 0x16C, page).Byte(manager + 0x1AD, 1);
        // Synthetic loaded caption bank, retaining the native vector/string contract.
        memory.Word(image + 0x41C3D8, 0x60000000).Word(0x60000000, 0x60001000).Word(0x60000004, 0x60001090);
        memory.Word(0x60001000 + 0x23 * 4, 0x60002000).Word(0x60002000, 0x60003000).Word(0x60002004, 0x60003000 + 0x77 * 24);
        memory.String(0x60003000 + 0x76 * 24, "Nothing to sell.");
        var result = new ShopCapture(memory).Capture(image, node);
        Assert.NotNull(result);
        Assert.Equal("Sell. Nothing to sell. Funds: 460 G.", result.Text);
        memory.Word(state, image + FieldSubmenuCapture.FocusableStateVtableRva);
        Assert.Null(new ShopCapture(memory).Capture(image, node));
    }

    [Fact] public void ShopEquipmentUsesTheVisibleOwnedEquipmentPage()
    {
        var (memory, image, node) = Frame("action");
        using var stream = typeof(ShopCaptureTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Native.Tests.Capture.field-submenu-native-0319.json")!;
        using var json = JsonDocument.Parse(stream);
        var equipment = json.RootElement.GetProperty("frames").GetProperty("equipment_character");
        var oldImage = equipment.GetProperty("imageBase").GetUInt32();
        var page = equipment.GetProperty("node").GetUInt32();
        foreach (var segment in equipment.GetProperty("segments").EnumerateObject())
            memory.Add(uint.Parse(segment.Name.Split(':')[0], NumberStyles.HexNumber), Convert.FromHexString(segment.Value.GetString()!));
        // Synthetic shop ownership around the actual Equipment capture, retaining its ASLR base.
        memory.Word(node, oldImage + ShopCapture.SceneVtableRva);
        memory.Word(page + 0x16C, node).Byte(node + 0x29A, 1)
            .Word(node + 0x160, 0x60010000).Word(node + 0x164, 0x60010004).Word(0x60010000, page);
        Assert.Contains("Equipment. Crono LV 1", new ShopCapture(memory).Capture(oldImage, node)!.Text);
        memory.Word(page + 0x16C, 0);
        Assert.Null(new ShopCapture(memory).Capture(oldImage, node));
    }

    internal static (NavigationMemory Memory, uint Image, uint Node) Frame(string name)
    {
        using var stream = typeof(ShopCaptureTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Native.Tests.Capture.shop-native-0325.json")!;
        using var json = JsonDocument.Parse(stream);
        var frame = json.RootElement.GetProperty("frames").GetProperty(name);
        var memory = new NavigationMemory();
        foreach (var segment in frame.GetProperty("segments").EnumerateObject())
            memory.Add(uint.Parse(segment.Name.Split(':')[0], NumberStyles.HexNumber), Convert.FromHexString(segment.Value.GetString()!));
        return (memory, frame.GetProperty("imageBase").GetUInt32(), frame.GetProperty("node").GetUInt32());
    }
    private static uint Word(NavigationMemory memory, uint address)
    {
        Span<byte> data = stackalloc byte[4];
        Assert.True(memory.TryRead(address, data));
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data);
    }
}
