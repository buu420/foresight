using System.Buffers.Binary;
using System.Text;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;
using static ChronoTriggerAccessibility.Native.Tests.Capture.SettingsFixtureEncoding;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class SteamSettingsCaptureTests
{
    private const nuint ImageBase = 0x400000;

    [Fact]
    public void CapturesTitleCategoryModeWithFourCategoriesAndTwoOrdinaryDescriptors()
    {
        var fixture = new SteamFixture(context: 1, categoryMode: true, categoryKey: 3);

        Assert.True(fixture.TryCapture(out var snapshot, out var diagnostic), diagnostic);
        Assert.Equal(SteamSettingsContext.Title, snapshot.Context);
        Assert.Equal(SteamSettingsMode.Categories, snapshot.Mode);
        Assert.Equal(4, snapshot.Categories.Count);
        Assert.Equal(2, snapshot.DescriptorCount);
        Assert.Equal(
            [SteamSettingsCategoryKind.OrdinaryPage, SteamSettingsCategoryKind.OrdinaryPage,
                SteamSettingsCategoryKind.SpecialAction, SteamSettingsCategoryKind.SpecialAction],
            snapshot.Categories.Select(category => category.Kind));
        Assert.Equal(SteamSettingsFocusKind.Category, snapshot.FocusKind);
        Assert.Equal(3, snapshot.FocusedCategoryIndex);
        Assert.Equal("Cat 3", snapshot.Categories[3].Label);
        Assert.Null(snapshot.Page);
    }

    [Fact]
    public void CapturesInGamePageWithValueActionAndReturnControls()
    {
        var fixture = new SteamFixture(
            context: 0,
            rows:
            [
                new("Window", ["Choose window"], ["Windowed", "Fullscreen"], 1),
                new("Rebind", ["Open controls"], [], null),
            ],
            focusKey: 10,
            activePage: 1);

        Assert.True(fixture.TryCapture(out var snapshot, out var diagnostic), diagnostic);
        Assert.Equal(SteamSettingsContext.InGame, snapshot.Context);
        Assert.Equal(SteamSettingsMode.Page, snapshot.Mode);
        Assert.Equal(6, snapshot.Categories.Count);
        Assert.Equal(4, snapshot.DescriptorCount);
        Assert.NotNull(snapshot.Page);
        Assert.Equal(1, snapshot.Page.Index);
        Assert.Equal(2, snapshot.Page.Rows.Count);
        Assert.Equal(SteamSettingsRowKind.Value, snapshot.Page.Rows[0].Kind);
        Assert.Equal(1, snapshot.Page.Rows[0].SelectedIndex);
        Assert.Equal("Fullscreen", snapshot.Page.Rows[0].Value);
        Assert.Equal(SteamFixture.RowsAddress + 0x58u, snapshot.Page.Rows[0].SetterAddress);
        Assert.Equal(SteamSettingsRowKind.Action, snapshot.Page.Rows[1].Kind);
        Assert.Null(snapshot.Page.Rows[1].SelectedIndex);
        Assert.Null(snapshot.Page.Rows[1].Value);
        Assert.Null(snapshot.Page.Rows[1].SetterAddress);
        Assert.Equal(SteamSettingsFocusKind.Row, snapshot.FocusKind);
        Assert.Equal(1, snapshot.FocusedRowIndex);
        Assert.Equal(0, snapshot.FocusedSubcontrol);
        Assert.Equal(20, snapshot.Page.ReturnControl.Key);
        Assert.Equal("Cat 1", snapshot.Page.ReturnControl.Label);
        Assert.Equal("Cat help 1", snapshot.Page.ReturnControl.Help);
    }

    [Fact]
    public void CapturesReturnToCategoriesFocusAtTheRowCountKey()
    {
        var fixture = new SteamFixture(rows:
        [
            new("First", ["First help"], ["Same", "Same"], 0),
            new("Second", ["Second help"], [], null),
        ], focusKey: 20);

        Assert.True(fixture.TryCapture(out var snapshot, out var diagnostic), diagnostic);
        Assert.Equal(SteamSettingsFocusKind.ReturnToCategories, snapshot.FocusKind);
        Assert.Null(snapshot.FocusedRowIndex);
        Assert.Equal(20, snapshot.FocusKey);
    }

    [Fact]
    public void CorrelatesDuplicateDisplayedTextOnlyByTheAuditedLiveSourceAddress()
    {
        var fixture = new SteamFixture(rows:
        [
            new("Duplicate", ["First", "Second"], ["Same", "Same"], 1),
        ]);

        Assert.True(fixture.TryCapture(out var snapshot, out var diagnostic), diagnostic);
        var row = Assert.Single(snapshot.Page!.Rows);
        Assert.Equal(1, row.SelectedIndex);
        Assert.Equal("Same", row.Value);
        Assert.Equal(fixture.Observations[0].ValueSourceAddress, row.SelectedValueSourceAddress);
    }

    [Fact]
    public void RejectsMissingMisalignedWrongGenerationDuplicateAndActionValueObservations()
    {
        AssertSteamFailure(new SteamFixture(), fixture => fixture.Observations.Clear(), "missing");
        AssertSteamFailure(new SteamFixture(), fixture =>
            fixture.Observations[0] = fixture.Observations[0] with
            {
                ValueSourceAddress = fixture.Observations[0].ValueSourceAddress + 1,
            }, "unaligned source");
        AssertSteamFailure(new SteamFixture(), fixture =>
            fixture.Observations[0] = fixture.Observations[0] with { RootAddress = SteamFixture.RootAddress + 4 }, "wrong root");
        AssertSteamFailure(new SteamFixture(), fixture =>
            fixture.Observations[0] = fixture.Observations[0] with { PageIndex = 1 }, "wrong page");
        AssertSteamFailure(new SteamFixture(), fixture =>
            fixture.Observations[0] = fixture.Observations[0] with { RowAddress = SteamFixture.RowsAddress + 0x88u }, "wrong row");
        AssertSteamFailure(new SteamFixture(), fixture => fixture.Observations.Add(fixture.Observations[0]), "duplicate");

        var action = new SteamFixture(rows: [new("Action", ["Open"], [], null)]);
        action.Observations.Add(new(
            SteamFixture.RootAddress,
            0,
            SteamFixture.RowsAddress,
            SteamFixture.RowsAddress + 0x24u));
        AssertSteamFailure(action, _ => { }, "action observation");
    }

    [Fact]
    public void RequiresTheExactAuditedCategoryAndDescriptorCountsForEachContext()
    {
        Assert.True(new SteamFixture(context: 1, categoryMode: true).TryCapture(out _, out var titleError), titleError);
        Assert.True(new SteamFixture(context: 0, categoryMode: true).TryCapture(out _, out var gameError), gameError);
        AssertSteamFailure(new SteamFixture(context: 1, categoryMode: true, categoryCount: 6, descriptorCount: 4), _ => { }, "title counts");
        AssertSteamFailure(new SteamFixture(context: 0, categoryMode: true, categoryCount: 4, descriptorCount: 2), _ => { }, "game counts");
    }

    [Fact]
    public void RejectsWrongRootManagerIdentityContextModeAndPageState()
    {
        AssertSteamFailure(new SteamFixture(), fixture => WritePointer(fixture.Root, 0, ImageBase + TouchSettingsCapture.RootVtableRva), "touch root");
        AssertSteamFailure(new SteamFixture(), fixture => WriteInt32(fixture.Root, 0x2F8, -1), "negative context");
        AssertSteamFailure(new SteamFixture(), fixture => WriteInt32(fixture.Root, 0x2F8, 2), "unknown context");
        AssertSteamFailure(new SteamFixture(), fixture => fixture.Root[0x314] = 2, "unknown mode");
        AssertSteamFailure(new SteamFixture(), fixture => WriteInt32(fixture.Root, 0x33C, 4), "page outside descriptors");
        AssertSteamFailure(new SteamFixture(), fixture => WritePointer(fixture.Root, 0x328, 0), "null active root");
        AssertSteamFailure(new SteamFixture(), fixture => WritePointer(fixture.CategoryManager, 0, ImageBase + 0x10), "category manager vtable");
        AssertSteamFailure(new SteamFixture(), fixture => WritePointer(fixture.ActiveManager, 0, ImageBase + 0x10), "active manager vtable");
        AssertSteamFailure(new SteamFixture(), fixture => WriteInt32(fixture.ActiveManager, 0x2C4, 99), "manager focus mismatch");
    }

    [Fact]
    public void RejectsMalformedOrUnreadableSteamVectors()
    {
        AssertSteamFailure(new SteamFixture(), fixture => WriteRawVector(fixture.Root, 0x2FC, 0, 0x2000C, 0x2000C), "descriptor null begin");
        AssertSteamFailure(new SteamFixture(), fixture => WriteRawVector(fixture.Root, 0x2FC, 0x2000C, 0x20000, 0x2000C), "descriptor reversed");
        AssertSteamFailure(new SteamFixture(), fixture => WriteRawVector(fixture.Root, 0x308, 0x21000, 0x21030, 0x21020), "category capacity");
        AssertSteamFailure(new SteamFixture(), fixture => WriteRawVector(fixture.Descriptors, 0, 0x30001, 0x30089, 0x30089), "row alignment");
        AssertSteamFailure(new SteamFixture(), fixture => WriteVector(fixture.Descriptors, 0, SteamFixture.RowsAddress, 0), "empty active rows");
        AssertSteamFailure(new SteamFixture(), fixture => WriteRawVector(fixture.Root, 0x32C, 0x51000, 0x51001, 0x51004), "manager stride");
        AssertSteamFailure(new SteamFixture(), fixture => fixture.Memory.Remove(SteamFixture.CategoriesAddress), "categories unreadable");
        AssertSteamFailure(new SteamFixture(), fixture => fixture.Memory.Remove(SteamFixture.RowsAddress), "rows unreadable");
        AssertSteamFailure(new SteamFixture(), fixture => fixture.Memory.Remove(SteamFixture.ActiveManagerAddress), "manager unreadable");
    }

    [Fact]
    public void RejectsBlankMalformedUnreadableAndInconsistentLocalizedSteamText()
    {
        AssertSteamFailure(new SteamFixture(), fixture => WriteInlineString(fixture.Categories, 0, " "), "category label blank");
        AssertSteamFailure(new SteamFixture(), fixture => WriteInlineString(fixture.Categories, 0x18, " "), "category help blank");
        AssertSteamFailure(new SteamFixture(), fixture => WriteInlineString(fixture.Rows, 0, " "), "row label blank");
        AssertSteamFailure(new SteamFixture(), fixture => WriteInvalidInlineString(fixture.Rows, 0), "row label malformed");
        AssertSteamFailure(new SteamFixture(), fixture => fixture.Memory.Remove(fixture.HelpAddresses[0]), "help unreadable");
        AssertSteamFailure(new SteamFixture(), fixture => WriteInlineString(fixture.HelpBlocks[0], 0, " "), "help blank");
        AssertSteamFailure(new SteamFixture(), fixture => WriteInlineString(fixture.ValueBlocks[0], 0, " "), "value blank");
        AssertSteamFailure(new SteamFixture(rows: [new("Bad help", ["One", "Two"], ["A", "B", "C"], 0)]), _ => { }, "help count mismatch");
    }

    [Fact]
    public void RejectsZeroOverflowThrowingAndCrossFamilyInputs()
    {
        var fixture = new SteamFixture();
        var aboveX86 = unchecked((nuint)uint.MaxValue + (nuint)1);
        AssertSteamCallFailure(null, ImageBase, SteamFixture.RootAddress, fixture.Observations, "null memory");
        AssertSteamCallFailure(fixture.Memory, 0, SteamFixture.RootAddress, fixture.Observations, "zero base");
        AssertSteamCallFailure(fixture.Memory, uint.MaxValue, SteamFixture.RootAddress, fixture.Observations, "base overflow");
        AssertSteamCallFailure(fixture.Memory, ImageBase, 0, fixture.Observations, "zero root");
        AssertSteamCallFailure(fixture.Memory, ImageBase, aboveX86, fixture.Observations, "root above x86");
        AssertSteamCallFailure(fixture.Memory, ImageBase, (nuint)uint.MaxValue - 0x33Eu, fixture.Observations, "root range overflow");
        AssertSteamCallFailure(new ThrowingMemory(), ImageBase, SteamFixture.RootAddress, fixture.Observations, "throwing memory");
        AssertSteamFailure(new SteamFixture(), current => WriteRawVector(current.Root, 0x2FC, 0xFFFF_FFF8, 0xFFFF_FFFC, 0xFFFF_FFFC), "descriptor overflow");
        AssertSteamFailure(new SteamFixture(), current => WriteRawVector(current.Descriptors, 0, 0xFFFF_FF80, 0xFFFF_FFF8, 0xFFFF_FFF8), "row overflow");

        var touch = new TouchFixture();
        AssertSteamCallFailure(touch.Memory, ImageBase, TouchFixture.RootAddress, [], "touch root in Steam API");
    }

    [Fact]
    public void RevalidatesSteamIdentityAndCapturedVectorGeneration()
    {
        var rootMutation = new SteamFixture();
        var rootMemory = new AfterReadMutationMemory(rootMutation.Memory, SteamFixture.RowsAddress, () => rootMutation.Root[0x314] = 1);
        AssertSteamCallFailure(rootMemory, ImageBase, SteamFixture.RootAddress, rootMutation.Observations, "root mutation");

        var categoryMutation = new SteamFixture();
        var categoryMemory = new AfterReadMutationMemory(categoryMutation.Memory, SteamFixture.CategoriesAddress, () => categoryMutation.Categories[0] ^= 0x20);
        AssertSteamCallFailure(categoryMemory, ImageBase, SteamFixture.RootAddress, categoryMutation.Observations, "category mutation");

        var valueMutation = new SteamFixture();
        var valueMemory = new AfterReadMutationMemory(valueMutation.Memory, valueMutation.ValueAddresses[0], () => valueMutation.ValueBlocks[0][0] ^= 0x20);
        AssertSteamCallFailure(valueMemory, ImageBase, SteamFixture.RootAddress, valueMutation.Observations, "value generation mutation");
    }

    [Fact]
    public void SteamSnapshotDefensivelyCopiesAndNeverDereferencesNativeFunctionTargets()
    {
        var fixture = new SteamFixture(rows: [new("Video", ["Video help"], ["Low", "High"], 1)]);
        const nuint getterTarget = 0xA1000;
        const nuint setterTarget = 0xA2000;
        WritePointer(fixture.Rows, 0x54, getterTarget);
        WritePointer(fixture.Rows, 0x7C, setterTarget);
        fixture.Memory.Forbid(getterTarget).Forbid(setterTarget);

        Assert.True(fixture.TryCapture(out var snapshot, out var diagnostic), diagnostic);
        fixture.Observations.Clear();
        Array.Fill(fixture.Root, (byte)0);
        Array.Fill(fixture.Categories, (byte)0);
        Array.Fill(fixture.Rows, (byte)0);
        Array.Fill(fixture.HelpBlocks[0], (byte)0);
        Array.Fill(fixture.ValueBlocks[0], (byte)0);

        Assert.Equal("Video", snapshot.Page!.Rows[0].Label);
        Assert.Equal("Video help", snapshot.Page.Rows[0].HelpTexts[0]);
        Assert.Equal("High", snapshot.Page.Rows[0].Value);
        Assert.Equal("Cat 0", snapshot.Categories[0].Label);
        Assert.DoesNotContain(fixture.Memory.Reads, read => read.Address == getterTarget || read.Address == setterTarget);
    }

    [Fact]
    public void LegacyMixedSettingsCaptureApiDoesNotExist()
    {
        Assert.Null(typeof(SteamSettingsCapture).Assembly.GetType("ChronoTriggerAccessibility.Native.Capture.SettingsCapture"));
        Assert.Null(typeof(SteamSettingsCapture).Assembly.GetType("ChronoTriggerAccessibility.Native.Capture.SettingsSnapshot"));
    }

    private static void AssertSteamFailure(SteamFixture fixture, Action<SteamFixture> mutate, string because)
    {
        mutate(fixture);
        Assert.False(fixture.TryCapture(out var snapshot, out var diagnostic), because);
        Assert.Null(snapshot);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic));
    }

    private static void AssertSteamCallFailure(
        IReadableMemory? memory,
        nuint imageBase,
        nuint root,
        IReadOnlyList<SteamSettingsValueObservation>? observations,
        string because)
    {
        Assert.False(SteamSettingsCapture.TryCreateSnapshot(memory, imageBase, root, observations, out var snapshot, out var diagnostic), because);
        Assert.Null(snapshot);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic));
    }
}

