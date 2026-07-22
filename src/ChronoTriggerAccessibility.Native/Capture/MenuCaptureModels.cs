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
