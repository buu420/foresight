using System.Reflection;
using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class QueenReturnNavigationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ReturnVisitFirstReachesTheNativeDoorContactEvenWhenTheOldMarkerIsDisabled(int opened)
    {
        var frame = Frame(opened, 0);
        var target = Assert.Single(frame.Targets, t => t.Id == "story:find-marle");
        Assert.False(target.IsStoryNote);
        Assert.Equal(new NavigationPoint(10880, 14591, 1), target.Position);
        Assert.NotEmpty(target.ApproachPoints);
        Assert.NotNull(NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route);
        Assert.DoesNotContain(frame.Targets, t => t.Id == "landmark:24");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ArmedReturnEventRemainsRoutableEvenIfTheDoorClosesOnReentry(int opened)
    {
        var frame = Frame(opened, 4);
        var target = Assert.Single(frame.Targets, t => t.Id == "story:find-marle");
        Assert.False(target.IsStoryNote);
        Assert.Contains(target.Position, new[] { new NavigationPoint(10368,12031,1), new NavigationPoint(10624,12031,1) });
        var result = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints);
        Assert.NotNull(result.Route);
        if (opened == 0) Assert.Equal("landmark:8", result.IntermediateId);
    }

    [Theory]
    [InlineData(27)]
    [InlineData(33)]
    public void NativeStoryWindowDoesNotOfferTheReturnContactOutsideItsActiveChapter(int point)
    {
        Assert.DoesNotContain(Frame(1, 4, point).Targets, t => t.Id == "story:find-marle");
    }

    [Fact]
    public void MissingNativeProgressDoesNotChooseACompletedOrPrematureContact()
    {
        var target = Assert.Single(Frame(1, null).Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.True(target.IsStoryNote);
        Assert.Empty(target.ApproachPoints);
    }

    private static NavigationFrame Frame(int opened, int? flag, int point = 28)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.queen-chamber-map-0330.json")!;
        var map = JsonSerializer.Deserialize<FieldMapSnapshot>(stream)!;
        if (opened != 0)
            foreach (var p in new[] { map.CollisionShapes, map.TerrainFlags, map.CollisionLayers })
                for (var y=0;y<3;y++) for(var x=0;x<3;x++) p[(y+54)*64+x+41]=p[(y+32)*64+x+3];
        var pc = FullGameNavigationTests.Actor(1, 14208, 11903, true) with { ClassTag = 0 };
        FieldActorSnapshot Marker(int i,int x,int y) => FullGameNavigationTests.Actor(i,x,y) with
        { ClassTag=7, VisualIndex=0, LoadedFlag=0, ActivationEnabled=0, ActivationBinding=0 };
        var actors = new[] { pc, Marker(8,10880,14591), Marker(24,10368,12287) with { ScriptCallsEnabled=flag==4 },
            Marker(25,10368,12031), Marker(26,10624,12031) };
        var field = FullGameNavigationTests.Field(122) with { LeadPlayer=pc, Actors=actors, ActorCount=28 };
        var state = new FieldStoryState(point, true) { Globals=flag.HasValue ? new Dictionary<int,int>{{0,point},{0xA1,flag.Value}} : new Dictionary<int,int>{{0,point}},
            Locals=new Dictionary<int,int>{{7,opened},{12,0}} };
        return new FieldNavigationSource(new NoMemory(),_=>{}).Build(field,map,new(12032,9984,16256,13184),[],state);
    }

    private sealed class NoMemory : IReadableMemory
    { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
