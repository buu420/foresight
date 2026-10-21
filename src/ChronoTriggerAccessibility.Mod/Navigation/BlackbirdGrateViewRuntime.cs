using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Speaks a Blackbird grate view while the game shows it. Runs at the field-script
/// dispatcher before the original instruction; it never writes input or game state, so the
/// player's own Confirm still ends the view. Only proven events change state: an unreadable
/// dispatch neither ends a view nor blocks the next one.</summary>
public sealed class BlackbirdGrateViewRuntime(Func<nint, int, BlackbirdGrateViewEvent?> capture,
    Func<int, string?> roomName, Action<string> speak, Func<bool>? repeatHeld = null, Action<string>? diagnostic = null)
{
    public const string Returned = "Returning to the ventilation shaft.";
    private readonly object gate = new();
    private bool enabled;
    private (uint Context, int Scene)? viewing;
    private bool repeatWasHeld;

    public void Enable() { lock (gate) { enabled = true; viewing = null; repeatWasHeld = false; } }
    public void Disable() { lock (gate) { enabled = false; viewing = null; repeatWasHeld = false; } }

    public void Dispatch(nint context, int opcode)
    {
        if (opcode is not (0xE3 or 0xE0)) return;
        try
        {
            lock (gate) if (enabled) Observe(capture(context, opcode));
        }
        catch (Exception exception)
        {
            lock (gate) viewing = null;
            try { diagnostic?.Invoke($"Grate view feedback recovered: {exception.GetType().Name}."); } catch { }
        }
    }

    private void Observe(BlackbirdGrateViewEvent? observed)
    {
        if (observed is not { } value) return;
        switch (value.Kind)
        {
            case BlackbirdGrateViewKind.Opening:
                // Shaft actor0 fn3: whatever came before, a new view starts here.
                viewing = null;
                break;
            case BlackbirdGrateViewKind.Viewing:
                var held = repeatHeld?.Invoke() == true;
                if (viewing != (value.Context, value.Scene))
                {
                    viewing = (value.Context, value.Scene);
                    speak(Announcement(value.Scene));
                }
                else if (held && !repeatWasHeld) speak(Announcement(value.Scene));
                repeatWasHeld = held;
                break;
            case BlackbirdGrateViewKind.Returning:
                if (viewing is not { } current || current.Context != value.Context) return;
                viewing = null;
                speak(Returned);
                break;
        }
    }

    private string Announcement(int scene)
    {
        var room = roomName(scene);
        if (string.IsNullOrWhiteSpace(room)) room = GameNavigationCatalog.AreaName(scene) ?? "the room below";
        return $"Looking through the grate into {room.Trim().TrimEnd('.')}. Press Confirm to return to the ventilation shaft.";
    }
}
