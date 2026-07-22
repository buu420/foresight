using System.Collections.ObjectModel;
using ChronoTriggerAccessibility.Core.Announcements;
using ChronoTriggerAccessibility.Core.Events;

namespace ChronoTriggerAccessibility.Core.NewGame;

public abstract record NewGameAccessibilityEvent : AccessibilityEvent;

public sealed record ControlDescriptionLine(
    IReadOnlyList<string> ControllerArtwork,
    IReadOnlyList<string> VisibleTexts);

public sealed record ControlDescriptionsPresented(
    string Title,
    IReadOnlyList<ControlDescriptionLine> Lines,
    string NextLabel) : NewGameAccessibilityEvent;

public sealed record ControlDescriptionNextActivated(string Label) : NewGameAccessibilityEvent;

public sealed record ModeSelectRowPresentation(
    string Label,
    string Value,
    string Help);

public sealed record ModeSelectPresented(
    IReadOnlyList<ModeSelectRowPresentation> Rows,
    string StartLabel,
    string LowerHelp,
    int CompositeFocus) : NewGameAccessibilityEvent;

public sealed record ModeSelectChanged(
    IReadOnlyList<ModeSelectRowPresentation> Rows,
    string StartLabel,
    string LowerHelp,
    int CompositeFocus) : NewGameAccessibilityEvent;

public sealed record ModeSelectActivated(string Label) : NewGameAccessibilityEvent;
public sealed record ModeSelectCancelled : NewGameAccessibilityEvent;

public sealed record NameEntryPresented(
    string Instructions,
    string CurrentName,
    string Restriction) : NewGameAccessibilityEvent;

public sealed record NameGridFocused(
    string Label,
    string PageLabel,
    int Row,
    int Column) : NewGameAccessibilityEvent;

public sealed record NameGridVisibilityChanged(bool Open) : NewGameAccessibilityEvent;

public sealed record NewGameNameChanged(string Name) : NewGameAccessibilityEvent;

public sealed record NameActionFocused(
    string Label,
    int SelectedIndex,
    int Count) : NewGameAccessibilityEvent;

public sealed record NameActionActivated(string Label) : NewGameAccessibilityEvent;

public sealed record KeyboardNameEntryFocused(string CurrentName) : NewGameAccessibilityEvent;
public sealed record KeyboardNameEntryClosed(string CurrentName) : NewGameAccessibilityEvent;

public sealed record EmptyNameRejected : NewGameAccessibilityEvent;

public sealed record NameConfirmationPresented(
    string Prompt,
    IReadOnlyList<string> Choices,
    int SelectedIndex) : NewGameAccessibilityEvent;

public sealed record NameConfirmationFocused(
    string Label,
    int SelectedIndex,
    int Count) : NewGameAccessibilityEvent;

public sealed record NameAccessibilityBatch(
    IReadOnlyList<NewGameAccessibilityEvent> Events) : NewGameAccessibilityEvent;

public sealed record NewGameCoverageFailed(string Diagnostic) : NewGameAccessibilityEvent;

