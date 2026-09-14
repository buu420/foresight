using System.Collections.ObjectModel;
using ChronoTriggerAccessibility.Core.Announcements;
using ChronoTriggerAccessibility.Core.Events;

namespace ChronoTriggerAccessibility.Core.Menus;

public sealed class MenuNarrator
{
    private static readonly IReadOnlyList<Announcement> NoAnnouncements = Array.Empty<Announcement>();
    private bool faulted;
    private bool active;
    private MenuOwner? activeOwner;
    private string? lastFocusText;
    private string? lastConfirmationText;
    private IReadOnlyList<string>? confirmationChoices;

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
                MenuPresented presented => Present(presented),
                MenuContentPresented content => PresentContent(content),
                MenuContentChanged content => ChangeContent(content),
                MenuFocusChanged changed => ChangeFocus(changed),
                MenuActivated activated => Activate(activated),
                MenuExited exited => Exit(exited),
                MenuUnsupported unsupported => AnnounceUnsupported(unsupported),
                MenuConfirmationPresented confirmation => PresentConfirmation(confirmation),
                MenuConfirmationFocused focused => FocusConfirmation(focused),
                MenuCoverageFailed failure => Fail(failure.Diagnostic),
                _ => NoAnnouncements,
            };
        }
        catch (Exception exception)
        {
            return Fail($"Menu semantic narration failed safely: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private IReadOnlyList<Announcement> Present(MenuPresented presented)
    {
        if (!IsIdentified(presented.Owner))
        {
            return Fail("Menu presentation has no owning native menu identity.");
        }
        if (string.IsNullOrWhiteSpace(presented.Title))
        {
            return Fail("Menu presentation title is blank or null.");
        }
        if (!TryFormatFocus(presented.Focus, out var focusText, out var error))
        {
            return Fail(error);
        }
        if (presented.StatusDetails is null || presented.StatusDetails.Any(string.IsNullOrWhiteSpace))
        {
            return Fail("Menu presentation contains a null or blank visible status detail.");
        }

        active = true;
        activeOwner = presented.Owner;
        confirmationChoices = null;
        lastConfirmationPrompt = null;
        lastConfirmationText = null;
        lastFocusText = focusText;
        var announcements = new List<Announcement>(presented.StatusDetails.Count + 2)
        {
            Interrupt(WithPeriod(presented.Title)),
            Queue(focusText),
        };
        announcements.AddRange(presented.StatusDetails.Select(Queue));
        return new ReadOnlyCollection<Announcement>(announcements);
    }

    private IReadOnlyList<Announcement> ChangeFocus(MenuFocusChanged changed)
    {
        if (!active)
        {
            return Fail("Menu focus changed without a validated active menu.");
        }
        if (!TryFormatFocus(changed.Focus, out var text, out var error))
        {
            return Fail(error);
        }
        if (string.Equals(lastFocusText, text, StringComparison.Ordinal))
        {
            return NoAnnouncements;
        }

        lastFocusText = text;
        return [Interrupt(text)];
    }

    private IReadOnlyList<Announcement> PresentContent(MenuContentPresented content)
    {
        if (!IsIdentified(content.Owner) || string.IsNullOrWhiteSpace(content.Title) ||
            string.IsNullOrWhiteSpace(content.Text)) return Fail("Submenu presentation is missing its owner or visible text.");
        active = true;
        activeOwner = content.Owner;
        confirmationChoices = null;
        lastConfirmationPrompt = null;
        lastConfirmationText = null;
        lastFocusText = content.Text;
        return [Interrupt(WithPeriod(content.Title)), Queue(content.Text)];
    }

    private IReadOnlyList<Announcement> ChangeContent(MenuContentChanged content)
    {
        if (!active || activeOwner != content.Owner) return NoAnnouncements;
        if (string.IsNullOrWhiteSpace(content.Text)) return Fail("Submenu selection has no visible text.");
        if (lastFocusText == content.Text) return NoAnnouncements;
        lastFocusText = content.Text;
        return [Interrupt(content.Text)];
    }

    private IReadOnlyList<Announcement> Activate(MenuActivated activated)
    {
        if (!active || string.IsNullOrWhiteSpace(activated.Label))
        {
            return Fail("Menu activation has no validated active menu or localized label.");
        }

        return [Interrupt($"{activated.Label} selected.")];
    }

    private IReadOnlyList<Announcement> Exit(MenuExited exited)
    {
        if (!IsIdentified(exited.Owner))
        {
            return Fail("Menu exit has no owning native menu identity.");
        }
        if (active && activeOwner is not null && activeOwner != exited.Owner)
        {
            // A parent menu was torn down after a different menu had already presented. The
            // close is authoritative only for its own owner, so the live menu stays active
            // and keeps its focus identity untouched.
            return NoAnnouncements;
        }

        active = false;
        activeOwner = null;
        lastFocusText = null;
        lastConfirmationPrompt = null;
        lastConfirmationText = null;
        confirmationChoices = null;
        return NoAnnouncements;
    }

    private static bool IsIdentified(MenuOwner? owner) =>
        owner is not null && !string.IsNullOrWhiteSpace(owner.Source);

    private IReadOnlyList<Announcement> AnnounceUnsupported(MenuUnsupported unsupported)
    {
        if (!active || string.IsNullOrWhiteSpace(unsupported.SelectedLabel) ||
            string.IsNullOrWhiteSpace(unsupported.BoundaryText) ||
            string.IsNullOrWhiteSpace(unsupported.ReturnInstruction))
        {
            return Fail("Unsupported menu boundary has no validated active menu, localized label, boundary text, or return instruction.");
        }

        return
        [
            Interrupt(unsupported.SelectedLabel),
            Queue(unsupported.BoundaryText),
            Queue(unsupported.ReturnInstruction),
        ];
    }

    private IReadOnlyList<Announcement> PresentConfirmation(MenuConfirmationPresented confirmation)
    {
        if (!active || string.IsNullOrWhiteSpace(confirmation.Prompt) ||
            confirmation.Choices is null || confirmation.Choices.Count == 0 ||
            confirmation.Choices.Any(string.IsNullOrWhiteSpace) ||
            confirmation.SelectedIndex < 0 || confirmation.SelectedIndex >= confirmation.Choices.Count)
        {
            return Fail("Menu confirmation is missing its prompt, localized choices, or selected position.");
        }

        var choice = confirmation.Choices[confirmation.SelectedIndex];
        var text = FormatChoice(choice, confirmation.SelectedIndex, confirmation.Choices.Count);
        if (confirmationChoices is not null &&
            string.Equals(lastConfirmationText, text, StringComparison.Ordinal) &&
            confirmationChoices.SequenceEqual(confirmation.Choices, StringComparer.Ordinal) &&
            string.Equals(lastConfirmationPrompt, confirmation.Prompt, StringComparison.Ordinal))
        {
            return NoAnnouncements;
        }

        confirmationChoices = confirmation.Choices.ToArray();
        lastConfirmationPrompt = confirmation.Prompt;
        lastConfirmationText = text;
        return [Interrupt(confirmation.Prompt), Queue(text)];
    }

    private string? lastConfirmationPrompt;

    private IReadOnlyList<Announcement> FocusConfirmation(MenuConfirmationFocused focused)
    {
        if (!active || confirmationChoices is null || string.IsNullOrWhiteSpace(focused.Label) ||
            focused.Count != confirmationChoices.Count || focused.SelectedIndex < 0 ||
            focused.SelectedIndex >= focused.Count ||
            !string.Equals(confirmationChoices[focused.SelectedIndex], focused.Label, StringComparison.Ordinal))
        {
            return Fail("Menu confirmation focus has no correlated localized choice or valid position.");
        }

        var text = FormatChoice(focused.Label, focused.SelectedIndex, focused.Count);
        if (string.Equals(lastConfirmationText, text, StringComparison.Ordinal))
        {
            return NoAnnouncements;
        }

        lastConfirmationText = text;
        return [Interrupt(text)];
    }

    private IReadOnlyList<Announcement> Fail(string diagnostic)
    {
        if (faulted)
        {
            return NoAnnouncements;
        }

        faulted = true;
        active = false;
        activeOwner = null;
        confirmationChoices = null;
        lastConfirmationPrompt = null;
        var exact = string.IsNullOrWhiteSpace(diagnostic)
            ? "Menu accessibility coverage failed without a diagnostic."
            : diagnostic;
        return [Interrupt($"Chrono Trigger accessibility stopped: {exact}")];
    }

    private static bool TryFormatFocus(
        MenuFocus? focus,
        out string text,
        out string error)
    {
        text = string.Empty;
        if (focus is null || string.IsNullOrWhiteSpace(focus.Label) ||
            focus.Position < 1 || focus.Count < 1 || focus.Position > focus.Count ||
            (focus.Value is not null && string.IsNullOrWhiteSpace(focus.Value)) ||
            (focus.Help is not null && string.IsNullOrWhiteSpace(focus.Help)))
        {
            error = "Menu focus is missing a localized label or has invalid value, help, position, or count.";
            return false;
        }

        text = focus.Value is null
            ? $"{focus.Label}, {focus.Position} of {focus.Count}"
            : $"{focus.Label}: {focus.Value}, {focus.Position} of {focus.Count}";
        if (focus.Disabled)
        {
            text += ", unavailable";
        }
        if (focus.Help is not null)
        {
            text += $". {WithPeriod(focus.Help)}";
        }

        error = string.Empty;
        return true;
    }

    private static string FormatChoice(string label, int selectedIndex, int count) =>
        $"{label}, {selectedIndex + 1} of {count}";

    private static string WithPeriod(string text) =>
        text.Trim().TrimEnd('.') + ".";

    private static Announcement Interrupt(string text) =>
        new(text, AnnouncementPriority.Interrupt, Interrupt: true);

    private static Announcement Queue(string text) =>
        new(text, AnnouncementPriority.Queued, Interrupt: false);
}
