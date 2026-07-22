using ChronoTriggerAccessibility.Prism;

namespace ChronoTriggerAccessibility.Mod.Runtime;

public sealed class PrismRuntimeFactory : IRuntimePrismFactory
{
    public IRuntimePrismSession Create() => new PrismRuntimeSession(new PrismSession());

    private sealed class PrismRuntimeSession(PrismSession session) : IRuntimePrismSession
    {
        public string BackendName => session.BackendName;
        public void Dispose() => session.Dispose();
    }
}
