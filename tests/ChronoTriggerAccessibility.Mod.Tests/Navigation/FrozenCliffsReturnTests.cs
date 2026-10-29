using ChronoTriggerAccessibility.Core.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class FrozenCliffsReturnTests
{
    [Theory]
    [InlineData(4466,12932,false)] [InlineData(4466,12932,true)]
    [InlineData(4493,12548,false)] [InlineData(4493,12548,true)]
    [InlineData(3225,13956,false)] [InlineData(3225,13956,true)]
    public void LoggedLatePosesUseTheNativeReturnCaveBeforeRetryingFromAbove(int x,int y,bool automatic)
    {
        var frame=FrozenCliffsNavigationTests.Sample(604,pose:new(x,y,1)).Frame;
        var story=Assert.Single(frame.Targets,t=>t.Category==NavigationCategory.StoryEvents&&!t.IsStoryNote);
        var search=NavigationPathfinder.Search(frame.Graph,frame.Player,story.ApproachPoints);
        var bottom=Assert.Single(frame.Targets,t=>t.Label=="Bottom cave F");
        Assert.NotNull(search.Route);
        Assert.Equal(bottom.Id,search.IntermediateId);
        var controller=Select(frame,story.Id);
        var result=controller.Handle(automatic?NavigationCommand.ToggleWalk:NavigationCommand.Guide,frame,1);
        Assert.True(result.Guiding);
        Assert.Contains(result.Speech,line=>line.Contains("return to the entrance",StringComparison.Ordinal));
        // Arriving at the return cave must finish with North, never just wait
        // for a door to open or synthesize Confirm.
        var at=frame with{Player=search.Route[^1]};
        result=controller.Update(at,32);
        if(automatic) Assert.Equal(NavigationDirection.North,result.Direction);
        else Assert.Equal(NavigationDirection.None,result.Direction);
        Assert.Contains(result.Speech,line=>line.Contains("Continue up",StringComparison.Ordinal));
    }

    [Fact]
    public void FreshUpperEntryStillGoesDirectlyToTheSelectedCave()
    {
        var frame=FrozenCliffsNavigationTests.Sample(604).Frame;
        foreach(var cave in frame.Targets.Where(t=>t.Id.StartsWith("script-region:9:",StringComparison.Ordinal)))
        {
            var search=NavigationPathfinder.Search(frame.Graph,frame.Player,cave.ApproachPoints);
            Assert.NotNull(search.Route);Assert.Null(search.IntermediateId);
        }
    }

    [Fact]
    public void SmallSearchBudgetDoesNotAuthorizeAReturn()
    {
        var frame=FrozenCliffsNavigationTests.Sample(604,pose:new(4466,12932,1)).Frame;
        var cave=frame.Targets.Single(t=>t.Label=="Left cave B");
        var search=NavigationPathfinder.Search(frame.Graph,frame.Player,cave.ApproachPoints,1);
        Assert.Null(search.Route);Assert.True(search.LimitReached);Assert.Null(search.IntermediateId);
    }

    [Fact]
    public void RevokedReturnEntranceStopsTheOldStage()
    {
        var frame=FrozenCliffsNavigationTests.Sample(604,pose:new(4466,12932,1)).Frame;
        var cave=frame.Targets.Single(t=>t.Label=="Left cave B");
        var controller=Select(frame,cave.Id);
        Assert.True(controller.Handle(NavigationCommand.ToggleWalk,frame,1).Guiding);
        var bottom=frame.Targets.Single(t=>t.Label=="Bottom cave F");
        var result=controller.Update(frame with{Targets=frame.Targets.Where(t=>t.Id!=bottom.Id).ToArray()},32);
        Assert.False(result.Guiding);Assert.Equal(NavigationDirection.None,result.Direction);
    }

    [Theory]
    [InlineData(602,28,12,0)] [InlineData(602,17,25,0)] [InlineData(602,15,34,0)] [InlineData(603,6,30,1)]
    public void NativeReturnLandingsHaveAStoryRouteBackTowardTheSlope(int scene,int x,int y,int exit)
    {
        var sample=FrozenCliffsNavigationTests.Sample(scene,pose:new(x*256+128,y*256+255,1));
        var story=Assert.Single(sample.Frame.Targets,t=>t.Id=="story:full:lavos");
        Assert.False(story.IsStoryNote);
        var doorway=sample.Frame.Targets.Single(t=>t.Id=="exit:"+exit);
        Assert.All(story.ApproachPoints,p=>Assert.Contains(p,doorway.ApproachPoints));
        Assert.True(FrozenCliffsNavigationTests.Start(sample.Frame,story.Id,false).Guiding);
        Assert.True(FrozenCliffsNavigationTests.Start(sample.Frame,story.Id,true).Guiding);
    }

    private static NavigationController Select(NavigationFrame frame,string id)
    {
        // Select by the destination's native navigation category.
        var category=frame.Targets.Single(t=>t.Id==id).Category;
        var controller=new NavigationController();
        for(int i=0;i<(int)category;i++) controller.Handle(NavigationCommand.NextCategory,frame,0);
        controller.Handle(NavigationCommand.Repeat,frame,0);
        for(int i=0;i<30&&controller.TargetId!=id;i++) controller.Handle(NavigationCommand.NextTarget,frame,0);
        Assert.Equal(id,controller.TargetId);return controller;
    }
}
