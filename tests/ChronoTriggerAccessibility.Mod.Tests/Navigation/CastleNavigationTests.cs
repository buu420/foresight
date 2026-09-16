using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class CastleNavigationTests
{
    [Theory]
    [InlineData(8064, 13823)]
    [InlineData(3456, 9599)]
    public void ClosedCastleStairRoutesFirstToItsAutomaticOpeningContact(int x, int y)
    {
        var map = ClosedStair();
        var player = Actor(1, x, y) with { IsPartyMember = true, ClassTag = 0 };
        var marker = Actor(9, 8064, 10495) with { ClassTag = 7 };
        var field = Field(120, player, marker);
        var state = new FieldStoryState(15, false) { Locals = new Dictionary<int, int> { [6] = 0 } };
        var frame = new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, map, new(0, 0, 512, 512), [], state);
        var target = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.Null(NavigationPathfinder.Find(new FieldNavigationGraph(map), frame.Player, target.ApproachPoints));
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route;
        Assert.NotNull(route);
        Assert.True(route[^1].Y > 40 * 256);
        Assert.DoesNotContain(route, p => target.ApproachPoints.Contains(p));
        foreach (var (a, b) in route.Zip(route.Skip(1)))
            Assert.Contains(b, new FieldNavigationGraph(map).Neighbours(a));
        Assert.Equal((byte)4, map.CollisionShapes[40 * 64 + 31]);
    }

    [Fact]
    public void ForestPickupsRemainAvailableOutsideCameraAndAtIdle()
    {
        var player = Actor(1, 384, 384) with { IsPartyMember = true, ClassTag = 0 };
        var bush = Actor(47, 1408, 1407) with { ClassTag = 7 };
        var sparkle = Actor(63, 1664, 1407) with { VisualIndex = 112 };
        var field = Field(119, player, bush, sparkle);
        var state = new FieldStoryState(12, false) { Locals = new Dictionary<int, int> { [15] = 0 } };
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var frame = source.Build(field, FullGameNavigationTests.Map(), new(0, 0, 512, 512), [], state);
        foreach (var id in new[] { "landmark:47", "actor:63:4:112" })
        {
            var target = Assert.Single(frame.Targets, t => t.Id == id);
            Assert.Equal(NavigationCategory.Objects, target.Category);
            Assert.True(target.GuideAvailable);
            Assert.False(target.Visible);
            Assert.NotNull(NavigationPathfinder.Find(frame.Graph, frame.Player, target.ApproachPoints));
        }
        var after = source.Build(field with { Actors = [player, bush, sparkle with { DrawMode = 0 }] },
            FullGameNavigationTests.Map(), new(0, 0, 512, 512), [],
            state with { Locals = new Dictionary<int, int> { [15] = 1 } });
        Assert.DoesNotContain(after.Targets, t => t.Id is "landmark:47" or "actor:63:4:112");
    }

    [Theory]
    [InlineData("unknown-condition")]
    [InlineData("finished-condition")]
    [InlineData("calls-disabled")]
    [InlineData("wrong-identity")]
    [InlineData("copy-still-blocked")]
    public void UnavailableOrIneffectiveContactCannotInventAStairRoute(string reason)
    {
        var map = ClosedStair();
        var marker = Actor(9, 8064, 10495) with { ClassTag = reason == "wrong-identity" ? 4 : 7,
            ScriptCallsEnabled = reason != "calls-disabled" };
        var state = new FieldStoryState(15, false) { Locals = reason == "unknown-condition" ? new Dictionary<int, int>() :
            new Dictionary<int, int> { [6] = reason == "finished-condition" ? 1 : 0 } };
        if (reason == "copy-still-blocked")
            for (var y = 0; y < 3; y++) for (var x = 3; x <= 5; x++) map.CollisionShapes[y * 64 + x] = 4;
        var player = Actor(1, 8064, 13823) with { IsPartyMember = true, ClassTag = 0 };
        var frame = new FieldNavigationSource(new NoMemory(), _ => { }).Build(Field(120, player, marker),
            map, new(0, 0, 512, 512), [], state);
        var target = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.Null(NavigationPathfinder.Find(frame.Graph, frame.Player, target.ApproachPoints));
    }

    internal static FieldMapSnapshot ClosedStair()
    {
        // Synthetic room with the native castle stair's closed tile and copy
        // source, not a redistributed game map. Its wall has one scripted door.
        var map = new FieldMapSnapshot(64, 64, Enumerable.Repeat((byte)4, 4096).ToArray(), new byte[4096],
            Enumerable.Repeat((byte)1, 4096).ToArray(), 1, false, 64, 64,
            Enumerable.Repeat((byte)128, 4096).ToArray());
        for (var y = 5; y <= 55; y++)
        for (var x = 30; x <= 32; x++) map.CollisionShapes[y * 64 + x] = 0;
        for (var y = 35; y <= 55; y++)
        for (var x = 12; x <= 14; x++) map.CollisionShapes[y * 64 + x] = 0;
        for (var y = 52; y <= 54; y++)
        for (var x = 12; x <= 32; x++) map.CollisionShapes[y * 64 + x] = 0;
        for (var y = 8; y <= 10; y++)
        for (var x = 30; x <= 39; x++) map.CollisionShapes[y * 64 + x] = 0;
        for (var x = 30; x <= 32; x++) map.CollisionShapes[40 * 64 + x] = 4;
        for (var y = 0; y <= 2; y++)
        for (var x = 3; x <= 5; x++) map.CollisionShapes[y * 64 + x] = 0;
        map.ExitCells[8 * 64 + 38] = 3;
        return map;
    }
    internal static FieldActorSnapshot Actor(int index, int x, int y) =>
        FullGameNavigationTests.Actor(index, x, y) with { ActivationBinding = 0, ActivationEnabled = 0 };
    internal static FieldNavigationSnapshot Field(int scene, FieldActorSnapshot player, params FieldActorSnapshot[] actors) =>
        FullGameNavigationTests.Field(scene) with { LeadPlayer = player, Actors = new[] { player }.Concat(actors).ToArray() };
    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
