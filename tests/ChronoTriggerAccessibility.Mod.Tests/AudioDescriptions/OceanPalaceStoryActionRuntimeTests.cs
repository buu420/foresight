using ChronoTriggerAccessibility.Core.Dialogue;
using ChronoTriggerAccessibility.Mod.AudioDescriptions;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.AudioDescriptions;

/// <summary>Runtime behaviour of the Ocean Palace scene actions. The two Crono AA 22 handlers are the
/// exact anchored locations, so the real <see cref="StorySceneActionCapture.SupersedesGesture"/> applies.</summary>
public sealed class OceanPalaceStoryActionRuntimeTests
{
    public static TheoryData<int, int, uint, string, StorySceneAction, string> ArmsPoses => new()
    {
        { 415, 223, 0x49A, "AA221103FC058940", StorySceneAction.CronoRaisesHandsToMachine, "Cinder raises both hands toward the machine." },
        { 423, 230, 0x7A0, "AA220FAD040D009C", StorySceneAction.CronoRaisesArmsBeforeLavos, "Cinder raises both arms toward Lavos." },
    };

    [Theory]
    [MemberData(nameof(ArmsPoses))]
    public void BothArmsPoseHandlersSpeakOnlyTheirSceneOnceAcrossRepeatedDispatches(int scene, int script, uint pc,
        string bytes, StorySceneAction action, string expected)
    {
        var test = new Session(Arms(scene, script, pc, bytes), action);
        test.Runtime.Enable();
        test.Dispatch(); test.Dispatch(); test.Dispatch();
        Assert.Equal([expected], test.Speech);
        Assert.Equal(3, test.Originals);
    }

    [Theory]
    [MemberData(nameof(ArmsPoses))]
    public void ARejectedArmsPoseIsSilentInsteadOfFallingBackToTheGenericGesture(int scene, int script, uint pc,
        string bytes, StorySceneAction action, string expected)
    {
        // The scene match is rejected (for example the subject is off camera) while the generic
        // proof and catalogue would otherwise say "Cinder waves their arms."
        var test = new Session(Arms(scene, script, pc, bytes), action) { Matched = false };
        test.Runtime.Enable();
        test.Dispatch(); test.Dispatch();
        Assert.Empty(test.Speech);

        test.Matched = true; test.Dispatch(); test.Dispatch();
        Assert.Equal([expected], test.Speech);
        Assert.Equal(4, test.Originals);
    }

    [Theory]
    [MemberData(nameof(ArmsPoses))]
    public void AnArmsPoseWithoutTheAppliedNativeResultSpeaksNothing(int scene, int script, uint pc,
        string bytes, StorySceneAction action, string expected)
    {
        _ = expected;
        var test = new Session(Arms(scene, script, pc, bytes), action) { Completed = false };
        test.Runtime.Enable();
        test.Dispatch(); test.Dispatch();
        Assert.Empty(test.Speech);
        Assert.Equal(2, test.Originals);
    }

    [Theory]
    [MemberData(nameof(ArmsPoses))]
    public void TheSameGestureAtAnyOtherInstructionKeepsItsGenericDescription(int scene, int script, uint pc,
        string bytes, StorySceneAction action, string expected)
    {
        _ = action; _ = expected;
        foreach (var location in new Func<StoryScriptLocation, StoryScriptLocation>[]
                 {
                     l => l with { Address = pc + 2 },
                     l => l with { Scene = scene + 1 },
                     l => l with { ScriptId = script + 1 },
                     l => l with { Actor = 2 },
                 })
        {
            var arms = Arms(scene, script, pc, bytes);
            var test = new Session(arms with { Location = location(arms.Location) }, action) { Matched = false };
            test.Runtime.Enable(); test.Dispatch();
            Assert.Equal(["Cinder waves their arms."], test.Speech);
        }
    }

    public static TheoryData<StorySceneAction, int, string> Actions => new()
    {
        { StorySceneAction.MuneSpinsAndVanishes, 0x92, "Mune spins around and vanishes." },
        { StorySceneAction.MasaSpinsAndVanishes, 0x92, "Masa spins around and vanishes." },
        { StorySceneAction.CronoRaisesHandsToMachine, 0xAA, "Cinder raises both hands toward the machine." },
        { StorySceneAction.KnifeRises, 0x92, "A glowing red knife rises." },
        { StorySceneAction.KnifeShootsIntoMachine, 0x92, "The knife shoots up into the machine." },
        { StorySceneAction.SwordAppearsInMachine, 0xAC, "A sword appears in the machine." },
        { StorySceneAction.SchalaHoldsOutHands, 0xAA, "Schala holds out both hands." },
        { StorySceneAction.PartyGoneSchalaKneels, 0xAA, "Schala kneels alone, head bowed." },
        { StorySceneAction.MagusStandsWhereProphetWas, 0xAA, "The hooded Prophet is gone. Magus stands in his place." },
        { StorySceneAction.SchalaKnockedToKnees, 0xAA, "Schala drops to her knees." },
        { StorySceneAction.MagusThrownDown, 0xAA, "Magus is thrown backward, face down." },
        { StorySceneAction.CronoRaisesArmsBeforeLavos, 0xAA, "Cinder raises both arms toward Lavos." },
        { StorySceneAction.CronoCrumbles, 0xAC, "Cinder turns into a dark silhouette and crumbles away." },
    };

