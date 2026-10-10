using ChronoTriggerAccessibility.Mod.Minigames;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Minigames;

public sealed class IokaContestRuntimeTests
{
    [Fact]
    public void AnnouncesAConfirmedStartOnlyAfterTheNativeCallAndUsesCurrentName()
    {
        var test = new Session();
        test.Next = Frame(IokaContestAction.Started);
        test.Runtime.Enable();
        test.Runtime.Dispatch(0x1000, 0x75, () =>
        { test.Originals++; Assert.Empty(test.Speech); test.Completed = true; });

        Assert.Single(test.Speech);
        Assert.Equal("Drinking contest started. Rapidly press Confirm to drink more than Rika.", test.Speech[0]);
        Assert.Equal(1, test.Originals);
    }

    [Fact]
    public void FailedNativeStateChangesAndOtherScriptsProduceNoCue()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Next = Frame(IokaContestAction.Started);
        test.Completed = false;
        test.Dispatch();
        test.Next = null; test.Completed = true;
        test.Dispatch();
        Assert.Empty(test.Speech);
        Assert.Equal(2, test.Originals);
    }

    [Fact]
    public void CoalescesVisibleDrinkingActionsWithoutInterruptingInstructionsOrAnnouncingScores()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Send(IokaContestAction.Started, 0);
        test.Send(IokaContestAction.PlayerDrinks, 10);
        test.Send(IokaContestAction.AylaDrinks, 20);
        test.Send(IokaContestAction.PlayerDrinks, 2000);
        Assert.Single(test.Speech);
        test.Send(IokaContestAction.AylaDrinks, 4000);
        Assert.Equal("You drink. Rika drinks.", test.Speech[1]);
        test.Send(IokaContestAction.PlayerDrinks, 4010);
        Assert.Equal(2, test.Speech.Count);
        test.Send(IokaContestAction.PlayerDrinks, 6100);
        Assert.Equal("You drink.", test.Speech[2]);
        test.Send(IokaContestAction.Finished, 6200);
        Assert.Equal("Drinking contest finished.", test.Speech[3]);
        Assert.DoesNotContain(test.Speech, text => text.Contains("score", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("win", StringComparison.OrdinalIgnoreCase) || text.Any(char.IsDigit));
    }

    [Fact]
    public void UnfocusedStartIsSilentAndReturningToTheActiveContestGivesCurrentInstructions()
    {
        var test = new Session(); test.Runtime.Enable(); test.Focused = false;
        test.Send(IokaContestAction.Started, 0);
        test.Send(IokaContestAction.AylaDrinks, 2000);
        Assert.Empty(test.Speech);
        test.Focused = true;
        test.Send(IokaContestAction.PlayerDrinks, 4000);
        Assert.Single(test.Speech);
        Assert.StartsWith("Drinking contest in progress.", test.Speech[0]);
        test.Focused = false;
        test.Send(IokaContestAction.Finished, 5000);
        Assert.Single(test.Speech);
    }

    [Fact]
    public void MissingTheStartOrChangingContextCannotReusePendingDrinks()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Send(IokaContestAction.PlayerDrinks, 5000);
        Assert.StartsWith("Drinking contest in progress.", Assert.Single(test.Speech));
        test.Send(IokaContestAction.AylaDrinks, 7000);
        test.Next = Frame(IokaContestAction.PlayerDrinks) with { Actors = 0xD1000 };
        test.Now = 9000; test.Dispatch();
        Assert.StartsWith("Drinking contest in progress.", test.Speech.Last());
        Assert.Equal(2, test.Speech.Count);
    }

    [Fact]
    public void DisabledHooksDoNotCaptureAndDisableDuringDispatchSuppressesStaleSpeech()
    {
        var test = new Session(); test.Next = Frame(IokaContestAction.Started);
        test.Dispatch(); Assert.Equal(0, test.Captures);
        test.Runtime.Enable();
        test.Runtime.Dispatch(0x1000, 0x75, () => { test.Originals++; test.Runtime.Disable(); });
        Assert.Empty(test.Speech);
        test.Runtime.Enable(); test.Send(IokaContestAction.Started, 10);
        Assert.Single(test.Speech);
        Assert.Equal(3, test.Originals);
    }

    [Fact]
    public void CaptureCompletionAndSpeechFailuresNeverSkipOrRepeatTheNativeCall()
    {
        var originals = 0;
        foreach (var failing in new[] { "capture", "complete", "speech", "foreground", "clock", "name" })
        {
            var runtime = new IokaContestRuntime(
                (_, _) => failing == "capture" ? throw new Exception("capture") : Frame(IokaContestAction.Started),
                _ => failing == "complete" ? throw new Exception("complete") : true,
                () => failing == "foreground" ? throw new Exception("foreground") : true,
                () => failing == "clock" ? throw new Exception("clock") : 0,
                () => failing == "name" ? throw new Exception("name") : "Ayla",
                _ => { if (failing == "speech") throw new Exception("speech"); },
                _ => throw new Exception("diagnostic"));
            runtime.Enable(); runtime.Dispatch(0x1000, 0x75, () => originals++);
        }
        Assert.Equal(6, originals);
    }

    [Fact]
    public void NativeExceptionsArePreservedAndNotRetried()
    {
        var test = new Session(); test.Runtime.Enable(); test.Next = Frame(IokaContestAction.Started);
        var error = Assert.Throws<InvalidOperationException>(() => test.Runtime.Dispatch(0x1000, 0x75,
            () => { test.Originals++; throw new InvalidOperationException("native"); }));
        Assert.Equal("native", error.Message); Assert.Equal(1, test.Originals); Assert.Empty(test.Speech);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ATemporaryNameOrSpeechFailureRetriesInstructionsAtTheNextVerifiedAction(bool nameFails)
    {
        var frame = Frame(IokaContestAction.Started); var first = true; var originals = 0;
        var speech = new List<string>();
        var runtime = new IokaContestRuntime((_, _) => frame, _ => true, () => true, () => 0,
            () => { if (first && nameFails) { first = false; throw new Exception("name"); } return "Ayla"; },
            text => { if (first && !nameFails) { first = false; throw new Exception("speech"); } speech.Add(text); }, _ => { });
        runtime.Enable(); runtime.Dispatch(0x1000, 0x75, () => originals++);
        Assert.Empty(speech);
        frame = Frame(IokaContestAction.PlayerDrinks);
        runtime.Dispatch(0x1000, 0xAA, () => originals++);
        Assert.StartsWith("Drinking contest in progress.", Assert.Single(speech));
        Assert.Equal(2, originals);
    }

    private static IokaContestSnapshot Frame(IokaContestAction action) =>
        new(0x1000, 0xA0000, 0xD0000, 0x5000, 0x7BD, 6, action == IokaContestAction.Started ? 0 : 1, action);

    private sealed class Session
    {
        public IokaContestSnapshot? Next;
        public bool Focused = true, Completed = true;
        public int Originals, Captures;
        public long Now;
        public List<string> Speech { get; } = [];
        public IokaContestRuntime Runtime { get; }
        public Session() => Runtime = new IokaContestRuntime((_, _) => { Captures++; return Next; },
            _ => Completed, () => Focused, () => Now, () => "Rika", Speech.Add, _ => { });
        public void Send(IokaContestAction action, long now) { Next = Frame(action); Now = now; Dispatch(); }
        public void Dispatch() => Runtime.Dispatch(0x1000, 0x75, () => Originals++);
    }
}
