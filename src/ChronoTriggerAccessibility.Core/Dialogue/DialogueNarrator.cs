using System.Collections.ObjectModel;
using ChronoTriggerAccessibility.Core.Announcements;
using ChronoTriggerAccessibility.Core.Events;

namespace ChronoTriggerAccessibility.Core.Dialogue;

public sealed class DialogueNarrator
{
    private static readonly IReadOnlyList<Announcement> NoAnnouncements = Array.Empty<Announcement>();
    private bool faulted;
    private bool active;
    private readonly HashSet<DialogueLineIdentity> lineIdentities = [];
    private IReadOnlyList<string>? choices;
    private string? lastChoiceText;
    private int selectedChoiceIndex = -1;

    public IReadOnlyList<Announcement> Apply(AccessibilityEvent accessibilityEvent)
    {
        if (faulted)
        {
            return NoAnnouncements;
        }

        try
        {
            return accessibilityEvent switch
            {
                DialogueOpened => Open(),
                DialogueLinePresented line => PresentLine(line),
                DialogueChoicesPresented presented => PresentChoices(presented),
                DialogueChoiceFocused focused => FocusChoice(focused),
                DialogueChoiceActivated activated => ActivateChoice(activated),
                DialogueClosed => Close(),
                DialogueCoverageFailed failure => Fail(failure.Diagnostic),
                _ => NoAnnouncements,
            };
        }
        catch (Exception exception)
        {
            return Fail($"Dialogue semantic narration failed safely: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private IReadOnlyList<Announcement> Open()
    {
        active = true;
        lineIdentities.Clear();
        choices = null;
        lastChoiceText = null;
        selectedChoiceIndex = -1;
        return NoAnnouncements;
    }

    private IReadOnlyList<Announcement> PresentLine(DialogueLinePresented line)
    {
        if (!active || line.PageBase < 0 || line.LineIndex < 0 || string.IsNullOrWhiteSpace(line.Text))
        {
            return Fail("Dialogue line has no validated active window, page, line index, or visible text.");
        }

        var identity = new DialogueLineIdentity(line.PageBase, line.LineIndex, line.Text);
        if (!lineIdentities.Add(identity))
        {
            return NoAnnouncements;
        }

        return [Queue(line.Text)];
    }

    private IReadOnlyList<Announcement> PresentChoices(DialogueChoicesPresented presented)
    {
        if (!active || presented.Choices is null || presented.Choices.Count == 0 ||
            presented.Choices.Any(string.IsNullOrWhiteSpace) || presented.SelectedIndex < -1 ||
            presented.SelectedIndex >= presented.Choices.Count)
        {
            return Fail("Dialogue choices have no validated active window, localized choices, or selected position.");
        }

        choices = presented.Choices.ToArray();
        selectedChoiceIndex = presented.SelectedIndex;
        lastChoiceText = selectedChoiceIndex < 0
            ? null
            : FormatChoice(choices[selectedChoiceIndex], selectedChoiceIndex, choices.Count);

        var announcements = new List<Announcement>(choices.Count + (selectedChoiceIndex < 0 ? 0 : 1));
        for (var index = 0; index < choices.Count; index++)
        {
            announcements.Add(index == 0 ? Interrupt(choices[index]) : Queue(choices[index]));
        }
        if (lastChoiceText is not null)
        {
            announcements.Add(Queue(lastChoiceText));
        }

        return new ReadOnlyCollection<Announcement>(announcements);
    }

    private IReadOnlyList<Announcement> FocusChoice(DialogueChoiceFocused focused)
    {
        if (!active || choices is null || string.IsNullOrWhiteSpace(focused.Label) ||
            focused.Count != choices.Count || focused.SelectedIndex < 0 ||
            focused.SelectedIndex >= focused.Count ||
            !string.Equals(choices[focused.SelectedIndex], focused.Label, StringComparison.Ordinal))
        {
            return Fail("Dialogue choice focus has no correlated localized choice or valid position.");
        }

        var text = FormatChoice(focused.Label, focused.SelectedIndex, focused.Count);
        if (string.Equals(lastChoiceText, text, StringComparison.Ordinal))
        {
            return NoAnnouncements;
        }

        selectedChoiceIndex = focused.SelectedIndex;
        lastChoiceText = text;
        return [Interrupt(text)];
    }

    private IReadOnlyList<Announcement> ActivateChoice(DialogueChoiceActivated activated)
    {
        if (!active || choices is null || selectedChoiceIndex < 0 ||
            string.IsNullOrWhiteSpace(activated.Label) ||
            !string.Equals(choices[selectedChoiceIndex], activated.Label, StringComparison.Ordinal))
        {
            return Fail("Dialogue choice activation has no validated selected localized choice.");
        }

        return [Interrupt($"{activated.Label} selected.")];
    }

    private IReadOnlyList<Announcement> Close()
    {
        active = false;
        lineIdentities.Clear();
        choices = null;
        lastChoiceText = null;
        selectedChoiceIndex = -1;
        return NoAnnouncements;
    }

    private IReadOnlyList<Announcement> Fail(string diagnostic)
    {
        if (faulted)
        {
            return NoAnnouncements;
        }

        faulted = true;
        active = false;
        lineIdentities.Clear();
        choices = null;
        var exact = string.IsNullOrWhiteSpace(diagnostic)
            ? "Dialogue accessibility coverage failed without a diagnostic."
            : diagnostic;
        return [Interrupt($"Chrono Trigger accessibility stopped: {exact}")];
    }

    private static string FormatChoice(string label, int selectedIndex, int count) =>
        $"{label}, {selectedIndex + 1} of {count}";

    private static Announcement Interrupt(string text) =>
        new(text, AnnouncementPriority.Interrupt, Interrupt: true);

    private static Announcement Queue(string text) =>
        new(text, AnnouncementPriority.Queued, Interrupt: false);

    private readonly record struct DialogueLineIdentity(int PageBase, int LineIndex, string Text);
}
