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
