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

public enum SettingsContext
{
    InGame = 0,
    Title = 1,
}

public sealed class SettingsSnapshot
{
    public SettingsSnapshot(
        SettingsContext context,
        int activePage,
        int pageCount,
        int uiType,
        int nativeKey,
        int selectedIndex,
        MenuControlSnapshot control)
    {
        ArgumentNullException.ThrowIfNull(control);
        Context = context;
        ActivePage = activePage;
        PageCount = pageCount;
        UiType = uiType;
        NativeKey = nativeKey;
        SelectedIndex = selectedIndex;
        Control = new MenuControlSnapshot(
            new string(control.Label.AsSpan()),
            control.Value is null ? null : new string(control.Value.AsSpan()),
            control.Help is null ? null : new string(control.Help.AsSpan()),
            control.Key,
            control.Position,
            control.Count,
            control.Enabled,
            control.Visible);
    }

    public SettingsContext Context { get; }
    public int ActivePage { get; }
    public int PageCount { get; }
    public int UiType { get; }
    public int NativeKey { get; }
    public int SelectedIndex { get; }
    public MenuControlSnapshot Control { get; }
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
