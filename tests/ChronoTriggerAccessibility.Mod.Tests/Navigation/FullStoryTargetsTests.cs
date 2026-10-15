using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class FullStoryTargetsTests
{
    [Theory]
    [InlineData(468, 122, 0)]
    [InlineData(468, 120, 3)]
    [InlineData(480, 121, 0)]
    [InlineData(480, 120, 3)]
    public void LaterCastleVisitsIncludeTheIntermediateTowerLandings(int scene, int goal, int first)
    {
        var exits = Enumerable.Range(0, 6).Select(i => Target($"exit:{i}")).ToArray();
        var choices = new SceneConnectionRouter().Next(scene, new(165, false), [goal], exits);
        Assert.Equal(Enumerable.Range(first, 3).Select(i => $"exit:{i}").Order(), choices.Select(t => t.Id).Order());
    }

    [Fact]
    public void ChangedNativeTowerConnectionsDoNotOfferTheOldIntermediateLandings()
    {
        var exits = Enumerable.Range(0, 6).Select(i => Target($"exit:{i}")).ToArray();
        var destinations = new Dictionary<int, int> { [0] = 497, [1] = 480, [2] = 121 };
        var choices = new SceneConnectionRouter().Next(480, new(165, false), [121], exits,
            liveFieldDestinations: destinations);
        Assert.Equal("exit:2", Assert.Single(choices).Id);
    }

    [Fact]
    public void BindsEndOfTimeObjectiveToPillarRegionWithoutAVisibleActor()
    {
        var region = GameNavigationCatalog.ForScene(464)!.Regions.Single(r => r.Kind == "Warp" && r.Destination == 36);
        var source = new FullStoryTargets();
        var target = Target(region.Id);
        var result = source.Build(464, new FieldStoryState(77, false)
            { Locals = new Dictionary<int, int> { [6] = 0 } }, [target], new(128, 128, 1));
        var story = Assert.Single(result, t => t.Category == NavigationCategory.StoryEvents);
        Assert.False(story.IsStoryNote);
        Assert.True(story.GuideAvailable);
        Assert.Equal(target.ApproachPoints, story.ApproachPoints);
    }

    [Fact]
    public void ReachingHeckransRoomStillRoutesToTheEncounterTrigger()
    {
        var region = GameNavigationCatalog.ForScene(47)!.Regions.Single(r => r.Kind == "Encounter" && r.Top == 31 && r.Bottom == 31);
        var state = new FieldStoryState(78, false)
        {
            Globals = Enumerable.Range(0, 512).ToDictionary(i => i, _ => 0),
            Locals = Enumerable.Range(0, 256).ToDictionary(i => i, _ => 0),
        };
        var target = Target(region.Id);
        var result = new FullStoryTargets().Build(47, state, [target], new(128, 128, 1));
        var story = Assert.Single(result, t => t.Category == NavigationCategory.StoryEvents);
        Assert.False(story.IsStoryNote);
        Assert.Equal(target.ApproachPoints, story.ApproachPoints);
    }

    [Fact]
    public void ScriptPassagesAreTerminalForUnrelatedRoutes()
    {
        var graph = new ScriptPassageGraph(new OpenGraph(), [("gate", 3, 3, 3, 3)]);
        Assert.True(graph.IsTerminal(new(3 * 256 + 64, 3 * 256 + 64, 1)));
        Assert.False(graph.IsSameTerminal(new(3 * 256 + 64, 3 * 256 + 64, 1), new(5 * 256, 5 * 256, 1)));
        Assert.True(graph.IsSameTerminal(new(3 * 256 + 64, 3 * 256 + 64, 1), new(3 * 256 + 128, 3 * 256 + 128, 1)));
    }

    [Fact]
    public void MissingConnectionPreservesTheBoundObjectiveIdentity()
    {
        var state = new FieldStoryState(77, false) { Locals = new Dictionary<int, int> { [6] = 0 } };
        var source = new FullStoryTargets();
        var missing = Assert.Single(source.Build(464, state, [], new(128, 128, 1)), t => t.Category == NavigationCategory.StoryEvents);
        var region = GameNavigationCatalog.ForScene(464)!.Regions.First(r => r.Kind == "Warp" && r.Destination == 36);
        var bound = Assert.Single(source.Build(464, state, [Target(region.Id)], new(128, 128, 1)), t => t.Category == NavigationCategory.StoryEvents);
        Assert.True(missing.IsStoryNote);
        Assert.Equal(bound.Id, missing.Id);
    }

    [Theory]
    [InlineData(0x19E, 0x10, 62, "cyrus:descendant")]
    [InlineData(0x1AD, 0x80, 161, "desert:retinite")]
    public void RemoteQuestPassageIsStoryGuidance(int index, int value, int destination, string suffix)
    {
        var globals = Enumerable.Range(0, 512).ToDictionary(i => i, _ => 0);
        globals[index] = value;
        globals[0xF7] = 2;
        var state = new FieldStoryState(0xD4, false) { Globals = globals };
        var entrance = Target("world:entrance") with { Label = "Residence" };
        var result = new FullStoryTargets().Build(496, state, [entrance], new(128, 128, 1),
            new Dictionary<string, int> { [entrance.Id] = destination });
        var quest = Assert.Single(result, t => t.Id == "story:full:quest:" + suffix);
        Assert.Equal(NavigationCategory.StoryEvents, quest.Category);
        Assert.Equal(entrance.ApproachPoints, quest.ApproachPoints);
        Assert.Contains("Next passage: Residence.", quest.Instruction);
    }

    [Fact]
    public void LocalQuestPersonKeepsThePeopleCategory()
    {
        var globals = Enumerable.Range(0, 512).ToDictionary(i => i, _ => 0);
        globals[0x1A3] = 1;
        var state = new FieldStoryState(0xAE, false) { Globals = globals };
        var fiona = Target("actor:8:native") with { Label = "Fiona", Category = NavigationCategory.People };
        var result = new FullStoryTargets().Build(158, state, [fiona], new(128, 128, 1));
        var quest = Assert.Single(result, t => t.Id == "story:full:quest:desert:robo");
        Assert.Equal(NavigationCategory.People, quest.Category);
        Assert.Equal(fiona.ApproachPoints, quest.ApproachPoints);
    }

    [Fact]
    public void EndOfTimePillarDoesNotIntroduceFionaButCanRouteAnAlreadyHeardRequest()
    {
        var globals = Enumerable.Range(0, 512).ToDictionary(i => i, _ => 0);
        globals[0xF7] = 2;
        globals[0xA6] = 1;
        var state = new FieldStoryState(0xAE, false)
        {
            Globals = globals, Locals = new Dictionary<int, int> { [6] = 0 },
        };
        var region = GameNavigationCatalog.ForScene(464)!.Regions.Single(r => r.Kind == "Warp" && r.Destination == 113);
        var pillar = Target(region.Id) with { Label = "Pillar 5" };
        var source = new FullStoryTargets();
        var unintroduced = source.Build(464, state, [pillar], new(128, 128, 1));
        Assert.DoesNotContain(unintroduced, t => t.Id.StartsWith("story:full:quest:desert:"));
        var heard = state with { Globals = new Dictionary<int, int>(globals) { [0x1AD] = 0x80 } };
        var introduced = source.Build(464, heard, [pillar], new(128, 128, 1));
        var quest = Assert.Single(introduced, t => t.Id == "story:full:quest:desert:retinite");
        Assert.Equal(NavigationCategory.StoryEvents, quest.Category);
        Assert.Equal(pillar.ApproachPoints, quest.ApproachPoints);
    }

    private static NavigationTarget Target(string id) => new(id, "Passage", NavigationCategory.Exits,
        new(3584, 2048, 1), [new(3584, 2048, 1)], false, false);
    private sealed class OpenGraph : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point) => [];
    }
}
