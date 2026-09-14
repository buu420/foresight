using System.Globalization;
using System.Text.Json;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

/// <summary>Only bytes read by the capture, retained from read-only process snapshots on
/// 2026-09-14. These retain the actual ASLR base and retail control/Label layout.</summary>
public sealed class FieldSubmenuLiveCaptureTests
{
    private static (NavigationMemory Memory, uint Image, uint Node) Frame(string name)
    {
        using var stream = typeof(FieldSubmenuLiveCaptureTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Native.Tests.Capture.field-submenu-native-0319.json")!;
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
}
