using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class TelepodDemoTests
{
    [Theory]
    [InlineData(NavigationCommand.ToggleWalk, 2069, 2058)]
    [InlineData(NavigationCommand.Guide, 637, 1783)]
    [InlineData(NavigationCommand.ToggleWalk, 1106, 2989)]
    public void DemonstrationRoutesFromReportedPositionsWhenTheMarkerTileIsBlocked(
        NavigationCommand command, int x, int y)
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => {});
        var frame = source.Build(Field(x, y), Map(), new(0, 0, 4224, 3200), [], State(1));
        var target = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.Equal("Try the left Telepod", target.Label);
        Assert.False(target.IsStoryNote);
        Assert.Equal(new NavigationPoint(1152, 1279, 1), target.Position);
        Assert.False(((FieldNavigationGraph)frame.Graph).TryPosition(1152, 1152, 1, out _));
        Assert.Equal(new NavigationPoint(1152, 1408, 1), Assert.Single(target.ApproachPoints));
        var path = NavigationPathfinder.Find(frame.Graph, frame.Player, target.ApproachPoints);
        Assert.NotNull(path);

        var controller = new NavigationController();
        controller.Handle(NavigationCommand.PreviousCategory, frame, 0);
        var result = controller.Handle(command, frame, 100);
        Assert.True(controller.IsActive);
        Assert.Equal(command == NavigationCommand.ToggleWalk, result.AutoWalking);
        Assert.DoesNotContain(result.Speech, s => s.Contains("No route") || s.Contains("cannot locate"));
        Assert.DoesNotContain(frame.Targets, t => t.Category == NavigationCategory.Objects);
    }

    [Fact]
    public void CompletionSwitchesToMarleAndStopsTheOldRoute()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => {});
        var field = Field();
        var marle = Actor(14, 1920, 3071, 3) with { VisualIndex = 1, LoadedFlag = 1 };
        field = field with { Actors = [.. field.Actors, marle], ActorCount = 3 };
        var controller = new NavigationController();
        var before = source.Build(field, Map(), new(0, 0, 4224, 3200), [], State(1));
        controller.Handle(NavigationCommand.PreviousCategory, before, 0);
        Assert.True(controller.Handle(NavigationCommand.ToggleWalk, before, 100).AutoWalking);

        var after = source.Build(field, Map(), new(0, 0, 4224, 3200), [], State(3));
        var story = Assert.Single(after.Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.Equal("Talk with Marle", story.Label);
        Assert.False(story.IsStoryNote);
        Assert.DoesNotContain(after.Targets, t => t.Id == "story:try-telepod");
        Assert.False(controller.Update(after, 200).AutoWalking);
        Assert.False(controller.IsActive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UnarmedOrUnknownDemonstrationDoesNotBindThePad(int flag)
    {
        var state = flag < 0 ? new FieldStoryState(10, true) : State(flag);
        var frame = new FieldNavigationSource(new NoMemory(), _ => {})
            .Build(Field(), Map(), new(0, 0, 4224, 3200), [], state);
        Assert.All(frame.Targets.Where(t => t.Category == NavigationCategory.StoryEvents),
            t => Assert.True(t.IsStoryNote));
    }

    [Theory]
    [InlineData("calls-disabled")]
    [InlineData("coordinates")]
    [InlineData("class")]
    [InlineData("party")]
    [InlineData("outside-map")]
    public void RetiredOrInvalidMarkerCannotKeepAStaleDestination(string invalid)
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => {});
        var field = Field();
        var before = source.Build(field, Map(), new(0, 0, 4224, 3200), [], State(1));
        Assert.False(Assert.Single(before.Targets).IsStoryNote);
        var pad = field.Actors.Single(a => a.Index == 12);
        pad = invalid switch
        {
            "calls-disabled" => pad with { ScriptCallsEnabled = false },
            "coordinates" => pad with { CoordinatesCoherent = false },
            "class" => pad with { ClassTag = 4 },
            "party" => pad with { IsPartyMember = true },
            "outside-map" => Actor(12, 65535, 65535, 7),
            _ => throw new ArgumentException(invalid),
        };
        var after = source.Build(field with { Actors = [field.LeadPlayer!, pad] },
            Map(), new(0, 0, 4224, 3200), [], State(1));
        Assert.True(Assert.Single(after.Targets, t => t.Category == NavigationCategory.StoryEvents).IsStoryNote);
    }

    [Fact]
    public void ContactGoalStillRespectsCollisionAndUnrelatedExits()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => {});
        var map = Map();
        map.ExitCells[5 * 16 + 4] = 0;
        var frame = source.Build(Field(), map, new(0, 0, 4224, 3200), [], State(1));
        Assert.Empty(Assert.Single(frame.Targets, t => t.Id == "story:try-telepod").ApproachPoints);
        map.ExitCells[5 * 16 + 4] = 128;
        map.CollisionLayers[5 * 16 + 4] = 0;
        frame = source.Build(Field(), map, new(0, 0, 4224, 3200), [], State(1));
        Assert.Empty(Assert.Single(frame.Targets, t => t.Id == "story:try-telepod").ApproachPoints);
    }

    private static FieldStoryState State(int flags) => new(10, true)
    { Globals = new Dictionary<int, int> { [0x54] = 8, [0x55] = 0x84, [0x56] = flags } };

    private static FieldNavigationSnapshot Field(int x = 2069, int y = 2058)
    {
        var lead = Actor(3, x, y, 0) with { IsPartyMember = true, LoadedFlag = 1 };
        var pad = Actor(12, 1152, 1279, 7);
        return new(0x1000, 0x4000, 0x2000, 0x20000, 2, 8, true, 1, 0, 3, 3, lead, [lead, pad]);
    }

    private static FieldActorSnapshot Actor(int index, int x, int y, int cls) =>
        new(index, x >> 8, x, x & 255, y >> 8, y, y & 255, 0, 1, 0, 0, cls,
            0, 0, 0, false, true, true, true);

    private static FieldMapSnapshot Map()
    {
        // Minimal terrain reproducing the installed map's blocked pad-marker
        // tile and the walkable area below it. Original map data is not bundled.
        var layers = Enumerable.Repeat((byte)1, 256).ToArray();
        Array.Fill(layers, (byte)0, 0, 5 * 16);
        return new(16, 16, new byte[256], new byte[256], layers, 1, false,
            16, 16, Enumerable.Repeat((byte)128, 256).ToArray());
    }

    private sealed class NoMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination) => false;
    }
}
