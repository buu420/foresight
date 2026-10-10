using System.Collections.Concurrent;
using ChronoTriggerAccessibility.Mod.AudioDescriptions;
using ChronoTriggerAccessibility.Mod.Runtime;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.AudioDescriptions;

public sealed class StoryVoiceSessionTests
{
    private static readonly StoryNarration Cue = new("Crono nods.", "party-00-16-named", "Crono nods.");

    [Fact]
    public async Task AStalledDriverCannotHoldFollowingNativeDialogueForever()
    {
        var test = new Fixture(); using var session = test.Create(); session.Narrate(Cue);
        await test.Wave.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        session.Output("Next dialogue", false); test.Milliseconds = 4000;
        await test.NextSpeech.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(["voice", "voice stopped", "speech:Crono nods.", "speech:Next dialogue"], test.Events.ToArray());
        Assert.Contains(test.Diagnostics, x => x.Contains("completion"));
    }

    [Fact]
    public async Task AFailedDeferredAnnouncementReachesTheIndependentAccessibleFailureBoundary()
    {
        var test = new Fixture { ThrowOnNext = true }; using var session = test.Create(); session.Narrate(Cue);
        await test.Wave.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        session.Output("Next dialogue", false); test.Wave.Finish();
        var message = await test.Failed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Contains("Deferred Prism announcement failed", message);
    }

    [Fact]
    public async Task OpeningDialogueChoicesWaitsForTheJustFlushedDescription()
    {
        var test = new Fixture(); using var session = test.Create(); session.Narrate(Cue);
        await test.Wave.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        session.OutputAfterNarration("Next dialogue", true);
        Assert.Equal(["voice"], test.Events.ToArray());
        test.Wave.Finish(); await test.NextSpeech.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(["voice", "speech:Next dialogue"], test.Events.ToArray());
    }

    [Fact]
    public async Task InitialChoicesRetainTheUnheardQuestionAndDoNotInterruptItsPrismQueue()
    {
        var test = new Fixture(); using var session = test.Create(); session.Narrate(Cue);
        await test.Wave.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        session.Output("Question", false); session.OutputAfterNarration("Next dialogue", true);
        test.Wave.Finish(); await test.NextSpeech.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(["voice", "speech:Question", "speech:Next dialogue"], test.Events.ToArray());
        Assert.Equal([false, false], test.Interrupts.ToArray());
    }

    [Fact]
    public async Task RecordedSpeechReplacesItsPrismUtteranceAndHoldsFollowingDialogueUntilDriverCompletion()
    {
        var test = new Fixture(); using var session = test.Create();
        session.Output("Previous dialogue", false); session.Narrate(Cue); session.Output("Next dialogue", false);
        await test.Wave.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(["speech:Previous dialogue", "voice"], test.Events.ToArray());
        test.Wave.Finish(); await test.NextSpeech.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(["speech:Previous dialogue", "voice", "speech:Next dialogue"], test.Events.ToArray());
    }

    [Fact]
    public async Task AMenuInterruptionStopsOwnVoiceBeforeAnnouncingTheMenuAndDropsOldDeferredSpeech()
    {
        var test = new Fixture(); using var session = test.Create(); session.Narrate(Cue);
        await test.Wave.Started.Task.WaitAsync(TimeSpan.FromSeconds(3)); session.Output("Old queued dialogue", false);
        session.Output("Next dialogue", true); await test.NextSpeech.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(["voice", "voice stopped", "speech:Next dialogue"], test.Events.ToArray());
    }

    [Fact]
    public async Task SceneCancellationStopsVoiceButPreservesDeferredNativeDialogue()
    {
        var test = new Fixture(); using var session = test.Create(); session.Narrate(Cue);
        await test.Wave.Started.Task.WaitAsync(TimeSpan.FromSeconds(3)); session.Output("Next dialogue", false);
        session.CancelNarration(); await test.NextSpeech.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(["voice", "voice stopped", "speech:Next dialogue"], test.Events.ToArray());
    }

    [Fact]
    public async Task AFailedRecordingFallsBackToItsDescriptionOnceAndReleasesFollowingDialogue()
    {
        var test = new Fixture { MissingClip = true }; using var session = test.Create();
        session.Narrate(Cue); session.Output("Next dialogue", false);
        await test.NextSpeech.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(["speech:Crono nods.", "speech:Next dialogue"], test.Events.ToArray());
        Assert.NotEmpty(test.Diagnostics);
    }

    [Fact]
    public async Task LosingForegroundStopsTheRecordedVoice()
    {
        var test = new Fixture(); using var session = test.Create(); session.Narrate(Cue);
        await test.Wave.Started.Task.WaitAsync(TimeSpan.FromSeconds(3)); test.Foreground = false;
        await test.Wave.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(3)); Assert.False(test.Wave.Playing);
    }

    [Fact]
    public async Task DisposeStopsPlaybackBeforeDisposingPrism()
    {
        var test = new Fixture(); var session = test.Create(); session.Narrate(Cue);
        await test.Wave.Started.Task.WaitAsync(TimeSpan.FromSeconds(3)); session.Dispose();
        Assert.Equal(["voice", "voice stopped", "wave disposed", "prism disposed"], test.Events.ToArray());
    }

    private sealed class Fixture
    {
        public ConcurrentQueue<string> Events { get; } = new();
        public ConcurrentQueue<string> Diagnostics { get; } = new();
        public ConcurrentQueue<bool> Interrupts { get; } = new();
        public TaskCompletionSource NextSpeech { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Wave Wave { get; }
        public volatile bool Foreground = true;
        public bool MissingClip;
        public bool ThrowOnNext;
        public TaskCompletionSource<string> Failed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public long Milliseconds;
        public Fixture() => Wave = new(Events);
        public StoryVoiceSession Create() => new(new Prism(this), () => Wave,
            cue => MissingClip ? throw new IOException("Missing clip") : new([1, 2], 1, cue.VoiceText),
            () => Foreground, Diagnostics.Enqueue, () => Interlocked.Read(ref Milliseconds),
            message => Failed.TrySetResult(message));
    }
    private sealed class Prism(Fixture test) : IRuntimePrismSession
    {
        public string BackendName => "test";
        public void Output(string text, bool interrupt)
        {
            if (text == "Next dialogue" && test.ThrowOnNext) throw new IOException("Unavailable speech backend");
            test.Interrupts.Enqueue(interrupt);
            test.Events.Enqueue("speech:" + text);
            if (text == "Next dialogue") test.NextSpeech.TrySetResult();
        }
        public void Stop() => test.Events.Enqueue("prism stopped");
        public void Dispose() => test.Events.Enqueue("prism disposed");
    }
    private sealed class Wave(ConcurrentQueue<string> events) : IStoryVoiceOutput
    {
        private volatile bool playing;
        public bool Playing => playing;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool TryPlay(byte[] wave)
        {
            Assert.False(playing); playing = true; events.Enqueue("voice"); Started.TrySetResult(); return true;
        }
        public void Finish() => playing = false;
        public void Stop()
        {
            if (playing) events.Enqueue("voice stopped"); playing = false; Stopped.TrySetResult();
        }
        public void Dispose() { Assert.False(playing); events.Enqueue("wave disposed"); }
    }
}