public sealed class NewGameNarrator
{
    private static readonly IReadOnlyList<Announcement> NoAnnouncements = Array.Empty<Announcement>();
    private bool faulted;
    private bool modeSelectActive;
    private bool nameEntryActive;
    private bool keyboardEntryActive;
    private string? lastModeText;
    private string? lastNameText;
    private string? lastGridText;
    private string? lastActionText;
    private string? lastConfirmationText;

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
                NameAccessibilityBatch batch => ApplyNameBatch(batch),
                ControlDescriptionsPresented control => PresentControlDescriptions(control),
                ControlDescriptionNextActivated activated => ActivateControlDescriptionNext(activated),
                ModeSelectPresented mode => PresentModeSelect(mode),
                ModeSelectChanged mode => ChangeModeSelect(mode),
                ModeSelectActivated activated => ActivateModeSelect(activated),
                ModeSelectCancelled => CancelModeSelect(),
                NameEntryPresented name => PresentNameEntry(name),
                NameGridFocused focus => FocusNameGrid(focus),
                NameGridVisibilityChanged visibility => ChangeGridVisibility(visibility),
                NewGameNameChanged changed => ChangeName(changed),
                NameActionFocused action => FocusNameAction(action),
                NameActionActivated activated => ActivateNameAction(activated),
                KeyboardNameEntryFocused focused => FocusKeyboardNameEntry(focused),
                KeyboardNameEntryClosed closed => CloseKeyboardNameEntry(closed),
                EmptyNameRejected => [Interrupt("A name is required.")],
                NameConfirmationPresented confirmation => PresentConfirmation(confirmation),
                NameConfirmationFocused focus => FocusConfirmation(focus),
                NewGameCoverageFailed failure => Fail(failure.Diagnostic),
                _ => NoAnnouncements,
            };
        }
        catch (Exception exception)
        {
            return Fail($"New Game semantic narration failed safely: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private IReadOnlyList<Announcement> ApplyNameBatch(NameAccessibilityBatch batch)
    {
        if (batch.Events is null || batch.Events.Count is < 1 or > 6 ||
            batch.Events.Any(item => item is null or NameAccessibilityBatch || item is not (
                NameEntryPresented or NameGridFocused or NameGridVisibilityChanged or
                NewGameNameChanged or NameActionFocused or NameActionActivated or
                KeyboardNameEntryFocused or KeyboardNameEntryClosed or EmptyNameRejected)))
        {
            return Fail("Name accessibility batch is null, nested, too large, or contains an unsupported event.");
        }

        var combined = new List<Announcement>();
        foreach (var item in batch.Events)
        {
            var next = item switch
            {
                NameEntryPresented entry => PresentNameEntry(entry),
                NameGridFocused focus => FocusNameGrid(focus),
                NameGridVisibilityChanged visibility => ChangeGridVisibility(visibility),
                NewGameNameChanged changed => ChangeName(changed),
                NameActionFocused focus => FocusNameAction(focus),
                NameActionActivated activated => ActivateNameAction(activated),
                KeyboardNameEntryFocused focused => FocusKeyboardNameEntry(focused),
                KeyboardNameEntryClosed closed => CloseKeyboardNameEntry(closed),
                EmptyNameRejected => new[] { Interrupt("A name is required.") },
                _ => NoAnnouncements,
            };
            if (faulted)
            {
                return next;
            }
            combined.AddRange(next);
        }

        return new ReadOnlyCollection<Announcement>(combined
            .Select((announcement, index) => index == 0
                ? announcement with
                {
                    Priority = AnnouncementPriority.Interrupt,
                    Interrupt = true,
                }
                : announcement with
                {
                    Priority = AnnouncementPriority.Queued,
                    Interrupt = false,
                })
            .ToArray());
    }

    private IReadOnlyList<Announcement> PresentControlDescriptions(ControlDescriptionsPresented presented)
    {
        if (string.IsNullOrWhiteSpace(presented.Title) ||
            string.IsNullOrWhiteSpace(presented.NextLabel) ||
            presented.Lines is null)
        {
            return Fail("Control Descriptions title, lines, or Next label is blank or null.");
        }

        var recordCount = 0;
        var lineText = new List<string>();
        foreach (var line in presented.Lines)
        {
            if (line is null || line.ControllerArtwork is null || line.VisibleTexts is null ||
                line.ControllerArtwork.Any(string.IsNullOrWhiteSpace) ||
                line.VisibleTexts.Any(string.IsNullOrWhiteSpace))
            {
                return Fail("Control Descriptions contains a null or blank localized line/artwork association.");
            }

            recordCount += line.VisibleTexts.Count;
            var text = string.Join(" ", line.VisibleTexts);
            if (line.ControllerArtwork.Count > 0)
            {
                text = $"{string.Join(", ", line.ControllerArtwork)}: {text}";
            }
            lineText.Add(text);
        }

        if (recordCount != 25)
        {
            return Fail($"Control Descriptions captured {recordCount} localized records; expected exactly 25.");
        }

        var announcements = new List<Announcement>(lineText.Count + 2)
        {
            Interrupt(WithPeriod(presented.Title)),
        };
        announcements.AddRange(lineText.Select(Queue));
        announcements.Add(Queue($"{presented.NextLabel}, 1 of 1"));
        return new ReadOnlyCollection<Announcement>(announcements);
    }

    private IReadOnlyList<Announcement> ActivateControlDescriptionNext(
        ControlDescriptionNextActivated activated)
    {
        if (string.IsNullOrWhiteSpace(activated.Label))
        {
            return Fail("Control Descriptions Next activation has no localized label.");
        }

        return [Interrupt($"{activated.Label} selected.")];
    }

    private IReadOnlyList<Announcement> PresentModeSelect(ModeSelectPresented presented)
    {
        if (!TryFormatMode(
                presented.Rows,
                presented.StartLabel,
                presented.LowerHelp,
                presented.CompositeFocus,
                out var focusText,
                out var error))
        {
            return Fail(error);
        }

        modeSelectActive = true;
        lastModeText = focusText;
        return [Interrupt("New Game settings."), Queue(focusText)];
    }

    private IReadOnlyList<Announcement> ChangeModeSelect(ModeSelectChanged changed)
    {
        if (!modeSelectActive)
        {
            return Fail("Mode Select changed without a validated screen entry.");
        }

        if (!TryFormatMode(
                changed.Rows,
                changed.StartLabel,
                changed.LowerHelp,
                changed.CompositeFocus,
                out var focusText,
                out var error))
        {
            return Fail(error);
        }

        if (string.Equals(lastModeText, focusText, StringComparison.Ordinal))
        {
            return NoAnnouncements;
        }
        lastModeText = focusText;
        return [Interrupt(focusText)];
    }

    private IReadOnlyList<Announcement> ActivateModeSelect(ModeSelectActivated activated)
    {
        if (!modeSelectActive || string.IsNullOrWhiteSpace(activated.Label))
        {
            return Fail("Mode Select activation has no validated active screen or localized label.");
        }
        modeSelectActive = false;
        lastModeText = null;
        return [Interrupt($"{activated.Label} selected.")];
    }

    private IReadOnlyList<Announcement> CancelModeSelect()
    {
        if (!modeSelectActive)
        {
            return NoAnnouncements;
        }
        modeSelectActive = false;
        lastModeText = null;
        return [Interrupt("New Game settings closed.")];
    }

    private static bool TryFormatMode(
        IReadOnlyList<ModeSelectRowPresentation>? rows,
        string startLabel,
        string lowerHelp,
        int compositeFocus,
        out string text,
        out string error)
    {
        text = string.Empty;
        if (rows is null || rows.Count != 3)
        {
            error = "Mode Select must contain exactly three localized setting rows.";
            return false;
        }
        if (rows.Any(row => row is null ||
                string.IsNullOrWhiteSpace(row.Label) ||
                string.IsNullOrWhiteSpace(row.Value) ||
                string.IsNullOrWhiteSpace(row.Help)) ||
            string.IsNullOrWhiteSpace(startLabel) ||
            string.IsNullOrWhiteSpace(lowerHelp))
        {
            error = "Mode Select contains a null or blank label, value, help, Start label, or lower help.";
            return false;
        }

        if (compositeFocus == 30)
        {
            text = $"{startLabel}, 4 of 4. {lowerHelp}";
            error = string.Empty;
            return true;
        }

        var rowIndex = compositeFocus / 10;
        var subfocus = compositeFocus % 10;
        if (compositeFocus < 0 || rowIndex >= rows.Count || subfocus is not 0 and not 1)
        {
            error = $"Mode Select composite focus {compositeFocus} is outside the audited focus keys.";
            return false;
        }

        var row = rows[rowIndex];
        var control = subfocus == 0 ? "Left" : "Right";
        text = $"{row.Label}: {row.Value}. {row.Help} {control} control, {rowIndex + 1} of 4";
        error = string.Empty;
        return true;
    }

    private IReadOnlyList<Announcement> PresentNameEntry(NameEntryPresented presented)
    {
        if (string.IsNullOrWhiteSpace(presented.Instructions) ||
            string.IsNullOrWhiteSpace(presented.Restriction) ||
            presented.CurrentName is null ||
            presented.CurrentName.Length > 5)
        {
            return Fail("Name Entry instructions/restriction is blank or the current name exceeds five UTF-16 code units.");
        }

        modeSelectActive = false;
        lastModeText = null;
        nameEntryActive = true;
        keyboardEntryActive = false;
        lastNameText = presented.CurrentName;
        lastGridText = null;
        lastActionText = null;
        lastConfirmationText = null;
        var current = presented.CurrentName.Length == 0 ? "empty" : presented.CurrentName;
        return [Interrupt(
            $"{WithoutTerminalPeriod(presented.Instructions)}. Current name: {current}. Maximum five characters. {WithoutTerminalPeriod(presented.Restriction)}.")];
    }

    private IReadOnlyList<Announcement> FocusNameGrid(NameGridFocused focus)
    {
        if (!nameEntryActive || string.IsNullOrWhiteSpace(focus.Label) ||
            string.IsNullOrWhiteSpace(focus.PageLabel) ||
            focus.Row is < 0 or > 7 || focus.Column is < 0 or > 10)
        {
            return Fail("Name Entry grid focus is outside the validated page, row, column, or label state.");
        }
        var text = $"{focus.Label}. {focus.PageLabel} page, row {focus.Row + 1}, column {focus.Column + 1}.";
        if (string.Equals(lastGridText, text, StringComparison.Ordinal))
        {
            return NoAnnouncements;
        }
        lastGridText = text;
        return [Interrupt(text)];
    }

    private IReadOnlyList<Announcement> ChangeGridVisibility(NameGridVisibilityChanged visibility)
    {
        if (!nameEntryActive)
        {
            return Fail("Name Entry grid visibility changed without a validated active screen.");
        }
        lastGridText = null;
        lastActionText = null;
        return visibility.Open
            ? NoAnnouncements
            : [Interrupt("Character grid closed.")];
    }

    private IReadOnlyList<Announcement> ChangeName(NewGameNameChanged changed)
    {
        if (!nameEntryActive || changed.Name is null || changed.Name.Length > 5)
        {
            return Fail("Name Entry resulting name is unavailable or exceeds five UTF-16 code units.");
        }
        if (string.Equals(lastNameText, changed.Name, StringComparison.Ordinal))
        {
            return NoAnnouncements;
        }
        lastNameText = changed.Name;
        return [Interrupt(changed.Name.Length == 0 ? "Name is empty." : $"Name: {changed.Name}.")];
    }

    private IReadOnlyList<Announcement> FocusNameAction(NameActionFocused focused)
    {
        if (!nameEntryActive || string.IsNullOrWhiteSpace(focused.Label) ||
            focused.Count != 3 ||
            focused.SelectedIndex < 0 || focused.SelectedIndex >= focused.Count)
        {
            return Fail("Name Entry action focus is missing its exact control correlation or has an invalid key.");
        }
        var text = $"{focused.Label}, {focused.SelectedIndex + 1} of {focused.Count}";
        if (string.Equals(lastActionText, text, StringComparison.Ordinal))
        {
            return NoAnnouncements;
        }
        lastActionText = text;
        return [Interrupt(text)];
    }

    private IReadOnlyList<Announcement> ActivateNameAction(NameActionActivated activated)
    {
        if (!nameEntryActive || string.IsNullOrWhiteSpace(activated.Label))
        {
            return Fail("Name Entry action activation has no validated active screen or localized label.");
        }
        return [Interrupt($"{activated.Label} selected.")];
    }

    private IReadOnlyList<Announcement> FocusKeyboardNameEntry(KeyboardNameEntryFocused focused)
    {
        if (!nameEntryActive || focused.CurrentName is null ||
            focused.CurrentName.Length > 5)
        {
            return Fail("Keyboard name entry opened without a validated current name.");
        }
        keyboardEntryActive = true;
        lastActionText = null;
        var current = focused.CurrentName.Length == 0 ? "empty" : focused.CurrentName;
        return [Interrupt($"Keyboard name entry. Current name: {current}.")];
    }

    private IReadOnlyList<Announcement> CloseKeyboardNameEntry(KeyboardNameEntryClosed closed)
    {
        if (!nameEntryActive || !keyboardEntryActive || closed.CurrentName is null ||
            closed.CurrentName.Length > 5)
        {
            return Fail("Keyboard name entry closed without a validated active keyboard entry or current name.");
        }
        keyboardEntryActive = false;
        lastNameText = closed.CurrentName;
        var current = closed.CurrentName.Length == 0 ? "empty" : closed.CurrentName;
        return [Interrupt($"Keyboard name entry closed. Current name: {current}.")];
    }

    private IReadOnlyList<Announcement> PresentConfirmation(NameConfirmationPresented presented)
    {
        if (!nameEntryActive || string.IsNullOrWhiteSpace(presented.Prompt) ||
            presented.Choices is null || presented.Choices.Count != 2 ||
            presented.Choices.Any(string.IsNullOrWhiteSpace) ||
            presented.SelectedIndex is < 0 or >= 2)
        {
            return Fail("Name confirmation is missing its prompt, two correlated choices, or selected manager key.");
        }
        var choice = presented.Choices[presented.SelectedIndex];
        lastConfirmationText = $"{choice}, {presented.SelectedIndex + 1} of 2";
        return [Interrupt(presented.Prompt), Queue(lastConfirmationText)];
    }

    private IReadOnlyList<Announcement> FocusConfirmation(NameConfirmationFocused focused)
    {
        if (!nameEntryActive || string.IsNullOrWhiteSpace(focused.Label) ||
            focused.Count != 2 || focused.SelectedIndex is < 0 or >= 2)
        {
            return Fail("Name confirmation focus has no correlated localized manager key.");
        }
        var text = $"{focused.Label}, {focused.SelectedIndex + 1} of {focused.Count}";
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
        modeSelectActive = false;
        nameEntryActive = false;
        var exact = string.IsNullOrWhiteSpace(diagnostic)
            ? "New Game accessibility coverage failed without a diagnostic."
            : diagnostic;
        return [Interrupt($"Chrono Trigger accessibility stopped: {exact}")];
    }

    private static string WithPeriod(string text) =>
        $"{WithoutTerminalPeriod(text)}.";

    private static string WithoutTerminalPeriod(string text) =>
        text.Trim().TrimEnd('.');

    private static Announcement Interrupt(string text) =>
        new(text, AnnouncementPriority.Interrupt, Interrupt: true);

    private static Announcement Queue(string text) =>
        new(text, AnnouncementPriority.Queued, Interrupt: false);
}
