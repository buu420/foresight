using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class FieldNavigationSourceTests
{
    [Fact]
    public void ListsVisibleEligiblePeopleExitsAndChestsAndExcludesPartyAndHiddenActors()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var field = Field(Actor(1, 128, 128, party: true), Actor(2, 512, 256),
            Actor(3, 768, 256) with { DrawMode = 0 }, Actor(4, 900, 256) with { ActivationEnabled = 0 });
        var map = Map(); map.ExitCells[3] = 0;
        var frame = source.Build(field, map, new(0, 0, 1024, 1024), [new(5, 640, 640)]);
        Assert.True(frame.CanNavigate);
        Assert.Single(frame.Targets, t => t.Category == NavigationCategory.People);
        Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Exits);
        Assert.Equal("Treasure chest", Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Objects).Label);
    }

    [Fact]
    public void OffscreenActorsAreNotDiscoveredOrTrackedAtTheirUnseenPositions()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var field = Field(Actor(1, 128, 128, party: true), Actor(2, 768, 256));
        Assert.Empty(source.Build(field, Map(), new(0, 0, 512, 512), []).Targets);
        var seen = Assert.Single(source.Build(field, Map(), new(0, 0, 1024, 512), []).Targets);
        var moved = field with { Actors = [field.Actors[0], Actor(2, 900, 256)] };
        var known = Assert.Single(source.Build(moved, Map(), new(0, 0, 512, 512), []).Targets);
        Assert.False(known.Visible);
        Assert.True(known.Discovered);
        Assert.Equal(seen.Position, known.Position);
        source.Reset();
        Assert.Empty(source.Build(moved, Map(), new(0, 0, 512, 512), []).Targets);
    }

    [Fact]
    public void NativeFixedPointFrameAndExitGoalsRemainStableAsPlayerMoves()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var field = Field(Actor(1, 128, 128, party: true));
        var map = Map(); map.ExitCells[3] = 0;
        var first = source.Build(field, map, new(0, 0, 1024, 1024), []);
        var second = source.Build(field with { LeadPlayer = Actor(1, 192, 128, party: true) }, map, new(0, 0, 1024, 1024), []);
        Assert.Equal(256, first.UnitsPerTile);
        Assert.Equal(Assert.Single(first.Targets).ApproachPoints, Assert.Single(second.Targets).ApproachPoints);
        Assert.All(Assert.Single(first.Targets).ApproachPoints, goal =>
        {
            Assert.InRange(goal.X % 256, 64, 192);
            Assert.InRange(goal.Y % 256, 64, 192);
        });
    }

    [Fact]
    public void LargeExitKeepsItsDiscoveredApproachesAcrossPlayerAndCameraMovement()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var field = Field(Actor(1, 128, 128, party: true));
        var map = Map(); Array.Fill(map.ExitCells, (byte)0);
        var first = Assert.Single(source.Build(field, map, new(0, 0, 1024, 1024), []).Targets);
        Assert.Equal(64, first.ApproachPoints.Count);
        var moved = field with { LeadPlayer = Actor(1, 960, 960, party: true) };
        var second = Assert.Single(source.Build(moved, map, new(512, 512, 1024, 1024), []).Targets);
        Assert.Equal(first.ApproachPoints, second.ApproachPoints);
        map.ExitCells[0] = 128;
        var changed = Assert.Single(source.Build(moved, map, new(0, 0, 1024, 1024), []).Targets);
        Assert.DoesNotContain(changed.ApproachPoints, p => p.X < 256 && p.Y < 256);
    }

    [Fact]
    public void OpeningLabelsAndStoryEventsRemainBoundToVisibleNativeRecords()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var mother = Actor(15, 512, 256) with { VisualIndex = 0x25 };
        var field = Field(Actor(1, 128, 128, party: true), mother) with { SceneId = 1 };
        var frame = source.Build(field, Map(), new(0, 0, 1024, 1024), [], new(3, false));
        Assert.Equal("Mother", Assert.Single(frame.Targets, t => t.Category == NavigationCategory.People).Label);
        Assert.Equal("Talk with Mother", Assert.Single(frame.Targets, t => t.Category == NavigationCategory.StoryEvents).Label);
        var hidden = field with { Actors = [field.Actors[0], mother with { DrawMode = 0 }] };
        Assert.Empty(source.Build(hidden, Map(), new(0, 0, 1024, 1024), [], new(3, false)).Targets);
        var otherAppearance = field with { Actors = [field.Actors[0], mother with { VisualIndex = 0x4C }] };
        Assert.DoesNotContain(source.Build(otherAppearance, Map(), new(0, 0, 1024, 1024), [], new(3, false)).Targets,
            t => t.Label == "Mother" || t.Category == NavigationCategory.StoryEvents);
        var machine = Field(Actor(1, 128, 128, party: true), Actor(11, 512, 256) with { VisualIndex = 0x63 }) with { SceneId = 8 };
        var telepod = Assert.Single(source.Build(machine, Map(), new(0, 0, 1024, 1024), []).Targets);
        Assert.Equal("Telepod", telepod.Label); Assert.Equal(NavigationCategory.Objects, telepod.Category);
    }

    private static FieldActorSnapshot Actor(int index, int x, int y, bool party = false) =>
        new(index, x >> 8, x, x & 255, y >> 8, y, y & 255, 1, 1, 1, 14, 4, 0x2020, 1, 1, party, true, true, true);
    private static FieldNavigationSnapshot Field(params FieldActorSnapshot[] actors) =>
        new(0x1000, 0x4000, 0x2000, 0x20000, actors.Length, 0, true, 1, 0, 1, 1, actors[0], actors);
    private static FieldMapSnapshot Map() => new(4, 4, new byte[16], new byte[16],
        Enumerable.Repeat((byte)1, 16).ToArray(), 1, false, 4, 4, Enumerable.Repeat((byte)128, 16).ToArray());
    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
