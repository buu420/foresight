using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class PendantStoryTests
{
    [Fact]
    public void TalkingAboutThePendantAdvancesTheSelectedStoryAndAllowsAnUnseenPickupRoute()
    {
        var log = new List<string>();
        var source = new FieldNavigationSource(new NoMemory(), log.Add);
        var field = Field();
        var map = Map();
        // The recorded pendant is drawn but outside this viewport, with +152 == 0.
        var view = new FieldViewport(4336, 2528, 8560, 5728);
        var before = source.Build(field, map, view, [], State(0x50, 0));
        var controller = new NavigationController();
        // Back twice from People: Enemies, then Story Events.
        controller.Handle(NavigationCommand.PreviousCategory, before, 0);
        var first = controller.Handle(NavigationCommand.PreviousCategory, before, 0);
        Assert.Contains(first.Speech, text => text.Contains("Talk with the young woman"));

        // Atel_0074 actor 3 sets global 55 bit 2 after her missing-pendant dialogue.
        var after = source.Build(field, map, view, [], State(0x50, 4));
        var objective = Assert.Single(after.Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.Equal("Pick up the fallen pendant", objective.Label);
        Assert.False(objective.IsStoryNote);
        Assert.False(objective.Discovered);
        Assert.False(objective.Visible);
        Assert.True(objective.GuideAvailable);
        Assert.Equal(new NavigationPoint(7808, 2303, 1), objective.Position);
        var pickup = Assert.Single(after.Targets, t => t.Category == NavigationCategory.Objects);
        Assert.Equal("Fallen pendant", pickup.Label);
        Assert.Equal(objective.Position, pickup.Position);
        Assert.True(pickup.GuideAvailable);
        Assert.NotEmpty(objective.ApproachPoints);

        var repeat = controller.Handle(NavigationCommand.Repeat, after, 100);
        Assert.Contains(repeat.Speech, text => text.Contains("Pick up the fallen pendant"));
        Assert.DoesNotContain(repeat.Speech, text => text.Contains("Talk with the young woman"));
        var walk = controller.Handle(NavigationCommand.ToggleWalk, after, 200);
        Assert.True(controller.IsActive);
        Assert.Contains(walk.Speech, text => text.Contains("Walking to Pick up the fallen pendant"));
        Assert.DoesNotContain(walk.Speech, text => text.Contains("No route") || text.Contains("not discovered"));
    }

    [Fact]
    public void PickingUpThePendantRetiresItsObjectAndRoutesBackToTheGirlThenOnward()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var field = Field();
        var map = Map();
        var view = new FieldViewport(0, 0, 16384, 16384);
        var dropped = source.Build(field, map, view, [], State(0x50, 4));
        Assert.Contains(dropped.Targets, t => t.Label == "Fallen pendant");

        // The pickup script sets 54/20 and clears 54/10 before removing its actor.
        var pickedUp = source.Build(field, map, view, [], State(0x60, 4));
        Assert.DoesNotContain(pickedUp.Targets, t => t.Label.Contains("fallen pendant", StringComparison.OrdinalIgnoreCase));
        var returning = Assert.Single(pickedUp.Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.Equal("Return the pendant to the young woman", returning.Label);
        Assert.False(returning.IsStoryNote);
        Assert.Equal(new NavigationPoint(6276, 3922, 1), returning.Position);

        map.ExitCells[22 * 64 + 22] = 0;
        var joined = source.Build(field, map, view, [], State(0, 8) with { Point = 8 });
        var next = Assert.Single(joined.Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.Equal("Return to the central plaza", next.Label);
        Assert.False(next.IsStoryNote);
        Assert.DoesNotContain(joined.Targets, t => t.Id is "story:pendant" or "story:girl-pendant");
    }

    [Theory]
    [InlineData(438, 15, 4, 99, 1, 0x50, true)]
    [InlineData(439, 14, 4, 99, 1, 0x50, true)]
    [InlineData(439, 15, 4, 98, 1, 0x50, true)]
    [InlineData(439, 15, 0x84, 99, 1, 0x50, true)]
    [InlineData(439, 15, 4, 99, 0, 0x50, true)]
    [InlineData(439, 15, 4, 99, 1, 0x40, true)]
    [InlineData(439, 15, 4, 99, 1, 0x50, false)]
    public void ScriptedPickupRequiresTheExactActivePendantState(
        int scene, int actorIndex, int actorClass, int visual, int draw, int flags, bool coherent)
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var field = Field();
        var pendant = field.Actors[2] with
        { Index = actorIndex, ClassTag = actorClass, VisualIndex = visual, DrawMode = draw };
        field = field with { SceneId = scene, SceneIdCoherent = coherent, Actors = [field.Actors[0], field.Actors[1], pendant] };

        var frame = source.Build(field, Map(), new(0, 0, 16384, 16384), [], State(flags, 4));

        Assert.DoesNotContain(frame.Targets, t => t.Category == NavigationCategory.Objects);
        Assert.DoesNotContain(frame.Targets, t => t.Id == "story:pendant" && !t.IsStoryNote);
    }

    [Fact]
    public void MissingPendantGlobalsDoNotInventAPickup()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var frame = source.Build(Field(), Map(), new(0, 0, 16384, 16384), [], new(6, false));

        Assert.DoesNotContain(frame.Targets, t => t.Category == NavigationCategory.Objects);
        Assert.DoesNotContain(frame.Targets, t => t.Id == "story:pendant");
    }

    private static FieldStoryState State(int pendantFlags, int fairFlags) => new(6, false)
    { Globals = new Dictionary<int, int> { [0x54] = pendantFlags, [0x55] = fairFlags } };

    private static FieldNavigationSnapshot Field()
    {
        var lead = Actor(1, 6872, 3567, 0, 0) with { IsPartyMember = true, LoadedFlag = 1 };
        var girl = Actor(3, 6276, 3922, 3, 1) with { LoadedFlag = 1 };
        var pendant = Actor(15, 7808, 2303, 4, 99);
        return new(0x1000, 0x4000, 0x2000, 0x20000, 3, 439, true, 1, 0, 0, 0, lead, [lead, girl, pendant]);
    }

    private static FieldActorSnapshot Actor(int index, int x, int y, int actorClass, int visual) =>
        new(index, x >> 8, x, x & 255, y >> 8, y, y & 255, 0, 1, 0, visual, actorClass,
            0x2020, 0, 128, false, true, true, true);

    private static FieldMapSnapshot Map() => new(64, 64, new byte[4096], new byte[4096],
        Enumerable.Repeat((byte)1, 4096).ToArray(), 1, false, 64, 64, Enumerable.Repeat((byte)128, 4096).ToArray());

    private sealed class NoMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination) => false;
    }
}
