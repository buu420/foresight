using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Mod.Runtime;

namespace ChronoTriggerAccessibility.Mod.Intro;

public sealed class IntroTraceDispatcher(ISemanticEventDispatcher inner, IntroTraceRecorder recorder)
    : ISemanticEventDispatcher
{
    public int Generation => inner.Generation;
    public void Attach(IRuntimePrismSession session) => inner.Attach(session);
    public void Detach(IRuntimePrismSession session) => inner.Detach(session);
    public void RecordDiagnostic(string message) => inner.RecordDiagnostic(message);
    public void ReportCoverageFailure(string message) => inner.ReportCoverageFailure(message);
    public void Publish(AccessibilityEvent value)
    {
        try { recorder.Observe(value); }
        catch (Exception) { /* Keep diagnostic observation independent of speech. */ }
        inner.Publish(value);
    }
}
