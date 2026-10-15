using ChronoTriggerAccessibility.Core.Battle;
using ChronoTriggerAccessibility.Mod.AudioDescriptions;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.AudioDescriptions;

/// <summary>Zeal plaza books: each verified player toggle describes the visible change once;
/// repeated open/close cycles are allowed, an identical repeat is not, and the passage is
/// described once when its native copy is queued. Only visible book effects are named; no puzzle order or future outcome is given.</summary>
public sealed class StoryBookActionRuntimeTests
{
    [Fact]
    public void EachVisibleToggleIsDescribedOnceAndCyclesAreAllowed()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Run(StorySceneAction.BookAOpens);
        test.Run(StorySceneAction.BookACloses);
        test.Run(StorySceneAction.BookAOpens);
        Assert.Equal(["Book A opens. A flame flickers above it.", "Book A closes, and the flame disappears.", "Book A opens. A flame flickers above it."], test.Speech);
        Assert.Equal(3, test.Originals);
    }

    [Fact]
    public void AnIdenticalRepeatOfTheSameChangeIsNotSpokenAgain()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Run(StorySceneAction.BookBOpens);
        test.Run(StorySceneAction.BookBOpens);
        Assert.Equal(["Book B opens. Water splashes above it."], test.Speech);
        Assert.Equal(2, test.Originals);
    }

    [Fact]
    public void EachBookKeepsItsOwnState()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Run(StorySceneAction.BookAOpens);
        test.Run(StorySceneAction.BookBOpens);
        test.Run(StorySceneAction.BookCOpens);
        test.Run(StorySceneAction.BookCCloses);
        Assert.Equal(["Book A opens. A flame flickers above it.", "Book B opens. Water splashes above it.", "Book C opens. Blue-green wisps swirl above it.", "Book C closes, and the swirling wisps disappear."], test.Speech);
    }

    [Fact]
    public void ThePassageIsDescribedOncePerSceneInstance()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Run(StorySceneAction.PassageOpens);
        test.Run(StorySceneAction.PassageOpens);
        Assert.Equal(["A passage opens."], test.Speech);
    }

    [Fact]
    public void ANewSceneInstanceStartsWithUnknownBookStates()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Run(StorySceneAction.BookAOpens);
        test.Before = test.Before with { Location = test.Before.Location with { Context = 0x2000 } };
        test.Run(StorySceneAction.BookAOpens);
        test.Run(StorySceneAction.PassageOpens);
        Assert.Equal(["Book A opens. A flame flickers above it.", "Book A opens. A flame flickers above it.", "A passage opens."], test.Speech);
    }

    [Fact]
    public void AResetForgetsTheLastDescribedState()
    {
        var test = new Session(); test.Runtime.Enable();
        test.Run(StorySceneAction.BookAOpens);
        test.Runtime.Observe(new BattleStarted(1));
        test.Run(StorySceneAction.BookAOpens);
        Assert.Equal(["Book A opens. A flame flickers above it.", "Book A opens. A flame flickers above it."], test.Speech);
    }

    [Fact]
    public void WithoutTheAppliedNativeCopyNothingIsSaidAndTheOriginalStillRunsOnce()
    {
        var test = new Session { Completed = false }; test.Runtime.Enable();
        test.Run(StorySceneAction.BookAOpens);
        Assert.Empty(test.Speech); Assert.Equal(1, test.Originals);
        // An unproven toggle must not block the next proven one.
        test.Completed = true;
        test.Run(StorySceneAction.BookAOpens);
        Assert.Equal(["Book A opens. A flame flickers above it."], test.Speech);
    }

    [Fact]
    public void BookChangesUseTheirOwnVerifiedRecordings()
    {
        var narrated = new List<StoryNarration>();
        var test = new Session(narrated.Add); test.Runtime.Enable();
        test.Run(StorySceneAction.BookAOpens);
        test.Run(StorySceneAction.PassageOpens);
        Assert.Empty(test.Speech);
        Assert.Equal(new[] { "scene-bookaopens", "scene-passageopens" }, narrated.Select(cue => cue.CueId));

        test.Run(StorySceneAction.CurtainsOpen);
        Assert.Equal("scene-curtainsopen", narrated[2].CueId);
    }

    [Fact]
    public void BookTextsNameOnlyStableLettersAndTheVisibleChange()
    {
        var before = StoryActionRuntimeTests.Frame();
        var texts = new[]
        {
            StorySceneAction.BookAOpens, StorySceneAction.BookACloses, StorySceneAction.BookBOpens,
            StorySceneAction.BookBCloses, StorySceneAction.BookCOpens, StorySceneAction.BookCCloses,
            StorySceneAction.PassageOpens,
        }.Select(action => StoryActionCatalog.DescribeScene(new(action, before, before.ActorState, null, false), _ => "Cinder")).ToArray();
        Assert.Equal(["Book A opens. A flame flickers above it.", "Book A closes, and the flame disappears.", "Book B opens. Water splashes above it.", "Book B closes, and the water disappears.", "Book C opens. Blue-green wisps swirl above it.",
            "Book C closes, and the swirling wisps disappear.", "A passage opens."], texts);
        foreach (var word in new[] { "hidden", "secret", "correct", "order" })
            Assert.All(texts, text => Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RecordedVoicePackIncludesSevenVerifiedBookAndPassageCues()
    {
        Assert.Equal(156, StoryVoiceCueCatalog.All.Count);
        Assert.Equal(7, StoryVoiceCueCatalog.All.Count(cue => cue.Id.Contains("book") || cue.Id.Contains("passage")));
        Assert.True(StoryVoiceCueCatalog.IsRecorded(StorySceneAction.BookAOpens));
        Assert.True(StoryVoiceCueCatalog.IsRecorded(StorySceneAction.PassageOpens));
        Assert.True(StoryVoiceCueCatalog.IsRecorded(StorySceneAction.GirlHops));
    }

    private sealed class Session
    {
        public StoryActionSnapshot Before;
        public bool Completed = true;
        public int Originals;
        public StorySceneAction Action;
        public List<string> Speech { get; } = [];
        public StoryActionRuntime Runtime { get; }
        public Session(Action<StoryNarration>? narrate = null)
        {
            Before = StoryActionRuntimeTests.Frame() with
            {
                Location = StoryActionRuntimeTests.Frame().Location with { Opcode = 0xE5 },
                Bytes = "E52F1F2F1F101841",
                ActorState = StoryActionRuntimeTests.Actor() with { DrawMode = 0 },
            };
            Runtime = new((_, _) => Before, _ => null, () => true, _ => "Cinder", Speech.Add, _ => { },
                matchScene: snapshot => new(Action, snapshot, StoryActionRuntimeTests.Actor(), null, false),
                completeScene: _ => Completed && Originals > 0, narrate: narrate);
        }
        public void Run(StorySceneAction action)
        {
            Action = action;
            Runtime.Dispatch(0x1000, 0xE5, () => Originals++);
        }
    }
}
