using System.Collections.ObjectModel;
using ChronoTriggerAccessibility.Core.Events;

namespace ChronoTriggerAccessibility.Core.Dialogue;

public abstract record DialogueAccessibilityEvent : AccessibilityEvent;

public sealed record DialogueOpened : DialogueAccessibilityEvent;

public sealed record DialogueLinePresented(int PageBase, int LineIndex, string Text) : DialogueAccessibilityEvent;

public sealed record DialogueChoicesPresented : DialogueAccessibilityEvent
{
    public DialogueChoicesPresented(IReadOnlyList<string>? choices, int selectedIndex)
    {
        Choices = choices is null
            ? null
            : new ReadOnlyCollection<string>(choices.ToArray());
        SelectedIndex = selectedIndex;
    }

    public IReadOnlyList<string>? Choices { get; }
    public int SelectedIndex { get; }
}

public sealed record DialogueChoiceFocused(string Label, int SelectedIndex, int Count) : DialogueAccessibilityEvent;

public sealed record DialogueChoiceActivated(string Label) : DialogueAccessibilityEvent;

public sealed record DialogueClosed : DialogueAccessibilityEvent;

public sealed record DialogueCoverageFailed(string Diagnostic) : DialogueAccessibilityEvent;
