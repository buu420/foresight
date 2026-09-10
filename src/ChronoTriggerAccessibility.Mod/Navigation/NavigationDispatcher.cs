using ChronoTriggerAccessibility.Core.Dialogue;
using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Core.Menus;
using ChronoTriggerAccessibility.Core.NewGame;
using ChronoTriggerAccessibility.Mod.Runtime;

namespace ChronoTriggerAccessibility.Mod.Navigation;

public sealed class NavigationDispatcher(ISemanticEventDispatcher inner, FieldNavigationRuntime navigation)
    : ISemanticEventDispatcher
{
    public int Generation => inner.Generation;
    public void Attach(IRuntimePrismSession session) => inner.Attach(session);
    public void Detach(IRuntimePrismSession session) { navigation.Disable(); inner.Detach(session); }
    public void RecordDiagnostic(string message) => inner.RecordDiagnostic(message);
    public void ReportCoverageFailure(string message)
    {
        navigation.Disable();
        inner.ReportCoverageFailure(message);
    }
    public void Publish(AccessibilityEvent value)
    {
        if (value is StartupSceneEntered or NewGameAccessibilityEvent) navigation.ResetDiscoveries();
        if (value is DialogueAccessibilityEvent) navigation.Suspend("dialogue");
        else if (value is MenuAccessibilityEvent or NewGameAccessibilityEvent or
            StartupSceneEntered or ScreenEntered or ConfirmationOpened) navigation.Suspend("menu or scene changed");
        inner.Publish(value);
    }
}
