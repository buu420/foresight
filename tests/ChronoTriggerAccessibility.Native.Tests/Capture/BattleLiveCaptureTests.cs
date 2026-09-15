using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class BattleLiveCaptureTests
{
    private static (NavigationMemory Memory, uint Image, uint Menu, uint Canvas) Frame(string name)
    {
        using var stream = typeof(BattleLiveCaptureTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Native.Tests.Capture.battle-native-0322.json")!;
        using var json = JsonDocument.Parse(stream);
        var frame = json.RootElement.GetProperty("frames").GetProperty(name);
        var memory = new NavigationMemory();
        foreach (var segment in frame.GetProperty("segments").EnumerateObject())
            memory.Add(uint.Parse(segment.Name.Split(':')[0], NumberStyles.HexNumber),
                Convert.FromHexString(segment.Value.GetString()!));
        var image = frame.GetProperty("imageBase").GetUInt32();
        Span<byte> pointer = stackalloc byte[4];
        Assert.True(memory.TryRead(image + BattleCapture.CanvasGlobalRva, pointer));
        return (memory, image, frame.GetProperty("menu").GetUInt32(),
            BinaryPrimitives.ReadUInt32LittleEndian(pointer));
    }

    [Fact]
    public void TheObservedEmptyItemListAnnouncesItsState()
    {
        var (memory, image, menu, _) = Frame("item_empty");
        var result = new BattleCapture(memory).Capture(image, menu);
        Assert.NotNull(result);
        Assert.Equal("item:empty:0", result.FocusIdentity);
        Assert.Equal("Crono: Item. Empty.", result.FocusText);
        Assert.Equal(43, Assert.Single(result.Party).Hp);
    }

    [Fact]
    public void TheObservedTechMenuReadsTheRenderedNameAndMpCost()
    {
        var (memory, image, menu, _) = Frame("tech_cyclone");
        var result = new BattleCapture(memory).Capture(image, menu);
        Assert.Equal("tech:0:0:1", result!.FocusIdentity);
        Assert.Equal("Crono: Cyclone, 2 MP", result.FocusText);
    }

    [Fact]
    public void TheObservedCycloneTargetGroupSurvivesTheMenuFix()
    {
        var (memory, image, menu, _) = Frame("tech_targets");
        var result = new BattlePresentationCapture(memory).ReadTargets(image, menu);
        Assert.True(result!.IsTargeting);
        Assert.Equal([5, 6, 9], result.Slots);
    }

    [Theory]
    [InlineData(0x19E78u, 255u)] // no acting member: the game closed the list
    [InlineData(0x19EECu, 1u)] // target selection, not the list
    [InlineData(0x19E90u, 0u)] // canvas and rendered panel disagree
    [InlineData(0x1ADB0u, uint.MaxValue)] // invalid count is not empty
    [InlineData(0x19EB8u, 1u)] // stale cursor from a populated list
    [InlineData(0x19EBCu, 6u)] // stale page from a populated list
    public void AnEmptyCountDoesNotOverrideClosedOrUnsettledPanels(uint offset, uint value)
    {
        var (memory, image, menu, canvas) = Frame("item_empty");
        memory.Word(canvas + offset, value);
        Assert.Null(new BattleCapture(memory).Capture(image, menu)!.FocusIdentity);
    }

    [Fact]
    public void AZeroCountWithARetainedRenderedRowCannotAnnounceEmpty()
    {
        var (memory, image, menu, canvas) = Frame("item_empty");
        memory.Word(canvas + BattleCapture.ItemIdOffset, 1);
        memory.Word(canvas + BattleCapture.ItemIdOffset + BattleCapture.ItemQuantityOffset, 1);
        Assert.Null(new BattleCapture(memory).Capture(image, menu)!.FocusIdentity);
    }

    [Fact]
    public void ACountChangingDuringCaptureCannotAnnounceEmpty()
    {
        var (memory, image, menu, canvas) = Frame("item_empty");
        var reads = 0;
        memory.BeforeRead = address =>
        {
            if (address == canvas + BattleCapture.ItemCountOffset && ++reads == 2)
                memory.Word(address, 1);
        };
        Assert.Null(new BattleCapture(memory).Capture(image, menu)!.FocusIdentity);
    }
}
