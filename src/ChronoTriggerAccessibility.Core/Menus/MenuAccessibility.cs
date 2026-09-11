using System.Collections.ObjectModel;
using ChronoTriggerAccessibility.Core.Events;

namespace ChronoTriggerAccessibility.Core.Menus;

public abstract record MenuAccessibilityEvent : AccessibilityEvent;

/// <summary>
/// Identifies the native menu that presented, and therefore owns, the current narration state.
/// <paramref name="Source"/> names the hook set; <paramref name="Instance"/> is the native root
/// pointer of that hook set's own active context. Native teardown of a parent menu can run after
/// a child menu has already presented, so a close is only authoritative for its own owner.
/// </summary>
public sealed record MenuOwner(string Source, ulong Instance);

public sealed record MenuFocus(
    string Label,
    string? Value,
    int Position,
    int Count,
    string? Help,
    bool Disabled);

public sealed record MenuPresented : MenuAccessibilityEvent
{
    public MenuPresented(
        MenuOwner? owner,
        string title,
        MenuFocus? focus,
        IReadOnlyList<string>? statusDetails)
    {
        Owner = owner;
        Title = title;
        Focus = focus;
        StatusDetails = Snapshot(statusDetails);
    }

    public MenuOwner? Owner { get; }
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

public sealed record MenuExited(MenuOwner? Owner) : MenuAccessibilityEvent;

public sealed record MenuUnsupported(
    string SelectedLabel,
    string BoundaryText,
    string ReturnInstruction) : MenuAccessibilityEvent;

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
