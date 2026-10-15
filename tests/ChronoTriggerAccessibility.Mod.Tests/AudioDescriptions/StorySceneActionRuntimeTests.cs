using ChronoTriggerAccessibility.Core.Dialogue;
using ChronoTriggerAccessibility.Mod.AudioDescriptions;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.AudioDescriptions;

public sealed class StorySceneActionRuntimeTests
{
    [Theory]
    [InlineData(StorySceneAction.CurtainsOpen, 0xE5, "Mother opens the curtains, filling the room with sunlight.")]
    [InlineData(StorySceneAction.MotherHeadsDownstairs, 0xA0, "Mother heads downstairs.")]
    [InlineData(StorySceneAction.BoyGetsOutOfBedAndStretches, 0x96, "Cinder climbs out of bed and stretches.")]
    [InlineData(StorySceneAction.FairCollision, 0xAC, "Cinder and a blonde girl bump into each other and fall to the ground.")]
    [InlineData(StorySceneAction.GirlGetsUp, 0xB7, "The blonde girl gets to her feet.")]
    [InlineData(StorySceneAction.BoyGetsUp, 0xAE, "Cinder gets to their feet.")]
    [InlineData(StorySceneAction.GirlHops, 0xAA, "The blonde girl hops up and down.")]
    [InlineData((StorySceneAction)14, 0x92, "The Nu walks up to the party.")]
    [InlineData((StorySceneAction)15, 0xAC, "The Nu raises both arms and opens its mouth wide.")]
    [InlineData((StorySceneAction)16, 0xAC, "The Nu settles down beneath the Epoch.")]
    public void VerifiedSceneActionsSpeakOnceAfterNativeEvenWithAnInvisibleController(StorySceneAction action, int opcode, string expected)
    {
        var test = new Session(action, opcode); test.Runtime.Enable();
        test.Dispatch(() => Assert.Empty(test.Speech)); test.Dispatch();
        Assert.Equal([expected], test.Speech); Assert.Equal(2, test.Originals);
    }

    [Fact]
    public void MatchingAWatchpointWithoutTheAppliedNativeResultDoesNotDescribeIt()
    {
        var test = new Session(StorySceneAction.CurtainsOpen, 0xE5) { Completed = false };
        test.Runtime.Enable(); test.Dispatch();
        Assert.Empty(test.Speech); Assert.Equal(1, test.Originals);
    }

    [Theory]
    [InlineData(StorySceneAction.CurtainsOpen, 0xE5)]
    [InlineData(StorySceneAction.MotherHeadsDownstairs, 0xA0)]
    [InlineData(StorySceneAction.BoyGetsOutOfBedAndStretches, 0x96)]
    public void ExactOpeningSceneProofDoesNotGuessTheInitialControlWord(StorySceneAction action, int opcode)
    {
        var test = new Session(action, opcode);
        test.Before = test.Before with { Control = 1 };
        test.Runtime.Enable(); test.Dispatch(); Assert.Single(test.Speech);
    }

    [Fact]
    public void SceneActionsWaitForTheNextDialogueBoundary()
    {
        var test = new Session(StorySceneAction.CurtainsOpen, 0xE5);
        test.Before = test.Before with { TextboxState = 1 };
        test.Runtime.Enable(); test.Dispatch(); Assert.Empty(test.Speech);
        test.Runtime.Observe(new DialogueLinePresented(0, 1, "Good morning."));
        Assert.Equal(["Mother opens the curtains, filling the room with sunlight."], test.Speech);
    }

    [Fact]
    public void GenericAndSpecificDescriptionsOfTheSameHopAreNotBothSpoken()
    {
        var test = new Session(StorySceneAction.GirlHops, 0xAA);
        test.Before = test.Before with { ActorState = test.Before.ActorState with { DrawMode = 1, Visual = 1 } };
        test.Runtime.Enable(); test.Dispatch();
        Assert.Equal(["The blonde girl hops up and down."], test.Speech);
    }

    [Fact]
    public void DisablingInsideTheOriginalDropsItsSceneAction()
    {
        var test = new Session(StorySceneAction.CurtainsOpen, 0xE5);
        test.Runtime.Enable(); test.Dispatch(test.Runtime.Disable);
        Assert.Empty(test.Speech); Assert.Equal(1, test.Originals);
    }

    [Theory]
    [InlineData(StorySceneAction.NuRaisesArms)]
    [InlineData(StorySceneAction.NuSettlesDown)]
    public void NuPosesAreDescribedAgainInTheNextPlayerConversation(StorySceneAction action)
    {
        var test = new Session(action, 0xAC);
        test.Before = test.Before with { Control = 1 };
        test.Runtime.Enable(); test.Dispatch(); test.Dispatch();
        Assert.Single(test.Speech);
        test.Runtime.Observe(new DialogueClosed());
        test.Dispatch(); test.Dispatch();
        Assert.Equal(2, test.Speech.Count);
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 1)]
    public void OnlyProvenNativeControlRestorationRearmsTheNuAfterItsIntroduction(bool restored, int expected)
    {
        var test = new Session(StorySceneAction.NuRaisesArms, 0xAC, _ => restored);
        var pose = test.Before with { Control = 0 };
        test.Before = pose; test.Runtime.Enable(); test.Dispatch();
        test.Before = pose with { Location = pose.Location with { Opcode = 0xE3 }, Bytes = "E301000000000000" };
        test.Dispatch(); test.Before = pose; test.Dispatch();
        Assert.Equal(expected, test.Speech.Count);
        Assert.Equal(3, test.Originals);
    }

    [Fact]
    public void ClosingDialogueDoesNotRepeatASceneOnceOnlyApproach()
    {
        var test = new Session(StorySceneAction.NuApproachesParty, 0x92);
        test.Before = test.Before with { Control = 1 };
        test.Runtime.Enable(); test.Dispatch(); test.Runtime.Observe(new DialogueClosed()); test.Dispatch();
        Assert.Single(test.Speech);
    }

    private sealed class Session
    {
        public StoryActionSnapshot Before;
        public bool Completed = true;
        public int Originals;
        public List<string> Speech { get; } = [];
        public StoryActionRuntime Runtime { get; }
        public Session(StorySceneAction action, int opcode, Func<StoryActionSnapshot, bool>? restore = null)
        {
            Before = StoryActionRuntimeTests.Frame() with
            {
                Location = StoryActionRuntimeTests.Frame().Location with { Opcode = opcode },
                Bytes = $"{opcode:X2}0B000000000000",
                ActorState = StoryActionRuntimeTests.Actor() with { DrawMode = 0 }
            };
            Runtime = new((_, _) => Before, _ => new(StoryAnimationKind.Looping, 0x0B),
                () => true, _ => "Cinder", Speech.Add, _ => { },
                matchScene: snapshot => new(action, snapshot, StoryActionRuntimeTests.Actor(), null, false),
                completeScene: _ => Completed && Originals > 0, controlRestored: restore);
        }
        public void Dispatch(Action? inside = null) => Runtime.Dispatch(0x1000, Before.Location.Opcode,
            () => { Originals++; inside?.Invoke(); });
    }
}
