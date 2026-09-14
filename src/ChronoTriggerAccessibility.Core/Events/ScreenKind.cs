namespace ChronoTriggerAccessibility.Core.Events;

public enum ScreenKind
{
    OpeningMovie,
    TitlePrompt,
    TitleMenu,
    NewGameConfiguration,
    NameEntry,

    /// <summary>The native save/load/bookmark node, including the Resume confirmation the
    /// title screen opens directly into.</summary>
    SaveLoad,
}
