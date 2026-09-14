using ChronoTriggerAccessibility.Core.Announcements;

namespace ChronoTriggerAccessibility.Core.Battle;

public sealed class BattleNarrator
{
    private long? active;
    private (string? Identity, string? Text) focus;
    private readonly HashSet<long> delivered = [];
    private readonly Queue<long> deliveryOrder = [];

    public IReadOnlyList<Announcement> Apply(BattleAccessibilityEvent value)
    {
        if (value is BattleStarted started)
        {
            if (active == started.BattleId) return [];
            active = started.BattleId;
            focus = default;
            delivered.Clear(); deliveryOrder.Clear();
            return [Speak("Battle.", true)];
        }
        if (active != value.BattleId) return [];
        switch (value)
        {
            case BattleEnded:
                active = null; focus = default; delivered.Clear(); deliveryOrder.Clear();
                return [Speak("Battle ended.", false)];
            case BattleFocusChanged changed:
                var next = (changed.Identity, changed.Text);
                if (focus == next) return [];
                focus = next;
                return string.IsNullOrWhiteSpace(changed.Text) ? [] : [Speak(changed.Text, true)];
            case BattleInspectionRequested requested when !string.IsNullOrWhiteSpace(requested.Text):
                return [Speak(requested.Text, true)];
            case BattleFeedbackPresented feedback when !string.IsNullOrWhiteSpace(feedback.Text):
                if (!delivered.Add(feedback.Serial)) return [];
                deliveryOrder.Enqueue(feedback.Serial);
                if (deliveryOrder.Count > 4096) delivered.Remove(deliveryOrder.Dequeue());
                return [Speak(feedback.Text, false)];
            default:
                return [];
        }
    }

    private static Announcement Speak(string text, bool interrupt) =>
        new(text, interrupt ? AnnouncementPriority.Interrupt : AnnouncementPriority.Queued, interrupt);
}