public sealed class TouchSettingsCaptureTests
{
    private const nuint ImageBase = 0x400000;

    [Fact]
    public void CapturesExactPagerAndAllRowTypesWhileAcceptingMinusOneFocus()
    {
        var fixture = new TouchFixture(
            rows:
            [
                new(0, "Toggle", "Toggle help", ["Off", "On"], 1),
                new(1, "Resolution", "Resolution help", ["800x600", "1280x720", "1920x1080"], 2),
                new(2, "Bindings", "Bindings help", ["Pad", "Keys"], 0),
            ],
            focusedKey: -1,
            activePage: 2);

        Assert.True(fixture.TryCapture(out var snapshot, out var diagnostic), diagnostic);
        Assert.Equal(4, snapshot.PageCount);
        Assert.Equal(2, snapshot.ActivePageIndex);
        Assert.False(snapshot.IsTransitioning);
        Assert.Equal(3, snapshot.Rows.Count);
        Assert.Equal([0, 1, 2], snapshot.Rows.Select(row => row.UiType));
        Assert.Equal("1920x1080", snapshot.Rows[1].Value);
        Assert.Equal(TouchSettingsFocusKind.None, snapshot.FocusKind);
        Assert.Equal(-1, snapshot.FocusedKey);
        Assert.Equal(3, snapshot.ControlGroupCount);
        Assert.Equal([1001, 1002], snapshot.SpecialControls.Select(control => control.Key));
    }

