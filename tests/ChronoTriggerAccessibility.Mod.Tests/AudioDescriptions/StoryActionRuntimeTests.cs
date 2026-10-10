using ChronoTriggerAccessibility.Core.Dialogue;
using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Core.Battle;
using ChronoTriggerAccessibility.Mod.AudioDescriptions;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.AudioDescriptions;

public sealed class StoryActionRuntimeTests
{
    [Fact]
    public void AVisibleNativeNodIsQueuedAfterTheOriginalAndUsesTheCurrentName()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Dispatch(() => Assert.Empty(test.Speech));
        Assert.Equal(["Cinder nods."], test.Speech);
        Assert.Equal(1, test.Originals);
    }

    [Fact]
    public void AnOperandWithoutAppliedAnimationProofIsSilent()
    {
        var test = new Session { Proof = null }; test.Runtime.Enable(); test.Dispatch();
        Assert.Empty(test.Speech); Assert.Equal(1, test.Originals);
    }

    [Theory]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 0)]
    [InlineData(true, null, 0)]
    [InlineData(true, true, 1)]
    public void HiddenOffscreenUnknownViewportAndPausedActionsAreSilent(bool drawn, bool? onScreen, uint paused)
    {
        var test = new Session(); test.Next = Frame() with
        {
            ActorState = Actor() with { DrawMode = drawn ? 1 : 0, OnScreen = onScreen },
            ScriptPause = paused
        };
        test.Runtime.Enable(); test.Dispatch();
        Assert.Empty(test.Speech); Assert.Equal(1, test.Originals);
    }

    [Fact]
    public void DescriptionsWaitBehindCurrentDialogueAndPrecedeItsNextLine()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Next = Frame() with { TextboxState = 1 };
        test.Dispatch(); Assert.Empty(test.Speech);
        test.Runtime.Observe(new DialogueLinePresented(0, 1, "Next visible line."));
        test.Speech.Add("Next visible line.");
        Assert.Equal(["Cinder nods.", "Next visible line."], test.Speech);
    }

    [Fact]
    public void ClosingTheNativeTextboxQueuesHeldActionsWithoutAssumingSpeechEnded()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Next = Frame() with { TextboxState = 1 }; test.Dispatch();
        test.Runtime.Observe(new DialogueClosed());
        Assert.Equal(["Cinder nods."], test.Speech);
    }

    [Fact]
    public void RepeatedLoopingAndPlayOnceWaitTicksDoNotRepeatTheDescription()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Dispatch(); test.Dispatch();
        test.Proof = new(StoryAnimationKind.PlayOnceWaiting, 0x16); test.Dispatch();
        test.Proof = new(StoryAnimationKind.PlayOnceFinished, 0x16); test.Dispatch();
        Assert.Single(test.Speech); Assert.Equal(4, test.Originals);
    }

    [Fact]
    public void ANewSceneDropsHeldActionsAndAllowsTheSameGestureAgain()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Next = Frame() with { TextboxState = 1 }; test.Dispatch();
        test.Next = Frame() with { Location = Frame().Location with { Scene = 287, ScriptId = 381 } };
        test.Dispatch(); test.Runtime.Observe(new DialogueClosed());
        Assert.Equal(["Cinder nods."], test.Speech);
    }

    [Fact]
    public void ReturningToTitleDropsHeldActions()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Next = Frame() with { TextboxState = 1 }; test.Dispatch();
        test.Runtime.Observe(new StartupSceneEntered(StartupSceneKind.Title));
        test.Runtime.Observe(new DialogueClosed()); Assert.Empty(test.Speech);
    }

    [Fact]
    public void BackgroundActionsAndTheirPendingDescriptionsStaySilent()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Next = Frame() with { TextboxState = 1 }; test.Dispatch();
        test.Foreground = false; test.Runtime.Observe(new DialogueClosed()); test.Dispatch();
        Assert.Empty(test.Speech); Assert.Equal(2, test.Originals);
    }

    [Fact]
    public void DisabledAndSuspendedObserversStillCallNativeExactlyOnce()
    {
        var test = new Session(); test.Dispatch();
        test.Runtime.Enable(); test.Runtime.Disable(); test.Dispatch();
        Assert.Empty(test.Speech); Assert.Equal(2, test.Originals);
    }

    [Fact]
    public void CaptureFailuresCannotSkipOrDuplicateTheNativeCall()
    {
        var test = new Session { CaptureThrows = true }; test.Runtime.Enable(); test.Dispatch();
        Assert.Empty(test.Speech); Assert.Equal(1, test.Originals);
    }

    [Fact]
    public void NativeExceptionsArePreservedAndProduceNoDescription()
    {
        var test = new Session(); test.Runtime.Enable();
        var error = new InvalidOperationException("Native failure");
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => test.Dispatch(() => throw error)));
        Assert.Empty(test.Speech); Assert.Equal(1, test.Originals);
    }

    [Fact]
    public void DisablingDuringNativeDispatchCannotLeakItsCue()
    {
        var test = new Session(); test.Runtime.Enable(); test.Dispatch(test.Runtime.Disable);
        Assert.Empty(test.Speech); Assert.Equal(1, test.Originals);
    }

    [Fact]
    public void SpeechFailureDoesNotChangeNativeExecutionOrPreventTheNextAction()
    {
        var test = new Session { SpeechThrows = true }; test.Runtime.Enable(); test.Dispatch();
        test.SpeechThrows = false;
        test.Next = Frame() with { Bytes = "AA17000000000000", Location = Frame().Location with { Address = 0x201 } };
        test.Proof = new(StoryAnimationKind.Looping, 0x17); test.Dispatch();
        Assert.Equal(["Cinder shakes their head."], test.Speech); Assert.Equal(2, test.Originals);
    }

    [Fact]
    public void OrdinaryWalkingAndUnreviewedSpriteAnimationsProduceNoNarration()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Proof = new(StoryAnimationKind.Looping, 1); test.Next = Frame() with { Bytes = "AA01000000000000" }; test.Dispatch();
        test.Proof = new(StoryAnimationKind.Looping, 0x16);
        test.Next = Frame() with { ActorState = Actor() with { Visual = 80, ClassTag = 4 } }; test.Dispatch();
        Assert.Empty(test.Speech);
    }

    [Fact]
    public void TheUnnamedGirlKeepsHerVisibleDescriptionBeforeTheNamingScene()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Next = Frame() with { StoryPoint = 3, ActorState = Actor() with { Visual = 1 } };
        test.Dispatch(); Assert.Equal(["The blonde girl nods."], test.Speech);
    }

    [Fact]
    public void ReturningToIdleDoesNotRepeatTheGestureWithinAScriptLoop()
    {
        var test = new Session(); test.Runtime.Enable(); test.Dispatch();
        test.Next = Frame() with { Bytes = "AA00000000000000", Location = Frame().Location with { Address = 0x220 } };
        test.Proof = new(StoryAnimationKind.Looping, 0); test.Dispatch();
        test.Next = Frame(); test.Proof = new(StoryAnimationKind.Looping, 0x16); test.Dispatch();
        Assert.Equal(["Cinder nods."], test.Speech);
    }

    [Fact]
    public void ARepeatedPlayOnceStartAfterCompletionIsStillOneGesture()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Next = Frame() with { Location = Frame().Location with { Opcode = 0xAB }, Bytes = "AB16000000000000" };
        test.Proof = new(StoryAnimationKind.PlayOnceStarted, 0x16); test.Dispatch();
        test.Proof = new(StoryAnimationKind.PlayOnceFinished, 0x16); test.Dispatch();
        test.Proof = new(StoryAnimationKind.PlayOnceStarted, 0x16); test.Dispatch();
        Assert.Equal(["Cinder nods."], test.Speech); Assert.Equal(3, test.Originals);
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 1)]
    public void OnlyVerifiedRestoredPlayerControlRearmsScriptedGestures(bool restored, int count)
    {
        var test = new Session { ControlRestored = restored }; test.Runtime.Enable(); test.Dispatch();
        test.Next = Frame() with { Location = Frame().Location with { Opcode = 0xE3, Address = 0x260 }, Bytes = "E301000000000000" };
        test.Dispatch(); test.Next = Frame(); test.Dispatch();
        Assert.Equal(count, test.Speech.Count); Assert.Equal(3, test.Originals);
    }

    [Theory]
    [InlineData(0u, 1)]
    [InlineData(1u, 2)]
    public void ClosingAnOrdinaryConversationRearmsButCutscenePagesDoNot(uint control, int count)
    {
        var test = new Session(); test.Runtime.Enable();
        test.Next = Frame() with { Control = control, TextboxState = 1 }; test.Dispatch();
        test.Runtime.Observe(new DialogueClosed()); test.Dispatch();
        test.Runtime.Observe(new DialogueClosed()); Assert.Equal(count, test.Speech.Count);
    }

    [Fact]
    public void ChoicesReceiveHeldDescriptionsBeforeTheirLabels()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Next = Frame() with { TextboxState = 1 }; test.Dispatch();
        test.Runtime.Observe(new DialogueChoicesPresented(["Yes", "No"], 0)); test.Speech.Add("Yes");
        Assert.Equal(["Cinder nods.", "Yes"], test.Speech);
    }

    [Fact]
    public void AControlHandlerCannotLeaveAnOldEnabledWordForDialogueClosure()
    {
        var test = new Session(); test.Runtime.Enable(); test.Dispatch();
        test.Next = Frame() with { Control = 1, Location = Frame().Location with
            { Opcode = 0xE3, Address = 0x260 }, Bytes = "E300000000000000" };
        test.Dispatch(); test.Runtime.Observe(new DialogueClosed());
        test.Next = Frame(); test.Dispatch(); Assert.Single(test.Speech);
    }

    [Fact]
    public void BattleStartDropsHeldFieldDescriptions()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Next = Frame() with { TextboxState = 1 }; test.Dispatch();
        test.Runtime.Observe(new BattleStarted(1)); test.Runtime.Observe(new DialogueClosed());
        Assert.Empty(test.Speech);
    }

    [Fact]
    public void AnAbortedTextboxFlushesAtTheNextCapturedIdleInstruction()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Next = Frame() with { TextboxState = 0xD }; test.Dispatch();
        test.Next = Frame() with { Bytes = "AA00000000000000", Location = Frame().Location with { Address = 0x220 } };
        test.Proof = new(StoryAnimationKind.Looping, 0); test.Dispatch();
        Assert.Equal(["Cinder nods."], test.Speech);
    }

    [Fact]
    public void UnverifiedResourcesDoNotReadOrDescribeNativeActions()
    {
        var captures = 0; var originals = 0; var speech = new List<string>();
        var runtime = new StoryActionRuntime((_, _) => { captures++; return Frame(); },
            _ => new(StoryAnimationKind.Looping, 0x16), () => true, _ => "Cinder", speech.Add, _ => { }, () => false);
        runtime.Enable(); runtime.Dispatch(0x1000, 0xAA, () => originals++);
        Assert.Empty(speech); Assert.Equal(0, captures); Assert.Equal(1, originals);
    }

    [Fact]
    public void AFailingOldCaptureCannotDiscardTheNewLifetimePendingAction()
    {
        StoryActionRuntime? runtime = null;
        var captures = 0; var originals = 0; var speech = new List<string>();
        runtime = new StoryActionRuntime((_, _) =>
        {
            if (++captures == 1)
            {
                runtime!.Disable(); runtime.Enable();
                runtime.Dispatch(0x1000, 0xAA, () => originals++);
                throw new InvalidOperationException("Old observer failed after reactivation");
            }
            return Frame() with { TextboxState = 1 };
        }, _ => new(StoryAnimationKind.Looping, 0x16), () => true, _ => "Cinder", speech.Add, _ => { });
        runtime.Enable(); runtime.Dispatch(0x1000, 0xAA, () => originals++);
        runtime.Observe(new DialogueClosed());
        Assert.Equal(["Cinder nods."], speech); Assert.Equal(2, originals);
    }

    internal static StoryActorState Actor() => new(1, 0, 0, 1, 1, 0, 0, 0, 0, 0, 160, 160, true, true);
    internal static StoryActionSnapshot Frame() => new(new(0x1000, 0x2000, 0x3000, 0x4000, 0x5000,
        280, 371, 1, 23, 0x200, 0xAA), "AA16000000000000", Actor(), 0x70, 0, 0, 0, -1);

    private sealed class Session
    {
        public StoryActionSnapshot? Next = Frame();
        public StoryAnimationProof? Proof = new(StoryAnimationKind.Looping, 0x16);
        public bool Foreground = true, CaptureThrows, SpeechThrows, ControlRestored;
        public int Originals;
        public List<string> Speech { get; } = [];
        public StoryActionRuntime Runtime { get; }
        public Session() => Runtime = new((_, _) => CaptureThrows ? throw new Exception("Capture failed") : Next,
            _ => Proof, () => Foreground, character => character == 0 ? "Cinder" : "Marle", text =>
            { if (SpeechThrows) throw new Exception("Speech failed"); Speech.Add(text); }, _ => { },
            controlRestored: _ => ControlRestored);
        public void Dispatch(Action? inside = null) => Runtime.Dispatch(0x1000, Next?.Location.Opcode ?? 0xAA,
            () => { Originals++; inside?.Invoke(); });
    }
}
