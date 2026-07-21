namespace ChronoTriggerAccessibility.Core.Announcements;

public sealed record Announcement(
    string Text,
    AnnouncementPriority Priority,
    bool Interrupt,
    int? Generation = null);
