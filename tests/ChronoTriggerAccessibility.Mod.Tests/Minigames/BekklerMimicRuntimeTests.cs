using ChronoTriggerAccessibility.Mod.Minigames;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Minigames;

public sealed class BekklerMimicRuntimeTests
{
    [Fact]
    public void StartAndTheFirstPoseShareOneButtonFirstUtteranceInsteadOfInterruptingEachOther()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Send(BekklerMimicAction.Window, 1);
        Assert.Empty(test.Speech);
        test.Send(BekklerMimicAction.LeftArm, 1, "A");
        Assert.Equal("A. Left arm. Copy game started.", Assert.Single(test.Speech));
        Assert.Equal(2, test.Originals);
    }

    [Fact]
    public void SpeaksShortImmediateCuesOnlyAfterTheNativePoseAndKeepsIdenticalRounds()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Send(BekklerMimicAction.Window, 1);
        test.Send(BekklerMimicAction.Laugh, 1, "Y");
        test.Send(BekklerMimicAction.Laugh, 1, "Y");
        test.Send(BekklerMimicAction.Window, 2);
        test.Send(BekklerMimicAction.Laugh, 2, "Y");
        test.Send(BekklerMimicAction.Window, 3);
        test.Send(BekklerMimicAction.RightArm, 3, "B");
        test.Send(BekklerMimicAction.Window, 4);
        test.Send(BekklerMimicAction.LeftArm, 4, "A");
        test.Send(BekklerMimicAction.Window, 5);
        test.Send(BekklerMimicAction.Surprise, 5, "X");
        test.Send(BekklerMimicAction.Failed, 5);
        Assert.Equal(["Y. Laugh. Copy game started.", "Y. Laugh.", "B. Right arm.",
            "A. Left arm.", "X. Surprise.", "Copy game ended."], test.Speech);
        Assert.Equal(12, test.Originals);
        Assert.DoesNotContain(test.Speech, x => x.Any(char.IsDigit));
    }

    [Fact]
    public void NoVerifiedWindowNoPoseCueAndNoUnrelatedEndAnnouncement()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Send(BekklerMimicAction.Laugh, 1, "Y");
        test.Send(BekklerMimicAction.Failed, 1);
        test.Completed = false; test.Send(BekklerMimicAction.Window, 1);
        test.Completed = true; test.Send(BekklerMimicAction.Laugh, 1, "Y");
        Assert.Empty(test.Speech); Assert.Equal(4, test.Originals);
    }

    [Fact]
    public void OnlyTheCurrentOwnersCurrentRoundCanSpeakAndAnUnknownButtonStillDescribesThePose()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Send(BekklerMimicAction.Window, 2);
        test.Send(BekklerMimicAction.Laugh, 1, "Y");
        test.Owner = 0xD1000; test.Send(BekklerMimicAction.Laugh, 2, "Y");
        test.Send(BekklerMimicAction.Window, 3);
        test.Send(BekklerMimicAction.Surprise, 3);
        Assert.Equal(["Surprise."], test.Speech);
    }

    [Fact]
    public void NativeWinAndRestartDoNotReuseRoundDeduplication()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Send(BekklerMimicAction.Window, 22);
        test.Send(BekklerMimicAction.LeftArm, 22, "X");
        test.Send(BekklerMimicAction.Won, 22);
        test.Send(BekklerMimicAction.Window, 1);
        test.Send(BekklerMimicAction.LeftArm, 1, "X");
        Assert.Equal(["X. Left arm.", "Copy game complete.", "X. Left arm. Copy game started."], test.Speech);
    }

    [Fact]
    public void ForegroundLossDiscardsPendingCuesAndNeverReplaysThem()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Send(BekklerMimicAction.Window, 2);
        test.Focused = false; test.Send(BekklerMimicAction.Laugh, 2, "Y");
        test.Focused = true; test.Send(BekklerMimicAction.Laugh, 2, "Y");
        Assert.Empty(test.Speech);
        test.Send(BekklerMimicAction.Window, 3); test.Send(BekklerMimicAction.Laugh, 3, "Y");
        Assert.Equal(["Y. Laugh."], test.Speech);
    }

    [Fact]
    public void DisabledAndInFlightOldEpochCallbacksAreSilent()
    {
        var test = new Session();
        test.Send(BekklerMimicAction.Window, 1); Assert.Equal(0, test.Captures);
        test.Runtime.Enable();
        test.Next = Frame(BekklerMimicAction.Window, 1);
        test.Runtime.Dispatch(0x1000, 0x4F, () => { test.Originals++; test.Runtime.Disable(); test.Runtime.Enable(); });
        Assert.Empty(test.Speech);
        test.Send(BekklerMimicAction.Laugh, 1, "Y"); Assert.Empty(test.Speech);
        test.Send(BekklerMimicAction.Window, 1); Assert.Empty(test.Speech);
        test.Send(BekklerMimicAction.Laugh, 1, "Y"); Assert.Single(test.Speech);
        Assert.Equal(5, test.Originals);
    }

    [Theory]
    [InlineData("capture")] [InlineData("complete")] [InlineData("foreground")]
    [InlineData("speech")] [InlineData("diagnostic")]
    public void ObserverFailuresNeverSkipOrRepeatTheNativeCall(string failing)
    {
        var originals = 0; BekklerMimicSnapshot? current = Frame(BekklerMimicAction.Window, 1);
        var runtime = new BekklerMimicRuntime((_, _) => failing == "capture" ? throw new Exception() : current,
            before => failing == "complete" ? throw new Exception() : new(before, "Y"),
            () => failing == "foreground" ? throw new Exception() : true,
            _ => { if (failing == "speech") throw new Exception(); },
            _ => { if (failing is "diagnostic" or "capture" or "complete" or "foreground" or "speech") throw new Exception(); });
        runtime.Enable(); runtime.Dispatch(0x1000, 0x4F, () => originals++);
        current = Frame(BekklerMimicAction.Laugh, 1);
        runtime.Dispatch(0x1000, 0xAA, () => originals++);
        Assert.Equal(2, originals);
    }

    [Fact]
    public void NativeExceptionsPropagateOnceWithoutLeakingAnArmedRound()
    {
        var test = new Session(); test.Runtime.Enable(); test.Send(BekklerMimicAction.Window, 2);
        Assert.Throws<InvalidOperationException>(() => test.Runtime.Dispatch(0x1000, 0xAA,
            () => { test.Originals++; throw new InvalidOperationException(); }));
        test.Send(BekklerMimicAction.Laugh, 2, "Y"); Assert.Empty(test.Speech);
        Assert.Equal(3, test.Originals);
    }

    private static BekklerMimicSnapshot Frame(BekklerMimicAction action, int round) =>
        new(0x1000, 0xA0000, 0xD0000, 0x5000, action == BekklerMimicAction.Window ? 0x382u : 0x554u, round, 0, action);
    private sealed class Session
    {
        public readonly List<string> Speech = [];
        public BekklerMimicSnapshot? Next;
        public int Originals, Captures;
        public uint Owner = 0xD0000;
        public bool Completed = true, Focused = true;
        private string? button;
        public BekklerMimicRuntime Runtime { get; }
        public Session()
        {
            Runtime = new((_, _) => { Captures++; return Next; },
                before => Completed ? new(before, button) : null, () => Focused,
                text => { Assert.True(Originals > 0); Speech.Add(text); }, _ => { });
        }
        public void Send(BekklerMimicAction action, int round, string? label = null)
        {
            Next = Frame(action, round) with { Actors = Owner }; button = label;
            Runtime.Dispatch(0x1000, 0xAA, () => Originals++);
        }
    }
}
