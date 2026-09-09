using System.Collections.ObjectModel;

namespace ChronoTriggerAccessibility.Native.Capture;

public sealed record MenuControlSnapshot(
    string Label,
    string? Value,
    string? Help,
    int Key,
    int Position,
    int Count,
    bool Enabled,
    bool Visible);

public enum SteamSettingsContext
{
    InGame = 0,
    Title = 1,
}

public enum SteamSettingsMode
{
    Page = 0,
    Categories = 1,
}

public enum SteamSettingsCategoryKind
{
    OrdinaryPage = 0,
    SpecialAction = 1,
}

public enum SteamSettingsRowKind
{
    Value = 0,
    Action = 1,
}

public enum SteamSettingsFocusKind
{
    Category = 0,
    Row = 1,
    ReturnToCategories = 2,
}

public sealed record SteamSettingsValueObservation(
    int CaptureGeneration,
    nuint RootAddress,
    int PageIndex,
    nuint RowAddress,
    nuint ValueSourceAddress,
    string? RenderedActionValue = null);

public sealed class SteamSettingsCategorySnapshot
{
    internal SteamSettingsCategorySnapshot(
        int index,
        SteamSettingsCategoryKind kind,
        string label,
        string help)
    {
        Index = index;
        Kind = kind;
        Label = new string(label.AsSpan());
        Help = new string(help.AsSpan());
    }

    public int Index { get; }
    public SteamSettingsCategoryKind Kind { get; }
    public string Label { get; }
    public string Help { get; }
}

public sealed class SteamSettingsRowSnapshot
{
    internal SteamSettingsRowSnapshot(
        int index,
        SteamSettingsRowKind kind,
        string label,
        IEnumerable<string> helpTexts,
        IEnumerable<string> values,
        int? selectedIndex,
        string? value,
        nuint nativeRowAddress,
        nuint? setterAddress,
        nuint? selectedValueSourceAddress)
    {
        Index = index;
        Kind = kind;
        Label = new string(label.AsSpan());
        HelpTexts = CopyStrings(helpTexts);
        Values = CopyStrings(values);
        SelectedIndex = selectedIndex;
        Value = value is null ? null : new string(value.AsSpan());
        NativeRowAddress = nativeRowAddress;
        SetterAddress = setterAddress;
        SelectedValueSourceAddress = selectedValueSourceAddress;
    }

    public int Index { get; }
    public SteamSettingsRowKind Kind { get; }
    public string Label { get; }
    public IReadOnlyList<string> HelpTexts { get; }
    public IReadOnlyList<string> Values { get; }
    public int? SelectedIndex { get; }
    public string? Value { get; }
    public nuint NativeRowAddress { get; }
    public nuint? SetterAddress { get; }
    public nuint? SelectedValueSourceAddress { get; }

    private static IReadOnlyList<string> CopyStrings(IEnumerable<string> values) =>
        new ReadOnlyCollection<string>(values.Select(value =>
            new string((value ?? throw new ArgumentNullException(nameof(values))).AsSpan())).ToArray());
}

public sealed class SteamSettingsPageSnapshot
{
    internal SteamSettingsPageSnapshot(
        int index,
        nuint nativeRootAddress,
        IEnumerable<SteamSettingsRowSnapshot> rows,
        MenuControlSnapshot returnControl)
    {
        Index = index;
        NativeRootAddress = nativeRootAddress;
        Rows = new ReadOnlyCollection<SteamSettingsRowSnapshot>(rows.ToArray());
        ReturnControl = new MenuControlSnapshot(
            new string(returnControl.Label.AsSpan()),
            returnControl.Value is null ? null : new string(returnControl.Value.AsSpan()),
            returnControl.Help is null ? null : new string(returnControl.Help.AsSpan()),
            returnControl.Key,
            returnControl.Position,
            returnControl.Count,
            returnControl.Enabled,
            returnControl.Visible);
    }

    public int Index { get; }
    public nuint NativeRootAddress { get; }
    public IReadOnlyList<SteamSettingsRowSnapshot> Rows { get; }
    public MenuControlSnapshot ReturnControl { get; }
}

public sealed class SteamSettingsSnapshot
{
    internal SteamSettingsSnapshot(
        SteamSettingsContext context,
        SteamSettingsMode mode,
        IEnumerable<SteamSettingsCategorySnapshot> categories,
        int descriptorCount,
        int activePageIndex,
        nuint categoryManagerAddress,
        IEnumerable<nuint> activeManagerAddresses,
        int focusKey,
        SteamSettingsFocusKind focusKind,
        int? focusedCategoryIndex,
        int? focusedRowIndex,
        int? focusedSubcontrol,
        SteamSettingsPageSnapshot? page)
    {
        Context = context;
        Mode = mode;
        Categories = new ReadOnlyCollection<SteamSettingsCategorySnapshot>(categories.ToArray());
        DescriptorCount = descriptorCount;
        ActivePageIndex = activePageIndex;
        CategoryManagerAddress = categoryManagerAddress;
        ActiveManagerAddresses = new ReadOnlyCollection<nuint>(activeManagerAddresses.ToArray());
        FocusKey = focusKey;
        FocusKind = focusKind;
        FocusedCategoryIndex = focusedCategoryIndex;
        FocusedRowIndex = focusedRowIndex;
        FocusedSubcontrol = focusedSubcontrol;
        Page = page;
    }

