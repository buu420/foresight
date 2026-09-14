using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Core.Menus;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Menus;

/// <summary>Native callback-driven submenu narration. It never synthesizes game input.</summary>
public sealed class FieldSubmenuSession(
    Func<nuint, FieldSubmenuSnapshot?> capture,
    Func<nuint, string?> title,
    Func<nuint, bool> confirmationActive,
    Func<bool> foreground,
    Action<AccessibilityEvent> publish,
    Action<string> diagnostic,
    Func<long>? clock = null)
{
    private readonly Func<long> now = clock ?? (() => Environment.TickCount64);
    private long? failureStarted;
    private nuint node;
    private MenuOwner? owner;
    private FieldSubmenuSnapshot? last;
    private bool paused, failedSelection;
    public bool HasContext => node != 0;
    public nuint Node => node;
    public void Pause(nuint value) { if (node == value && node != 0) paused = true; }

    public void Enter(nuint value)
    {
        if (node != 0 && node == value) { Refresh(); return; }
        Close();
        if (value == 0 || title(value) is null) return;
        node = value;
        owner = new("field-submenu", (ulong)value);
        Refresh();
    }
    public void Close(nuint value = 0)
    {
        if (value != 0 && node != value) return;
        var previousOwner = owner;
        node = 0; owner = null; last = null; paused = false; failedSelection = false; failureStarted = null;
        if (previousOwner is not null) publish(new MenuExited(previousOwner));
    }
    public void Refresh(nuint value = 0)
    {
        if (node == 0 || owner is null || (value != 0 && node != value) || !foreground()) return;
        if (confirmationActive(node)) { paused = true; failureStarted = null; return; }
        var currentTitle = title(node);
        if (currentTitle is null) { Close(); return; }
        var next = capture(node);
        if (next is null)
        {
            if (failureStarted is null)
            {
                failureStarted = now();
                diagnostic($"Submenu capture unavailable: node=0x{node:X}, title={currentTitle}.");
            }
            // Native page builders briefly replace the selection and its detail panels. Retry
            // those redraws without interrupting speech, but make persistent failure audible.
            if (!failedSelection && now() - failureStarted.Value >= 500)
            {
                publish(new MenuContentPresented(owner, currentTitle, "Unable to read the current selection."));
                failedSelection = true;
            }
            return;
        }
        if (last is null || paused || failedSelection)
            publish(new MenuContentPresented(owner, next.Title, next.Text));
        else if (next.FocusIdentity != last.FocusIdentity || next.Text != last.Text)
            publish(new MenuContentChanged(owner, next.Text));
        last = next; paused = false; failedSelection = false; failureStarted = null;
    }
}
