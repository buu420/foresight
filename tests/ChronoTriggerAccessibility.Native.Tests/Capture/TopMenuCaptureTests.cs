using System.Buffers.Binary;
using System.Text;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class TopMenuCaptureTests
{
    private const nuint ImageBase = 0x00400000;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassicCapture_IncludesRenderedFooterLinesAfterCurrency(bool twoLines)
    {
        var fixture = new Fixture(TopMenuStyle.Classic, activeCount: 1, reserveCount: 1);
        using var scope = fixture.Begin();
        fixture.RecordComplete(scope);
        Assert.True(scope.TryRecordUtf8(ImageBase + (twoLines ? 0x1D0DBCu : 0x1D0D78u),
            "Rendered first line", out var error), error);
        if (twoLines)
        {
            Assert.True(scope.TryRecordUtf8(ImageBase + 0x1D0E05u, "Rendered second line", out error), error);
        }
        Assert.True(scope.TryRecordUtf8(ImageBase + 0x1D0ED3u, "Rendered context line", out error), error);

        Assert.True(scope.TryCreateSnapshot(out var snapshot, out error), error);
        Assert.Equal(twoLines
            ? ["Low HP", "Rendered first line", "Rendered second line", "Rendered context line"]
            : new[] { "Low HP", "Rendered first line", "Rendered context line" }, snapshot.ConditionalLines);
        Assert.Equal("Rendered context line", snapshot.FlattenedStatus[^1]);
    }

    [Fact]
    public void ClassicCapture_RejectsAnIncompleteTwoLineFooter()
    {
        var fixture = new Fixture(TopMenuStyle.Classic, activeCount: 1, reserveCount: 1);
        using var scope = fixture.Begin();
        fixture.RecordComplete(scope);
        Assert.True(scope.TryRecordUtf8(ImageBase + 0x1D0DBCu, "First of two", out var error), error);

        Assert.False(scope.TryCreateSnapshot(out _, out error));
        Assert.Contains("footer", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TouchCapture_IncludesInitialStatusCaptionInNativeConstructionOrder()
    {
        var fixture = new Fixture(TopMenuStyle.Touch, activeCount: 1, reserveCount: 0);
        using var scope = fixture.Begin();
        fixture.RecordTouchActive(scope, "Crono");
        Assert.True(scope.TryRecordUtf8(ImageBase + 0x22ECEB, "Rendered caption", out var error), error);
        fixture.RecordRowsAndControls(scope);
        fixture.RecordTimeAndCurrency(scope);
        fixture.RecordStatus(scope);

        Assert.True(scope.TryCreateSnapshot(out var snapshot, out error), error);
        Assert.Equal(new[] { "Rendered caption" }, snapshot.ConditionalLines);
    }

    [Fact]
    public void ClassicCapture_CorrelatesCompleteLocalizedStatusAndNativeFocus()
    {
        var fixture = new Fixture(TopMenuStyle.Classic, activeCount: 1, reserveCount: 1);
        using var scope = fixture.Begin();
        fixture.RecordClassicActive(scope, "Crono", usePlaceholder: false);
        fixture.RecordCompactReserve(scope);
        fixture.RecordRowsAndControls(scope, duplicateCaptions: true, disabledPosition: 3);
        fixture.RecordStatus(scope, "Low HP", "Low HP");
        fixture.RecordTimeAndCurrency(scope);

        Assert.True(scope.TryCreateSnapshot(out var snapshot, out var diagnostic), diagnostic);

        Assert.Equal(TopMenuStyle.Classic, snapshot.Style);
        Assert.Equal(7, snapshot.Controls.Count);
        Assert.Equal(42, snapshot.FocusedKey);
        Assert.Equal(4, snapshot.Controls[3].Position);
        Assert.False(snapshot.Controls[3].Enabled);
        Assert.Equal(snapshot.Controls[0].Label, snapshot.Controls[1].Label);
        Assert.Equal(["Low HP", "Low HP"], snapshot.ConditionalLines);
        Assert.Equal(2, snapshot.Members.Count);
        Assert.Equal(TopMenuMemberKind.Active, snapshot.Members[0].Kind);
        Assert.Equal("Crono", snapshot.Members[0].Name);
        Assert.Equal(TopMenuMemberKind.Reserve, snapshot.Members[1].Kind);
        Assert.Equal("Marle", snapshot.Members[1].Name);
        Assert.Equal(["250 /", "300"], snapshot.Members[0].Rows[1].ValueTokens);
        Assert.Contains("12:34", snapshot.FlattenedStatus);
        Assert.Contains("9999 G", snapshot.FlattenedStatus);
    }

    [Fact]
    public void TouchCapture_UsesCompactActiveGrammarAndTouchPhaseOrder()
    {
        var fixture = new Fixture(TopMenuStyle.Touch, activeCount: 1, reserveCount: 1);
        using var scope = fixture.Begin();
        fixture.RecordTouchActive(scope, "クロノ");
        fixture.RecordCompactReserve(scope);
        fixture.RecordRowsAndControls(scope);
        fixture.RecordTimeAndCurrency(scope);
        fixture.RecordStatus(scope);

        Assert.True(scope.TryCreateSnapshot(out var snapshot, out var diagnostic), diagnostic);
        Assert.Equal("クロノ", snapshot.Members[0].Name);
        Assert.Equal("Marle", snapshot.Members[1].Name);
        Assert.Empty(snapshot.ConditionalLines);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(3, 6)]
    public void Capture_AcceptsExactZeroToThreeActiveAndZeroToSixReserveCounts(int activeCount, int reserveCount)
    {
        var fixture = new Fixture(TopMenuStyle.Touch, activeCount, reserveCount);
        using var scope = fixture.Begin();
        for (var index = 0; index < activeCount; index++)
        {
            fixture.RecordTouchActive(scope, $"Active {index}");
        }
        for (var index = 0; index < reserveCount; index++)
        {
            fixture.RecordCompactReserve(scope);
        }
        fixture.RecordRowsAndControls(scope);
        fixture.RecordTimeAndCurrency(scope);
        fixture.RecordStatus(scope);

        Assert.True(scope.TryCreateSnapshot(out var snapshot, out var diagnostic), diagnostic);
        Assert.Equal(activeCount + reserveCount, snapshot.Members.Count);
    }

    [Fact]
    public void Capture_UsesLowBytePresenceAndAllowsHolesInActiveSlots()
    {
        var fixture = new Fixture(TopMenuStyle.Touch, activeCount: 0, reserveCount: 0);
        fixture.SetSlot(0, 0x00000180); // bit 0x80 in the low byte: absent
        fixture.SetSlot(1, 0x80000000); // sign bit is irrelevant: present
        using var scope = fixture.Begin();
        fixture.RecordTouchActive(scope, "Robo");
        fixture.RecordRowsAndControls(scope);
        fixture.RecordTimeAndCurrency(scope);
        fixture.RecordStatus(scope);

        Assert.True(scope.TryCreateSnapshot(out var snapshot, out var diagnostic), diagnostic);
        Assert.Single(snapshot.Members);
        Assert.Equal(TopMenuMemberKind.Active, snapshot.Members[0].Kind);
    }

    [Fact]
    public void Capture_PairsRuntimeRenamedReserveIdentitiesInAscendingSlotOrder()
    {
        var fixture = new Fixture(TopMenuStyle.Touch, activeCount: 1, reserveCount: 2);
        fixture.SetSlot(3, 1);
        fixture.SetSlot(4, 0x80);
        fixture.SetSlot(5, 0);
        using var scope = fixture.Begin();
        fixture.RecordTouchActive(scope, "Crono");
        fixture.RecordCompactReserve(scope);
        fixture.RecordCompactReserve(scope);
        fixture.RecordRowsAndControls(scope);
        fixture.RecordTimeAndCurrency(scope);
        fixture.RecordStatus(scope);

        Assert.True(scope.TryCreateSnapshot(out var snapshot, out var diagnostic), diagnostic);
        Assert.Equal("Reserve 1", snapshot.Members[1].Name);
        Assert.Equal("Marle", snapshot.Members[2].Name);
    }

    [Fact]
    public void Capture_DisplayGateMakesReserveSequenceEmpty()
    {
        var fixture = new Fixture(TopMenuStyle.Touch, activeCount: 1, reserveCount: 1);
        fixture.SetGate(enabled: false, threshold: 0x48);
        using var scope = fixture.Begin();
        fixture.RecordTouchActive(scope, "Crono");
        fixture.RecordRowsAndControls(scope);
        fixture.RecordTimeAndCurrency(scope);
        fixture.RecordStatus(scope);

        Assert.True(scope.TryCreateSnapshot(out var snapshot, out var diagnostic), diagnostic);
        Assert.Single(snapshot.Members);
    }

    [Theory]
    [InlineData(TopMenuStyle.Classic, 0x23AE3Au)]
    [InlineData(TopMenuStyle.Touch, 0x23AE3Au)]
    [InlineData(TopMenuStyle.Classic, 0x23A9E6u)]
    [InlineData(TopMenuStyle.Touch, 0x23B1D0u)]
    [InlineData(TopMenuStyle.Touch, 0x123456u)]
    public void Capture_RejectsUnknownUnreachableAndStyleMismatchedReturnAddresses(
        TopMenuStyle style,
        uint rva)
    {
        var fixture = new Fixture(style, activeCount: 1, reserveCount: 0);
        using var scope = fixture.Begin();

        Assert.False(scope.TryRecordUtf8(ImageBase + rva, "unexpected", out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.False(scope.TryCreateSnapshot(out var snapshot, out var diagnostic));
        Assert.Null(snapshot);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic));
    }

    [Fact]
    public void ClassicCapture_AcceptsExactPlaceholderBranch()
    {
        var fixture = new Fixture(TopMenuStyle.Classic, activeCount: 1, reserveCount: 0);
        using var scope = fixture.Begin();
        fixture.RecordClassicActive(scope, "Crono", usePlaceholder: true);
        fixture.RecordRowsAndControls(scope);
        fixture.RecordStatus(scope);
        fixture.RecordTimeAndCurrency(scope);

        Assert.True(scope.TryCreateSnapshot(out var snapshot, out var diagnostic), diagnostic);
        Assert.Equal(["---"], snapshot.Members[0].Rows[1].ValueTokens);
    }

    [Fact]
    public void Capture_RejectsWrongCrossCategoryOrderAndIncompleteGrammar()
    {
        var fixture = new Fixture(TopMenuStyle.Classic, activeCount: 1, reserveCount: 0);
        using var scope = fixture.Begin();
        fixture.RecordClassicActive(scope, "Crono", usePlaceholder: false, omitMaximum: true);
        fixture.RecordRowsAndControls(scope);
        fixture.RecordTimeAndCurrency(scope); // classic requires StatusBar first
        fixture.RecordStatus(scope);

        AssertFailure(scope);
    }

    [Fact]
    public void Capture_RejectsActiveAfterReserveAndActiveCountMismatch()
    {
        var fixture = new Fixture(TopMenuStyle.Touch, activeCount: 2, reserveCount: 1);
        using var scope = fixture.Begin();
        fixture.RecordTouchActive(scope, "Crono");
        fixture.RecordCompactReserve(scope);
        fixture.RecordTouchActive(scope, "Marle");
        fixture.RecordRowsAndControls(scope);
        fixture.RecordTimeAndCurrency(scope);
        fixture.RecordStatus(scope);

        AssertFailure(scope);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(7, false)]
    [InlineData(3, true)]
    public void Capture_RejectsInvalidRowPositionsOrHiddenControls(int alteredPosition, bool hidden)
    {
        var fixture = new Fixture(TopMenuStyle.Touch, activeCount: 1, reserveCount: 0);
        using var scope = fixture.Begin();
        fixture.RecordTouchActive(scope, "Crono");
        Assert.False(scope.TryRecordConstructedControl(0x5000, alteredPosition, true, !hidden, out _));
        fixture.RecordRowsAndControls(scope);
        fixture.RecordTimeAndCurrency(scope);
        fixture.RecordStatus(scope);

        AssertFailure(scope);
    }

    [Fact]
    public void Capture_RejectsClassicManagerStackWhoseLastEntryIsNotBinderManager()
    {
        var fixture = new Fixture(TopMenuStyle.Classic, activeCount: 1, reserveCount: 0);
        fixture.SetClassicManagerStack([0x00012100]);
        using var scope = fixture.Begin();
        fixture.RecordComplete(scope);

        AssertFailure(scope);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void Capture_RejectsClassicManagerStackOutsideOneToSixteenEntries(int count)
    {
        var fixture = new Fixture(TopMenuStyle.Classic, activeCount: 1, reserveCount: 0);
        fixture.SetClassicManagerStack(Enumerable.Repeat((nuint)0x12000, count).ToArray());
        using var scope = fixture.Begin();
        fixture.RecordClassicActive(scope, "Crono", usePlaceholder: false);
        fixture.RecordRowsAndControls(scope);
        fixture.RecordStatus(scope);
        fixture.RecordTimeAndCurrency(scope);

        AssertFailure(scope);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(777)]
    public void Capture_RejectsNegativeOrUnmatchedFocusedManagerKey(int focusedKey)
    {
        var fixture = new Fixture(TopMenuStyle.Touch, activeCount: 1, reserveCount: 0);
        fixture.SetFocusedKey(focusedKey);
        using var scope = fixture.Begin();
        fixture.RecordTouchActive(scope, "Crono");
        fixture.RecordRowsAndControls(scope);
        fixture.RecordTimeAndCurrency(scope);
        fixture.RecordStatus(scope);

        AssertFailure(scope);
    }

    [Fact]
    public void Capture_RejectsWrongClassicStatusOwnerPointerAndDuplicateStatusScope()
    {
        var wrongOwner = new Fixture(TopMenuStyle.Classic, activeCount: 1, reserveCount: 0);
        wrongOwner.SetClassicStatusBar(0x11004);
        using (var scope = wrongOwner.Begin())
        {
            wrongOwner.RecordClassicActive(scope, "Crono", usePlaceholder: false);
            wrongOwner.RecordRowsAndControls(scope);
            Assert.False(scope.TryBeginStatusBar(Fixture.Status, out _, out var statusError));
            Assert.Contains("own", statusError, StringComparison.OrdinalIgnoreCase);
            AssertFailure(scope);
        }

        var duplicate = new Fixture(TopMenuStyle.Touch, activeCount: 1, reserveCount: 0);
        using var duplicateScope = duplicate.Begin();
        duplicate.RecordTouchActive(duplicateScope, "Crono");
        duplicate.RecordRowsAndControls(duplicateScope);
        duplicate.RecordTimeAndCurrency(duplicateScope);
        Assert.True(duplicateScope.TryBeginStatusBar(Fixture.Status, out var first, out var firstError), firstError);
        using (first)
        {
            Assert.False(duplicateScope.TryBeginStatusBar(Fixture.Status, out _, out var duplicateError));
            Assert.Contains("more than one", duplicateError, StringComparison.OrdinalIgnoreCase);
            Assert.True(first.TryComplete(out var completeError), completeError);
        }
        AssertFailure(duplicateScope);
    }

    [Theory]
    [InlineData(8u, 0u)]
    [InlineData(0u, 0u)]
    [InlineData(1u, 2u)]
    public void Capture_RejectsInvalidOrDuplicateReserveIds(uint first, uint second)
    {
        var fixture = new Fixture(TopMenuStyle.Touch, activeCount: 1, reserveCount: 2);
        fixture.SetSlot(3, first);
        fixture.SetSlot(4, second);
        using var scope = fixture.Begin();
        fixture.RecordTouchActive(scope, "Crono");
        fixture.RecordCompactReserve(scope);
        fixture.RecordCompactReserve(scope);
        fixture.RecordRowsAndControls(scope);
        fixture.RecordTimeAndCurrency(scope);
        fixture.RecordStatus(scope);

        AssertFailure(scope);
    }

    [Theory]
    [InlineData(MutationTarget.GlobalPointer)]
    [InlineData(MutationTarget.Gate)]
    [InlineData(MutationTarget.GateThreshold)]
    [InlineData(MutationTarget.Owner)]
    [InlineData(MutationTarget.Slots)]
    [InlineData(MutationTarget.NameHeader)]
    [InlineData(MutationTarget.InlineNameHeader)]
    [InlineData(MutationTarget.NameHeapPayload)]
    [InlineData(MutationTarget.Focus)]
    [InlineData(MutationTarget.StatusVtable)]
    [InlineData(MutationTarget.RootVtable)]
    [InlineData(MutationTarget.ManagerStackOwner)]
    [InlineData(MutationTarget.ManagerVector)]
    [InlineData(MutationTarget.StatusOwner)]
    public void Capture_RejectsEveryRelevantConcurrentMutation(MutationTarget target)
    {
        var fixture = new Fixture(
            TopMenuStyle.Classic,
            activeCount: 1,
            reserveCount: 1,
            heapReserveName: target != MutationTarget.InlineNameHeader);
        using var scope = fixture.Begin(target);
        fixture.RecordComplete(scope);

        AssertFailure(scope);
    }

    [Fact]
    public void StatusScope_AcceptsZeroAndSixtyFourEqualLines()
    {
        var emptyFixture = new Fixture(TopMenuStyle.Touch, 1, 0);
        using (var emptyScope = emptyFixture.Begin())
        {
            emptyFixture.RecordTouchActive(emptyScope, "Crono");
            emptyFixture.RecordRowsAndControls(emptyScope);
            emptyFixture.RecordTimeAndCurrency(emptyScope);
            emptyFixture.RecordStatus(emptyScope);
            Assert.True(emptyScope.TryCreateSnapshot(out var snapshot, out var error), error);
            Assert.Empty(snapshot.ConditionalLines);
        }

        var fullFixture = new Fixture(TopMenuStyle.Touch, 1, 0);
        using var fullScope = fullFixture.Begin();
        fullFixture.RecordTouchActive(fullScope, "Crono");
        fullFixture.RecordRowsAndControls(fullScope);
        fullFixture.RecordTimeAndCurrency(fullScope);
        fullFixture.RecordStatus(fullScope, Enumerable.Repeat("same", 64).ToArray());
        Assert.True(fullScope.TryCreateSnapshot(out var full, out var fullError), fullError);
        Assert.Equal(64, full.ConditionalLines.Count);
        Assert.All(full.ConditionalLines, line => Assert.Equal("same", line));
    }

    [Fact]
    public void ClassicCapture_RejectsTimeAndCurrencyBeforeStatusScopeNormallyCompletes()
    {
        var fixture = new Fixture(TopMenuStyle.Classic, activeCount: 1, reserveCount: 0);
        using var scope = fixture.Begin();
        fixture.RecordClassicActive(scope, "Crono", usePlaceholder: false);
        fixture.RecordRowsAndControls(scope);
        Assert.True(scope.TryBeginStatusBar(Fixture.Status, out var status, out var beginError), beginError);
        using (status)
        {
            fixture.RecordStatusLine(status, "Low HP");
            fixture.RecordTimeAndCurrency(scope);
            Assert.True(status.TryComplete(out var completeError), completeError);
        }

        AssertFailure(scope);
    }

    [Fact]
    public void TouchCapture_RejectsStatusScopeThatBeginsBeforeTimeAndCurrency()
    {
        var fixture = new Fixture(TopMenuStyle.Touch, activeCount: 1, reserveCount: 0);
        using var scope = fixture.Begin();
        fixture.RecordTouchActive(scope, "Crono");
        fixture.RecordRowsAndControls(scope);
        Assert.True(scope.TryBeginStatusBar(Fixture.Status, out var status, out var beginError), beginError);
        using (status)
        {
            fixture.RecordTimeAndCurrency(scope);
            fixture.RecordStatusLine(status, "Low HP");
            Assert.True(status.TryComplete(out var completeError), completeError);
        }

        AssertFailure(scope);
    }

    [Fact]
    public void StatusScope_RejectsCountAggregateWrongCallerAndNoncompletion()
    {
        foreach (var scenario in new[] { "count", "aggregate", "caller", "incomplete" })
        {
            var fixture = new Fixture(TopMenuStyle.Touch, 1, 0);
            using var scope = fixture.Begin();
            fixture.RecordTouchActive(scope, "Crono");
            fixture.RecordRowsAndControls(scope);
            fixture.RecordTimeAndCurrency(scope);
            Assert.True(scope.TryBeginStatusBar(Fixture.Status, out var status, out var beginError), beginError);
            using (status)
            {
                if (scenario == "count")
                {
                    for (var index = 0; index < 65; index++)
                    {
                        fixture.RecordStatusLine(status, "x");
                    }
                    status.TryComplete(out _);
                }
                else if (scenario == "aggregate")
                {
                    fixture.RecordStatusLine(status, new string('a', 2048));
                    fixture.RecordStatusLine(status, new string('b', 2048));
                    fixture.RecordStatusLine(status, "c");
                    status.TryComplete(out _);
                }
                else if (scenario == "caller")
                {
                    Assert.False(status.TryRecordRenderedLine(
                        ImageBase + 0x22F3BE,
                        fixture.AddWideString("wrong"),
                        out _));
                    status.TryComplete(out _);
                }
                // Deliberately omit completion for the final scenario.
            }

            AssertFailure(scope);
        }
    }

    [Fact]
    public void Scope_RejectsNestingCrossThreadUseAndCrossThreadDisposalThenRecovers()
    {
        var fixture = new Fixture(TopMenuStyle.Touch, 1, 0);
        using var ownerReady = new ManualResetEventSlim();
        using var workerDone = new ManualResetEventSlim();
        TopMenuCaptureScope? stale = null;
        string? ownerResult = null;
        var owner = new Thread(() =>
        {
            stale = fixture.Begin();
            Assert.False(TopMenuCaptureScope.TryBegin(
                fixture.Memory,
                ImageBase,
                Fixture.Root,
                TopMenuStyle.Touch,
                out _,
                out var nestedError));
            Assert.Contains("active", nestedError, StringComparison.OrdinalIgnoreCase);
            ownerReady.Set();
            workerDone.Wait();
            Assert.False(stale.TryRecordUtf8(ImageBase + 0x23A9E6, "stale", out var staleError));
            using var replacement = fixture.Begin();
            ownerResult = staleError;
        });
        owner.Start();
        ownerReady.Wait();

        var access = stale!.TryRecordUtf8(ImageBase + 0x23A9E6, "wrong thread", out var accessError);
        var disposeError = RecordDisposeError(stale);
        workerDone.Set();
        owner.Join();

        Assert.False(access);
        Assert.Contains("thread", accessError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("thread", disposeError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("abandoned", ownerResult, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Capture_ContainsThrowingMemoryAndX86OverflowWithoutPartialSnapshot()
    {
        Assert.False(TopMenuCaptureScope.TryBegin(
            new ThrowingMemory(), ImageBase, Fixture.Root, TopMenuStyle.Touch,
            out var throwingScope, out var throwingError));
        Assert.Null(throwingScope);
        Assert.False(string.IsNullOrWhiteSpace(throwingError));

        Assert.False(TopMenuCaptureScope.TryBegin(
            new SegmentedMemory(), uint.MaxValue - 1u, Fixture.Root, TopMenuStyle.Touch,
            out var overflowScope, out var overflowError));
        Assert.Null(overflowScope);
        Assert.Contains("x86", overflowError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Capture_RejectsRootStyleMismatchAndPublishesReadOnlyDefensiveCollections()
    {
        var mismatch = new Fixture(TopMenuStyle.Touch, 1, 0);
        Assert.False(TopMenuCaptureScope.TryBegin(
            mismatch.Memory, ImageBase, Fixture.Root, TopMenuStyle.Classic,
            out _, out var mismatchError));
        Assert.Contains("vtable", mismatchError, StringComparison.OrdinalIgnoreCase);

        var fixture = new Fixture(TopMenuStyle.Touch, 1, 0);
        using var scope = fixture.Begin();
        fixture.RecordTouchActive(scope, "Crono");
        fixture.RecordRowsAndControls(scope);
        fixture.RecordTimeAndCurrency(scope);
        fixture.RecordStatus(scope, "Ready");
        Assert.True(scope.TryCreateSnapshot(out var snapshot, out var diagnostic), diagnostic);

        Assert.Throws<NotSupportedException>(() => ((IList<MenuControlSnapshot>)snapshot.Controls).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<TopMenuMemberSnapshot>)snapshot.Members).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)snapshot.ConditionalLines).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)snapshot.FlattenedStatus).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)snapshot.Members[0].Rows[0].ValueTokens).Clear());
    }

    private static void AssertFailure(TopMenuCaptureScope scope)
    {
        Assert.False(scope.TryCreateSnapshot(out var snapshot, out var diagnostic));
        Assert.Null(snapshot);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic));
    }

    private static string RecordDisposeError(TopMenuCaptureScope scope)
    {
        try
        {
            scope.Dispose();
            return string.Empty;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
    }

    public enum MutationTarget
    {
        GlobalPointer,
        Gate,
        GateThreshold,
        Owner,
        Slots,
        NameHeader,
        InlineNameHeader,
        NameHeapPayload,
        Focus,
        StatusVtable,
        RootVtable,
        ManagerStackOwner,
        ManagerVector,
        StatusOwner,
    }

    private sealed class Fixture
    {
        public const nuint Root = 0x00010000;
        public const nuint Status = 0x00011000;
        private const nuint Manager = 0x00012000;
        private const nuint ManagerStack = 0x00013000;
        private const nuint ManagerElements = 0x00014000;
        private const nuint Global = 0x00020000;
        private const nuint Roster = 0x00040000;
        private const nuint NameHeap = 0x00050000;
        private const nuint WideBase = 0x00060000;
        private const nuint WideHeapBase = 0x00070000;
        private const nuint GlobalPointerAddress = ImageBase + 0x41B4C4;

        private readonly TopMenuStyle style;
        private readonly byte[] root = new byte[0x2D0];
        private readonly byte[] status = new byte[4];
        private readonly byte[] manager = new byte[0x2C8];
        private readonly byte[] managerStack = new byte[16];
        private byte[] managerElements = new byte[4];
        private readonly byte[] global = new byte[0x11100];
        private readonly byte[] roster = new byte[0x21C0];
        private readonly byte[] globalPointer = new byte[4];
        private readonly byte[] nameHeap = new byte[256];
        private readonly SegmentedMemory backing = new();
        private int wideIndex;

        public Fixture(TopMenuStyle style, int activeCount, int reserveCount, bool heapReserveName = false)
        {
            this.style = style;
            Write32(root, 0, checked((uint)(ImageBase + (style == TopMenuStyle.Classic ? 0x3A4024u : 0x3AA058u))));
            Write32(status, 0, checked((uint)(ImageBase + 0x3ABCB8u)));
            Write32(manager, 0x2C4, 42);
            Write32(root, 0x2C0, checked((uint)ManagerStack));
            Write32(root, 0x2CC, checked((uint)Status));
            Write32(globalPointer, 0, checked((uint)Global));
            Write32(global, 0x28, checked((uint)Roster));
            global[0x10F84] = 1;
            Write32(global, 0x110B0, 0);
            SetClassicManagerStack([Manager]);

            for (var slot = 0; slot < 9; slot++)
            {
                SetSlot(slot, 0x80);
            }
            for (var slot = 0; slot < activeCount; slot++)
            {
                SetSlot(slot, checked((uint)slot));
            }
            for (var reserve = 0; reserve < reserveCount; reserve++)
            {
                SetSlot(3 + reserve, checked((uint)reserve));
                SetName(reserve, reserve == 0 ? "Marle" : $"Reserve {reserve}", heapReserveName && reserve == 0);
            }

            backing
                .Add(Root, root)
                .Add(Status, status)
                .Add(Manager, manager)
                .Add(ManagerStack, managerStack)
                .Add(GlobalPointerAddress, globalPointer)
                .Add(Global, global)
                .Add(Roster, roster)
                .Add(NameHeap, nameHeap);
            Memory = backing;
        }

        public IReadableMemory Memory { get; private set; }

        public TopMenuCaptureScope Begin(MutationTarget? mutation = null)
        {
            if (mutation is not null)
            {
                var (address, length) = mutation.Value switch
                {
                    MutationTarget.GlobalPointer => (GlobalPointerAddress, 4),
                    MutationTarget.Gate => (Global + 0x10F84, 1),
                    MutationTarget.GateThreshold => (Global + 0x110B0, 4),
                    MutationTarget.Owner => (Global + 0x28, 4),
                    MutationTarget.Slots => (Roster + 0x219C, 36),
                    MutationTarget.NameHeader or MutationTarget.InlineNameHeader => (Global + 0x1908, 24),
                    MutationTarget.NameHeapPayload => (NameHeap, Encoding.UTF8.GetByteCount("Marle")),
                    MutationTarget.Focus => (Manager + 0x2C4, 4),
                    MutationTarget.StatusVtable => (Status, 4),
                    MutationTarget.RootVtable => (Root, 4),
                    MutationTarget.ManagerStackOwner => (Root + 0x2C0, 4),
                    MutationTarget.ManagerVector => (ManagerStack + 4, 12),
                    MutationTarget.StatusOwner => (Root + 0x2CC, 4),
                    _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
                };
                Memory = new MutatingMemory(backing, address, length);
            }

            Assert.True(TopMenuCaptureScope.TryBegin(
                Memory, ImageBase, Root, style, out var scope, out var diagnostic), diagnostic);
            return scope;
        }

        public void SetGate(bool enabled, int threshold)
        {
            global[0x10F84] = enabled ? (byte)1 : (byte)0;
            Write32(global, 0x110B0, threshold);
        }

        public void SetSlot(int slot, uint value) => Write32(roster, 0x219C + slot * 4, value);

        public void SetFocusedKey(int key) => Write32(manager, 0x2C4, key);

        public void SetClassicStatusBar(nuint value) => Write32(root, 0x2CC, checked((uint)value));

        public void SetClassicManagerStack(IReadOnlyList<nuint> managers)
        {
            managerElements = new byte[managers.Count * 4];
            for (var index = 0; index < managers.Count; index++)
            {
                Write32(managerElements, index * 4, checked((uint)managers[index]));
            }
            Write32(managerStack, 4, checked((uint)ManagerElements));
            Write32(managerStack, 8, checked((uint)(ManagerElements + (nuint)managerElements.Length)));
            Write32(managerStack, 12, checked((uint)(ManagerElements + (nuint)managerElements.Length)));
            backing.Replace(ManagerElements, managerElements);
        }

        public void RecordComplete(TopMenuCaptureScope scope)
        {
            if (style == TopMenuStyle.Classic)
            {
                RecordClassicActive(scope, "Crono", usePlaceholder: false);
            }
            else
            {
                RecordTouchActive(scope, "Crono");
            }
            RecordCompactReserve(scope);
            RecordRowsAndControls(scope);
            if (style == TopMenuStyle.Classic)
            {
                RecordStatus(scope, "Low HP");
                RecordTimeAndCurrency(scope);
            }
            else
            {
                RecordTimeAndCurrency(scope);
                RecordStatus(scope, "Low HP");
            }
        }

        public void RecordClassicActive(
            TopMenuCaptureScope scope,
            string name,
            bool usePlaceholder,
            bool omitMaximum = false)
        {
            Record(scope, 0x23B1D0, name);
            Record(scope, 0x23A2F2, "LV");
            Record(scope, 0x23A552, "12");
            Record(scope, 0x23A73B, "★");
            Record(scope, 0x23A2F2, "HP");
            if (usePlaceholder)
            {
                Record(scope, 0x23A3C2, "---");
            }
            else
            {
                Record(scope, 0x23A5EF, "250 /");
                if (!omitMaximum)
                {
                    Record(scope, 0x23A697, "300");
                }
            }
            Record(scope, 0x23A73B, "HP bonus");
            Record(scope, 0x23A2F2, "MP");
            Record(scope, 0x23A5EF, "40 /");
            Record(scope, 0x23A697, "50");
            Record(scope, 0x23A73B, "MP bonus");
        }

        public void RecordTouchActive(TopMenuCaptureScope scope, string name)
        {
            Record(scope, 0x23A9E6, name);
            RecordCompact(scope);
        }

        public void RecordCompactReserve(TopMenuCaptureScope scope) => RecordCompact(scope);

        public void RecordRowsAndControls(
            TopMenuCaptureScope scope,
            bool duplicateCaptions = false,
            int disabledPosition = -1)
        {
            var captionRva = style == TopMenuStyle.Classic ? 0x1D179Eu : 0x2221C6u;
            for (var position = 0; position < 7; position++)
            {
                var control = (nuint)(0x5000 + position * 0x10);
                var caption = duplicateCaptions && position < 2 ? "Items" : $"Row {position}";
                Record(scope, captionRva, caption);
                Assert.True(scope.TryRecordConstructedControl(
                    control, position, position != disabledPosition, visible: true, out var controlError), controlError);
                Assert.True(scope.TryRecordManagerKeyBinding(
                    Manager, control, position == 2 ? 42 : 100 + position, out var bindingError), bindingError);
            }
        }

        public void RecordTimeAndCurrency(TopMenuCaptureScope scope)
        {
            Record(scope, style == TopMenuStyle.Classic ? 0x1D0AAAu : 0x221A4Fu, "12:34");
            Record(scope, style == TopMenuStyle.Classic ? 0x1D0B54u : 0x221B1Cu, "9999 G");
        }

        public void RecordStatus(TopMenuCaptureScope scope, params string[] lines)
        {
            Assert.True(scope.TryBeginStatusBar(Status, out var statusScope, out var beginError), beginError);
            using (statusScope)
            {
                foreach (var line in lines)
                {
                    RecordStatusLine(statusScope, line);
                }
                Assert.True(statusScope.TryComplete(out var completeError), completeError);
            }
        }

        public void RecordStatusLine(TopMenuStatusCaptureScope statusScope, string line)
        {
            var address = AddWideString(line);
            statusScope.TryRecordRenderedLine(ImageBase + 0x22F3BD, address, out _);
        }

        public nuint AddWideString(string value)
        {
            var index = wideIndex++;
            var address = WideBase + (nuint)(index * 0x20);
            var encoded = Encoding.Unicode.GetBytes(value);
            if (value.Length < 8)
            {
                var layout = new byte[24];
                encoded.CopyTo(layout, 0);
                Write32(layout, 0x10, value.Length);
                Write32(layout, 0x14, 7);
                backing.Add(address, layout);
            }
            else
            {
                var heap = WideHeapBase + (nuint)(index * 0x2000);
                var layout = new byte[24];
                Write32(layout, 0, checked((uint)heap));
                Write32(layout, 0x10, value.Length);
                Write32(layout, 0x14, Math.Max(8, value.Length));
                backing.Add(address, layout).Add(heap, encoded);
            }
            return address;
        }

        private void RecordCompact(TopMenuCaptureScope scope)
        {
            Record(scope, 0x23973B, "LV");
            Record(scope, 0x239891, "12");
            Record(scope, 0x239A73, "★");
            Record(scope, 0x23973B, "HP");
            Record(scope, 0x239914, "250 /");
            Record(scope, 0x2399AA, "300");
            Record(scope, 0x239A73, "HP bonus");
            Record(scope, 0x23973B, "MP");
            Record(scope, 0x239914, "40 /");
            Record(scope, 0x2399AA, "50");
            Record(scope, 0x239A73, "MP bonus");
        }

        private static void Record(TopMenuCaptureScope scope, uint rva, string text) =>
            Assert.True(scope.TryRecordUtf8(ImageBase + rva, text, out var error), error);

        private void SetName(int id, string value, bool heap)
        {
            var offset = 0x1908 + id * 24;
            var encoded = Encoding.UTF8.GetBytes(value);
            if (!heap)
            {
                encoded.CopyTo(global, offset);
                Write32(global, offset + 0x10, encoded.Length);
                Write32(global, offset + 0x14, 15);
            }
            else
            {
                encoded.CopyTo(nameHeap, 0);
                Write32(global, offset, checked((uint)NameHeap));
                Write32(global, offset + 0x10, encoded.Length);
                Write32(global, offset + 0x14, Math.Max(16, encoded.Length));
            }
        }

        private static void Write32(byte[] bytes, int offset, int value) =>
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);

        private static void Write32(byte[] bytes, int offset, uint value) =>
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    }

    private sealed class SegmentedMemory : IReadableMemory
    {
        private readonly Dictionary<nuint, byte[]> segments = [];

        public SegmentedMemory Add(nuint address, byte[] bytes)
        {
            segments.Add(address, bytes);
            return this;
        }

        public void Replace(nuint address, byte[] bytes) => segments[address] = bytes;

        public bool TryRead(nuint address, Span<byte> destination)
        {
            foreach (var (segmentAddress, segment) in segments)
            {
                if (address < segmentAddress)
                {
                    continue;
                }
                var offset = address - segmentAddress;
                if (offset <= int.MaxValue &&
                    (ulong)offset + (ulong)destination.Length <= (ulong)segment.Length)
                {
                    segment.AsSpan((int)offset, destination.Length).CopyTo(destination);
                    return true;
                }
            }
            return false;
        }
    }

    private sealed class MutatingMemory(IReadableMemory inner, nuint targetAddress, int targetLength) : IReadableMemory
    {
        private int matchingReads;

        public bool TryRead(nuint address, Span<byte> destination)
        {
            var succeeded = inner.TryRead(address, destination);
            if (succeeded && address == targetAddress && destination.Length == targetLength &&
                Interlocked.Increment(ref matchingReads) >= 2)
            {
                destination[0] ^= 1;
            }
            return succeeded;
        }
    }

    private sealed class ThrowingMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination) =>
            throw new InvalidOperationException("fixture fault");
    }
}
