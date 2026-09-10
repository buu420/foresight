using System.Buffers.Binary;
using System.Text;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class NameInputCaptureTests
{
    private const nuint ImageBase = 0x400000;
    private const nuint Scene = 0x10000;
    private const nuint NameEdit = 0x80000;
    private const nuint TextField = 0x81000;
    private const nuint DisplayedName = TextField + 0x584;
    private const nuint CocosBase = 0x10000000;

    private static readonly string[][] ExpectedPages =
    [
        [
            "ABCDEabcdeABC", "FGHIJfghijかな", "KLMNOklmnoカナ", "PQRSTpqrst ",
            "UVWXYuvwxy ", "Z&!?@z()#% ", "=+-*/,.:;$ ", "1234567890 ",
        ],
        [
            "あいうえおらりるれろABC", "かきくけこがぎぐげごかな",
            "さしすせそざじずぜぞカナ", "たちつてとだぢづでど ",
            "なにぬねのばびぶべぼ ", "はひふへほぱぴぷぺぽー",
            "まみむめもぁぃぅぇぉ ", "やゆよわをんっゃゅょ ",
        ],
        [
            "アイウエオラリルレロABC", "カキクケコガギグゲゴかな",
            "サシスセソザジズゼゾカナ", "タチツテトダヂヅデド ",
            "ナニヌネノバビブベボヴ", "ハヒフヘホパピプペポー",
            "マミムメモァィゥェォ ", "ヤユヨワヲンッャュョ ",
        ],
    ];

    [Fact]
    public void ModelsTheExactAuditedThreePageEightByElevenGrid()
    {
        Assert.Equal(3 * 8 * 11, NameInputCapture.AuditedGrid.Count);

        for (var page = 0; page < 3; page++)
        {
            for (var row = 0; row < 8; row++)
            {
                var actual = string.Concat(NameInputCapture.AuditedGrid
                    .Where(cell => cell.Page == page && cell.Row == row)
                    .OrderBy(cell => cell.Column)
                    .Select(cell => string.IsNullOrEmpty(cell.RawText) ? " " : cell.RawText));
                Assert.Equal(ExpectedPages[page][row], actual);
            }
        }

        Assert.Equal(NameGridCellKind.PageLatin, Cell(0, 0, 10).Kind);
        Assert.Equal(NameGridCellKind.PageHiragana, Cell(0, 1, 10).Kind);
        Assert.Equal(NameGridCellKind.PageKatakana, Cell(0, 2, 10).Kind);
        Assert.Equal(NameGridCellKind.Delete, Cell(0, 6, 10).Kind);
        Assert.Equal(NameGridCellKind.LocalizedAction, Cell(0, 7, 10).Kind);
        Assert.All(Enumerable.Range(0, 3), page =>
        {
            Assert.Equal(NameGridCellKind.Delete, Cell(page, 6, 10).Kind);
            Assert.Equal(NameGridCellKind.LocalizedAction, Cell(page, 7, 10).Kind);
        });
        Assert.Equal(NameGridCellKind.Glyph, Cell(1, 5, 10).Kind);
        Assert.Equal("ー", Cell(1, 5, 10).RawText);
        Assert.Equal("ヴ", Cell(2, 4, 10).RawText);
    }

    [Fact]
    public void CapturesValidatedNameClosuresLanguageAndFocusedRuntimeGridPointer()
    {
        var memory = CreateValidMemory("Crono", page: 0, row: 0, column: 0, language: 1, gridText: "A");

        Assert.True(NameInputCapture.TryCreateSnapshot(
            memory, ImageBase, Scene, "Localized action",
            out var snapshot, out var error), error);

        Assert.Equal("Crono", snapshot.Name);
        Assert.True(snapshot.GridActive);
        Assert.Equal(0, snapshot.Page);
        Assert.Equal("Latin", snapshot.PageLabel);
        Assert.Equal("A", snapshot.FocusedCell!.Label);
        Assert.Equal(NameGridCellKind.Glyph, snapshot.FocusedCell.Kind);
        Assert.Equal(0x60000u, snapshot.GlyphAppendTarget);
        Assert.Equal(0x60100u, snapshot.DeleteTarget);
        Assert.Equal(0x60200u, snapshot.RefreshTarget);
    }

    [Theory]
    [InlineData("", "Crono")]
    [InlineData("Crono", "Lucca")]
    [InlineData("Crono", "")]
    public void ReadsCurrentTextFieldInsteadOfTheSavedGridEntryName(string savedName, string displayedName)
    {
        var memory = CreateValidMemory(displayedName, -1, 0, 0, 1, "", active: 0)
            .AddInlineMsvcString(Scene + 0x350, savedName);

        Assert.True(NameInputCapture.TryCreateSnapshot(
            memory, ImageBase, Scene, null, out var snapshot, out var error), error);

        Assert.Equal(displayedName, snapshot.Name);
        Assert.False(snapshot.GridActive);
    }

    [Theory]
    [InlineData(0x60004u, "owner")]
    [InlineData(0x60104u, "owner")]
    [InlineData(0x80000u, "NameEdit")]
    [InlineData(0x8027Cu, "text field")]
    [InlineData(0x7857D8u, "Cocos")]
    [InlineData(0x81000u, "text field")]
    [InlineData(0x81278u, "text field")]
    [InlineData(0x104B97D4u, "getter")]
    public void RejectsUnverifiedDisplayedNamePointersInsteadOfUsingTheSavedName(uint address, string diagnostic)
    {
        AssertRejected(CreateValidMemory("Crono", -1, 0, 0, 1, "", active: 0)
            .AddPointer(address, 0), diagnostic);
    }

    [Fact]
    public void RejectsDifferentEditOwnersAndAChangedTextGetter()
    {
        AssertRejected(CreateValidMemory("Crono", -1, 0, 0, 1, "", active: 0)
            .AddPointer(0x60104, NameEdit + 4), "owner");
        AssertRejected(CreateValidMemory("Crono", -1, 0, 0, 1, "", active: 0)
            .ReplaceByte(CocosBase + 0x2D6762 + 2, 0x10), "getter");
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(0)]
    public void AllowsJapanesePagesOnlyForTheFourAuditedLanguages(int language)
    {
        var memory = CreateValidMemory("クロノ", page: 1, row: 0, column: 0, language, gridText: "あ");
        Assert.True(NameInputCapture.TryCreateSnapshot(
            memory, ImageBase, Scene, "Localized action",
            out var snapshot, out var error), error);
        Assert.Equal("あ", snapshot.FocusedCell!.Label);
        Assert.Equal("Hiragana", snapshot.PageLabel);
    }

    [Fact]
    public void RejectsJapanesePageForOtherLanguagesAndNeverFallsBackToLatin()
    {
        AssertRejected(
            CreateValidMemory("Crono", page: 1, row: 0, column: 0, language: 1, gridText: "あ"),
            "language");
    }

    [Fact]
    public void EnforcesFiveUtf16CodeUnitsAndRequiresStrictUtf8WithNulTermination()
    {
        var fiveUnits = CreateValidMemory("😀ABC", 0, 0, 0, 1, "A");
        Assert.True(NameInputCapture.TryCreateSnapshot(
            fiveUnits, ImageBase, Scene, "Localized action",
            out var valid, out var validError), validError);
        Assert.Equal(5, valid.Name.Length);

        AssertRejected(CreateValidMemory("😀ABCD", 0, 0, 0, 1, "A"), "UTF-16");

        var external = CreateValidMemory("Crono", 0, 0, 0, 1, "A", externalName: true);
        Assert.True(NameInputCapture.TryCreateSnapshot(
            external, ImageBase, Scene, "Localized action",
            out var externalSnapshot, out var externalError), externalError);
        Assert.Equal("Crono", externalSnapshot.Name);

        var unterminated = CreateValidMemory("Crono", 0, 0, 0, 1, "A", externalName: true)
            .ReplaceByte(0x50000u + 5, 0x58);
        AssertRejected(unterminated, "NUL");

        var invalidUtf8 = CreateValidMemory("Crono", 0, 0, 0, 1, "A", externalName: true)
            .ReplaceBytes(0x50000, [0xC3, 0x28, 0, 0, 0, 0]);
        AssertRejected(invalidUtf8, "UTF-8");
    }

    [Fact]
    public void RejectsEmbeddedNulAndNonVisibleControlScalarsInInlineAndHeapNames()
    {
        AssertRejected(CreateValidMemory("Cr\0no", 0, 0, 0, 1, "A"), "embedded NUL");
        AssertRejected(
            CreateValidMemory("Cr\0no", 0, 0, 0, 1, "A", externalName: true),
            "embedded NUL");
        AssertRejected(CreateValidMemory("Cr\nno", 0, 0, 0, 1, "A"), "non-visible");
        AssertRejected(
            CreateValidMemory("Cr\u200Dno", 0, 0, 0, 1, "A", externalName: true),
            "non-visible");
    }

    [Fact]
    public void IdentifiesDeleteAndLocalizedActionWithoutTreatingEmptyTableTextAsMissing()
    {
        var delete = CreateValidMemory("Crono", 0, 6, 10, 1, string.Empty);
        Assert.True(NameInputCapture.TryCreateSnapshot(
            delete, ImageBase, Scene, "Localized action",
            out var deleteSnapshot, out var deleteError), deleteError);
        Assert.Equal(NameGridCellKind.Delete, deleteSnapshot.FocusedCell!.Kind);
        Assert.Equal("Delete", deleteSnapshot.FocusedCell.Label);

        var action = CreateValidMemory("Crono", 0, 7, 10, 1, string.Empty);
        Assert.True(NameInputCapture.TryCreateSnapshot(
            action, ImageBase, Scene, "Localized action",
            out var actionSnapshot, out var actionError), actionError);
        Assert.Equal(NameGridCellKind.LocalizedAction, actionSnapshot.FocusedCell!.Kind);
        Assert.Equal("Localized action", actionSnapshot.FocusedCell.Label);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void DeleteAndLocalizedActionApplyOnEveryCharacterPage(int page)
    {
        var delete = CreateValidMemory("Crono", page, 6, 10, 0, string.Empty);
        Assert.True(NameInputCapture.TryCreateSnapshot(
            delete, ImageBase, Scene, "Localized action",
            out var deleteSnapshot, out var deleteError), deleteError);
        Assert.Equal(NameGridCellKind.Delete, deleteSnapshot.FocusedCell!.Kind);
        Assert.Equal("Delete", deleteSnapshot.FocusedCell.Label);

        var action = CreateValidMemory("Crono", page, 7, 10, 0, string.Empty);
        Assert.True(NameInputCapture.TryCreateSnapshot(
            action, ImageBase, Scene, "Localized action",
            out var actionSnapshot, out var actionError), actionError);
        Assert.Equal(NameGridCellKind.LocalizedAction, actionSnapshot.FocusedCell!.Kind);
        Assert.Equal("Localized action", actionSnapshot.FocusedCell.Label);
    }

    [Fact]
    public void CapturesNoGridCellWhenInactiveOrOnTheAuditedMinusOneActionPage()
    {
        var inactive = CreateValidMemory("Crono", 0, 99, 99, 1, string.Empty, active: 0);
        Assert.True(NameInputCapture.TryCreateSnapshot(
            inactive, ImageBase, Scene, "Localized action",
            out var inactiveSnapshot, out var inactiveError), inactiveError);
        Assert.False(inactiveSnapshot.GridActive);
        Assert.Null(inactiveSnapshot.FocusedCell);

        var actionPage = CreateValidMemory("Crono", -1, 99, 99, 1, string.Empty);
        Assert.True(NameInputCapture.TryCreateSnapshot(
            actionPage, ImageBase, Scene, "Localized action",
            out var actionSnapshot, out var actionError), actionError);
        Assert.Null(actionSnapshot.FocusedCell);
    }

    [Fact]
    public void RejectsUnreadableAmbiguousOrMismatchedNativeStateAtomically()
    {
        AssertRejected(CreateValidMemory("Crono", 3, 0, 0, 1, "A"), "page");
        AssertRejected(CreateValidMemory("Crono", -2, 0, 0, 1, "A"), "page");
        AssertRejected(CreateValidMemory("Crono", 0, 8, 0, 1, "A"), "row");
        AssertRejected(CreateValidMemory("Crono", 0, 0, 11, 1, "A"), "column");
        AssertRejected(CreateValidMemory("Crono", 0, 0, 0, 1, "B"), "audited grid");
        AssertRejected(CreateValidMemory("Crono", 0, 0, 0, 1, "A").AddPointer(
            Scene + NameInputCapture.DeleteTargetOffset, 0), "delete");
        AssertRejected(CreateValidMemory("Crono", 0, 0, 0, 1, "A").AddPointer(
            0x60000, 0), "glyph append");
        AssertRejected(CreateValidMemory("Crono", 0, 0, 0, 1, "A").AddPointer(
            0x61100 + 8, ImageBase + NameInputCapture.GlyphAppendInvokeRva), "delete");
        AssertRejected(CreateValidMemory("Crono", 0, 0, 0, 1, "A").AddPointer(
            0x61200 + 8, ImageBase + 0x123456), "refresh");
        AssertRejected(CreateValidMemory("Crono", 0, 0, 0, 1, "A").AddByte(
            Scene + NameInputCapture.ActiveOffset, 2), "active");
        AssertRejected(CreateValidMemory("Crono", 0, 7, 10, 1, string.Empty), "localized action", localizedAction: "");
        AssertRejected(CreateValidMemory("Crono", 0, 0, 0, 1, "A").AddPointer(
            0x61200 + 8, ImageBase + NameInputCapture.RefreshBodyRva), "refresh");
    }

    [Fact]
    public void CorrelatesDefaultsAndAcceptFromCapturedLocalizedControls()
    {
        var observations = new NameActionObservation[]
        {
            new(1, 0xA100, "Localized accept"),
            new(0, 0xA000, "Localized defaults"),
        };

        Assert.True(NameInputCapture.TryCorrelateMainActions(
            observations, out var actions, out var error), error);
        Assert.Equal("Localized defaults", actions[0].Label);
        Assert.Equal("Localized accept", actions[1].Label);
        Assert.Equal(new LocalizedMessageKey(0x41, 0x35), NameInputCapture.DefaultsTextKey);
        Assert.Equal(new LocalizedMessageKey(0x41, 0x08), NameInputCapture.AcceptTextKey);

        Assert.False(NameInputCapture.TryCorrelateMainActions(
            [observations[0], observations[0]], out _, out var duplicateError));
        Assert.Contains("duplicate", duplicateError, StringComparison.OrdinalIgnoreCase);

        var nullException = Record.Exception(() =>
        {
            Assert.False(NameInputCapture.TryCorrelateMainActions(
                [null!, observations[0]], out _, out var nullError));
            Assert.Contains("null", nullError, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Null(nullException);
    }

    [Fact]
    public void CorrelatesConfirmationLabelsToActualManagerKeysAndPreservesRenderedPrompt()
    {
        var choices = new NameConfirmationChoiceObservation[]
        {
            new(1, 0xB100, "Localized choice B"),
            new(0, 0xB000, "Localized choice A"),
        };

        Assert.True(NameInputCapture.TryCreateConfirmation(
            "Begin as\nCrono?", "Crono", choices, initialManagerKey: 1,
            out var confirmation, out var error), error);
        Assert.Equal("Begin as\nCrono?", confirmation.Prompt);
        Assert.Equal(new[] { "Localized choice A", "Localized choice B" },
            confirmation.Choices.Select(choice => choice.Label));
        Assert.Equal(1, confirmation.SelectedIndex);
        Assert.Equal(1, confirmation.SelectedChoice.ManagerKey);
        Assert.Equal(new LocalizedMessageKey(0x23, 0xDA), NameInputCapture.ConfirmationPromptTextKey);
        Assert.Equal(
            new[] { new LocalizedMessageKey(0x41, 0x11), new LocalizedMessageKey(0x41, 0x12) },
            NameInputCapture.ConfirmationChoiceTextKeys);

        Assert.False(NameInputCapture.TryCreateConfirmation(
            " ", "Crono", choices, 1, out _, out var promptError));
        Assert.Contains("rendered", promptError, StringComparison.Ordinal);
        Assert.False(NameInputCapture.TryCreateConfirmation(
            "Begin as Crono?", "Crono", choices, 0, out _, out var focusError));
        Assert.Contains("initial", focusError, StringComparison.OrdinalIgnoreCase);
        Assert.False(NameInputCapture.TryCreateConfirmation(
            "Begin as Crono?", "Crono", [choices[0], choices[0]], 1,
            out _, out var duplicateError));
        Assert.Contains("duplicate", duplicateError, StringComparison.OrdinalIgnoreCase);

        var nullException = Record.Exception(() =>
        {
            Assert.False(NameInputCapture.TryCreateConfirmation(
                "Begin as Crono?", "Crono", [null!, choices[0]], 1,
                out _, out var nullError));
            Assert.Contains("null", nullError, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Null(nullException);
    }

    [Fact]
    public void RenderedConfirmationTextIsNeverSubstitutedAgain()
    {
        Assert.True(NameInputCapture.TryCreateConfirmation(
            "Crono, <NAME>?", "Crono",
            [new(0, 0xB000, "Yes"), new(1, 0xB100, "No")], 1,
            out var confirmation, out var error), error);

        Assert.Equal("Crono, <NAME>?", confirmation.Prompt);
    }

    [Fact]
    public void DecodesExactSixWordByValueConfirmationNameBeforeNativeMutation()
    {
        var inline = new byte[16];
        Encoding.UTF8.GetBytes("Crono").CopyTo(inline, 0);
        inline[5] = 0;

        Assert.True(NameInputCapture.TryDecodeByValueName(
            new TestMemory(),
            BinaryPrimitives.ReadUInt32LittleEndian(inline),
            BinaryPrimitives.ReadUInt32LittleEndian(inline.AsSpan(4)),
            BinaryPrimitives.ReadUInt32LittleEndian(inline.AsSpan(8)),
            BinaryPrimitives.ReadUInt32LittleEndian(inline.AsSpan(12)),
            5,
            15,
            out var inlineName,
            out var inlineError), inlineError);
        Assert.Equal("Crono", inlineName);

        var heap = new TestMemory().AddCString(0x50000, "Lucca");
        Assert.True(NameInputCapture.TryDecodeByValueName(
            heap, 0x50000, 0, 0, 0, 5, 16,
            out var heapName, out var heapError), heapError);
        Assert.Equal("Lucca", heapName);
    }

    [Fact]
    public void ByValueConfirmationNameRejectsInvalidCapacityTerminationUtf8AndUtf16Limit()
    {
        Assert.False(NameInputCapture.TryDecodeByValueName(
            new TestMemory(), 0, 0, 0, 0, 6, 5,
            out _, out var capacityError));
        Assert.Contains("capacity", capacityError, StringComparison.OrdinalIgnoreCase);

        var unterminated = new TestMemory().ReplaceBytes(
            0x50000, Encoding.UTF8.GetBytes("Crono!"));
        Assert.False(NameInputCapture.TryDecodeByValueName(
            unterminated, 0x50000, 0, 0, 0, 5, 16,
            out _, out var terminationError));
        Assert.Contains("NUL", terminationError, StringComparison.OrdinalIgnoreCase);

        var invalidUtf8 = new TestMemory().ReplaceBytes(0x50000, [0xC3, 0x28, 0]);
        Assert.False(NameInputCapture.TryDecodeByValueName(
            invalidUtf8, 0x50000, 0, 0, 0, 2, 16,
            out _, out var utf8Error));
        Assert.Contains("UTF-8", utf8Error, StringComparison.OrdinalIgnoreCase);

        var sixUnits = new TestMemory().AddCString(0x50000, "123456");
        Assert.False(NameInputCapture.TryDecodeByValueName(
            sixUnits, 0x50000, 0, 0, 0, 6, 16,
            out _, out var lengthError));
        Assert.Contains("five UTF-16", lengthError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SnapshotTryApiContainsMalformedMemoryReaderExceptions()
    {
        var exception = Record.Exception(() =>
        {
            Assert.False(NameInputCapture.TryCreateSnapshot(
                new ThrowingMemory(), ImageBase, Scene, "Localized action",
                out var snapshot, out var error));
            Assert.Null(snapshot);
            Assert.Contains("memory", error, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Null(exception);
    }

    private static NameGridCellDescriptor Cell(int page, int row, int column) =>
        NameInputCapture.AuditedGrid.Single(cell =>
            cell.Page == page && cell.Row == row && cell.Column == column);

    private static TestMemory CreateValidMemory(
        string name,
        int page,
        int row,
        int column,
        int language,
        string gridText,
        bool externalName = false,
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
            .AddPointer(0x60000 + 4, NameEdit)
            .AddPointer(0x60100 + 4, NameEdit)
            .AddPointer(NameEdit, ImageBase + 0x3B748C)
            .AddPointer(NameEdit + 0x27C, TextField)
            .AddPointer(ImageBase + 0x3857D8, CocosBase + 0x285CA1)
            .AddPointer(TextField, CocosBase + 0x4B94B8)
            .AddPointer(TextField + 0x278, CocosBase + 0x4B97CC)
            .AddPointer(CocosBase + 0x4B97CC + 8, CocosBase + 0x2D6762)
            .ReplaceBytes(CocosBase + 0x2D6762, [0x8D, 0x81, 0x0C, 0x03, 0, 0, 0xC3])
            .AddInt32(ImageBase + NameInputCapture.LanguageGlobalRva, language);

        if (externalName)
        {
            memory.AddExternalMsvcString(Scene + 0x350, 0x50000, name);
            memory.AddExternalMsvcString(DisplayedName, 0x50000, name);
        }
        else
        {
            memory.AddInlineMsvcString(Scene + 0x350, name);
            memory.AddInlineMsvcString(DisplayedName, name);
        }

        if (active != 0 && page >= 0 && page <= 2 && row is >= 0 and <= 7 && column is >= 0 and <= 10)
        {
            var index = (row + page * 8) * 11 + column;
            memory
                .AddPointer(ImageBase + NameInputCapture.GridPointerTableRva + (nuint)(index * 4), 0x70000)
                .AddCString(0x70000, gridText);
        }

        return memory;
    }

    private static void AssertRejected(
        IReadableMemory memory,
        string expectedDiagnostic,
        string localizedAction = "Localized action")
    {
        var exception = Record.Exception(() =>
        {
            Assert.False(NameInputCapture.TryCreateSnapshot(
                memory, ImageBase, Scene, localizedAction,
                out var snapshot, out var error));
            Assert.Null(snapshot);
            Assert.Contains(expectedDiagnostic, error, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Null(exception);
    }

    private sealed class TestMemory : IReadableMemory
    {
        private readonly Dictionary<nuint, byte[]> segments = [];

        public TestMemory AddByte(nuint address, byte value)
        {
            segments[address] = [value];
            return this;
        }

        public TestMemory AddPointer(nuint address, nuint value) =>
            AddUInt32(address, checked((uint)value));

        public TestMemory AddInt32(nuint address, int value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
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

        public TestMemory AddExternalMsvcString(nuint address, nuint dataAddress, string value)
        {
            var encoded = Encoding.UTF8.GetBytes(value);
            var layout = new byte[MsvcStringReader.LayoutSize];
            BinaryPrimitives.WriteUInt32LittleEndian(layout, checked((uint)dataAddress));
            BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x10), (uint)encoded.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(layout.AsSpan(0x14), (uint)Math.Max(16, encoded.Length));
            segments[address] = layout;
            segments[dataAddress] = [.. encoded, 0];
            return this;
        }

        public TestMemory AddCString(nuint address, string value)
        {
            segments[address] = [.. Encoding.UTF8.GetBytes(value), 0];
            return this;
        }

        public TestMemory ReplaceByte(nuint address, byte value)
        {
            foreach (var (start, bytes) in segments)
            {
                if (address >= start && address < start + (nuint)bytes.Length)
                {
                    bytes[checked((int)(address - start))] = value;
                    return this;
                }
            }
            throw new InvalidOperationException($"Address 0x{address:X} is not present.");
        }

        public TestMemory ReplaceBytes(nuint address, byte[] value)
        {
            segments[address] = value;
            return this;
        }

        private TestMemory AddUInt32(nuint address, uint value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            segments[address] = bytes;
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

    private sealed class ThrowingMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination) =>
            throw new AccessViolationException("simulated malformed pointer");
    }
}
