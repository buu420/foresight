namespace ChronoTriggerAccessibility.Core.Events;

public abstract record AccessibilityEvent;

public enum StartupSceneKind
{
    SquareEnixLogo,
    OpeningMovie,
    Title,
}

public sealed record StartupSceneEntered(StartupSceneKind Scene) : AccessibilityEvent;

public sealed record ScreenEntered(ScreenKind Screen) : AccessibilityEvent;

public sealed record ScreenExited(ScreenKind Screen) : AccessibilityEvent;

public sealed record FocusChanged(string Label, int Position, int Count, bool Disabled) : AccessibilityEvent;

public sealed record ValueChanged(string Label, string Value, string Help) : AccessibilityEvent;

public sealed record NameChanged(string Name) : AccessibilityEvent;

public sealed record ConfirmationOpened(string Prompt, IReadOnlyList<string> Choices, int SelectedIndex) : AccessibilityEvent;

public sealed record TimedDescription(string Text, int Generation) : AccessibilityEvent;

public sealed record ControlActivated(string Label) : AccessibilityEvent;
