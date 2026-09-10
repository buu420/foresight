using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

public enum NameGridCellKind
{
    Glyph,
    PageLatin,
    PageHiragana,
    PageKatakana,
    Empty,
    Delete,
    LocalizedAction,
}

public sealed record NameGridCellDescriptor(
    int Page,
    int Row,
    int Column,
    string RawText,
    NameGridCellKind Kind);

public sealed record NameGridCellSnapshot(
    int Page,
    int Row,
    int Column,
    string RawText,
    string Label,
    NameGridCellKind Kind);

public sealed record NameInputSnapshot(
    string Name,
    bool GridActive,
    int Page,
    string PageLabel,
    NameGridCellSnapshot? FocusedCell,
    nuint GlyphAppendTarget,
    nuint DeleteTarget,
    nuint RefreshTarget);

public sealed record NameActionObservation(int ActionId, nuint Control, string Label);
public sealed record NameActionSnapshot(int ActionId, nuint Control, string Label);

public sealed record NameConfirmationChoiceObservation(int ManagerKey, nuint Control, string Label);
public sealed record NameConfirmationChoice(int ManagerKey, nuint Control, string Label);

public sealed record NameConfirmationSnapshot(
    string Prompt,
    IReadOnlyList<NameConfirmationChoice> Choices,
    int SelectedIndex)
{
    public NameConfirmationChoice SelectedChoice => Choices[SelectedIndex];
}

public static class NameInputCapture
{
    public const uint ActiveOffset = 0x294;
    public const uint PageOffset = 0x298;
    public const uint ColumnOffset = 0x29C;
    public const uint RowOffset = 0x2A0;
    public const uint GlyphAppendTargetOffset = 0x2FC;
    public const uint DeleteTargetOffset = 0x324;
    public const uint RefreshTargetOffset = 0x34C;
    public const uint GlyphAppendBodyRva = 0x2C1BE0;
    public const uint DeleteBodyRva = 0x2C1D10;
    public const uint RefreshBodyRva = 0x2C2A20;
    // std::function vtable slot +8 points to _Do_call, which adjusts ECX by
    // four bytes before entering the body. It never points straight at the body.
    public const uint GlyphAppendInvokeRva = 0x2C5D90;
    public const uint DeleteInvokeRva = 0x2C5D20;
    public const uint RefreshInvokeRva = 0x2C5CE0;
    // scene+0x350 holds the saved name from opening the grid, not its current text.
    // NameEdit::getText (RVA 0x2BF880) reads the UICCTextField's LabelProtocol.
    public const uint NameEditVtableRva = 0x3B748C;
    public const uint NameEditTextFieldOffset = 0x27C;
    public const uint CocosTextFieldCreateImportRva = 0x3857D8;
    public const uint CocosTextFieldCreateRva = 0x285CA1;
    public const uint CocosTextFieldVtableRva = 0x4B94B8;
    public const uint TextFieldLabelProtocolOffset = 0x278;
    public const uint CocosTextFieldLabelProtocolVtableRva = 0x4B97CC;
    public const uint CocosTextFieldGetStringRva = 0x2D6762;
    public const uint TextFieldCurrentNameOffset = 0x584;
    public const uint GridPointerTableRva = 0x39B708;
    public const uint LanguageGlobalRva = 0x3FA168;
    public const int MaximumNameUtf16CodeUnits = 5;