    public SteamSettingsContext Context { get; }
    public SteamSettingsMode Mode { get; }
    public IReadOnlyList<SteamSettingsCategorySnapshot> Categories { get; }
    public int DescriptorCount { get; }
    public int ActivePageIndex { get; }
    public nuint CategoryManagerAddress { get; }
    public IReadOnlyList<nuint> ActiveManagerAddresses { get; }
    public int FocusKey { get; }
    public SteamSettingsFocusKind FocusKind { get; }
    public int? FocusedCategoryIndex { get; }
    public int? FocusedRowIndex { get; }
    public int? FocusedSubcontrol { get; }
    public SteamSettingsPageSnapshot? Page { get; }
}

public enum TouchSettingsFocusKind
{
    None = 0,
    Row = 1,
    SpecialControl = 2,
}

public sealed record TouchSettingsSpecialControlObservation(
    int CaptureGeneration,
    nuint RootAddress,
    int PageIndex,
    int Key,
    nuint ControlAddress,
    string Label);

public sealed record TouchSettingsManagerObservation(
    int CaptureGeneration,
    nuint RootAddress,
    int PageIndex,
    nuint ManagerAddress,
    nuint FocusedControlAddress);

public sealed class TouchSettingsRowSnapshot
{
    internal TouchSettingsRowSnapshot(
        int index,
        int uiType,
        string label,
        string help,
        IEnumerable<string> values,
        int selectedIndex,
        nuint nativeRowAddress)
    {
        Index = index;
        UiType = uiType;
        Label = new string(label.AsSpan());
        Help = new string(help.AsSpan());
        Values = new ReadOnlyCollection<string>(values.Select(value =>
            new string((value ?? throw new ArgumentNullException(nameof(values))).AsSpan())).ToArray());
        SelectedIndex = selectedIndex;
        Value = new string(Values[selectedIndex].AsSpan());
        NativeRowAddress = nativeRowAddress;
    }

    public int Index { get; }
    public int UiType { get; }
    public string Label { get; }
    public string Help { get; }
    public IReadOnlyList<string> Values { get; }
    public int SelectedIndex { get; }
    public string Value { get; }
    public nuint NativeRowAddress { get; }
}

public sealed class TouchSettingsSpecialControlSnapshot
{
    internal TouchSettingsSpecialControlSnapshot(int key, nuint nativeControlAddress, string label)
    {
        Key = key;
        NativeControlAddress = nativeControlAddress;
        Label = new string(label.AsSpan());
    }

    public int Key { get; }
    public nuint NativeControlAddress { get; }
    public string Label { get; }
}

public sealed class TouchSettingsSnapshot
{
    internal TouchSettingsSnapshot(
        nuint nativePagerAddress,
        nuint nativePageRootAddress,
        int activePageIndex,
        int pageCount,
        bool isTransitioning,
        int controlGroupCount,
        IEnumerable<TouchSettingsRowSnapshot> rows,
        IEnumerable<TouchSettingsSpecialControlSnapshot> specialControls,
        int focusedKey,
        TouchSettingsFocusKind focusKind,
        int? focusedRowIndex,
        int? focusedSubcontrol,
        int? focusedSpecialKey)
    {
        NativePagerAddress = nativePagerAddress;
        NativePageRootAddress = nativePageRootAddress;
        ActivePageIndex = activePageIndex;
        PageCount = pageCount;
        IsTransitioning = isTransitioning;
        ControlGroupCount = controlGroupCount;
        Rows = new ReadOnlyCollection<TouchSettingsRowSnapshot>(rows.ToArray());
        SpecialControls = new ReadOnlyCollection<TouchSettingsSpecialControlSnapshot>(specialControls.ToArray());
        FocusedKey = focusedKey;
        FocusKind = focusKind;
        FocusedRowIndex = focusedRowIndex;
        FocusedSubcontrol = focusedSubcontrol;
        FocusedSpecialKey = focusedSpecialKey;
    }

    public nuint NativePagerAddress { get; }
    public nuint NativePageRootAddress { get; }
    public int ActivePageIndex { get; }
    public int PageCount { get; }
    public bool IsTransitioning { get; }
    public int ControlGroupCount { get; }
    public IReadOnlyList<TouchSettingsRowSnapshot> Rows { get; }
    public IReadOnlyList<TouchSettingsSpecialControlSnapshot> SpecialControls { get; }
    public int FocusedKey { get; }
    public TouchSettingsFocusKind FocusKind { get; }
    public int? FocusedRowIndex { get; }
    public int? FocusedSubcontrol { get; }
    public int? FocusedSpecialKey { get; }
}

