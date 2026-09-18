using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class GuideAvailabilityTests
{
    [Theory]
    [InlineData(NavigationCommand.Guide)]
    [InlineData(NavigationCommand.ToggleWalk)]
    public void UnseenOpeningExitAndStoryBothSupportGuideNavigation(NavigationCommand command)
    {
        var source = Source();
        var map = Map(); map.ExitCells[6 * 8 + 6] = 0;
        var frame = source.Build(Field(2), map, new(0, 0, 256, 256), [], new(3, false));
        var target = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.True(Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Exits).GuideAvailable);
        Assert.Equal("Go downstairs", target.Label);
        Assert.False(target.Visible);
        Assert.False(target.Discovered);
        Assert.False(target.IsStoryNote);
        Assert.NotEmpty(target.ApproachPoints);
        var controller = new NavigationController();
        // Back twice from People: Enemies, then Story Events.
        controller.Handle(NavigationCommand.PreviousCategory, frame, 0);
        controller.Handle(NavigationCommand.PreviousCategory, frame, 0);
        var result = controller.Handle(command, frame, 16);
        Assert.True(result.Guiding);
        Assert.Equal(command == NavigationCommand.ToggleWalk, result.AutoWalking);
        Assert.DoesNotContain(result.Speech, s => s.Contains("discovered"));
    }

    [Fact]
    public void StoryFollowsAnActiveOffscreenActorAndRetiresItsOldPosition()
    {
        var source = Source();
        var mother = Actor(15, 1408, 1408) with { ClassTag = 4, VisualIndex = 37, DrawMode = 1 };
        var frame = source.Build(Field(1, mother), Map(), new(0, 0, 256, 256), [], new(3, false));
        var story = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.False(story.IsStoryNote);
        Assert.Equal(new(1408, 1408, 1), story.Position);
        var moved = source.Build(Field(1, mother with { FineX = 1664, TileX = 6 }), Map(), new(0, 0, 256, 256), [], new(3, false));
        Assert.Equal(1664, Assert.Single(moved.Targets, t => t.Category == NavigationCategory.StoryEvents).Position.X);
        var retired = source.Build(Field(1, mother with { FineX = 65535, TileX = 255 }), Map(), new(0, 0, 256, 256), [], new(3, false));
        Assert.True(Assert.Single(retired.Targets, t => t.Category == NavigationCategory.StoryEvents).IsStoryNote);
        Assert.DoesNotContain(retired.Targets, t => t.Position == story.Position);
    }

    [Fact]
    public void UnseenStoryUsesCollisionAndBecomesRoutableWhenTheDoorOpens()
    {
        var source = Source();
        var map = Map(); map.ExitCells[6 * 8 + 6] = 0;
        for (var y = 0; y < 8; y++) map.CollisionLayers[y * 8 + 4] = 0;
        var closed = source.Build(Field(2), map, new(0, 0, 256, 256), [], new(3, false));
        var target = Assert.Single(closed.Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.False(target.IsStoryNote);
        Assert.Null(NavigationPathfinder.Find(closed.Graph, closed.Player, target.ApproachPoints));
        map.CollisionLayers[3 * 8 + 4] = map.CollisionLayers[4 * 8 + 4] = 1;
        var opened = source.Build(Field(2), map, new(0, 0, 256, 256), [], new(3, false));
        Assert.NotNull(NavigationPathfinder.Find(opened.Graph, opened.Player,
            Assert.Single(opened.Targets, t => t.Category == NavigationCategory.StoryEvents).ApproachPoints));
    }

    [Fact]
    public void UnseenTowerStairsIncludeTheReachableFloorDespiteALargeNearerExitAcrossAWall()
    {
        var map = new FieldMapSnapshot(16, 16, new byte[256], new byte[256],
            Enumerable.Repeat((byte)1, 256).ToArray(), 1, false, 16, 16,
            Enumerable.Repeat((byte)128, 256).ToArray());
        for (var y = 0; y < 16; y++) map.CollisionLayers[y * 16 + 8] = 0;
        for (var y = 2; y < 10; y++) map.ExitCells[y * 16 + 10] = 0;
        for (var y = 12; y < 16; y++) map.ExitCells[y * 16 + 4] = 1;
        var frame = Source().Build(Field(468), map, new(0, 0, 256, 256), [], new(15, false));
        var stairs = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.InRange(stairs.ApproachPoints.Count, 1, 64);
        var route = NavigationPathfinder.Find(frame.Graph, frame.Player, stairs.ApproachPoints);
        Assert.NotNull(route);
        // Scene 468's own exits warp back into it, so the frame's graph is wrapped to
        // allow a flight as an intermediate leg. Ask the map itself which exit this is.
        Assert.Equal(1, new FieldNavigationGraph(map).ExitAt(route[^1].X, route[^1].Y));
    }

    [Fact]
    public void OptionalNpcAndItemStayInTheirCategoriesAndUseActiveNativeRecords()
    {
        var source = Source();
        var merchant = Actor(14, 1408, 1408) with { ClassTag = 4, VisualIndex = 0, DrawMode = 1 };
        var pickup = new FieldTreasure(8, 1152, 1408) { IsChest = false };
        var frame = source.Build(Field(5, merchant), Map(), new(0, 0, 256, 256), [pickup], new(3, false));
        var person = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.People);
        var item = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Objects);
        Assert.Equal("Melchior the swordsmith", person.Label);
        Assert.Equal("Item pickup", item.Label);
        Assert.All(new[] { person, item }, t => { Assert.True(t.GuideAvailable); Assert.False(t.Discovered); });
        var controller = new NavigationController();
        Assert.True(controller.Handle(NavigationCommand.Guide, frame, 0).Guiding);
        var after = source.Build(Field(5, merchant with { DrawMode = 0 }), Map(), new(0, 0, 256, 256), [], new(3, false));
        Assert.DoesNotContain(after.Targets, t => t.Id == person.Id || t.Id == item.Id);
    }

    [Fact]
    public void OptionalObjectRequiresAnActiveInteractionAndRetiresWhenDisabled()
    {
        var source = Source();
        var lunch = Actor(28, 1408, 1408) with { ClassTag = 4, VisualIndex = 101, DrawMode = 1,
            ActivationEnabled = 0, ActivationBinding = 0 };
        var frame = source.Build(Field(439, lunch), Map(), new(0, 0, 256, 256), [], new(3, false));
        var item = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Objects);
        Assert.Equal("Wrapped lunch", item.Label);
        Assert.True(item.GuideAvailable);
        var after = source.Build(Field(439, lunch with { ScriptCallsEnabled = false }), Map(), new(0, 0, 256, 256), [], new(3, false));
        Assert.DoesNotContain(after.Targets, t => t.Id == item.Id);
    }

    [Fact]
    public void MissingNativeDestinationDoesNotTellThePlayerToDiscoverIt()
    {
        var missing = StoryTarget.Bind("exit:0", "Go downstairs", []);
        Assert.True(missing.IsStoryNote);
        Assert.DoesNotContain("discover", missing.Instruction!, StringComparison.OrdinalIgnoreCase);
    }

    private static FieldNavigationSource Source() => new(new NoMemory(), _ => { });
    private static FieldActorSnapshot Actor(int index, int x, int y) =>
        new(index, x >> 8, x, x & 255, y >> 8, y, y & 255, 0, 0, 0, 0, 7, 0, 1, 1, false, true, true, true);
    private static FieldNavigationSnapshot Field(int scene, params FieldActorSnapshot[] objects)
    {
        var lead = Actor(0, 384, 384) with { ClassTag = 2, DrawMode = 1, IsPartyMember = true };
        return new(0x1000, 0x4000, 0x2000, 0x20000, objects.Length + 1, scene, true, 1, 0, 0, 0, lead, [lead, .. objects]);
    }
    private static FieldMapSnapshot Map() => new(8, 8, new byte[64], new byte[64],
        Enumerable.Repeat((byte)1, 64).ToArray(), 1, false, 8, 8, Enumerable.Repeat((byte)128, 64).ToArray());
    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