    [Theory]
    [InlineData(0, 1, 2)]
    [InlineData(1, 2, 1)]
    [InlineData(2, 1, 1)]
    public void DecodesValidNormalFocusKeysByFourForEveryUiType(int uiType, int selectedIndex, int focusedKey)
    {
        var values = uiType == 1 ? new[] { "Low", "Mid", "High" } : new[] { "Off", "On" };
        var fixture = new TouchFixture(rows: [new(uiType, "Setting", "Help", values, selectedIndex)], focusedKey: focusedKey);

        Assert.True(fixture.TryCapture(out var snapshot, out var diagnostic), diagnostic);
        Assert.Equal(TouchSettingsFocusKind.Row, snapshot.FocusKind);
        Assert.Equal(0, snapshot.FocusedRowIndex);
        Assert.Equal(focusedKey, snapshot.FocusedSubcontrol);
        Assert.Equal(selectedIndex, snapshot.Rows[0].SelectedIndex);
    }

    [Theory]
    [InlineData(1001)]
    [InlineData(1002)]
    public void ModelsAuditedPermanentSpecialControlsSeparatelyFromRows(int key)
    {
        var fixture = new TouchFixture(focusedKey: key);

        Assert.True(fixture.TryCapture(out var snapshot, out var diagnostic), diagnostic);
        Assert.Equal(TouchSettingsFocusKind.SpecialControl, snapshot.FocusKind);
        Assert.Equal(key, snapshot.FocusedSpecialKey);
        Assert.Contains(snapshot.SpecialControls, control => control.Key == key && !string.IsNullOrWhiteSpace(control.Label));
    }

