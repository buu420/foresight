using ChronoTriggerAccessibility.Core.Announcements;
using ChronoTriggerAccessibility.Core.Events;

namespace ChronoTriggerAccessibility.Core.State;

public sealed class AccessibilityState
{
    private static readonly IReadOnlyList<Announcement> NoAnnouncements = Array.Empty<Announcement>();

    private ScreenKind? _activeScreen;
    private string? _lastSemanticIdentity;

    public int Generation { get; private set; }

    public IReadOnlyList<Announcement> Apply(AccessibilityEvent accessibilityEvent)
    {
        return accessibilityEvent switch
        {
            ScreenEntered screenEntered => ApplyScreenEntered(screenEntered),
            ScreenExited screenExited => ApplyScreenExited(screenExited),
            FocusChanged focusChanged => ApplyFocusChanged(focusChanged),
            ValueChanged valueChanged => ApplyValueChanged(valueChanged),
            NameChanged nameChanged => ApplyNameChanged(nameChanged),
            ConfirmationOpened confirmationOpened => ApplyConfirmationOpened(confirmationOpened),
            TimedDescription timedDescription => ApplyTimedDescription(timedDescription),
            _ => NoAnnouncements,
        };
    }

    private IReadOnlyList<Announcement> ApplyScreenEntered(ScreenEntered screenEntered)
    {
        if (_activeScreen == screenEntered.Screen)
        {
            return NoAnnouncements;
        }

        _activeScreen = screenEntered.Screen;
        _lastSemanticIdentity = null;

        return screenEntered.Screen == ScreenKind.TitlePrompt
            ? [Interrupt("Chrono Trigger. Press confirm.")]
            : NoAnnouncements;
    }

    private IReadOnlyList<Announcement> ApplyScreenExited(ScreenExited screenExited)
    {
        if (_activeScreen != screenExited.Screen)
        {
            return NoAnnouncements;
        }

        _activeScreen = null;
        _lastSemanticIdentity = null;

        if (screenExited.Screen == ScreenKind.OpeningMovie)
        {
            Generation++;
        }

        return NoAnnouncements;
    }

    private IReadOnlyList<Announcement> ApplyFocusChanged(FocusChanged focusChanged)
    {
        if (_activeScreen is null || !Remember(focusChanged))
        {
            return NoAnnouncements;
        }

        var text = $"{focusChanged.Label}, {focusChanged.Position} of {focusChanged.Count}";
        if (focusChanged.Disabled)
        {
            text += ", unavailable";
        }

        return [Interrupt(text)];
    }

    private IReadOnlyList<Announcement> ApplyValueChanged(ValueChanged valueChanged)
    {
        if (_activeScreen is null || !Remember(valueChanged))
        {
            return NoAnnouncements;
        }

        var text = $"{valueChanged.Label}: {valueChanged.Value}";
        if (!string.IsNullOrWhiteSpace(valueChanged.Help))
        {
            text += $". {valueChanged.Help}";
        }

        return [Interrupt(text)];
    }

    private IReadOnlyList<Announcement> ApplyNameChanged(NameChanged nameChanged)
    {
        return _activeScreen is not null && Remember(nameChanged)
            ? [Interrupt(nameChanged.Name)]
            : NoAnnouncements;
    }

    private IReadOnlyList<Announcement> ApplyConfirmationOpened(ConfirmationOpened confirmationOpened)
    {
        if (_activeScreen is null || !Remember(confirmationOpened))
        {
            return NoAnnouncements;
        }

        var choice = confirmationOpened.Choices[confirmationOpened.SelectedIndex];
        return
        [
            Queue(confirmationOpened.Prompt),
            Queue($"{choice}, {confirmationOpened.SelectedIndex + 1} of {confirmationOpened.Choices.Count}"),
        ];
    }

    private IReadOnlyList<Announcement> ApplyTimedDescription(TimedDescription timedDescription)
    {
        return _activeScreen == ScreenKind.OpeningMovie && Generation == timedDescription.Generation
            ? [Queue(timedDescription.Text, timedDescription.Generation)]
            : NoAnnouncements;
    }

    private bool Remember(AccessibilityEvent accessibilityEvent)
    {
        var semanticIdentity = accessibilityEvent.ToString();
        if (semanticIdentity == _lastSemanticIdentity)
        {
            return false;
        }

        _lastSemanticIdentity = semanticIdentity;
        return true;
    }

    private static Announcement Interrupt(string text) =>
        new(text, AnnouncementPriority.Interrupt, Interrupt: true);

    private static Announcement Queue(string text, int? generation = null) =>
        new(text, AnnouncementPriority.Queued, Interrupt: false, generation);
}
