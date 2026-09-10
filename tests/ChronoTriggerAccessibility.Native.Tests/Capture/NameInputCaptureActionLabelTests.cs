using System.Buffers.Binary;
using System.Text;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

/// <summary>
/// Pins the Name Entry localized-label contract and the point at which a missing
/// grid action label is allowed to be missing.
///
/// Native evidence (see <c>docs/native-audits/2026-09-09-name-entry-labels.md</c>):
/// the grid action label is (0x42,0x08), requested by the grid refresh callback at
/// <c>RefreshBodyRva</c> for row 7 column 10 only. Defaults (0x41,0x35) and Accept
/// (0x41,0x08) come from the two-entry id table at RVA 0x3B715C. Because the refresh
/// runs after <c>NameInputScene::init</c> — and the grid starts inactive on page -1 —
/// a capture can legitimately precede the label, so the capture must degrade to
/// exactly the one cell that needs it rather than failing the whole screen.
/// </summary>
public sealed class NameInputCaptureActionLabelTests
{
    private const nuint ImageBase = 0x400000;
    private const nuint Scene = 0x10000;

    [Fact]
    public void LocalizedLabelKeysMatchTheAuditedNativeRequests()
    {
        // Grid action: bytes 6A 08 6A 42 at RVA 0x2C2A8A inside the refresh callback,
        // reached only when the row/column loop is at row 7 column 10.
        Assert.Equal(new LocalizedMessageKey(0x42, 0x08), NameInputCapture.GridActionTextKey);

        // RVA 0x3B715C holds exactly { 0x35, 0x08 }, requested from bank 0x41 by
        // the button loop in the Name Entry builder at RVA 0x2C08D0.
        Assert.Equal(new LocalizedMessageKey(0x41, 0x35), NameInputCapture.DefaultsTextKey);
        Assert.Equal(new LocalizedMessageKey(0x41, 0x08), NameInputCapture.AcceptTextKey);

        // The refresh callback the label arrives from is already part of the contract.
        Assert.Equal(0x2C2A20u, NameInputCapture.RefreshBodyRva);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingActionLabelStillCapturesEveryCellThatDoesNotNeedIt(string? actionLabel)
    {
        // The screen opens with the grid inactive on page -1, and the label only
        // arrives on a later refresh, so every one of these states is reachable
        // before the label exists. None of them needs it to describe itself.
        var initial = CreateValidMemory("Crono", -1, 99, 99, 1, string.Empty);
        Assert.True(NameInputCapture.TryCreateSnapshot(
            initial, ImageBase, Scene, actionLabel, out var initialSnapshot, out var initialError),
            initialError);
        Assert.Equal("Crono", initialSnapshot.Name);
        Assert.Null(initialSnapshot.FocusedCell);

        var glyph = CreateValidMemory("Crono", 0, 0, 0, 1, "A");
        Assert.True(NameInputCapture.TryCreateSnapshot(
            glyph, ImageBase, Scene, actionLabel, out var glyphSnapshot, out var glyphError), glyphError);
        Assert.Equal("Crono", glyphSnapshot.Name);
        Assert.Equal(NameGridCellKind.Glyph, glyphSnapshot.FocusedCell!.Kind);
        Assert.Equal("A", glyphSnapshot.FocusedCell.Label);

        var delete = CreateValidMemory("Crono", 0, 6, 10, 1, string.Empty);
        Assert.True(NameInputCapture.TryCreateSnapshot(
            delete, ImageBase, Scene, actionLabel, out var deleteSnapshot, out var deleteError), deleteError);
        Assert.Equal("Delete", deleteSnapshot.FocusedCell!.Label);

        var inactive = CreateValidMemory("Crono", 0, 99, 99, 1, string.Empty, active: 0);
        Assert.True(NameInputCapture.TryCreateSnapshot(
            inactive, ImageBase, Scene, actionLabel, out var inactiveSnapshot, out var inactiveError),
            inactiveError);
        Assert.False(inactiveSnapshot.GridActive);
        Assert.Null(inactiveSnapshot.FocusedCell);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MissingActionLabelFailsClosedOnlyWhenThatCellIsFocused(int page)
    {
        // Row 7 column 10 is the one cell the refresh callback labels from
        // (0x42,0x08). Announcing it without that label would tell the player less
        // than the screen shows, so this is where a missing capture must stop.
        var memory = CreateValidMemory("Crono", page, 7, 10, 0, string.Empty);

        Assert.False(NameInputCapture.TryCreateSnapshot(
            memory, ImageBase, Scene, null, out var snapshot, out var error));
        Assert.Null(snapshot);
        Assert.Contains("localized action cell", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"({page},7,10)", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SuppliedActionLabelStillNamesTheLocalizedActionCell(int page)
    {
        var memory = CreateValidMemory("Crono", page, 7, 10, 0, string.Empty);

        Assert.True(NameInputCapture.TryCreateSnapshot(
            memory, ImageBase, Scene, "End", out var snapshot, out var error), error);
        Assert.Equal(NameGridCellKind.LocalizedAction, snapshot.FocusedCell!.Kind);
        Assert.Equal("End", snapshot.FocusedCell.Label);
    }

    private static TestMemory CreateValidMemory(
        string name,
        int page,
        int row,
        int column,
        int language,
        string gridText,
        byte active = 1)
    {
        var memory = new TestMemory()
            .AddByte(Scene + NameInputCapture.ActiveOffset, active)
            .AddInt32(Scene + NameInputCapture.PageOffset, page)
            .AddInt32(Scene + NameInputCapture.ColumnOffset, column)
            .AddInt32(Scene + NameInputCapture.RowOffset, row)
            .AddPointer(Scene + NameInputCapture.GlyphAppendTargetOffset, 0x60000)
            .AddPointer(Scene + NameInputCapture.DeleteTargetOffset, 0x60100)
            .AddPointer(Scene + NameInputCapture.RefreshTargetOffset, 0x60200)
            .AddPointer(0x60000, 0x61000)
            .AddPointer(0x61000 + 8, ImageBase + NameInputCapture.GlyphAppendInvokeRva)
            .AddPointer(0x60100, 0x61100)
            .AddPointer(0x61100 + 8, ImageBase + NameInputCapture.DeleteInvokeRva)
            .AddPointer(0x60200, 0x61200)
            .AddPointer(0x61200 + 8, ImageBase + NameInputCapture.RefreshInvokeRva)
            .AddInt32(ImageBase + NameInputCapture.LanguageGlobalRva, language)
            .AddInlineMsvcString(Scene + NameInputCapture.NameOffset, name);

        if (active != 0 && page is >= 0 and <= 2 && row is >= 0 and <= 7 && column is >= 0 and <= 10)
        {
            var index = (row + page * 8) * 11 + column;
            memory
                .AddPointer(ImageBase + NameInputCapture.GridPointerTableRva + (nuint)(index * 4), 0x70000)
                .AddCString(0x70000, gridText);
        }

        return memory;
    }

    private sealed class TestMemory : IReadableMemory
    {
        private readonly Dictionary<nuint, byte[]> segments = [];

        public TestMemory AddByte(nuint address, byte value)
        {
            segments[address] = [value];
            return this;
        }

        public TestMemory AddInt32(nuint address, int value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            segments[address] = bytes;
            return this;
        }

        public TestMemory AddPointer(nuint address, nuint value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, checked((uint)value));
            segments[address] = bytes;
            return this;
        }

        public TestMemory AddInlineMsvcString(nuint address, string value)
        {
            var encoded = Encoding.UTF8.GetBytes(value);
            Assert.True(encoded.Length < 16);
            var layout = new byte[MsvcStringReader.LayoutSize];
            encoded.CopyTo(layout, 0);
            layout[encoded.Length] = 0;
            BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x10), (uint)encoded.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x14), 15);
            segments[address] = layout;
            return this;
        }

        public TestMemory AddCString(nuint address, string value)
        {
            segments[address] = [.. Encoding.UTF8.GetBytes(value), 0];
            return this;
        }

        public bool TryRead(nuint address, Span<byte> destination)
        {
            foreach (var (start, bytes) in segments)
            {
                if (address < start || address + (nuint)destination.Length > start + (nuint)bytes.Length)
                {
                    continue;
                }

                bytes.AsSpan(checked((int)(address - start)), destination.Length).CopyTo(destination);
                return true;
            }
            return false;
        }
    }
}
