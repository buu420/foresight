using ChronoTriggerAccessibility.Core.Events;

namespace ChronoTriggerAccessibility.Core.Navigation;

public sealed record NavigationAnnouncement(string Text) : AccessibilityEvent;
