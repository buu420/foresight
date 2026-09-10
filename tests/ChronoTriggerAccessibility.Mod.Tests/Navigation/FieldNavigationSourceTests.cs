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
    }

    private static FieldActorSnapshot Actor(int index, int x, int y, bool party = false) =>
        new(index, x >> 8, x, x & 255, y >> 8, y, y & 255, 1, 1, 1, 14, 4, 0x2020, 1, 1, party, true, true, true);
    private static FieldNavigationSnapshot Field(params FieldActorSnapshot[] actors) =>
        new(0x1000, 0x4000, 0x2000, 0x20000, actors.Length, 0, true, 1, 0, 1, 1, actors[0], actors);
    private static FieldMapSnapshot Map() => new(4, 4, new byte[16], new byte[16],
        Enumerable.Repeat((byte)1, 16).ToArray(), 1, false, 4, 4, Enumerable.Repeat((byte)128, 16).ToArray());
    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
