using System.Collections.Concurrent;
using ChronoTriggerAccessibility.Core.Dialogue;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using ChronoTriggerAccessibility.Mod.Runtime;
using ChronoTriggerAccessibility.Mod.AudioDescriptions;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.AudioDescriptions;

public sealed class StoryVoiceCompositionTests
{
    [Fact]
    public async Task AVerifiedHeldActionIsHeardBeforeTheNativeDialogueChoices()
    {
        var prism = new Prism(); var wave = new Wave(); var log = new Log(); var originals = 0;
        using var session = new StoryVoiceRuntimeFactory(new Factory(prism), () => true, log.Info,
            () => wave, cue => new([1, 2], 1, cue.VoiceText)).Create();
        var dispatcher = new SemanticEventDispatcher(log, new Fatal()); dispatcher.Attach(session);
        dispatcher.Publish(new DialogueOpened());
        var runtime = new StoryActionRuntime((_, _) => StoryActionRuntimeTests.Frame() with { TextboxState = 1 },
            _ => new(ChronoTriggerAccessibility.Native.Capture.StoryAnimationKind.Looping, 0x16),
            () => true, _ => "Crono", _ => Assert.Fail("Duplicate description"), log.Info,
            narrate: dispatcher.PublishNarration, stopNarration: dispatcher.CancelNarration);
        runtime.Enable(); runtime.Dispatch(0x1000, 0xAA, () => originals++);
        var choices = new DialogueChoicesPresented(["Continue"], 0);
        runtime.Observe(choices); dispatcher.Publish(choices);
        await wave.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Empty(prism.Speech); Assert.Equal(1, originals);
        wave.Playing = false; await prism.Said.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("Continue", prism.Speech.First());
        Assert.DoesNotContain(prism.Speech, x => x.Contains("nods"));
    }
    [Fact]
    public async Task ActualDispatcherPreservesCaptionAndOrdersNativeDialogueAfterRecordedDescription()
    {
        var prism = new Prism(); var wave = new Wave(); var log = new Log();
        var factory = new StoryVoiceRuntimeFactory(new Factory(prism), () => true, log.Info,
            () => wave, cue => new([1, 2], 1, cue.VoiceText));
        using var session = factory.Create(); Assert.IsType<StoryVoiceSession>(session);
        var dispatcher = new SemanticEventDispatcher(log, new Fatal()); dispatcher.Attach(session);
        dispatcher.Publish(new DialogueOpened());
        dispatcher.PublishNarration(new("Cinder nods.", "party-00-16-appearance", "The spiky-haired boy nods."));
        dispatcher.Publish(new DialogueLinePresented(1, 0, "Following dialogue"));
        await wave.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Empty(prism.Speech); Assert.Equal(["Cinder nods."], prism.Captions.ToArray());
        Assert.Contains(log.Messages, x => x.Contains("text=Cinder nods."));
        wave.Playing = false;
        await prism.Said.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(["Following dialogue"], prism.Speech.ToArray());
        dispatcher.Detach(session);
    }

    [Fact]
    public async Task SceneCancellationThroughActualDispatcherStopsVoiceAndManualSpeechRemainsPrism()
    {
        var prism = new Prism(); var wave = new Wave(); var log = new Log();
        using var session = new StoryVoiceRuntimeFactory(new Factory(prism), () => true, log.Info,
            () => wave, cue => new([1, 2], 1, cue.VoiceText)).Create();
        var dispatcher = new SemanticEventDispatcher(log, new Fatal()); dispatcher.Attach(session);
        dispatcher.PublishNarration(new("Crono nods.", "party-00-16-named", "Crono nods."));
        await wave.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        dispatcher.CancelNarration(); Assert.False(wave.Playing);
        dispatcher.Publish(new NavigationAnnouncement("Door nearby."));
        Assert.Equal(["Door nearby."], prism.Speech.ToArray());
    }

    private sealed class Factory(Prism prism) : IRuntimePrismFactory { public IRuntimePrismSession Create() => prism; }
    private sealed class Prism : IRuntimePrismSession
    {
        public string BackendName => "NVDA";
        public ConcurrentQueue<string> Speech { get; } = new();
        public ConcurrentQueue<string> Captions { get; } = new();
        public TaskCompletionSource Said { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Output(string text, bool interrupt) { Speech.Enqueue(text); Said.TrySetResult(); }
        public void Braille(string text) => Captions.Enqueue(text);
        public void Stop() { }
        public void Dispose() { }
    }
    private sealed class Wave : IStoryVoiceOutput
    {
        public bool Playing { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool TryPlay(byte[] wave) { Playing = true; Started.TrySetResult(); return true; }
        public void Stop() => Playing = false;
        public void Dispose() { }
    }
    private sealed class Log : IModLog
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public void Info(string message) => Messages.Enqueue(message);
        public void Error(string message) => Messages.Enqueue(message);
    }
    private sealed class Fatal : IAccessibleFatalError { public void Show(string message) => Assert.Fail(message); }
}