    [Fact]
    public void ModelsConditionalKeyOneThousandOnlyWithAnExplicitCapturedLabel()
    {
        var fixture = new TouchFixture(focusedKey: 1000, includeConditional1000: true);

        Assert.True(fixture.TryCapture(out var snapshot, out var diagnostic), diagnostic);
        Assert.Equal(1000, snapshot.FocusedSpecialKey);
        Assert.Equal("Observed 1000", snapshot.SpecialControls.Single(control => control.Key == 1000).Label);

        fixture = new TouchFixture(focusedKey: 1000, includeConditional1000: true);
        fixture.SpecialControls.RemoveAll(control => control.Key == 1000);
        AssertTouchFailure(fixture, _ => { }, "unlabeled key 1000");
    }

    [Fact]
    public void RejectsInvalidSpecialControlPointersKeysLabelsAndCorrelation()
    {
        AssertTouchFailure(new TouchFixture(), fixture => WritePointer(fixture.Root, 0x2DC, 0), "required 1001 pointer");
        AssertTouchFailure(new TouchFixture(), fixture => fixture.SpecialControls.RemoveAll(control => control.Key == 1001), "missing 1001 observation");
        AssertTouchFailure(new TouchFixture(), fixture => fixture.SpecialControls[0] = fixture.SpecialControls[0] with { Label = " " }, "blank label");
        AssertTouchFailure(new TouchFixture(), fixture => fixture.SpecialControls[0] = fixture.SpecialControls[0] with { ControlAddress = 0xDEAD }, "wrong pointer");
        AssertTouchFailure(new TouchFixture(), fixture => fixture.SpecialControls[0] = fixture.SpecialControls[0] with { Key = 1002 }, "duplicate key");
        AssertTouchFailure(new TouchFixture(focusedKey: 1000), _ => { }, "1000 without pointer");
        AssertTouchFailure(new TouchFixture(focusedKey: 1003), _ => { }, "unknown special key");
    }

    [Fact]
    public void StableCaptureRejectsTransitionAndAuditedTransitionCaptureAcceptsOnlyZeroOrOne()
    {
        var fixture = new TouchFixture(transition: 1);
        Assert.False(fixture.TryCapture(out var stable, out var stableDiagnostic));
        Assert.Null(stable);
        Assert.False(string.IsNullOrWhiteSpace(stableDiagnostic));

        Assert.True(fixture.TryTransitionCapture(out var transition, out var transitionDiagnostic), transitionDiagnostic);
        Assert.True(transition.IsTransitioning);

        fixture.Pager[0x2E1] = 2;
        Assert.False(fixture.TryTransitionCapture(out var invalid, out var invalidDiagnostic));
        Assert.Null(invalid);
        Assert.False(string.IsNullOrWhiteSpace(invalidDiagnostic));
    }

    [Fact]
    public void RejectsWrongTouchAndPagerIdentitiesAndInconsistentPageState()
    {
        AssertTouchFailure(new TouchFixture(), fixture => WritePointer(fixture.Root, 0, ImageBase + SteamSettingsCapture.RootVtableRva), "Steam root");
        AssertTouchFailure(new TouchFixture(), fixture => WritePointer(fixture.Pager, 0, ImageBase + 0x10), "pager vtable");
        AssertTouchFailure(new TouchFixture(), fixture => WriteInt32(fixture.Pager, 0x2D0, 3), "page count");
        AssertTouchFailure(new TouchFixture(), fixture => WriteInt32(fixture.Pager, 0x2D4, 1), "pager/root page mismatch");
        AssertTouchFailure(new TouchFixture(), fixture => WriteInt32(fixture.Root, 0x2E8, 4), "page outside descriptors");
        AssertTouchFailure(new TouchFixture(), fixture => WritePointer(fixture.Pager, 0x2D8, 0), "null active page root");
    }

    [Fact]
    public void RequiresFourDescriptorsAndMatchingReadableRowControlGroups()
    {
        AssertTouchFailure(new TouchFixture(), fixture => WriteVector(fixture.Root, 0x2C8, TouchFixture.DescriptorsAddress, 3 * 0x0C), "three descriptors");
        AssertTouchFailure(new TouchFixture(), fixture => WriteRawVector(fixture.Root, 0x2C8, 0, 0x13000C, 0x13000C), "null descriptors");
        AssertTouchFailure(new TouchFixture(), fixture => WriteRawVector(fixture.Descriptors, 0, 0x14001, 0x14099, 0x14099), "unaligned rows");
        AssertTouchFailure(new TouchFixture(), fixture => WriteVector(fixture.Descriptors, 0, TouchFixture.RowsAddress, 0), "empty rows");
        AssertTouchFailure(new TouchFixture(), fixture => WriteVector(fixture.Root, 0x2EC, TouchFixture.GroupsAddress, 0), "empty groups");
        AssertTouchFailure(new TouchFixture(rows: [TouchRowSpec.Default, TouchRowSpec.Default]), fixture =>
            WriteVector(fixture.Root, 0x2EC, TouchFixture.GroupsAddress, 0x10), "group count mismatch");
        AssertTouchFailure(new TouchFixture(), fixture => fixture.Memory.Remove(TouchFixture.PagerAddress), "pager unreadable");
        AssertTouchFailure(new TouchFixture(), fixture => fixture.Memory.Remove(TouchFixture.GroupsAddress), "groups unreadable");
    }