    private const int PageCount = 3;
    private const int RowsPerPage = 8;
    private const int ColumnsPerRow = 11;
    private const int MaximumGridUtf8Bytes = 64;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static readonly string[][][] RawPages =
    [
        [
            ["A", "B", "C", "D", "E", "a", "b", "c", "d", "e", "ABC"],
            ["F", "G", "H", "I", "J", "f", "g", "h", "i", "j", "かな"],
            ["K", "L", "M", "N", "O", "k", "l", "m", "n", "o", "カナ"],
            ["P", "Q", "R", "S", "T", "p", "q", "r", "s", "t", ""],
            ["U", "V", "W", "X", "Y", "u", "v", "w", "x", "y", ""],
            ["Z", "&", "!", "?", "@", "z", "(", ")", "#", "%", ""],
            ["=", "+", "-", "*", "/", ",", ".", ":", ";", "$", ""],
            ["1", "2", "3", "4", "5", "6", "7", "8", "9", "0", ""],
        ],
        [
            ["あ", "い", "う", "え", "お", "ら", "り", "る", "れ", "ろ", "ABC"],
            ["か", "き", "く", "け", "こ", "が", "ぎ", "ぐ", "げ", "ご", "かな"],
            ["さ", "し", "す", "せ", "そ", "ざ", "じ", "ず", "ぜ", "ぞ", "カナ"],
            ["た", "ち", "つ", "て", "と", "だ", "ぢ", "づ", "で", "ど", ""],
            ["な", "に", "ぬ", "ね", "の", "ば", "び", "ぶ", "べ", "ぼ", ""],
            ["は", "ひ", "ふ", "へ", "ほ", "ぱ", "ぴ", "ぷ", "ぺ", "ぽ", "ー"],
            ["ま", "み", "む", "め", "も", "ぁ", "ぃ", "ぅ", "ぇ", "ぉ", ""],
            ["や", "ゆ", "よ", "わ", "を", "ん", "っ", "ゃ", "ゅ", "ょ", ""],
        ],
        [
            ["ア", "イ", "ウ", "エ", "オ", "ラ", "リ", "ル", "レ", "ロ", "ABC"],
            ["カ", "キ", "ク", "ケ", "コ", "ガ", "ギ", "グ", "ゲ", "ゴ", "かな"],
            ["サ", "シ", "ス", "セ", "ソ", "ザ", "ジ", "ズ", "ゼ", "ゾ", "カナ"],
            ["タ", "チ", "ツ", "テ", "ト", "ダ", "ヂ", "ヅ", "デ", "ド", ""],
            ["ナ", "ニ", "ヌ", "ネ", "ノ", "バ", "ビ", "ブ", "ベ", "ボ", "ヴ"],
            ["ハ", "ヒ", "フ", "ヘ", "ホ", "パ", "ピ", "プ", "ペ", "ポ", "ー"],
            ["マ", "ミ", "ム", "メ", "モ", "ァ", "ィ", "ゥ", "ェ", "ォ", ""],
            ["ヤ", "ユ", "ヨ", "ワ", "ヲ", "ン", "ッ", "ャ", "ュ", "ョ", ""],
        ],
    ];

    public static IReadOnlyList<NameGridCellDescriptor> AuditedGrid { get; } =
        new ReadOnlyCollection<NameGridCellDescriptor>(BuildGrid());

    /// <summary>
    /// The grid's localized action cell (row 7, column 10 on every page).
    ///
    /// Proven by the grid refresh callback at <see cref="RefreshBodyRva"/>: it walks
    /// 8 rows by 11 columns, and for row 7 column 10 alone it calls
    /// <c>TextManager::getMsg(out, 0x42, 0x08)</c> — bytes <c>6A 08 6A 42</c> at
    /// RVA 0x2C2A8A, guarded by <c>CMP ESI,0xA</c> — and assigns the result to that
    /// cell's label. Every other cell takes its text from the audited pointer table
    /// at <see cref="GridPointerTableRva"/>. In the English build bank 0x42 index 8
    /// is "Accept".
    ///
    /// This label is requested by the refresh, not by <c>NameInputScene::init</c>,
    /// so a capture taken before the first refresh legitimately has no label yet.
    /// See <c>docs/native-audits/2026-09-09-name-entry-labels.md</c>.
    /// </summary>
    public static LocalizedMessageKey GridActionTextKey { get; } = new(0x42, 0x08);

    /// <summary>
    /// Proven: the Name Entry button builder (RVA 0x2C08D0) reads its two button
    /// message ids from the table at RVA 0x3B715C, which holds exactly
    /// <c>{ 0x35, 0x08 }</c>, and requests them from bank 0x41 via
    /// <c>TextManager::getMsg</c> at RVA 0x2C0CDF.
    /// </summary>
    public static LocalizedMessageKey DefaultsTextKey { get; } = new(0x41, 0x35);

    /// <inheritdoc cref="DefaultsTextKey"/>
    public static LocalizedMessageKey AcceptTextKey { get; } = new(0x41, 0x08);
    public static LocalizedMessageKey ConfirmationPromptTextKey { get; } = new(0x23, 0xDA);
    public static IReadOnlyList<LocalizedMessageKey> ConfirmationChoiceTextKeys { get; } =
        new ReadOnlyCollection<LocalizedMessageKey>([new(0x41, 0x11), new(0x41, 0x12)]);