    [Theory]
    [MemberData(nameof(Actions))]
    public void EachOceanActionIsDescribedOncePerSceneInstanceAfterItsNativeProof(StorySceneAction action, int opcode, string expected)
    {
        var frame = StoryActionRuntimeTests.Frame() with
        {
            Location = StoryActionRuntimeTests.Frame().Location with { Opcode = opcode },
            Bytes = $"{opcode:X2}00000000000000", StoryPoint = 0xC9,
        };
        var test = new Session(frame, action);
        test.Runtime.Enable();
        test.Dispatch(() => Assert.Empty(test.Speech)); test.Dispatch();
        test.Runtime.Observe(new DialogueClosed()); test.Dispatch();
        Assert.Equal([expected], test.Speech);

        // A new scene instance (the actor store reloaded) may show the action again.
        test.Before = frame with { Location = frame.Location with { Data = 0x2100 } };
        test.Dispatch(); test.Dispatch();
        Assert.Equal([expected, expected], test.Speech);
        Assert.Equal(5, test.Originals);
    }

    [Theory]
    [InlineData("Crono", "scene-cronocrumbles-named", "Crono turns into a dark silhouette and crumbles away.")]
    [InlineData("Cinder", "scene-cronocrumbles-appearance", "The red-haired boy turns into a dark silhouette and crumbles away.")]
    public void RecordedCronoNarrationKeepsTheCurrentNameInTheCaptionAndASafeVoice(string name, string id, string voice)
    {
        var narrated = new List<StoryNarration>();
        var frame = StoryActionRuntimeTests.Frame() with
        {
            Location = StoryActionRuntimeTests.Frame().Location with { Opcode = 0xAC },
            Bytes = "ACF09C4002ACF19C", StoryPoint = 0xC9,
        };
        var runtime = new StoryActionRuntime((_, _) => frame, _ => null, () => true, _ => name,
            _ => Assert.Fail("Recorded narration must not also use screen-reader speech"), _ => { },
            matchScene: snapshot => new(StorySceneAction.CronoCrumbles, snapshot, snapshot.ActorState, null, false),
            completeScene: _ => true, narrate: narrated.Add);
        runtime.Enable(); runtime.Dispatch(0x1000, 0xAC, () => { });
        var cue = Assert.Single(narrated);
        Assert.Equal(id, cue.CueId); Assert.Equal(voice, cue.VoiceText);
        Assert.Equal($"{name} turns into a dark silhouette and crumbles away.", cue.Text);
    }

    [Fact]
    public void RecordedNonCronoNarrationUsesItsSingleCue()
    {
        var narrated = new List<StoryNarration>();
        var frame = StoryActionRuntimeTests.Frame() with
        {
            Location = StoryActionRuntimeTests.Frame().Location with { Opcode = 0x92 },
            Bytes = "92C00CC19900C19A", StoryPoint = 0xC9,
        };
        var runtime = new StoryActionRuntime((_, _) => frame, _ => null, () => true, _ => "Cinder", _ => { }, _ => { },
            matchScene: snapshot => new(StorySceneAction.KnifeRises, snapshot, snapshot.ActorState, null, false),
            completeScene: _ => true, narrate: narrated.Add);
        runtime.Enable(); runtime.Dispatch(0x1000, 0x92, () => { });
        Assert.Equal(new StoryNarration("A glowing red knife rises.", "scene-kniferises", "A glowing red knife rises."),
            Assert.Single(narrated));
    }

    /// <summary>Crono (class 0, visual 0) drawn on screen at the exact anchored AA 22 instruction.</summary>
    private static StoryActionSnapshot Arms(int scene, int script, uint pc, string bytes) =>
        StoryActionRuntimeTests.Frame() with
        {
            Location = new(0x1000, 0x2000, 0x3000, 0x4000, 0x5000, scene, script, 1, 28, pc, 0xAA),
            Bytes = bytes, StoryPoint = 0xC9,
        };

    private sealed class Session
    {
        public StoryActionSnapshot Before;
        public bool Matched = true, Completed = true;
        public int Originals;
        public List<string> Speech { get; } = [];
        public StoryActionRuntime Runtime { get; }
        public Session(StoryActionSnapshot before, StorySceneAction action)
        {
            Before = before;
            Runtime = new((_, _) => Before, snapshot => new(StoryAnimationKind.Looping, snapshot.Operand),
                () => true, _ => "Cinder", Speech.Add, _ => { },
                matchScene: snapshot => Matched ? new(action, snapshot, snapshot.ActorState, null, false) : null,
                completeScene: _ => Completed && Originals > 0);
        }
        public void Dispatch(Action? inside = null) => Runtime.Dispatch(0x1000, Before.Location.Opcode,
            () => { Originals++; inside?.Invoke(); });
    }
}
