using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Core.Battle;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Battle;

public sealed record BattlePartyMember(int Slot, string Name, int Hp, int MaximumHp, int Mp, int MaximumMp, string? Status);
public sealed record BattleFrame(IReadOnlyList<BattlePartyMember> Party, string? FocusIdentity, string? FocusText,
    IReadOnlyDictionary<int, string> BattlerNames);

public sealed class BattleRuntime(Action<AccessibilityEvent> publish, BattleKeyboard keyboard,
    Func<bool> foreground, Action<bool> activeChanged)
{
    private readonly object gate = new();
    private nuint currentOwner;
    private long battleId, serial;
    private int selectedSlot;
    private bool announced, wasForeground;
    private BattleFrame? current;
    private (string? Identity, string? Text) lastFocus;
    private string? lastMessage;
    private readonly Dictionary<int, (string Name, string Status)> statuses = [];
    private readonly Dictionary<int, string> names = [];
    private readonly Dictionary<int, PendingPopup> pending = [];
    private sealed record PendingPopup(long Serial, int Kind, string? Name);

    public bool IsActive { get { lock (gate) return currentOwner != 0; } }

    public void Observe(nuint owner, BattleFrame? frame)
    {
        if (owner == 0) return;
        lock (gate)
        {
            if (currentOwner != owner)
            {
                End(currentOwner);
                currentOwner = owner; battleId++; selectedSlot = 0;
                announced = false; wasForeground = false; lastFocus = default;
                names.Clear(); pending.Clear(); statuses.Clear(); lastMessage = null; keyboard.Suspend();
                activeChanged(true);
            }
            var previous = current;
            current = frame is null ? null : frame with
            {
                Party = frame.Party.ToArray(), BattlerNames = new Dictionary<int,string>(frame.BattlerNames),
            };
            if (current is not null)
                foreach (var pair in current.BattlerNames) names[pair.Key] = pair.Value;
            var commands = keyboard.Poll(); // Poll even in background to consume held-key edges.
            if (!foreground()) { wasForeground = false; pending.Clear(); return; }
            EnsureAnnounced();
            if (!wasForeground)
            {
                publish(new BattleFocusChanged(battleId, null, null)); lastFocus = default;
            }
            wasForeground = true;
            var nextFocus = (current?.FocusIdentity, current?.FocusText);
            if (lastFocus != nextFocus)
            {
                lastFocus = nextFocus;
                publish(new BattleFocusChanged(battleId, nextFocus.Item1, nextFocus.Item2));
            }
            if (current is not null)
            {
                foreach (var member in current.Party)
                {
                    if (member.Status is { } status)
                    {
                        statuses.TryGetValue(member.Slot, out var known);
                        if (known.Name != member.Name) known = (member.Name, "");
                        if (status != known.Status)
                            Feedback(string.IsNullOrWhiteSpace(status)
                                ? $"{member.Name}. {known.Status} ended." : $"{member.Name}. {status}.");
                        statuses[member.Slot] = (member.Name, status);
                    }
                    var before = previous?.Party.FirstOrDefault(x => x.Slot == member.Slot && x.Name == member.Name);
                    if (before?.Hp > 0 && member.Hp == 0) Feedback($"{member.Name}. HP zero.");
                }
            }
            foreach (var command in commands) Handle(command);
        }
    }

    public void Handle(BattleCommand command)
    {
        lock (gate)
        {
            if (currentOwner == 0 || !foreground()) return;
            EnsureAnnounced();
            if (current is null) { Inspect("Battle information is unavailable."); return; }
            if (command is BattleCommand.SelectFirst or BattleCommand.SelectSecond or BattleCommand.SelectThird)
            {
                var slot = command switch
                {
                    BattleCommand.SelectFirst => 0,
                    BattleCommand.SelectSecond => 1,
                    BattleCommand.SelectThird => 2,
                    _ => throw new ArgumentOutOfRangeException(nameof(command)),
                };
                var member = current.Party.FirstOrDefault(x => x.Slot == slot);
                if (member is null) Inspect($"Party slot {slot + 1} is empty.");
                else { selectedSlot = slot; Inspect($"{member.Name}."); }
                return;
            }
            if (command == BattleCommand.Repeat)
            {
                Inspect(string.IsNullOrWhiteSpace(current.FocusText)
                    ? "No battle command is currently selected." : current.FocusText);
                return;
            }
            var selected = current.Party.FirstOrDefault(x => x.Slot == selectedSlot);
            if (selected is null) { Inspect($"Party slot {selectedSlot + 1} is empty."); return; }
            if (command == BattleCommand.ReadHp) Inspect($"{selected.Name}. HP {selected.Hp} of {selected.MaximumHp}.");
            else if (command == BattleCommand.ReadMp) Inspect($"{selected.Name}. MP {selected.Mp} of {selected.MaximumMp}.");
        }
    }

    public void MarkPopup(nuint owner, int slot, int kind)
    {
        lock (gate)
        {
            if (owner == 0 || owner != currentOwner || slot is < 0 or > 10) return;
            if (!foreground()) { pending.Remove(slot); return; }
            names.TryGetValue(slot, out var name);
            pending[slot] = new(++serial, kind, name);
        }
    }

    public void RenderPopups(nuint owner, IReadOnlyList<BattlePopupSnapshot> popups)
    {
        lock (gate)
        {
            if (owner == 0 || owner != currentOwner) return;
            if (!foreground()) { pending.Clear(); return; }
            foreach (var popup in popups)
            {
                if (!pending.Remove(popup.Slot, out var presentation)) continue;
                var name = presentation.Name ?? names.GetValueOrDefault(popup.Slot);
                if (string.IsNullOrWhiteSpace(name)) name = BattleIdentity.Unnamed(popup.Slot);
                // The native presentation proves recovery versus damage. Its two
                // motion tables do not yet prove an HP versus MP unit, so do not
                // append one to the displayed number. H/M use the audited HUD.
                var text = presentation.Kind switch
                {
                    1 or 2 => $"{name} recovers {popup.Text}.",
                    3 or 4 => $"{name} takes {popup.Text} damage.",
                    _ => $"{name}. {popup.Text}",
                };
                EnsureAnnounced();
                publish(new BattleFeedbackPresented(battleId, presentation.Serial, text));
            }
        }
    }

    public void Message(nuint owner, string? text)
    {
        lock (gate)
        {
            if (owner == 0 || owner != currentOwner) return;
            text = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            if (text == lastMessage) return;
            lastMessage = text;
            if (foreground() && text is not null) { EnsureAnnounced(); Feedback(text); }
        }
    }

    public void End(nuint owner)
    {
        lock (gate)
        {
            if (owner == 0 || owner != currentOwner) return;
            currentOwner = 0; current = null; pending.Clear(); names.Clear(); statuses.Clear();
            lastFocus = default; lastMessage = null;
            keyboard.Suspend(); activeChanged(false);
            if (announced && foreground()) publish(new BattleEnded(battleId));
            announced = false;
        }
    }

    public void Disable() { lock (gate) End(currentOwner); }
    private void EnsureAnnounced() { if (!announced) { publish(new BattleStarted(battleId)); announced = true; } }
    private void Inspect(string text) => publish(new BattleInspectionRequested(battleId, text));
    private void Feedback(string text) => publish(new BattleFeedbackPresented(battleId, ++serial, text));
}