public sealed class MenuStatusSnapshot
{
    public MenuStatusSnapshot(string? status, IEnumerable<MenuControlSnapshot> controls, int? focusedKey)
    {
        ArgumentNullException.ThrowIfNull(controls);
        Status = status is null ? null : new string(status.AsSpan());
        Controls = new ReadOnlyCollection<MenuControlSnapshot>(controls.ToArray());
        FocusedKey = focusedKey;
    }

    public string? Status { get; }
    public IReadOnlyList<MenuControlSnapshot> Controls { get; }
    public int? FocusedKey { get; }
}

public sealed class MenuCaptureResult
{
    private MenuCaptureResult(MenuStatusSnapshot? snapshot, string diagnostic)
    {
        Snapshot = snapshot;
        Diagnostic = diagnostic;
    }

    public MenuStatusSnapshot? Snapshot { get; }
    public string Diagnostic { get; }
    public bool Succeeded => Snapshot is not null && string.IsNullOrEmpty(Diagnostic);

    public static MenuCaptureResult Success(MenuStatusSnapshot snapshot) =>
        new(snapshot ?? throw new ArgumentNullException(nameof(snapshot)), string.Empty);

    public static MenuCaptureResult Failure(string diagnostic) =>
        new(null, string.IsNullOrWhiteSpace(diagnostic) ? "Capture failed without a diagnostic." : diagnostic);
}

public enum TopMenuStyle
{
    Classic = 0,
    Touch = 1,
}

public enum TopMenuMemberKind
{
    Active = 0,
    Reserve = 1,
}

public sealed class TopMenuStatRowSnapshot
{
    public TopMenuStatRowSnapshot(string label, IEnumerable<string> valueTokens, string? extra)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(valueTokens);
        Label = new string(label.AsSpan());
        ValueTokens = new ReadOnlyCollection<string>(valueTokens.Select(token =>
            new string((token ?? throw new ArgumentNullException(nameof(valueTokens))).AsSpan())).ToArray());
        Extra = extra is null ? null : new string(extra.AsSpan());
    }

    public string Label { get; }
    public IReadOnlyList<string> ValueTokens { get; }
    public string? Extra { get; }
}

public sealed class TopMenuMemberSnapshot
{
    public TopMenuMemberSnapshot(
        string name,
        TopMenuMemberKind kind,
        IEnumerable<TopMenuStatRowSnapshot> rows)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(rows);
        Name = new string(name.AsSpan());
        Kind = kind;
        Rows = new ReadOnlyCollection<TopMenuStatRowSnapshot>(rows.Select(row =>
            new TopMenuStatRowSnapshot(
                (row ?? throw new ArgumentNullException(nameof(rows))).Label,
                row.ValueTokens,
                row.Extra)).ToArray());
    }

    public string Name { get; }
    public TopMenuMemberKind Kind { get; }
    public IReadOnlyList<TopMenuStatRowSnapshot> Rows { get; }
}

public sealed class TopMenuSnapshot
{
    public TopMenuSnapshot(
        TopMenuStyle style,
        IEnumerable<MenuControlSnapshot> controls,
        int focusedKey,
        string time,
        string currency,
        IEnumerable<TopMenuMemberSnapshot> members,
        IEnumerable<string> conditionalLines,
        IEnumerable<string> flattenedStatus)
    {
        ArgumentNullException.ThrowIfNull(controls);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(currency);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(conditionalLines);
        ArgumentNullException.ThrowIfNull(flattenedStatus);

        Style = style;
        Controls = new ReadOnlyCollection<MenuControlSnapshot>(controls.Select(control =>
        {
            ArgumentNullException.ThrowIfNull(control);
            return new MenuControlSnapshot(
                new string(control.Label.AsSpan()),
                control.Value is null ? null : new string(control.Value.AsSpan()),
                control.Help is null ? null : new string(control.Help.AsSpan()),
                control.Key,
                control.Position,
                control.Count,
                control.Enabled,
                control.Visible);
        }).ToArray());
        FocusedKey = focusedKey;
        Time = new string(time.AsSpan());
        Currency = new string(currency.AsSpan());
        Members = new ReadOnlyCollection<TopMenuMemberSnapshot>(members.Select(member =>
            new TopMenuMemberSnapshot(
                (member ?? throw new ArgumentNullException(nameof(members))).Name,
                member.Kind,
                member.Rows)).ToArray());
        ConditionalLines = new ReadOnlyCollection<string>(conditionalLines.Select(line =>
            new string((line ?? throw new ArgumentNullException(nameof(conditionalLines))).AsSpan())).ToArray());
        FlattenedStatus = new ReadOnlyCollection<string>(flattenedStatus.Select(item =>
            new string((item ?? throw new ArgumentNullException(nameof(flattenedStatus))).AsSpan())).ToArray());
    }

    public TopMenuStyle Style { get; }
    public IReadOnlyList<MenuControlSnapshot> Controls { get; }
    public int FocusedKey { get; }
    public string Time { get; }
    public string Currency { get; }
    public IReadOnlyList<TopMenuMemberSnapshot> Members { get; }
    public IReadOnlyList<string> ConditionalLines { get; }
    public IReadOnlyList<string> FlattenedStatus { get; }
}
