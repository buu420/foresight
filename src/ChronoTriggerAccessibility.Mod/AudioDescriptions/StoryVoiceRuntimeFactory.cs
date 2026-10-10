using ChronoTriggerAccessibility.Mod.Runtime;

namespace ChronoTriggerAccessibility.Mod.AudioDescriptions;

public sealed class StoryVoiceRuntimeFactory(IRuntimePrismFactory inner, Func<bool> foreground,
    Action<string> diagnostic, Func<IStoryVoiceOutput>? output = null,
    Func<StoryNarration, StoryVoiceClip>? clip = null, Action<string>? coverageFailure = null) : IRuntimePrismFactory
{
    private readonly Lazy<StoryVoicePack> pack = new(() => StoryVoicePack.Load());
    public IRuntimePrismSession Create()
    {
        return new StoryVoiceSession(inner.Create(), output ?? (() => new StoryVoiceWaveOutput()),
            clip ?? (cue => pack.Value.Get(cue)), foreground, diagnostic, coverageFailure: coverageFailure);
    }
}
