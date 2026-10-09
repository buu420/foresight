namespace ChronoTriggerAccessibility.Native.Capture;

/// <summary>
/// One coherent read of a field main-menu submenu page.
/// <para><see cref="Kind"/> names the page from the node's own class, never from the top-menu row
/// the player came through, so a page opened by any route identifies itself the same way.
/// <see cref="Title"/> is the caption the node loaded for itself. <see cref="FocusIdentity"/> is a
/// stable machine key for the current selection so a consumer can suppress unchanged frames, and
/// <see cref="Text"/> is what the page is presenting for that selection.</para>
/// <para>Every field is non-null and non-empty: a page that cannot produce all four is reported as
/// no snapshot at all rather than as a partly filled one.</para>
/// </summary>
public sealed record FieldSubmenuSnapshot(string Kind, string Title, string FocusIdentity, string Text,
    string? FocusText = null, string? SupplementalText = null);
