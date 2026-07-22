using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class ExtrasCaptureTests
{
    private const nuint ImageBase = 0x400000;

    [Fact]
    public void CapturesHubOnlyWhenAllVisibleControlsAreValidated()
    {
        var controls = new List<ExtrasHubControlInput>
        {
            new(0, 0, "Illustrations", "View illustrations", true, true),
            new(1, 1, "Music", "Listen to music", true, true),
            new(2, 2, "Scenes", "View scenes", true, true),
            new(3, 3, "Endings", "View endings", false, true),
            new(4, 4, "Return", null, false, true),
        };

        Assert.True(ExtrasCapture.TryCaptureHub(ImageBase, ImageBase + ExtrasCapture.ExtrasHubVtableRva, controls, out var snapshot, out var error), error);
        controls.Clear();
        Assert.Equal(5, snapshot.Controls.Count);
        Assert.Equal("Endings", snapshot.Controls[3].Label);
        Assert.False(snapshot.Controls[3].Enabled);
        Assert.True(snapshot.TryGetLockedActivationHelp(3, out var help));
        Assert.Equal("View endings", help);
        Assert.False(snapshot.TryGetLockedActivationHelp(0, out _));
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
        Assert.False(ExtrasCapture.TryCaptureHub(ImageBase, ImageBase + ExtrasCapture.ExtrasHubVtableRva, controls, out var snapshot, out var error));
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
        var blankHelp = ValidHubControls();
        blankHelp[0] = blankHelp[0] with { Help = " " };
        AssertHubFailure(blankHelp);
    }

    [Fact]
    public void CapturesEndingLogWithoutHiddenLockedText()
    {
        var rows = Enumerable.Range(0, 19).Select(index => new EndingLogRowInput(index, index == 4 ? "?" : $"Visible {index}", index == 4)).ToList();
        var back = new ExtrasBackControlInput(1000, "Return", true, true);

        Assert.True(ExtrasCapture.TryCaptureEndingLog(ImageBase, ImageBase + ExtrasCapture.EndingLogVtableRva, rows, back, out var snapshot, out var error), error);
        rows.Clear();
        Assert.Equal(19, snapshot.Rows.Count);
        Assert.Equal("?", snapshot.Rows[4].VisibleLabel);
        Assert.True(snapshot.Rows[4].Locked);
        Assert.Equal("Return", snapshot.Back.Label);
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
    }

    [Fact]
    public void EndingLogPreservesAnArbitraryNonCollidingBackBinderKey()
    {
        var back = ValidBack() with { Key = -1 };

        Assert.True(ExtrasCapture.TryCaptureEndingLog(
            ImageBase,
            ImageBase + ExtrasCapture.EndingLogVtableRva,
            ValidEndingRows(),
            back,
            out var snapshot,
            out var error), error);
        Assert.Equal(-1, snapshot.Back.Key);
    }

    [Fact]
    public void CapturesEndingDetailOnlyWithVisibleRequiredLocalizedContentAndExactKeys()
    {
        var controls = new List<EndingDetailControlInput>
        {
            new(0, "Review", true, true),
            new(1, "Return", true, true),
        };
        Assert.True(ExtrasCapture.TryCaptureEndingDetail(
            ImageBase,
            ImageBase + ExtrasCapture.EndingDetailVtableRva,
            new EndingDetailInput("Ending A", "Requirements", "Finish chapter", controls),
            out var snapshot,
            out var error), error);
        controls.Clear();
        Assert.Equal("Ending A", snapshot.VisibleTitle);
        Assert.Equal("Requirements", snapshot.VisibleRequirementsLabel);
        Assert.Equal("Finish chapter", snapshot.VisibleRequirementText);
        Assert.Equal([0, 1], snapshot.Controls.Select(control => control.Key));
    }

    [Fact]
    public void EndingDetailFailsForEveryBlankOrWrongOrDuplicateControlCondition()
    {
        foreach (var input in new[]
        {
            ValidDetail() with { VisibleTitle = " " },
            ValidDetail() with { VisibleRequirementsLabel = " " },
            ValidDetail() with { VisibleRequirementText = " " },
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

    private static ExtrasHubControlInput[] ValidHubControls() =>
    [
        new(0, 0, "Illustrations", "View illustrations", true, true),
        new(1, 1, "Music", "Listen to music", true, true),
        new(2, 2, "Scenes", "View scenes", true, true),
        new(3, 3, "Endings", "View endings", false, true),
        new(4, 4, "Return", null, false, true),
    ];

    private static EndingLogRowInput[] ValidEndingRows() =>
        Enumerable.Range(0, 19).Select(index => new EndingLogRowInput(index, index == 3 ? "?" : $"Visible {index}", index == 3)).ToArray();

    private static ExtrasBackControlInput ValidBack() => new(1000, "Return", true, true);

    private static EndingDetailInput ValidDetail(IReadOnlyList<EndingDetailControlInput>? controls = null) =>
        new("Ending A", "Requirements", "Finish chapter", controls ??
        [new(0, "Review", true, true), new(1, "Return", true, true)]);

    private static void AssertHubFailure(IReadOnlyList<ExtrasHubControlInput> controls, nuint imageBase = ImageBase)
    {
        Assert.False(ExtrasCapture.TryCaptureHub(imageBase, imageBase + ExtrasCapture.ExtrasHubVtableRva, controls, out var snapshot, out var error));
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
