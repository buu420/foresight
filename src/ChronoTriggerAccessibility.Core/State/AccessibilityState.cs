using ChronoTriggerAccessibility.Core.Announcements;
using ChronoTriggerAccessibility.Core.Events;

namespace ChronoTriggerAccessibility.Core.State;

public sealed class AccessibilityState
{
    private static readonly IReadOnlyList<Announcement> NoAnnouncements = Array.Empty<Announcement>();

    private ScreenKind? _activeScreen;
    private StartupSceneKind? _startupScene;
    private bool _titleAnnouncedForCurrentScene;
    private AccessibilityEvent? _lastSemanticIdentity;

    public int Generation { get; private set; }

    public IReadOnlyList<Announcement> Apply(AccessibilityEvent accessibilityEvent)
    {
        return accessibilityEvent switch
        {
            StartupSceneEntered startupSceneEntered => ApplyStartupSceneEntered(startupSceneEntered),
            ScreenEntered screenEntered => ApplyScreenEntered(screenEntered),
            ScreenExited screenExited => ApplyScreenExited(screenExited),
            FocusChanged focusChanged => ApplyFocusChanged(focusChanged),
            ValueChanged valueChanged => ApplyValueChanged(valueChanged),
            NameChanged nameChanged => ApplyNameChanged(nameChanged),
            ConfirmationOpened confirmationOpened => ApplyConfirmationOpened(confirmationOpened),
            TimedDescription timedDescription => ApplyTimedDescription(timedDescription),
            ControlActivated controlActivated => ApplyControlActivated(controlActivated),
            _ => NoAnnouncements,
        };
    }

    private IReadOnlyList<Announcement> ApplyStartupSceneEntered(StartupSceneEntered entered)
    {
        if (_startupScene == entered.Scene)
        {
            return NoAnnouncements;
        }

        if (_activeScreen == ScreenKind.OpeningMovie && entered.Scene != StartupSceneKind.OpeningMovie)
        {
            Generation++;
        }

        _startupScene = entered.Scene;
        _activeScreen = entered.Scene == StartupSceneKind.OpeningMovie
            ? ScreenKind.OpeningMovie
            : null;
        _titleAnnouncedForCurrentScene = entered.Scene == StartupSceneKind.Title;
        _lastSemanticIdentity = null;

        return entered.Scene switch
        {
            StartupSceneKind.SquareEnixLogo => [Interrupt("Square Enix.")],
            StartupSceneKind.OpeningMovie => [Interrupt("Opening movie.")],
            StartupSceneKind.Title => [Interrupt("Chrono Trigger.")],
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
            ? [Interrupt(_titleAnnouncedForCurrentScene ? "Press confirm." : "Chrono Trigger. Press confirm.")]
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
        if (_activeScreen != ScreenKind.OpeningMovie ||
            Generation != timedDescription.Generation ||
            !Remember(timedDescription))
        {
            return NoAnnouncements;
        }

        return [Queue(timedDescription.Text, timedDescription.Generation)];
    }

    private IReadOnlyList<Announcement> ApplyControlActivated(ControlActivated activated)
    {
        if (_activeScreen is null || string.IsNullOrWhiteSpace(activated.Label) || !Remember(activated))
        {
            return NoAnnouncements;
        }

        return [Interrupt($"{activated.Label} selected.")];
    }

    private bool Remember(AccessibilityEvent accessibilityEvent)
    {
        if (HasSameSemanticIdentity(_lastSemanticIdentity, accessibilityEvent))
        {
            return false;
        }

        _lastSemanticIdentity = Snapshot(accessibilityEvent);
        return true;
    }

    private static bool HasSameSemanticIdentity(
        AccessibilityEvent? previous,
        AccessibilityEvent current)
    {
        if (previous is ConfirmationOpened previousConfirmation &&
            current is ConfirmationOpened currentConfirmation)
        {
            return previousConfirmation.Prompt == currentConfirmation.Prompt &&
                previousConfirmation.SelectedIndex == currentConfirmation.SelectedIndex &&
                previousConfirmation.Choices.SequenceEqual(currentConfirmation.Choices, StringComparer.Ordinal);
        }

        return previous == current;
    }

    private static AccessibilityEvent Snapshot(AccessibilityEvent accessibilityEvent)
    {
        return accessibilityEvent is ConfirmationOpened confirmationOpened
            ? confirmationOpened with { Choices = confirmationOpened.Choices.ToArray() }
            : accessibilityEvent;
    }

    private static Announcement Interrupt(string text) =>
        new(text, AnnouncementPriority.Interrupt, Interrupt: true);

    private static Announcement Queue(string text, int? generation = null) =>
        new(text, AnnouncementPriority.Queued, Interrupt: false, generation);
}
