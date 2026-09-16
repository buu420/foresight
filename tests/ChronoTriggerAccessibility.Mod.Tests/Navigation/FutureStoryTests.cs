using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class FutureStoryTests
{
    [Fact]
    public void ArrisInvestigationAdvancesFromFoodToRatToTheRightConsole()
    {
        var available = new[] { Target("exit:1"), Target("exit:2"), Target("landmark:8"), Target("actor:9:4:112") };
        Assert.Equal("Search for the food stores", Goal(216, State(53, (0xEC, 0)), available).Label);
        Assert.Equal("Return to the rat in the rafters", Goal(216, State(53, (0xEC, 0x10)), available).Label);
        var console = Goal(216, State(53, (0xEC, 0x40), (0xA4, 0)), available);
        Assert.Equal(available[3].ApproachPoints, console.ApproachPoints);
        Assert.Equal("Use the right door console", console.Label);
        Assert.Equal("Enter the eastern basement passage",
            Goal(216, State(53, (0xEC, 0x40), (0xA4, 0x40)), available).Label);
        Assert.Equal("Return upstairs to Doan", Goal(216, State(54), [Target("exit:0")]).Label);
    }

    [Fact]
    public void UnknownFlagsDoNotInventPuzzleProgressOrExposeUnseenTargets()
    {
        var unknown = Goal(216, State(53), [Target("landmark:8")]);
        Assert.True(unknown.IsStoryNote);
        Assert.Contains("unavailable", unknown.Instruction);
        var missing = Goal(235, State(60, (0x1D0, 0)), []);
        Assert.True(missing.IsStoryNote);
        Assert.Empty(missing.ApproachPoints);
        Assert.DoesNotContain("B R", missing.Instruction);
        Assert.Empty(FutureStoryTargets.Build(214, null, [], default));
        Assert.Empty(FutureStoryTargets.Build(214, State(100), [], default));
    }

    [Fact]
    public void CompletedRatHuntRetiresTheChaseEvenThoughTheRatIsStillVisible()
    {
        var rat = Target("actor:12:5:134");
        Assert.Equal("Catch the rat", Goal(221, State(53, (0xEC, 0x10)), [rat]).Label);
        var after = Goal(221, State(53, (0xEC, 0x40)), [rat, Target("exit:0")]);
        Assert.Equal("Return to the basement consoles", after.Label);
        Assert.Equal(Target("exit:0").ApproachPoints, after.ApproachPoints);
    }

    [Fact]
    public void ReadingTheSupplyNoteTurnsTheRouteBackThroughTheGuardianChamber()
    {
        var exits = new[] { Target("exit:0"), Target("exit:1") };
        Assert.Equal(exits[1].ApproachPoints, Goal(219, State(53, (0xEC, 0)), exits).ApproachPoints);
        Assert.Equal(exits[0].ApproachPoints, Goal(219, State(53, (0xEC, 0x10)), exits).ApproachPoints);
        Assert.Equal(exits[0].ApproachPoints, Goal(219, State(53, (0xEC, 0x40)), exits).ApproachPoints);
        Assert.Equal("Return to the rafters", Goal(344, State(53, (0xEC, 0x10)), exits).Label);
    }

    [Fact]
    public void FactoryObjectivesUseTheCraneAndSecurityFlagsAndAvoidDisabledLifts()
    {
        var targets = new[] { Target("actor:8:4:127"), Target("landmark:9"), Target("landmark:10") };
        Assert.Equal("Deactivate the entrance security", Goal(228, State(60, (0x58, 0)), targets).Label);
        Assert.Equal("Take the lift to the warehouse", Goal(228, State(60, (0x58, 4)), targets).Label);
        Assert.Equal("Take the lift to the laboratory", Goal(228, State(60, (0x58, 0x64)), targets).Label);
        Assert.Equal("Enter the security passcode", Goal(235, State(60, (0x1D0, 0)), [Target("landmark:15")]).Label);
        Assert.Equal("Activate the power switch", Goal(235, State(60, (0x1D0, 1)), [Target("landmark:10")]).Label);
        Assert.Equal("Escape by the laboratory ladder", Goal(235, State(63), [Target("exit:0"), Target("landmark:9")]).Label);
    }

    [Theory]
    [InlineData(212, 51, 1, 0)]
    [InlineData(213, 51, 0, 1)]
    [InlineData(215, 53, 1, 0)]
    [InlineData(215, 54, 0, 1)]
    [InlineData(217, 53, 1, 0)]
    [InlineData(217, 54, 0, 1)]
    [InlineData(224, 55, 1, 0)]
    [InlineData(225, 55, 0, 1)]
    public void PassageObjectivesChooseTheOnwardExitEvenWhenTheReturnIsNearer(int scene, int point, int onward, int back)
    {
        var forward = Target($"exit:{onward}") with { Position = new(8192, 8192, 1), ApproachPoints = [new(8192, 8192, 1)] };
        var objective = Goal(scene, State(point), [Target($"exit:{back}"), forward]);
        Assert.Equal(forward.ApproachPoints, objective.ApproachPoints);
    }

    [Fact]
    public void GuideSceneryUsesLivePositionsAndDropsRetiredMarkers()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var marker = Actor(16, 1280, 1280) with { ActivationEnabled = 0, ActivationBinding = 0 };
        var field = Field(210, marker);
        var first = source.Build(field, Map(), new(0, 0, 2048, 2048), [], State(51));
        var rest = Assert.Single(first.Targets, t => t.Id == "landmark:16");
        Assert.Equal("Enertron", rest.Label);
        Assert.Equal(new(1280, 1280, 1), rest.Position);
        var away = source.Build(field, Map(), new(4096, 4096, 8192, 8192), [], State(51));
        Assert.Contains(away.Targets, t => t.Id == rest.Id && !t.Visible && t.Discovered);
        var inactive = source.Build(field with { Actors = [field.Actors[0], marker with { ScriptCallsEnabled = false }] },
            Map(), new(0, 0, 2048, 2048), [], State(51));
        Assert.DoesNotContain(inactive.Targets, t => t.Id == rest.Id);
        source.Reset();
        var unseen = Assert.Single(source.Build(field, Map(), new(4096, 4096, 8192, 8192), [], State(51)).Targets, t => t.Id == rest.Id);
        Assert.True(unseen.GuideAvailable);
        Assert.False(unseen.Discovered);
        Assert.DoesNotContain(source.Build(field with { SceneIdCoherent = false }, Map(), new(0, 0, 2048, 2048), [], State(51)).Targets,
            t => t.Id == rest.Id || t.Category == NavigationCategory.StoryEvents);
    }

    [Fact]
    public void JohnnyUsesDifferentScriptSlotsOnTheTwoSidesOfTheHighway()
    {
        var west = Actor(13, 1024, 1024) with { ClassTag = 4, VisualIndex = 2, DrawMode = 1 };
        var east = west with { Index = 8 };
        Assert.Equal("Johnny", FutureAreaLabels.ActorLabel(223, west, State(55)));
        Assert.Equal("Johnny", FutureAreaLabels.ActorLabel(225, east, State(55)));
        Assert.Null(FutureAreaLabels.ActorLabel(225, west, State(55)));
        Assert.Null(FutureAreaLabels.ActorLabel(223, east, State(55)));
    }

    [Fact]
    public void HighwayGoalReachesTheNativeTransitionRegionInsteadOfStoppingAtTheJetbike()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var field = Field(223, Actor(8, 43 * 256 + 128, 5 * 256 + 255));
        var frame = source.Build(field, Map(), new(42 * 256, 0, 50 * 256, 12 * 256), [], State(55));
        var onward = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.False(onward.IsStoryNote);
        Assert.NotEmpty(onward.ApproachPoints);
        Assert.All(onward.ApproachPoints, p => { Assert.True(p.X / 256 > 46); Assert.True(p.Y / 256 < 11); });
        Assert.DoesNotContain(frame.Targets, t => t.Id == "landmark:8");
        Assert.Contains(frame.Targets, t => t.Category == NavigationCategory.Exits && t.Label == "Eastern highway");
        source.Reset();
        var unseen = Assert.Single(source.Build(field, Map(), new(0, 0, 256, 256), [], State(55)).Targets);
        Assert.False(unseen.IsStoryNote);
        Assert.False(unseen.Discovered);
        Assert.NotEmpty(unseen.ApproachPoints);
    }

    [Fact]
    public void TheRecordRoomUsesTheNativeArrivalRowAndEndsAfterTheRecording()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var field = Field(218, Actor(11, 65535, 65535));
        var before = source.Build(field, Map(), new(52 * 256, 40 * 256, 60 * 256, 47 * 256), [], State(53));
        var objective = Assert.Single(before.Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.NotEmpty(objective.ApproachPoints);
        Assert.All(objective.ApproachPoints, p => Assert.Equal(42, p.Y / 256));
        var after = source.Build(field, Map(), new(52 * 256, 40 * 256, 60 * 256, 47 * 256), [], State(54));
        Assert.DoesNotContain(after.Targets, t => t.Id.Contains("record-arrival"));
        Assert.Equal("Return to Doan", Assert.Single(after.Targets).Label);
    }

    [Fact]
    public void EndOfTimeTracksTheIntroductionCallBackAndMagicLesson()
    {
        var oldMan = Target("actor:28:4:72");
        Assert.Equal("Speak with the old man", Goal(464, State(72), [oldMan]).Label);
        Assert.Equal("Return toward the pillars of light", Goal(464, State(73), [Target("landmark:24")]).Label);
        Assert.Equal("Speak with the old man again", Goal(464, State(74), [oldMan]).Label);
        Assert.Equal("Enter the room behind the old man", Goal(464, State(75), [Target("exit:0")]).Label);
        Assert.Equal("Speak with Spekkio", Goal(465, State(75), [Target("actor:10:5:225")]).Label);
        var lesson = Goal(465, State(76), []);
        Assert.True(lesson.IsStoryNote);
        Assert.Contains("clockwise", lesson.Instruction);
        Assert.Equal("Return to the End of Time platform", Goal(465, State(77), [Target("exit:0")]).Label);
        Assert.Empty(FutureStoryTargets.Build(465, State(78), [], default));
    }

    private static NavigationTarget Goal(int scene, FieldStoryState state, IReadOnlyList<NavigationTarget> targets) =>
        Assert.Single(FutureStoryTargets.Build(scene, state, targets, default));
    private static FieldStoryState State(int point, params (int Index, int Value)[] flags) => new(point, false)
    { Globals = flags.ToDictionary(x => x.Index, x => x.Value) };
    private static NavigationTarget Target(string id) => new(id, id, NavigationCategory.Objects,
        new(512, 512, 1), [new(id.Select((c, i) => c * (i + 1)).Sum() & 0xFFF, 512, 1)], true, true);
    private static FieldActorSnapshot Actor(int index, int x, int y) =>
        new(index, x >> 8, x, x & 255, y >> 8, y, y & 255, 0, 0, 0, 0, 7, 0, 1, 1, false, true, true, true);
    private static FieldNavigationSnapshot Field(int scene, params FieldActorSnapshot[] objects)
    {
        var lead = Actor(0, 128, 128) with { ClassTag = 2, DrawMode = 1, IsPartyMember = true };
        return new(0x1000, 0x4000, 0x2000, 0x20000, objects.Length + 1, scene, true, 1, 0, 0, 0, lead, [lead, .. objects]);
    }
    private static FieldMapSnapshot Map() => new(64, 64, new byte[4096], new byte[4096],
        Enumerable.Repeat((byte)1, 4096).ToArray(), 1, false, 64, 64, Enumerable.Repeat((byte)128, 4096).ToArray());
    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