    [Fact]
    public void RejectsUnknownTypesInvalidSelectionsAndEveryInvalidNormalKeyCorrelation()
    {
        AssertTouchFailure(new TouchFixture(), fixture => WriteInt32(fixture.Rows, 0, -1), "negative type");
        AssertTouchFailure(new TouchFixture(), fixture => WriteInt32(fixture.Rows, 0, 3), "unknown type");
        AssertTouchFailure(new TouchFixture(), fixture => WriteInt32(fixture.Rows, 0x90, -1), "negative selection");
        AssertTouchFailure(new TouchFixture(), fixture => WriteInt32(fixture.Rows, 0x90, 2), "selection beyond values");
        AssertTouchFailure(new TouchFixture(focusedKey: -2), _ => { }, "negative key other than -1");
        AssertTouchFailure(new TouchFixture(focusedKey: 5), _ => { }, "row outside vector");
        AssertTouchFailure(new TouchFixture(focusedKey: 0), _ => { }, "type zero subcontrol zero");
        AssertTouchFailure(new TouchFixture(focusedKey: 3), _ => { }, "type zero wrong selected subcontrol");
        AssertTouchFailure(new TouchFixture(rows: [new(1, "Setting", "Help", ["A", "B"], 0)], focusedKey: 2), _ => { }, "type one wrong subcontrol");
        AssertTouchFailure(new TouchFixture(rows: [new(2, "Setting", "Help", ["A", "B"], 0)], focusedKey: 2), _ => { }, "type two wrong subcontrol");
        AssertTouchFailure(new TouchFixture(rows: [new(0, "Setting", "Help", ["Only"], 0)], focusedKey: 1), _ => { }, "type zero value count");
        AssertTouchFailure(new TouchFixture(rows: [new(2, "Setting", "Help", ["A", "B", "C"], 0)], focusedKey: 1), _ => { }, "type two value count");
    }

    [Fact]
    public void RejectsBlankMalformedAndUnreadableTouchTextAndValues()
    {
        AssertTouchFailure(new TouchFixture(), fixture => WriteInlineString(fixture.Rows, 0x04, " "), "blank label");
        AssertTouchFailure(new TouchFixture(), fixture => WriteInlineString(fixture.Rows, 0x1C, " "), "blank help");
        AssertTouchFailure(new TouchFixture(), fixture => WriteInvalidInlineString(fixture.Rows, 0x04), "malformed label");
        AssertTouchFailure(new TouchFixture(), fixture => fixture.Memory.Remove(fixture.ValueAddresses[0]), "unreadable values");
        AssertTouchFailure(new TouchFixture(), fixture => WriteInlineString(fixture.ValueBlocks[0], 0, " "), "blank value");
        AssertTouchFailure(new TouchFixture(rows: [new(1, "Setting", "Help", [], 0)]), _ => { }, "empty values");
        AssertTouchFailure(new TouchFixture(), fixture => fixture.SpecialControls[0] = fixture.SpecialControls[0] with { Label = "\t" }, "blank special label");
    }

    [Fact]
    public void RejectsZeroOverflowThrowingAndCrossFamilyTouchInputs()
    {
        var fixture = new TouchFixture();
        var aboveX86 = unchecked((nuint)uint.MaxValue + (nuint)1);
        AssertTouchCallFailure(null, ImageBase, TouchFixture.RootAddress, fixture.SpecialControls, false, "null memory");
        AssertTouchCallFailure(fixture.Memory, 0, TouchFixture.RootAddress, fixture.SpecialControls, false, "zero base");
        AssertTouchCallFailure(fixture.Memory, uint.MaxValue, TouchFixture.RootAddress, fixture.SpecialControls, false, "base overflow");
        AssertTouchCallFailure(fixture.Memory, ImageBase, 0, fixture.SpecialControls, false, "zero root");
        AssertTouchCallFailure(fixture.Memory, ImageBase, aboveX86, fixture.SpecialControls, false, "root above x86");
        AssertTouchCallFailure(fixture.Memory, ImageBase, (nuint)uint.MaxValue - 0x2FAu, fixture.SpecialControls, false, "root range overflow");
        AssertTouchCallFailure(new ThrowingMemory(), ImageBase, TouchFixture.RootAddress, fixture.SpecialControls, false, "throwing memory");
        AssertTouchFailure(new TouchFixture(), current => WritePointer(current.Root, 0x2D4, (nuint)uint.MaxValue - 0x2E0u), "pager overflow");
        AssertTouchFailure(new TouchFixture(), current => WriteRawVector(current.Rows, 0x34, 0xFFFF_FFF0, 0xFFFF_FFF8, 0xFFFF_FFF8), "values overflow");

        var steam = new SteamFixture();
        AssertTouchCallFailure(steam.Memory, ImageBase, SteamFixture.RootAddress, [], false, "Steam root in Touch API");
    }

