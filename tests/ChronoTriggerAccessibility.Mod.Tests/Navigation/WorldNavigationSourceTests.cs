using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class WorldNavigationSourceTests
{
    [Fact]
    public void OnlyVisibleOrDiscoveredEnabledEntrancesBecomeTargets()
    {
        var source = Source();
        var inn = Entrance(6, 25, 19, 6, 12, 400,304);
        var far = Entrance(9, 70, 20, 10, 5, 1120,320);
        var world = Snapshot([inn, far]);
        var frame = source.Build(world, Label);
        Assert.Single(frame.Targets);
        Assert.Equal("Truce Inn", frame.Targets[0].Label);
        var away = world with { Viewport = new(16000, 0, 24000, 16000) };
        frame = source.Build(away, Label);
        Assert.Contains(frame.Targets, t => t.Label == "Truce Inn" && !t.Visible && t.Discovered);
        frame = source.Build(away with { Entrances = [inn with { Available = false }, far] }, Label);
        Assert.DoesNotContain(frame.Targets, t => t.Label == "Truce Inn");
    }

    [Fact]
    public void MultipleDoorRecordsHaveOneStableSelectionAndRetainAllUsableGoals()
    {
        var source = Source();
        var first = Entrance(6,25,19,6,12,400,304);
        var second = Entrance(46,26,18,6,12,416,288);
        var world = Snapshot([first, second]);
        var target = Assert.Single(source.Build(world, Label).Targets);
        Assert.Equal(2, target.ApproachPoints.Count);
        Assert.Equal("Press Confirm to enter.", target.Instruction);
        var moved = world with { Motion = world.Motion with { PixelX = 410, PixelY = 290 } };
        var again = Assert.Single(source.Build(moved, Label).Targets);
        Assert.Equal(target.Id, again.Id);
        Assert.Equal(target.ApproachPoints, again.ApproachPoints);
    }

    [Fact]
    public void CurrentStoryGoalsUseUnseenEligibleEntrancesAndRetireWhenUnavailable()
    {
        var fair = Entrance(9,26,14,10,5,416,224);
        var source = Source(); var world = Snapshot([fair]) with { StoryPoint = 3 };
        Assert.Contains(source.Build(world, Label).Targets, t => t.Category == NavigationCategory.StoryEvents && !t.IsStoryNote);
        source.Reset();
        var hidden = world with { Viewport = new(0,0,64,64) };
        var unseen = Assert.Single(source.Build(hidden, Label).Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.Equal(NavigationCategory.StoryEvents, unseen.Category);
        Assert.False(unseen.IsStoryNote);
        Assert.False(unseen.Visible);
        Assert.False(unseen.Discovered);
        Assert.True(unseen.GuideAvailable);
        var controller = new NavigationController();
        var frame = source.Build(hidden, Label);
        controller.Handle(NavigationCommand.PreviousCategory, frame, 0);
        Assert.True(controller.Handle(NavigationCommand.ToggleWalk, frame, 16).AutoWalking);
        Assert.Empty(source.Build(hidden with { Entrances = [fair with { Available = false }] }, Label).Targets);
        Assert.DoesNotContain(source.Build(world with { StoryPoint = 45 }, Label).Targets,
            t => t.Category == NavigationCategory.StoryEvents && t.Label.Contains("fair"));
    }

    [Theory]
    [InlineData(51, 212, 214, -1)]
    [InlineData(55, 223, 226, -1)]
    [InlineData(60, 228, -1, -1)]
    [InlineData(66, 226, -1, -1)]
    [InlineData(77, -1, -1, -1)]
    public void FutureWorldObjectivesAdvanceAndUseTheCorrectSideOfEachRuin(int point, int a, int b, int c)
    {
        var destinations = new[] { 210, 212, 213, 214, 223, 225, 226, 228 };
        var entries = destinations.Select((d, i) => Entrance(i, 25 + i, 19, 38 + i, d, 400 + i * 16, 304)).ToArray();
        var world = Snapshot(entries) with { StoryPoint = point, Viewport = new(0, 0, 20000, 16000) };
        world = world with { Motion = world.Motion with { World = 2 } };
        var goals = Source().Build(world, id => "Dome " + id).Targets.Where(t => t.Category == NavigationCategory.StoryEvents).ToArray();
        var expected = new[] { a, b, c }.Where(d => d != -1).Order().ToArray();
        // A story copy retains the native entrance arrival, including its destination.
        var actual = goals.Select(g => Assert.Single(entries, e =>
            g.ApproachPoints.Contains(new(e.ContactPoints[0].X * 16, e.ContactPoints[0].Y * 16, 1))).Destination).Order().ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FutureStoryDoesNotSendThePlayerAcrossAnUnconnectedWorldRegion()
    {
        var ruins = Entrance(12, 26, 19, 42, 223, 416, 304);
        var proto = Entrance(5, 45, 19, 43, 226, 720, 304);
        var world = Snapshot([ruins, proto]) with { StoryPoint = 55, Viewport = new(0, 0, 20000, 16000) };
        world = world with { Motion = world.Motion with { World = 2 } };
        var map = new byte[6144]; var properties = new byte[512];
        properties[2] = properties[3] = 0x11;
        for (var y = 0; y < 64; y++) map[y * 96 + 35] = 1;
        world = world with { Map = map, Properties = properties };
        var source = Source();
        var west = source.Build(world, id => id == 42 ? "Site 32" : "Proto Dome");
        Assert.Equal("Cross Site 32 toward Proto Dome", Assert.Single(west.Targets, t => t.Category == NavigationCategory.StoryEvents).Label);
        Assert.Contains(west.Targets, t => t.Label == "Proto Dome" && t.Category == NavigationCategory.Exits);
        var east = source.Build(world with { Motion = world.Motion with { PixelX = 720 } }, id => "Dome " + id);
        Assert.Equal("Visit Proto Dome", Assert.Single(east.Targets, t => t.Category == NavigationCategory.StoryEvents).Label);
        // A changed native collision table must invalidate the cached region index.
        var opened = source.Build(world with { Map = new byte[6144] }, id => "Dome " + id);
        Assert.Equal(2, opened.Targets.Count(t => t.Category == NavigationCategory.StoryEvents));
        var closed = source.Build(world, id => "Dome " + id);
        Assert.Single(closed.Targets, t => t.Category == NavigationCategory.StoryEvents);
    }

    [Fact]
    public void OptionalDomeIsAnExitBeforeDiscoveryAndNeverAStoryRequirement()
    {
        var dome = Entrance(2, 26, 19, 38, 210, 416, 304);
        var world = Snapshot([dome]) with { StoryPoint = 51, Viewport = new(0, 0, 64, 64) };
        world = world with { Motion = world.Motion with { World = 2 } };
        var source = Source();
        var optional = Assert.Single(source.Build(world, _ => "Trann Dome").Targets);
        Assert.Equal(NavigationCategory.Exits, optional.Category);
        Assert.True(optional.GuideAvailable);
        Assert.False(optional.Discovered);
        var blocked = new byte[512]; Array.Fill(blocked, (byte)0x11);
        Assert.Empty(source.Build(world with { Properties = blocked }, _ => "Trann Dome").Targets);
        Assert.Single(source.Build(world, _ => "Trann Dome").Targets);
    }

    private static WorldNavigationSource Source() => new(new EmptyMemory(), _ => { });
    private static string? Label(int id) => id switch {6 => "Truce Inn", 10 => "Leene Square", _ => null};
    private static WorldEntrance Entrance(int id,int tx,int ty,int name,int dest,int x,int y) =>
        new(id,tx,ty,name,dest,0,52,28,true) { ContactPoints = [new(x,y)] };
    private static WorldNavigationSnapshot Snapshot(IReadOnlyList<WorldEntrance> exits) =>
        new(new(1,2,3,4,0,0x400,400,304), new byte[6144],new byte[512],exits,
            new(300*16,190*16,520*16,410*16),null);
    private sealed class EmptyMemory : IReadableMemory
    { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
