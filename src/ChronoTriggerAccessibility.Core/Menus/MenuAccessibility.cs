using System.Collections.ObjectModel;
using ChronoTriggerAccessibility.Core.Events;

namespace ChronoTriggerAccessibility.Core.Menus;

public abstract record MenuAccessibilityEvent : AccessibilityEvent;

public sealed record MenuFocus(
    string Label,
    string? Value,
    int Position,
    int Count,
    string? Help,
    bool Disabled);

public sealed record MenuPresented : MenuAccessibilityEvent
{
    public MenuPresented(string title, MenuFocus? focus, IReadOnlyList<string>? statusDetails)
    {
        Title = title;
        Focus = focus;
        StatusDetails = Snapshot(statusDetails);
    }

    public string Title { get; }
    public MenuFocus? Focus { get; }
    public IReadOnlyList<string>? StatusDetails { get; }

    private static IReadOnlyList<string>? Snapshot(IReadOnlyList<string>? values) =>
        values is null
            ? null
            : new ReadOnlyCollection<string>(values.ToArray());
}

public sealed record MenuFocusChanged(MenuFocus? Focus) : MenuAccessibilityEvent;

public sealed record MenuActivated(string Label) : MenuAccessibilityEvent;

public sealed record MenuExited : MenuAccessibilityEvent;

public sealed record MenuUnsupported(string Label) : MenuAccessibilityEvent;

public sealed record MenuConfirmationPresented : MenuAccessibilityEvent
{
    public MenuConfirmationPresented(string prompt, IReadOnlyList<string>? choices, int selectedIndex)
    {
        Prompt = prompt;
        Choices = Snapshot(choices);
        SelectedIndex = selectedIndex;
    }

    public string Prompt { get; }
    public IReadOnlyList<string>? Choices { get; }
    public int SelectedIndex { get; }

    private static IReadOnlyList<string>? Snapshot(IReadOnlyList<string>? values) =>
        values is null
            ? null
            : new ReadOnlyCollection<string>(values.ToArray());
}

public sealed record MenuConfirmationFocused(string Label, int SelectedIndex, int Count) : MenuAccessibilityEvent;

public sealed record MenuCoverageFailed(string Diagnostic) : MenuAccessibilityEvent;
