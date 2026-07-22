using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class ExtrasCaptureTests
{
    private const nuint ImageBase = 0x400000;

    [Fact]
    public void CapturesHubWithOnlyTheNativeInitiallyFocusedHelp()
    {
        var controls = new List<ExtrasHubControlInput>
        {
            new(0, 0, "Illustrations", null, true, true),
            new(1, 1, "Music", null, true, true),
            new(2, 2, "Scenes", null, true, true),
            new(3, 3, "Endings", "View endings", false, true),
            new(4, 4, "Return", null, true, true),
        };

        Assert.True(ExtrasCapture.TryCaptureHub(ImageBase, ImageBase + ExtrasCapture.ExtrasHubVtableRva, 3, controls, out var snapshot, out var error), error);
        controls.Clear();
        Assert.Equal(3, snapshot.FocusedKey);
        Assert.Equal(5, snapshot.Controls.Count);
        Assert.Equal([1, 2, 3, 4, 5], snapshot.Controls.Select(control => control.Position));
        Assert.Null(snapshot.Controls[0].Help);
        Assert.Equal("Endings", snapshot.Controls[3].Label);
        Assert.False(snapshot.Controls[3].Enabled);
        Assert.True(snapshot.TryGetFocusedControl(out var focused));
        Assert.Equal("Endings", focused.Label);
        Assert.True(snapshot.TryGetLockedActivationHelp(3, out var help));
        Assert.Equal("View endings", help);
        Assert.False(snapshot.TryGetLockedActivationHelp(0, out _));
    }

    [Fact]
    public void HubFocusUpdateAddsOnlyTheNewlyResolvedNativeHelp()
    {
        Assert.True(ExtrasCapture.TryCaptureHub(
            ImageBase,
            ImageBase + ExtrasCapture.ExtrasHubVtableRva,
            2,
            ValidHubControls(),
            out var initial,
            out var initialError), initialError);

        Assert.True(initial.TryMoveFocus(3, "Locked extras", out var moved, out var moveError), moveError);
        Assert.Equal(3, moved.FocusedKey);
        Assert.Equal("Locked extras", moved.Controls[3].Help);
        Assert.True(moved.TryGetLockedActivationHelp(3, out var lockedHelp));
        Assert.Equal("Locked extras", lockedHelp);
        Assert.Null(moved.Controls[0].Help);
        Assert.Equal(2, initial.FocusedKey);
        Assert.Null(initial.Controls[3].Help);

        Assert.True(moved.TryMoveFocus(4, null, out var back, out var backError), backError);
        Assert.Equal(4, back.FocusedKey);
        Assert.Null(back.Controls[4].Help);
        Assert.False(back.TryGetLockedActivationHelp(3, out _));
    }

    [Theory]
    [InlineData(-1, "Help")]
    [InlineData(5, "Help")]
    [InlineData(0, null)]
    [InlineData(1, " ")]
    public void HubRejectsUncorrelatedOrMissingLazyFocusHelp(int key, string? help)
    {
        Assert.True(ExtrasCapture.TryCaptureHub(
            ImageBase,
            ImageBase + ExtrasCapture.ExtrasHubVtableRva,
            2,
            ValidHubControls(),
            out var initial,
            out var initialError), initialError);

        Assert.False(initial.TryMoveFocus(key, help, out var moved, out var error));
        Assert.Null(moved);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void HubFailsWhenAnyRequiredControlIsMissing(int missingPosition)
    {
        var controls = ValidHubControls().Where(control => control.Position != missingPosition).ToArray();
        Assert.False(ExtrasCapture.TryCaptureHub(ImageBase, ImageBase + ExtrasCapture.ExtrasHubVtableRva, 2, controls, out var snapshot, out var error));
        Assert.Null(snapshot);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void HubFailsForDuplicatePositionWrongKeyOrBlankText()
    {
        var duplicatePosition = ValidHubControls();
        duplicatePosition[4] = duplicatePosition[4] with { Position = 3 };
        AssertHubFailure(duplicatePosition);
        var wrongKey = ValidHubControls();
        wrongKey[2] = wrongKey[2] with { Key = 7 };
        AssertHubFailure(wrongKey);
        var blankObservedHelp = ValidHubControls();
        blankObservedHelp[2] = blankObservedHelp[2] with { Help = " " };
        AssertHubFailure(blankObservedHelp);
        var blankUnfocusedHelp = ValidHubControls();
        blankUnfocusedHelp[0] = blankUnfocusedHelp[0] with { Help = " " };
        AssertHubFailure(blankUnfocusedHelp);
        var unavailableBack = ValidHubControls();
        unavailableBack[4] = unavailableBack[4] with { NativeAvailable = false };
        AssertHubFailure(unavailableBack);
        AssertHubFailure(ValidHubControls(), focusedKey: -1);
        AssertHubFailure(ValidHubControls(), focusedKey: 5);
    }

    [Fact]
    public void CapturesEndingLogWithoutHiddenLockedText()
    {
        var rows = Enumerable.Range(0, 19).Select(index => new EndingLogRowInput(index, index == 4 ? "???" : $"Visible {index}", index == 4)).ToList();
        var back = new ExtrasBackControlInput(1000, "Return", true, true);

        Assert.True(ExtrasCapture.TryCaptureEndingLog(ImageBase, ImageBase + ExtrasCapture.EndingLogVtableRva, rows, back, out var snapshot, out var error), error);
        rows.Clear();
        Assert.Equal(19, snapshot.Rows.Count);
        Assert.Equal("???", snapshot.Rows[4].VisibleLabel);
        Assert.True(snapshot.Rows[4].Locked);
        Assert.Equal("Return", snapshot.Back.Label);
        Assert.Equal(20, snapshot.Back.Position);
        Assert.Equal(20, snapshot.Back.Count);
        Assert.DoesNotContain(typeof(EndingLogRowInput).GetProperties(), property =>
            property.Name.Contains("hidden", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("requirement", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EndingLogFailsForMissingDuplicateBlankWrongVtableOrUnavailableBack()
    {
        var rows = ValidEndingRows();
        AssertEndingLogFailure(rows.Take(18).ToArray(), ValidBack());
        var duplicate = ValidEndingRows();
        duplicate[18] = duplicate[18] with { Position = 17 };
        AssertEndingLogFailure(duplicate, ValidBack());
        var blank = ValidEndingRows();
        blank[2] = blank[2] with { VisibleLabel = " " };
        AssertEndingLogFailure(blank, ValidBack());
        Assert.False(ExtrasCapture.TryCaptureEndingLog(ImageBase, ImageBase + 0x10, rows, ValidBack(), out var wrongSnapshot, out var wrongError));
        Assert.Null(wrongSnapshot);
        Assert.False(string.IsNullOrWhiteSpace(wrongError));
        AssertEndingLogFailure(rows, ValidBack() with { NativeAvailable = false });
        AssertEndingLogFailure(rows, ValidBack() with { Key = 18 });
        AssertEndingLogFailure(rows, ValidBack() with { Key = -1 });
        var wrongLockedLiteral = ValidEndingRows();
        wrongLockedLiteral[3] = wrongLockedLiteral[3] with { VisibleLabel = "?" };
        AssertEndingLogFailure(wrongLockedLiteral, ValidBack());
        var unlockedQuestionMarks = ValidEndingRows();
        unlockedQuestionMarks[5] = unlockedQuestionMarks[5] with { VisibleLabel = "???" };
        AssertEndingLogFailure(unlockedQuestionMarks, ValidBack());
    }

    [Fact]
    public void CapturesEndingDetailOnlyWithVisibleRequiredLocalizedContentAndExactKeys()
    {
        var controls = new List<EndingDetailControlInput>
        {
            new(0, "Review", true, true),
            new(1, "Return", true, true),
        };
        var input = new EndingDetailInput("Ending A", "Ending Details", "Requirements", "Finish chapter", controls);
        controls.Clear();
        Assert.True(ExtrasCapture.TryCaptureEndingDetail(
            ImageBase,
            ImageBase + ExtrasCapture.EndingDetailVtableRva,
            input,
            out var snapshot,
            out var error), error);
        Assert.Equal("Ending A", snapshot.VisibleTitle);
        Assert.Equal("Ending Details", snapshot.VisibleHeader);
        Assert.Equal("Requirements", snapshot.VisibleRequirementsLabel);
        Assert.Equal("Finish chapter", snapshot.VisibleRequirementText);
        Assert.Equal([0, 1], snapshot.Controls.Select(control => control.Key));
        Assert.Equal([1, 2], snapshot.Controls.Select(control => control.Position));
    }

    [Fact]
    public void EndingDetailFailsForEveryBlankOrWrongOrDuplicateControlCondition()
    {
        foreach (var input in new[]
        {
            ValidDetail(visibleTitle: " "),
            ValidDetail(visibleHeader: " "),
            ValidDetail(visibleRequirementsLabel: " "),
            ValidDetail(visibleRequirementText: " "),
            ValidDetail([new(0, "Review", true, true)]),
            ValidDetail([new(0, "Review", true, true), new(0, "Return", true, true)]),
            ValidDetail([new(0, "Review", true, true), new(2, "Return", true, true)]),
            ValidDetail([new(0, "Review", false, true), new(1, "Return", true, true)]),
            ValidDetail([new(0, "Review", true, true), new(1, "Return", true, false)]),
        })
        {
            Assert.False(ExtrasCapture.TryCaptureEndingDetail(ImageBase, ImageBase + ExtrasCapture.EndingDetailVtableRva, input, out var snapshot, out var error));
            Assert.Null(snapshot);
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
    }

    [Fact]
    public void EveryCaptureRejectsX86Overflow()
    {
        const nuint overflowingBase = 0xFFFF_FFFF;
        AssertHubFailure(ValidHubControls(), overflowingBase);
        Assert.False(ExtrasCapture.TryCaptureEndingLog(overflowingBase, overflowingBase, ValidEndingRows(), ValidBack(), out var log, out var logError));
        Assert.Null(log);
        Assert.False(string.IsNullOrWhiteSpace(logError));
        Assert.False(ExtrasCapture.TryCaptureEndingDetail(overflowingBase, overflowingBase, ValidDetail(), out var detail, out var detailError));
        Assert.Null(detail);
        Assert.False(string.IsNullOrWhiteSpace(detailError));
    }

    [Fact]
    public void EveryCaptureRejectsZeroImageBase()
    {
        Assert.False(ExtrasCapture.TryCaptureHub(0, ExtrasCapture.ExtrasHubVtableRva, 2, ValidHubControls(), out var hub, out var hubError));
        Assert.Null(hub);
        Assert.False(string.IsNullOrWhiteSpace(hubError));
        Assert.False(ExtrasCapture.TryCaptureEndingLog(0, ExtrasCapture.EndingLogVtableRva, ValidEndingRows(), ValidBack(), out var log, out var logError));
        Assert.Null(log);
        Assert.False(string.IsNullOrWhiteSpace(logError));
        Assert.False(ExtrasCapture.TryCaptureEndingDetail(0, ExtrasCapture.EndingDetailVtableRva, ValidDetail(), out var detail, out var detailError));
        Assert.Null(detail);
        Assert.False(string.IsNullOrWhiteSpace(detailError));
    }

    private static ExtrasHubControlInput[] ValidHubControls() =>
    [
        new(0, 0, "Illustrations", null, true, true),
        new(1, 1, "Music", null, true, true),
        new(2, 2, "Scenes", "View scenes", true, true),
        new(3, 3, "Endings", null, false, true),
        new(4, 4, "Return", null, true, true),
    ];

    private static EndingLogRowInput[] ValidEndingRows() =>
        Enumerable.Range(0, 19).Select(index => new EndingLogRowInput(index, index == 3 ? "???" : $"Visible {index}", index == 3)).ToArray();

    private static ExtrasBackControlInput ValidBack() => new(1000, "Return", true, true);

    private static EndingDetailInput ValidDetail(
        IReadOnlyList<EndingDetailControlInput>? controls = null,
        string visibleTitle = "Ending A",
        string visibleHeader = "Ending Details",
        string visibleRequirementsLabel = "Requirements",
        string visibleRequirementText = "Finish chapter") =>
        new(visibleTitle, visibleHeader, visibleRequirementsLabel, visibleRequirementText, controls ??
        [new(0, "Review", true, true), new(1, "Return", true, true)]);

    private static void AssertHubFailure(
        IReadOnlyList<ExtrasHubControlInput> controls,
        nuint imageBase = ImageBase,
        int focusedKey = 2)
    {
        Assert.False(ExtrasCapture.TryCaptureHub(imageBase, imageBase + ExtrasCapture.ExtrasHubVtableRva, focusedKey, controls, out var snapshot, out var error));
        Assert.Null(snapshot);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    private static void AssertEndingLogFailure(IReadOnlyList<EndingLogRowInput> rows, ExtrasBackControlInput back)
    {
        Assert.False(ExtrasCapture.TryCaptureEndingLog(ImageBase, ImageBase + ExtrasCapture.EndingLogVtableRva, rows, back, out var snapshot, out var error));
        Assert.Null(snapshot);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

}
