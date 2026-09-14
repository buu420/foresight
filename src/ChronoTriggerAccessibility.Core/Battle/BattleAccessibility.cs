using ChronoTriggerAccessibility.Core.Events;

namespace ChronoTriggerAccessibility.Core.Battle;

public abstract record BattleAccessibilityEvent(long BattleId) : AccessibilityEvent;
public sealed record BattleStarted(long BattleId) : BattleAccessibilityEvent(BattleId);
public sealed record BattleEnded(long BattleId) : BattleAccessibilityEvent(BattleId);
public sealed record BattleFocusChanged(long BattleId, string? Identity, string? Text) : BattleAccessibilityEvent(BattleId);
public sealed record BattleFeedbackPresented(long BattleId, long Serial, string Text) : BattleAccessibilityEvent(BattleId);
public sealed record BattleInspectionRequested(long BattleId, string Text) : BattleAccessibilityEvent(BattleId);
