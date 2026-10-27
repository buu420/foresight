using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Menus;

/// <summary>
/// Speaks Lucca's scene-4 password letter panel as the game shows it. It wraps one field-script
/// instruction and polls AFTER the original returns, because show/hide and E3 change state inside
/// instructions. It never writes input, memory or timing, never names the expected letters, never
/// judges an entry and never keeps an entry history (the panel shows none).
/// <list type="bullet">
/// <item>A new session (first Shown frame for an owner) speaks the title, both rows and the focus.</item>
/// <item>A native focus change while shown speaks the letter, row and column.</item>
/// <item>The panel closes only when it is hidden AND native player control is back (E3 01, the
/// rejected-letter return). The hidden gap after an accepted letter keeps E3 00, so it is silent.</item>
/// <item>Absent frames, owner changes, foreground loss, Enable and Disable reset silently; success
/// and timeout end that way when the scene changes. Unknown (null) frames change nothing.</item>
/// </list>
/// </summary>
public sealed class PasswordPanelRuntime(Func<nint, int, PasswordPanelFrame?> capture, Func<bool> foreground,
    Action<string> speak, Action<string> diagnostic)
{
    public const string Title = "Password panel.";
    public const string Closed = "Password panel closed.";

    private readonly object gate = new();
    private bool enabled, open;
    private (uint Context, uint Field, uint Pad, uint Manager)? owner;
    private int? focus;

    public void Enable() { lock (gate) { enabled = true; Reset(); } }
    public void Disable() { lock (gate) { enabled = false; Reset(); } }

    public void Dispatch(nint context, int opcode, Action original)
    {
        ArgumentNullException.ThrowIfNull(original);
        // Native execution is owned by this single call and always happens first.
        try { original(); }
        catch { lock (gate) Reset(); throw; }
        // Only these native pad handlers can begin a session. Once shown,
        // poll every dispatch through focus changes, hide gaps and E3 closure.
        lock (gate) if (!enabled || !open && !CanShowPanel(opcode)) return;
        PasswordPanelFrame? frame;
        try { frame = capture(context, opcode); }
        catch (Exception exception)
        {
            Report($"Password panel capture failed: {exception.GetType().Name}.");
            return;
        }
        if (frame is null) return;
        if (frame.State == PasswordPanelState.Absent)
        {
            lock (gate) Reset();
            return;
        }
        bool focused;
        try { focused = foreground(); }
        catch (Exception) { focused = false; }
        lock (gate)
        {
            if (!enabled) return;
            if (!focused) { Reset(); return; }
            string? text;
            try { text = Observe(frame); }
            catch (Exception exception)
            {
                Reset();
                Report($"Password panel feedback failed: {exception.GetType().Name}.");
                return;
            }
            if (text is null) return;
            try { speak(text); }
            catch (Exception exception) { Report($"Password panel speech failed: {exception.GetType().Name}."); }
        }
    }

    private string? Observe(PasswordPanelFrame frame)
    {
        if (frame.State == PasswordPanelState.Absent) { Reset(); return null; }
        var identity = (frame.Context, frame.Field, frame.Pad, frame.Manager);
        if (owner != identity) { Reset(); owner = identity; }
        if (frame.State == PasswordPanelState.Hidden)
        {
            if (!open || !frame.ControlRestored) return null;
            open = false; focus = null;
            return Closed;
        }
        if (!open)
        {
            var rows = Describe(frame.Language);
            if (rows is null) { Report($"Password panel language {frame.Language} has no proven glyphs."); return null; }
            open = true; focus = frame.FocusKey;
            var located = frame.FocusKey is { } key ? Focus(frame.Language, key) : null;
            return located is null ? $"{Title} {rows}" : $"{Title} {rows} {located}";
        }
        if (frame.FocusKey is not { } moved || moved == focus) return null;
        focus = moved;
        return Focus(frame.Language, moved);
    }

    private static string? Describe(int language)
    {
        var glyphs = Enumerable.Range(0, PasswordPanelCapture.KeyCount).Select(key => PasswordPanelCapture.Glyph(language, key)).ToArray();
        if (glyphs.Any(glyph => glyph is null)) return null;
        var columns = PasswordPanelCapture.Columns;
        return $"Row 1: {string.Join(", ", glyphs[..columns])}. Row 2: {string.Join(", ", glyphs[columns..])}.";
    }

    private static string? Focus(int language, int key) =>
        PasswordPanelCapture.Glyph(language, key) is { } glyph
            ? $"{glyph}, row {key / PasswordPanelCapture.Columns + 1}, column {key % PasswordPanelCapture.Columns + 1}."
            : null;

    private void Reset() { open = false; owner = null; focus = null; }

    private static bool CanShowPanel(int opcode) => opcode is 0x2D or >= 0x34 and <= 0x39 or >= 0x3F and <= 0x44;

    private void Report(string message)
    {
        try { diagnostic(message); } catch (Exception) { }
    }
}