    public static bool TryDecodeByValueName(
        IReadableMemory? memory,
        uint stringWord0,
        uint stringWord1,
        uint stringWord2,
        uint stringWord3,
        uint stringLength,
        uint stringCapacity,
        out string name,
        out string error)
    {
        name = string.Empty;
        if (memory is null)
        {
            error = "Confirmation name memory is unavailable.";
            return false;
        }
        if (stringLength > MsvcStringReader.MaximumByteLength)
        {
            error = "Confirmation name length exceeds the audited allocation limit.";
            return false;
        }
        if (stringCapacity < stringLength)
        {
            error = "Confirmation name capacity is smaller than its declared length.";
            return false;
        }

        byte[] encoded;
        if (stringCapacity < 16)
        {
            Span<byte> inline = stackalloc byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(inline, stringWord0);
            BinaryPrimitives.WriteUInt32LittleEndian(inline[4..], stringWord1);
            BinaryPrimitives.WriteUInt32LittleEndian(inline[8..], stringWord2);
            BinaryPrimitives.WriteUInt32LittleEndian(inline[12..], stringWord3);
            if (stringLength >= 16 || inline[checked((int)stringLength)] != 0)
            {
                error = "Inline confirmation name is not NUL terminated at its declared length.";
                return false;
            }
            encoded = inline[..checked((int)stringLength)].ToArray();
        }
        else
        {
            var terminated = new byte[checked((int)stringLength + 1)];
            if (stringWord0 == 0 || !memory.TryRead(stringWord0, terminated))
            {
                error = "Heap confirmation name allocation is unreadable.";
                return false;
            }
            if (terminated[^1] != 0)
            {
                error = "Heap confirmation name is not NUL terminated at its declared length.";
                return false;
            }
            encoded = terminated[..^1];
        }

        try
        {
            name = StrictUtf8.GetString(encoded);
        }
        catch (DecoderFallbackException)
        {
            error = "Confirmation name is not valid UTF-8.";
            return false;
        }

        if (!TryValidateNameText(name, encoded, "Confirmation name", out error))
        {
            name = string.Empty;
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <param name="localizedActionLabel">
    /// The localized label of the grid's action cell, or <see langword="null"/> when
    /// the caller has not observed it yet. The game requests it from the grid refresh
    /// callback (<see cref="RefreshBodyRva"/>), not from <c>NameInputScene::init</c>,
    /// and the grid starts inactive on page -1, so a snapshot taken before the first
    /// refresh has no label to supply. It is consumed at exactly one place — the
    /// focused cell, when that cell's kind is
    /// <see cref="NameGridCellKind.LocalizedAction"/> — so a missing label fails
    /// closed only there. Requiring it for every capture made the whole screen fail
    /// before the grid had ever been drawn.
    /// </param>
    public static bool TryCreateSnapshot(
        IReadableMemory? memory,
        nuint imageBase,
        nuint scene,
        string? localizedActionLabel,
        out NameInputSnapshot snapshot,
        out string error)
    {
        try
        {
            return TryCreateSnapshotCore(
                memory, imageBase, scene, localizedActionLabel,
                out snapshot, out error);
        }
        catch (Exception exception)
        {
            snapshot = null!;
            error = $"Name Entry memory capture failed safely: {exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    private static bool TryCreateSnapshotCore(
        IReadableMemory? memory,
        nuint imageBase,
        nuint scene,
        string? localizedActionLabel,
        out NameInputSnapshot snapshot,
        out string error)
    {
        snapshot = null!;
        if (memory is null || imageBase == 0 || scene == 0)
        {
            error = "Name Entry memory, image base, or scene is unavailable.";
            return false;
        }

        if (!TryReadByte(memory, scene + ActiveOffset, out var active) || active is not 0 and not 1)
        {
            error = "Name Entry active byte is unreadable or outside the audited 0/1 range.";
            return false;
        }

        if (!TryReadInt32(memory, scene + PageOffset, out var page))
        {
            error = "Name Entry page is unreadable.";
            return false;
        }

        if (!TryReadRequiredTarget(
                memory, imageBase, scene + GlyphAppendTargetOffset, GlyphAppendInvokeRva,
                "glyph append", out var glyphAppend, out error) ||
            !TryReadRequiredTarget(
                memory, imageBase, scene + DeleteTargetOffset, DeleteInvokeRva,
                "delete", out var delete, out error) ||
            !TryReadRequiredTarget(
                memory, imageBase, scene + RefreshTargetOffset, RefreshInvokeRva,
                "refresh", out var refresh, out error))
        {
            return false;
        }

        if (!TryReadDisplayedName(memory, imageBase, glyphAppend, delete, out var name, out error))
        {
            return false;
        }

        NameGridCellSnapshot? focused = null;
        var pageLabel = string.Empty;
        var gridActive = active != 0 && page >= 0;
        if (active != 0)
        {
            if (page is < -1 or >= PageCount)
            {
                error = $"Name Entry page {page} is outside the audited -1..2 range.";
                return false;
            }

            if (page >= 0)
            {
                if (!TryReadInt32(memory, imageBase + LanguageGlobalRva, out var language))
                {
                    error = "Name Entry language global is unreadable.";
                    return false;
                }

                if (page != 0 && language is not 0 and not 6 and not 7 and not 8)
                {
                    error = $"Name Entry page {page} is unavailable for audited language {language}; refusing a Latin fallback.";
                    return false;
                }

                if (!TryReadInt32(memory, scene + RowOffset, out var row) || row is < 0 or >= RowsPerPage)
                {
                    error = "Name Entry grid row is unreadable or outside 0..7.";
                    return false;
                }

                if (!TryReadInt32(memory, scene + ColumnOffset, out var column) || column is < 0 or >= ColumnsPerRow)
                {
                    error = "Name Entry grid column is unreadable or outside 0..10.";
                    return false;
                }

                var descriptor = GetCell(page, row, column);
                var index = checked((row + page * RowsPerPage) * ColumnsPerRow + column);
                var readError = "pointer is null or unreadable";
                if (!TryReadPointer(memory, imageBase + GridPointerTableRva + checked((nuint)(index * 4)), out var gridTextPointer) ||
                    gridTextPointer == 0 ||
                    !TryReadCString(memory, gridTextPointer, out var rawText, out readError))
                {
                    error = $"Name Entry audited grid pointer {index} is invalid: {readError}";
                    return false;
                }

                if (!string.Equals(rawText, descriptor.RawText, StringComparison.Ordinal))
                {
                    error = $"Name Entry runtime text '{rawText}' does not match audited grid cell ({page},{row},{column}) '{descriptor.RawText}'.";
                    return false;
                }

                if (descriptor.Kind == NameGridCellKind.LocalizedAction &&
                    string.IsNullOrWhiteSpace(localizedActionLabel))
                {
                    // The one cell whose visible text is localized rather than an
                    // audited glyph. Announcing it without its label would tell the
                    // player less than the screen shows, so stop here instead.
                    error = $"Name Entry focused its localized action cell ({page},{row},{column}) " +
                        "without a captured localized label.";
                    return false;
                }

                var label = descriptor.Kind switch
                {
                    NameGridCellKind.Empty => "Empty",
                    NameGridCellKind.Delete => "Delete",
                    NameGridCellKind.LocalizedAction => localizedActionLabel!,
                    _ => rawText,
                };
                focused = new NameGridCellSnapshot(page, row, column, rawText, new string(label.AsSpan()), descriptor.Kind);
                pageLabel = page switch
                {
                    0 => "Latin",
                    1 => "Hiragana",
                    2 => "Katakana",
                    _ => string.Empty,
                };
            }
        }

        snapshot = new NameInputSnapshot(
            name,
            gridActive,
            page,
            pageLabel,
            focused,
            glyphAppend,
            delete,
            refresh);
        error = string.Empty;
        return true;
    }

    public static bool TryCorrelateMainActions(
        IReadOnlyList<NameActionObservation>? observations,
        out IReadOnlyList<NameActionSnapshot> actions,
        out string error)
    {
        actions = Array.Empty<NameActionSnapshot>();
        if (observations is null || observations.Count != 2)
        {
            error = "Name Entry main-action correlation must contain exactly Defaults and Accept.";
            return false;
        }

        if (observations.Any(item => item is null))
        {
            error = "Name Entry main-action correlation contains a null observation.";
            return false;
        }

        if (observations.Select(item => item.ActionId).Distinct().Count() != observations.Count ||
            observations.Select(item => item.Control).Distinct().Count() != observations.Count)
        {
            error = "Name Entry main-action correlation contains a duplicate action ID or control pointer.";
            return false;
        }

        if (observations.Any(item => item.ActionId is not 0 and not 1 ||
                item.Control == 0 || string.IsNullOrWhiteSpace(item.Label)) ||
            !observations.Any(item => item.ActionId == 0) ||
            !observations.Any(item => item.ActionId == 1))
        {
            error = "Name Entry main actions are missing a readable localized Defaults or Accept control.";
            return false;
        }

        actions = new ReadOnlyCollection<NameActionSnapshot>(observations
            .OrderBy(item => item.ActionId)
            .Select(item => new NameActionSnapshot(
                item.ActionId,
                item.Control,
                new string(item.Label.AsSpan())))
            .ToArray());
        error = string.Empty;
        return true;
    }

    public static bool TryCreateConfirmation(
        string promptTemplate,
        string name,
        IReadOnlyList<NameConfirmationChoiceObservation>? observations,
        int initialManagerKey,
        out NameConfirmationSnapshot confirmation,
        out string error)
    {
        confirmation = null!;
        const string placeholder = "<NAME>";
        if (string.IsNullOrWhiteSpace(promptTemplate) ||
            promptTemplate.IndexOf(placeholder, StringComparison.Ordinal) < 0 ||
            promptTemplate.IndexOf(placeholder, StringComparison.Ordinal) !=
            promptTemplate.LastIndexOf(placeholder, StringComparison.Ordinal))
        {
            error = "The localized confirmation prompt must contain exactly one <NAME> placeholder.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(name) || name.Length > MaximumNameUtf16CodeUnits)
        {
            error = "Confirmation requires a non-empty name of at most five UTF-16 code units.";
            return false;
        }

        if (initialManagerKey != 1)
        {
            error = $"The confirmation initial manager key is {initialManagerKey}; expected audited key 1.";
            return false;
        }

        if (observations is null || observations.Count != 2)
        {
            error = "Confirmation correlation must contain exactly two localized choices.";
            return false;
        }

        if (observations.Any(item => item is null))
        {
            error = "Confirmation correlation contains a null observation.";
            return false;
        }

        if (observations.Select(item => item.ManagerKey).Distinct().Count() != observations.Count ||
            observations.Select(item => item.Control).Distinct().Count() != observations.Count)
        {
            error = "Confirmation correlation contains a duplicate manager key or control pointer.";
            return false;
        }

        if (observations.Any(item => item.ManagerKey is not 0 and not 1 ||
                item.Control == 0 || string.IsNullOrWhiteSpace(item.Label)) ||
            !observations.Any(item => item.ManagerKey == 0) ||
            !observations.Any(item => item.ManagerKey == 1))
        {
            error = "Confirmation choices are missing a readable manager-key 0 or 1 correlation.";
            return false;
        }

        var choices = observations
            .OrderBy(item => item.ManagerKey)
            .Select(item => new NameConfirmationChoice(
                item.ManagerKey,
                item.Control,
                new string(item.Label.AsSpan())))
            .ToArray();
        var selectedIndex = Array.FindIndex(choices, choice => choice.ManagerKey == initialManagerKey);
        confirmation = new NameConfirmationSnapshot(
            promptTemplate.Replace(placeholder, name, StringComparison.Ordinal),
            new ReadOnlyCollection<NameConfirmationChoice>(choices),
            selectedIndex);
        error = string.Empty;
        return true;
    }

    private static NameGridCellDescriptor[] BuildGrid()
    {
        var cells = new NameGridCellDescriptor[PageCount * RowsPerPage * ColumnsPerRow];
        var index = 0;
        for (var page = 0; page < PageCount; page++)
        {
            for (var row = 0; row < RowsPerPage; row++)
            {
                for (var column = 0; column < ColumnsPerRow; column++)
                {
                    var raw = RawPages[page][row][column];
                    var kind = DetermineKind(page, row, column, raw);
                    cells[index++] = new NameGridCellDescriptor(page, row, column, raw, kind);
                }
            }
        }
        return cells;
    }

    private static NameGridCellKind DetermineKind(int page, int row, int column, string raw)
    {
        if (column != 10)
        {
            return NameGridCellKind.Glyph;
        }
        if (row == 0)
        {
            return NameGridCellKind.PageLatin;
        }
        if (row == 1)
        {
            return NameGridCellKind.PageHiragana;
        }
        if (row == 2)
        {
            return NameGridCellKind.PageKatakana;
        }
        if (row == 6)
        {
            return NameGridCellKind.Delete;
        }
        if (row == 7)
        {
            return NameGridCellKind.LocalizedAction;
        }
        return string.IsNullOrEmpty(raw) ? NameGridCellKind.Empty : NameGridCellKind.Glyph;
    }

    private static NameGridCellDescriptor GetCell(int page, int row, int column) =>
        AuditedGrid[(page * RowsPerPage + row) * ColumnsPerRow + column];

    private static bool TryReadDisplayedName(
        IReadableMemory memory,
        nuint imageBase,
        nuint glyphAppend,
        nuint delete,
        out string name,
        out string error)
    {
        name = string.Empty;
        // The audited _Do_call thunks pass implementation+4 as the closure;
        // its first field is the same NameEdit used by native append/delete/Accept.
        if (!TryReadPointer(memory, glyphAppend + 4, out var nameEdit) || nameEdit == 0 ||
            !TryReadPointer(memory, delete + 4, out var deleteOwner) || deleteOwner != nameEdit)
        {
            error = "Name Entry glyph/delete closures do not share a readable non-null text owner.";
            return false;
        }
        if (!TryReadPointer(memory, nameEdit, out var editVtable) ||
            editVtable != imageBase + NameEditVtableRva)
        {
            error = "Name Entry text owner is not the audited NameEdit type.";
            return false;
        }
        if (!TryReadPointer(memory, imageBase + CocosTextFieldCreateImportRva, out var create) ||
            create <= CocosTextFieldCreateRva)
        {
            error = "Name Entry Cocos text-field import is unreadable or invalid.";
            return false;
        }
        var cocosBase = create - CocosTextFieldCreateRva;
        if (!TryReadPointer(memory, nameEdit + NameEditTextFieldOffset, out var textField) || textField == 0 ||
            !TryReadPointer(memory, textField, out var fieldVtable) ||
            fieldVtable != cocosBase + CocosTextFieldVtableRva ||
            !TryReadPointer(memory, textField + TextFieldLabelProtocolOffset, out var protocolVtable) ||
            protocolVtable != cocosBase + CocosTextFieldLabelProtocolVtableRva)
        {
            error = "Name Entry displayed text field does not match the audited UICCTextField layout.";
            return false;
        }
        Span<byte> getterBytes = stackalloc byte[7];
        if (!TryReadPointer(memory, protocolVtable + 8, out var getter) ||
            getter != cocosBase + CocosTextFieldGetStringRva ||
            !memory.TryRead(getter, getterBytes) ||
            !getterBytes.SequenceEqual<byte>([0x8D, 0x81, 0x0C, 0x03, 0, 0, 0xC3]))
        {
            error = "Name Entry displayed text getter is not the audited LEA EAX,[ECX+0x30C]; RET.";
            return false;
        }
        // getString returns the MSVC string at LabelProtocol+0x30C. Read it
        // directly so capture never calls game code or allocates a native string.
        return TryReadName(memory, textField + TextFieldCurrentNameOffset, out name, out error);
    }

    private static bool TryReadName(
        IReadableMemory memory,
        nuint address,
        out string value,
        out string error)
    {
        value = string.Empty;
        Span<byte> layout = stackalloc byte[MsvcStringReader.LayoutSize];
        if (!memory.TryRead(address, layout))
        {
            error = "Name MSVC string layout is unreadable.";
            return false;
        }

        var length = BinaryPrimitives.ReadUInt32LittleEndian(layout[0x10..]);
        var capacity = BinaryPrimitives.ReadUInt32LittleEndian(layout[0x14..]);
        if (length > MsvcStringReader.MaximumByteLength || capacity < length)
        {
            error = "Name MSVC string length/capacity is outside audited allocation bounds.";
            return false;
        }

        byte[] data;
        if (capacity < 16)
        {
            if (length >= 16 || layout[checked((int)length)] != 0)
            {
                error = "Inline Name MSVC string is not NUL terminated at its declared length.";
                return false;
            }
            data = layout[..checked((int)length)].ToArray();
        }
        else
        {
            var dataAddress = BinaryPrimitives.ReadUInt32LittleEndian(layout);
            var terminated = new byte[checked((int)length + 1)];
            if (dataAddress == 0 || !memory.TryRead(dataAddress, terminated))
            {
                error = "Heap Name MSVC string allocation is unreadable.";
                return false;
            }
            if (terminated[^1] != 0)
            {
                error = "Heap Name MSVC string is not NUL terminated at its declared length.";
                return false;
            }
            data = terminated[..^1];
        }

        try
        {
            value = StrictUtf8.GetString(data);
        }
        catch (DecoderFallbackException)
        {
            error = "Name MSVC string is not valid UTF-8.";
            return false;
        }

        if (!TryValidateNameText(value, data, "Name", out error))
        {
            value = string.Empty;
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateNameText(
        string value,
        ReadOnlySpan<byte> encoded,
        string diagnosticPrefix,
        out string error)
    {
        if (encoded.Contains((byte)0))
        {
            error = $"{diagnosticPrefix} contains an embedded NUL before its declared length.";
            return false;
        }
        if (value.Length > MaximumNameUtf16CodeUnits)
        {
            error = $"{diagnosticPrefix} exceeds the audited maximum of five UTF-16 code units.";
            return false;
        }
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is
                UnicodeCategory.Control or
                UnicodeCategory.Format or
                UnicodeCategory.LineSeparator or
                UnicodeCategory.ParagraphSeparator or
                UnicodeCategory.OtherNotAssigned)
            {
                error = $"{diagnosticPrefix} contains a non-visible or control Unicode scalar.";
                return false;
            }
        }
        error = string.Empty;
        return true;
    }

    private static bool TryReadCString(
        IReadableMemory memory,
        nuint address,
        out string value,
        out string error)
    {
        value = string.Empty;
        var bytes = new List<byte>();
        Span<byte> current = stackalloc byte[1];
        for (var index = 0; index <= MaximumGridUtf8Bytes; index++)
        {
            if (!memory.TryRead(address + (nuint)index, current))
            {
                error = $"UTF-8 grid string at 0x{address:X} is unreadable.";
                return false;
            }
            if (current[0] == 0)
            {
                try
                {
                    value = StrictUtf8.GetString(bytes.ToArray());
                    error = string.Empty;
                    return true;
                }
                catch (DecoderFallbackException)
                {
                    error = "Grid string is not valid UTF-8.";
                    return false;
                }
            }
            bytes.Add(current[0]);
        }

        error = $"Grid string exceeds {MaximumGridUtf8Bytes} bytes without NUL termination.";
        return false;
    }

    private static bool TryReadRequiredTarget(
        IReadableMemory memory,
        nuint imageBase,
        nuint address,
        uint expectedInvokeRva,
        string name,
        out nuint target,
        out string error)
    {
        if (!TryReadPointer(memory, address, out target) || target == 0 ||
            !TryReadPointer(memory, target, out var vtable) || vtable == 0 ||
            !TryReadPointer(memory, vtable + 8, out var method) ||
            method != imageBase + expectedInvokeRva)
        {
            error = $"Name Entry {name} closure target/vtable/_Do_call is null, unreadable, or not the audited exact-build thunk.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool TryReadByte(IReadableMemory memory, nuint address, out byte value)
    {
        Span<byte> bytes = stackalloc byte[1];
        if (!memory.TryRead(address, bytes))
        {
            value = 0;
            return false;
        }
        value = bytes[0];
        return true;
    }

    private static bool TryReadPointer(IReadableMemory memory, nuint address, out nuint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        if (!memory.TryRead(address, bytes))
        {
            value = 0;
            return false;
        }
        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        return true;
    }

    private static bool TryReadInt32(IReadableMemory memory, nuint address, out int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        if (!memory.TryRead(address, bytes))
        {
            value = 0;
            return false;
        }
        value = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        return true;
    }
}