    [Fact]
    public void RevalidatesTouchRootPagerRowsGroupsAndValueGeneration()
    {
        var rootMutation = new TouchFixture();
        var rootMemory = new AfterReadMutationMemory(rootMutation.Memory, TouchFixture.RowsAddress, () => WriteInt32(rootMutation.Root, 0x2E8, 1));
        AssertTouchCallFailure(rootMemory, ImageBase, TouchFixture.RootAddress, rootMutation.SpecialControls, false, "root mutation");

        var pagerMutation = new TouchFixture();
        var pagerMemory = new AfterReadMutationMemory(pagerMutation.Memory, TouchFixture.RowsAddress, () => pagerMutation.Pager[0x2E1] = 1);
        AssertTouchCallFailure(pagerMemory, ImageBase, TouchFixture.RootAddress, pagerMutation.SpecialControls, false, "pager mutation");

        var groupMutation = new TouchFixture();
        var groupMemory = new AfterReadMutationMemory(groupMutation.Memory, TouchFixture.GroupsAddress, () => groupMutation.Groups[0] = 1);
        AssertTouchCallFailure(groupMemory, ImageBase, TouchFixture.RootAddress, groupMutation.SpecialControls, false, "group mutation");

        var valueMutation = new TouchFixture();
        var valueMemory = new AfterReadMutationMemory(valueMutation.Memory, valueMutation.ValueAddresses[0], () => valueMutation.ValueBlocks[0][0] ^= 0x20);
        AssertTouchCallFailure(valueMemory, ImageBase, TouchFixture.RootAddress, valueMutation.SpecialControls, false, "value mutation");
    }

    [Fact]
    public void TouchSnapshotDefensivelyCopiesAndNeverDereferencesGetterOrSetterTargets()
    {
        var fixture = new TouchFixture(rows: [new(1, "Video", "Video help", ["Low", "High"], 1)]);
        const nuint getterTarget = 0xB1000;
        const nuint setterTarget = 0xB2000;
        WritePointer(fixture.Rows, 0x64, getterTarget);
        WritePointer(fixture.Rows, 0x8C, setterTarget);
        fixture.Memory.Forbid(getterTarget).Forbid(setterTarget);

        Assert.True(fixture.TryCapture(out var snapshot, out var diagnostic), diagnostic);
        fixture.SpecialControls.Clear();
        Array.Fill(fixture.Root, (byte)0);
        Array.Fill(fixture.Pager, (byte)0);
        Array.Fill(fixture.Rows, (byte)0);
        Array.Fill(fixture.ValueBlocks[0], (byte)0);

        Assert.Equal("Video", snapshot.Rows[0].Label);
        Assert.Equal("Video help", snapshot.Rows[0].Help);
        Assert.Equal("High", snapshot.Rows[0].Value);
        Assert.Equal("Confirm A", snapshot.SpecialControls.Single(control => control.Key == 1001).Label);
        Assert.DoesNotContain(fixture.Memory.Reads, read => read.Address == getterTarget || read.Address == setterTarget);
    }

    private static void AssertTouchFailure(TouchFixture fixture, Action<TouchFixture> mutate, string because)
    {
        mutate(fixture);
        Assert.False(fixture.TryCapture(out var snapshot, out var diagnostic), because);
        Assert.Null(snapshot);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic));
    }

    private static void AssertTouchCallFailure(
        IReadableMemory? memory,
        nuint imageBase,
        nuint root,
        IReadOnlyList<TouchSettingsSpecialControlObservation>? controls,
        bool transition,
        string because)
    {
        var succeeded = transition
            ? TouchSettingsCapture.TryCreateTransitionSnapshot(memory, imageBase, root, controls, out var snapshot, out var diagnostic)
            : TouchSettingsCapture.TryCreateSnapshot(memory, imageBase, root, controls, out snapshot, out diagnostic);
        Assert.False(succeeded, because);
        Assert.Null(snapshot);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic));
    }
}

internal sealed record SteamRowSpec(
    string Label,
    IReadOnlyList<string> Help,
    IReadOnlyList<string> Values,
    int? SelectedIndex);

internal sealed class SteamFixture
{
    internal const nuint RootAddress = 0x10000;
    internal const nuint DescriptorsAddress = 0x20000;
    internal const nuint CategoriesAddress = 0x21000;
    internal const nuint RowsAddress = 0x30000;
    internal const nuint CategoryManagerAddress = 0x40000;
    internal const nuint ActiveManagerPointersAddress = 0x41000;
    internal const nuint ActiveManagerAddress = 0x42000;
    internal const nuint ActiveRootAddress = 0x43000;

    private const nuint ImageBase = 0x400000;

