using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class FullStoryTargetsTests
{
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

    private static NavigationTarget Target(string id) => new(id, "Passage", NavigationCategory.Exits,
        new(3584, 2048, 1), [new(3584, 2048, 1)], false, false);
    private sealed class OpenGraph : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point) => [];
    }
}
