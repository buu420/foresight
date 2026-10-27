using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.AudioDescriptions;

/// <summary>
/// Says which earlier boss a Lavos shell flashback shows: "Lavos flashback: &lt;native name&gt;."
/// once per real flashback scene entry. The capture runs BEFORE the original instruction (it needs
/// the live opcode byte at PC); the original always runs exactly once.
/// <list type="bullet">
/// <item>Names come only from the native lookups root supplies: monster.txt (bank 0x32) and, for
/// Magus, the current character-6 name first. Unresolved tokens and blanks are unavailable; then
/// nothing is spoken and one diagnostic is logged per entry.</item>
/// <item>Outside frames (a proven other scene) rearm; null frames and other instructions inside
/// the flashback scene do not. A new owner is a new entry.</item>
/// <item>Foreground loss resets; a flashback is not marked spoken until it was actually spoken.</item>
/// </list>
/// No form index, global, battle id, HP, strategy or debug scene name is used or spoken.
/// </summary>
public sealed class LavosFlashbackRuntime(Func<nint, int, LavosFlashbackFrame?> capture, Func<bool> foreground,
    Func<int, string?> monsterName, Func<int, string?> characterName, Action<string> speak, Action<string> diagnostic)
{
    public const string Prefix = "Lavos flashback: ";

    private readonly object gate = new();
    private bool enabled;
    private (uint Context, uint Data, uint Actors, uint Field, int Scene, int ScriptId)? spoken, unavailable;

    public void Enable() { lock (gate) { enabled = true; Reset(); } }
    public void Disable() { lock (gate) { enabled = false; Reset(); } }

    public void Dispatch(nint context, int opcode, Action original)
    {
        ArgumentNullException.ThrowIfNull(original);
        bool observing;
        // AD is the only caption anchor. After a spoken or withheld caption,
        // retain observation until a coherent scene/owner change rearms it.
        lock (gate) observing = enabled && (opcode == 0xAD || spoken is not null || unavailable is not null);
        LavosFlashbackFrame? frame = null;
        if (observing)
        {
            try { frame = capture(context, opcode); }
            catch (Exception exception) { Report($"Lavos flashback capture failed: {exception.GetType().Name}."); }
        }
        // Native execution is owned by this single call, even when observation fails.
        try { original(); }
        catch { lock (gate) Reset(); throw; }
        if (frame is null) return;
        try
        {
            if (frame.Kind == LavosFlashbackKind.Outside) { lock (gate) if (enabled) Reset(); return; }
            if (frame.Subject is not { } subject) return;
            var focused = foreground();
            lock (gate)
            {
                if (!enabled) return;
                if (!focused) { Reset(); return; }
                var identity = (frame.Context, frame.Data, frame.Actors, frame.Field, frame.Scene, frame.ScriptId);
                if (spoken == identity) return;
                var label = Name(subject);
                if (label is null)
                {
                    if (unavailable != identity) Report($"Lavos flashback name for {subject} is unavailable.");
                    unavailable = identity;
                    return;
                }
                spoken = identity; unavailable = null;
                speak($"{Prefix}{label}.");
            }
        }
        catch (Exception exception)
        {
            lock (gate) Reset();
            Report($"Lavos flashback feedback failed: {exception.GetType().Name}.");
        }
    }

    private string? Name(LavosFlashbackSubject subject)
    {
        var label = LavosFlashbackCapture.Label(subject);
        if (label.Character is { } character && Clean(characterName(character)) is { } current) return current;
        var names = new List<string>();
        foreach (var monster in label.Monsters)
        {
            if (Clean(monsterName(monster)) is not { } name) return null;
            names.Add(name);
        }
        return names.Count == 0 ? null : string.Join(" and ", names);
    }

    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains('<') || text.Contains('>')) return null;
        var trimmed = text.Trim().TrimEnd('.').Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private void Reset() { spoken = null; unavailable = null; }

    private void Report(string message)
    {
        try { diagnostic(message); } catch (Exception) { }
    }
}