    public SteamFixture(
        int context = 1,
        bool categoryMode = false,
        IReadOnlyList<SteamRowSpec>? rows = null,
        int focusKey = 0,
        int activePage = 0,
        int categoryKey = 0,
        int? categoryCount = null,
        int? descriptorCount = null)
    {
        rows ??= [new("Display", ["Choose display"], ["Low", "High"], 0)];
        categoryCount ??= context == 0 ? 6 : 4;
        descriptorCount ??= context == 0 ? 4 : 2;
        Root = new byte[0x340];
        Descriptors = new byte[descriptorCount.Value * 0x0C];
        Categories = new byte[categoryCount.Value * 0x30];
        Rows = new byte[rows.Count * 0x88];
        CategoryManager = new byte[0x2C8];
        ActiveManager = new byte[0x2C8];
        ActiveManagerPointers = new byte[4];

        WritePointer(Root, 0, ImageBase + SteamSettingsCapture.RootVtableRva);
        WriteInt32(Root, 0x2F8, context);
        WriteVector(Root, 0x2FC, DescriptorsAddress, Descriptors.Length);
        WriteVector(Root, 0x308, CategoriesAddress, Categories.Length);
        Root[0x314] = categoryMode ? (byte)1 : (byte)0;
        WritePointer(Root, 0x324, CategoryManagerAddress);
        WritePointer(Root, 0x328, categoryMode ? 0 : ActiveRootAddress);
        if (categoryMode)
        {
            WriteRawVector(Root, 0x32C, 0, 0, 0);
        }
        else
        {
            WriteVector(Root, 0x32C, ActiveManagerPointersAddress, ActiveManagerPointers.Length);
        }
        WriteInt32(Root, 0x338, focusKey);
        WriteInt32(Root, 0x33C, activePage);

        WritePointer(CategoryManager, 0, ImageBase + SteamSettingsCapture.InputManagerVtableRva);
        WriteInt32(CategoryManager, 0x2C4, categoryMode ? categoryKey : activePage);
        WritePointer(ActiveManagerPointers, 0, ActiveManagerAddress);
        WritePointer(ActiveManager, 0, ImageBase + SteamSettingsCapture.InputManagerVtableRva);
        WriteInt32(ActiveManager, 0x2C4, focusKey);

        for (var index = 0; index < Categories.Length / 0x30; index++)
        {
            WriteInlineString(Categories, index * 0x30, $"Cat {index}");
            WriteInlineString(Categories, index * 0x30 + 0x18, $"Cat help {index}");
        }
        for (var index = 0; index < Descriptors.Length / 0x0C; index++)
        {
            if (index == activePage)
            {
                WriteVector(Descriptors, index * 0x0C, RowsAddress, Rows.Length);
            }
            else
            {
                WriteRawVector(Descriptors, index * 0x0C, 0, 0, 0);
            }
        }

        Memory = new SegmentedMemory()
            .Add(RootAddress, Root)
            .Add(DescriptorsAddress, Descriptors)
            .Add(CategoriesAddress, Categories)
            .Add(RowsAddress, Rows)
            .Add(CategoryManagerAddress, CategoryManager)
            .Add(ActiveManagerPointersAddress, ActiveManagerPointers)
            .Add(ActiveManagerAddress, ActiveManager)
            .Add(ActiveRootAddress, [0]);

        for (var index = 0; index < rows.Count; index++)
        {
            var spec = rows[index];
            var rowOffset = index * 0x88;
            WriteInlineString(Rows, rowOffset, spec.Label);
            var helpAddress = 0x50000u + (uint)(index * 0x2000);
            var helpBlock = CreateStringVector(spec.Help);
            HelpAddresses.Add(helpAddress);
            HelpBlocks.Add(helpBlock);
            Memory.Add(helpAddress, helpBlock);
            WriteVector(Rows, rowOffset + 0x18, helpAddress, helpBlock.Length);

            if (spec.Values.Count == 0)
            {
                WriteRawVector(Rows, rowOffset + 0x24, 0, 0, 0);
            }
            else
            {
                var valueAddress = 0x51000u + (uint)(index * 0x2000);
                var valueBlock = CreateStringVector(spec.Values);
                ValueAddresses.Add(valueAddress);
                ValueBlocks.Add(valueBlock);
                Memory.Add(valueAddress, valueBlock);
                WriteVector(Rows, rowOffset + 0x24, valueAddress, valueBlock.Length);
                if (!categoryMode && spec.SelectedIndex is int selected)
                {
                    Observations.Add(new(
                        RootAddress,
                        activePage,
                        RowsAddress + (nuint)rowOffset,
                        valueAddress + (nuint)(selected * MsvcStringReader.LayoutSize)));
                }
            }
        }
    }

    public SegmentedMemory Memory { get; }
    public byte[] Root { get; }
    public byte[] Descriptors { get; }
    public byte[] Categories { get; }
    public byte[] Rows { get; }
    public byte[] CategoryManager { get; }
    public byte[] ActiveManagerPointers { get; }
    public byte[] ActiveManager { get; }
    public List<nuint> HelpAddresses { get; } = [];
    public List<byte[]> HelpBlocks { get; } = [];
    public List<nuint> ValueAddresses { get; } = [];
    public List<byte[]> ValueBlocks { get; } = [];
    public List<SteamSettingsValueObservation> Observations { get; } = [];

    public bool TryCapture(out SteamSettingsSnapshot snapshot, out string diagnostic) =>
        SteamSettingsCapture.TryCreateSnapshot(Memory, ImageBase, RootAddress, Observations, out snapshot, out diagnostic);
}

internal sealed record TouchRowSpec(
    int UiType,
    string Label,
    string Help,
    IReadOnlyList<string> Values,
    int SelectedIndex)
{
    public static TouchRowSpec Default { get; } = new(0, "Setting", "Help", ["Off", "On"], 0);
}

internal sealed class TouchFixture
{
    internal const nuint RootAddress = 0x110000;
    internal const nuint PagerAddress = 0x120000;
    internal const nuint DescriptorsAddress = 0x130000;
    internal const nuint RowsAddress = 0x140000;
    internal const nuint GroupsAddress = 0x150000;
    internal const nuint Control1001Address = 0x160000;
    internal const nuint Control1002Address = 0x160100;
    internal const nuint Control1000Address = 0x160200;

    private const nuint ImageBase = 0x400000;

    public TouchFixture(
        IReadOnlyList<TouchRowSpec>? rows = null,
        int focusedKey = 1,
        int activePage = 0,
        byte transition = 0,
        bool includeConditional1000 = false)
    {
        rows ??= [TouchRowSpec.Default];
        Root = new byte[0x2FC];
        Pager = new byte[0x2E2];
        Descriptors = new byte[4 * 0x0C];
        Rows = new byte[rows.Count * 0x98];
        Groups = new byte[rows.Count * 0x10];

        WritePointer(Root, 0, ImageBase + TouchSettingsCapture.RootVtableRva);
        WriteVector(Root, 0x2C8, DescriptorsAddress, Descriptors.Length);
        WritePointer(Root, 0x2D4, PagerAddress);
        WritePointer(Root, 0x2DC, Control1001Address);
        WritePointer(Root, 0x2E0, Control1002Address);
        WritePointer(Root, 0x2E4, includeConditional1000 ? Control1000Address : 0);
        WriteInt32(Root, 0x2E8, activePage);
        WriteVector(Root, 0x2EC, GroupsAddress, Groups.Length);
        WriteInt32(Root, 0x2F8, focusedKey);

        WritePointer(Pager, 0, ImageBase + TouchSettingsCapture.PagerVtableRva);
        WriteInt32(Pager, 0x2D0, 4);
        WriteInt32(Pager, 0x2D4, activePage);
        WritePointer(Pager, 0x2D8, 0x180000);
        Pager[0x2E1] = transition;

        for (var index = 0; index < 4; index++)
        {
            if (index == activePage)
            {
                WriteVector(Descriptors, index * 0x0C, RowsAddress, Rows.Length);
            }
            else
            {
                WriteRawVector(Descriptors, index * 0x0C, 0, 0, 0);
            }
        }

        Memory = new SegmentedMemory()
            .Add(RootAddress, Root)
            .Add(PagerAddress, Pager)
            .Add(DescriptorsAddress, Descriptors)
            .Add(RowsAddress, Rows)
            .Add(GroupsAddress, Groups)
            .Add(Control1001Address, [0])
            .Add(Control1002Address, [0])
            .Add(0x180000, [0]);
        if (includeConditional1000)
        {
            Memory.Add(Control1000Address, [0]);
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var spec = rows[index];
            var rowOffset = index * 0x98;
            WriteInt32(Rows, rowOffset, spec.UiType);
            WriteInlineString(Rows, rowOffset + 0x04, spec.Label);
            WriteInlineString(Rows, rowOffset + 0x1C, spec.Help);
            var valueAddress = 0x170000u + (uint)(index * 0x2000);
            var valueBlock = CreateStringVector(spec.Values);
            ValueAddresses.Add(valueAddress);
            ValueBlocks.Add(valueBlock);
            if (valueBlock.Length == 0)
            {
                WriteRawVector(Rows, rowOffset + 0x34, 0, 0, 0);
            }
            else
            {
                WriteVector(Rows, rowOffset + 0x34, valueAddress, valueBlock.Length);
                Memory.Add(valueAddress, valueBlock);
            }
            WriteInt32(Rows, rowOffset + 0x90, spec.SelectedIndex);
        }

        SpecialControls =
        [
            new(1001, Control1001Address, "Confirm A"),
            new(1002, Control1002Address, "Confirm B"),
        ];
        if (includeConditional1000)
        {
            SpecialControls.Add(new(1000, Control1000Address, "Observed 1000"));
        }
    }

    public SegmentedMemory Memory { get; }
    public byte[] Root { get; }
    public byte[] Pager { get; }
    public byte[] Descriptors { get; }
    public byte[] Rows { get; }
    public byte[] Groups { get; }
    public List<nuint> ValueAddresses { get; } = [];
    public List<byte[]> ValueBlocks { get; } = [];
    public List<TouchSettingsSpecialControlObservation> SpecialControls { get; }

    public bool TryCapture(out TouchSettingsSnapshot snapshot, out string diagnostic) =>
        TouchSettingsCapture.TryCreateSnapshot(Memory, ImageBase, RootAddress, SpecialControls, out snapshot, out diagnostic);

    public bool TryTransitionCapture(out TouchSettingsSnapshot snapshot, out string diagnostic) =>
        TouchSettingsCapture.TryCreateTransitionSnapshot(Memory, ImageBase, RootAddress, SpecialControls, out snapshot, out diagnostic);
}

internal sealed class SegmentedMemory : IReadableMemory
{
    private readonly Dictionary<nuint, byte[]> segments = [];
    private readonly HashSet<nuint> forbidden = [];

    public List<(nuint Address, int Length)> Reads { get; } = [];

    public SegmentedMemory Add(nuint address, byte[] bytes)
    {
        segments.Add(address, bytes);
        return this;
    }

    public void Remove(nuint address) => segments.Remove(address);

    public SegmentedMemory Forbid(nuint address)
    {
        forbidden.Add(address);
        return this;
    }

    public bool TryRead(nuint address, Span<byte> destination)
    {
        Reads.Add((address, destination.Length));
        if (forbidden.Contains(address))
        {
            throw new InvalidOperationException($"Native function target 0x{address:X8} was dereferenced.");
        }
        foreach (var (segmentAddress, segment) in segments)
        {
            if (address < segmentAddress)
            {
                continue;
            }
            var offset = address - segmentAddress;
            if (offset <= int.MaxValue && (ulong)offset + (ulong)destination.Length <= (ulong)segment.Length)
            {
                segment.AsSpan((int)offset, destination.Length).CopyTo(destination);
                return true;
            }
        }
        return false;
    }
}

internal sealed class AfterReadMutationMemory(IReadableMemory inner, nuint triggerAddress, Action mutate) : IReadableMemory
{
    private bool mutated;

    public bool TryRead(nuint address, Span<byte> destination)
    {
        var succeeded = inner.TryRead(address, destination);
        if (!mutated && address == triggerAddress)
        {
            mutated = true;
            mutate();
        }
        return succeeded;
    }
}

internal sealed class ThrowingMemory : IReadableMemory
{
    public bool TryRead(nuint address, Span<byte> destination) =>
        throw new InvalidOperationException("Synthetic Settings read failure.");
}

internal static class SettingsFixtureEncoding
{
    public static byte[] CreateStringVector(IReadOnlyList<string> values)
    {
        var bytes = new byte[values.Count * MsvcStringReader.LayoutSize];
        for (var index = 0; index < values.Count; index++)
        {
            WriteInlineString(bytes, index * MsvcStringReader.LayoutSize, values[index]);
        }
        return bytes;
    }

    public static void WriteVector(byte[] destination, int offset, nuint begin, int byteLength)
    {
        WritePointer(destination, offset, begin);
        WritePointer(destination, offset + 4, begin + (nuint)byteLength);
        WritePointer(destination, offset + 8, begin + (nuint)byteLength);
    }

    public static void WriteRawVector(byte[] destination, int offset, uint begin, uint end, uint capacity)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset), begin);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset + 4), end);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset + 8), capacity);
    }

    public static void WriteInlineString(byte[] destination, int offset, string value)
    {
        destination.AsSpan(offset, MsvcStringReader.LayoutSize).Clear();
        var encoded = Encoding.UTF8.GetBytes(value);
        Assert.True(encoded.Length <= 15, "Fixture strings must fit the x86 MSVC small-string buffer.");
        encoded.CopyTo(destination, offset);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset + 0x10), (uint)encoded.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset + 0x14), 15);
    }

    public static void WriteInvalidInlineString(byte[] destination, int offset)
    {
        destination.AsSpan(offset, MsvcStringReader.LayoutSize).Clear();
        destination[offset] = 0xFF;
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset + 0x10), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset + 0x14), 15);
    }

    public static void WritePointer(byte[] destination, int offset, nuint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset), checked((uint)value));

    public static void WriteInt32(byte[] destination, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(destination.AsSpan(offset), value);
}
