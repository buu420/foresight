using System.Reflection;
using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class ActorCollisionNavigationTests
{
    [Theory]
    [InlineData(-1, 0)] [InlineData(1, 0)] [InlineData(0, -1)] [InlineData(0, 1)]
    public void SolidTouchOnlySwitchCanBeBumpedFromEverySide(int dx, int dy)
    {
        var frame = TouchSwitchFrame(new(1664 + dx * 768, 1664 + dy * 768, 1));
        var target = Assert.Single(frame.Targets, t => t.Id == "actor:9:4:100");
        Assert.NotNull(NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route);
    }

    [Fact]
    public void TouchDestinationDoesNotMakeAnOverlappingSolidCharacterPassable()
    {
        var frame = TouchSwitchFrame(new(896, 1664, 1), overlap: true);
        var target = Assert.Single(frame.Targets, t => t.Id == "actor:9:4:100");
        Assert.Null(NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route);
    }

    [Fact]
    public void RoutingElsewhereKeepsTheTouchSwitchAsAnObstacleAfterItWasSelected()
    {
        var frame = TouchSwitchFrame(new(896, 1664, 1));
        var target = Assert.Single(frame.Targets, t => t.Id == "actor:9:4:100");
        Assert.NotNull(NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route);
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, [new(2432, 1664, 1)]).Route;
        Assert.NotNull(route);
        Assert.All(route.Zip(route.Skip(1)), e => Assert.Contains(e.Second, frame.Graph.Neighbours(e.First)));
    }

    internal static NavigationFrame TouchSwitchFrame(NavigationPoint position, bool overlap = false)
    {
        var pc = FullGameNavigationTests.Actor(1, position.X, position.Y, true) with { ClassTag = 0 };
        // Ocean Palace 412/9: only the touch handler opens the bridge, after
        // both wall switches set 162:40. Use an open floor to isolate contact.
        var trigger = FullGameNavigationTests.Actor(9, 1664, 1664) with { VisualIndex = 100 };
        var actors = overlap ? new[] { pc, trigger, FullGameNavigationTests.Actor(35, 1664, 1664) }
            : new[] { pc, trigger };
        var map = new FieldMapSnapshot(13, 13, new byte[169], new byte[169],
            Enumerable.Repeat((byte)1, 169).ToArray(), 1, false, 13, 13,
            Enumerable.Repeat((byte)128, 169).ToArray());
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(
            FullGameNavigationTests.Field(412) with { LeadPlayer = pc, Actors = actors },
            map, new(0, 0, 3328, 3328), [], new FieldStoryState(198, false)
            { Globals = new Dictionary<int, int> { [0x162] = 0x77, [0x15A] = 0 } });
    }

    [Theory]
    [InlineData(-1,0,0)] [InlineData(1,0,0)] [InlineData(0,-1,0)] [InlineData(0,1,0)]
    [InlineData(-1,0,1)] [InlineData(1,0,1)] [InlineData(0,-1,1)] [InlineData(0,1,1)]
    [InlineData(-1,0,1,224)] [InlineData(1,0,1,224)] [InlineData(0,-1,1,224)] [InlineData(0,1,1,224)]
    public void ConfirmTargetRemainsReachableFromEverySideOfANarrowCorridor(int dx, int dy, int activation, int radius = 160)
    {
        var map = new FieldMapSnapshot(13,13,Enumerable.Repeat((byte)4,169).ToArray(),new byte[169],
            Enumerable.Repeat((byte)1,169).ToArray(),1,false,13,13,Enumerable.Repeat((byte)128,169).ToArray());
        for (var n=0;n<=4;n++) map.CollisionShapes[(6+dy*n)*13+6+dx*n]=0;
        var pc=FullGameNavigationTests.Actor(1,1664+dx*768,1664+dy*768,true) with { ClassTag=0 };
        var npc=FullGameNavigationTests.Actor(10,1664,1664) with { ActivationEnabled=activation };
        var frame=new FieldNavigationSource(new NoMemory(),_=>{}).Build(
            FullGameNavigationTests.Field(118) with { LeadPlayer=pc,Actors=[pc,npc],ActorCollisionRadius=radius },map,new(0,0,3328,3328),[],new FieldStoryState(18,true));
        var target=Assert.Single(frame.Targets,t=>t.Id=="actor:10:4:14");
        var route=NavigationPathfinder.Search(frame.Graph,frame.Player,target.ApproachPoints).Route;
        Assert.NotNull(route);
        Assert.True(FieldInteractionRange.ReachesWithin(route[^1].X,route[^1].Y,1664,1664,32));
    }

    [Fact]
    public void LoggedCathedralCornerCanGoLeftButCannotWalkIntoTheOccupiedSpaceAbove()
    {
        var frame = CathedralFrame(new(9471, 2074, 1));
        // Native 175E90 accepts the final ten units up to Y2064, but rejects
        // a full pixel farther. Precision routing may retain that safe boundary.
        Assert.Contains(new NavigationPoint(9471, 2064, 1), frame.Graph.Neighbours(frame.Player));
        Assert.DoesNotContain(frame.Graph.Neighbours(frame.Player), p => p.Y < 2064);
        Assert.DoesNotContain(frame.Graph.Neighbours(new(9471, 2064, 1)), p => p.Y < 2064);
        Assert.Contains(frame.Graph.Neighbours(frame.Player), p => p.X < frame.Player.X);
    }

    [Fact]
    public void PassageRouteAccountsForTheCharacterEvenWhenItsScriptCallsAreDisabled()
    {
        var frame = CathedralFrame(new(8959, 5626, 1));
        var target = Assert.Single(frame.Targets, t => t.Id == "story:cathedral-passage");
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route;
        Assert.NotNull(route);
        // Native 178980 rejects north's leading probe at y-112 against actor20.
        Assert.All(route.Zip(route.Skip(1)), e =>
        {
            if (e.Second.Y >= e.First.Y) return;
            Assert.False(Math.Abs(9344 - e.Second.X - 1) < 160 &&
                Math.Abs(1791 - (e.Second.Y - 112) - 1) < 160);
        });
    }

    internal static NavigationFrame CathedralFrame(NavigationPoint position)
    {
        var pc = FullGameNavigationTests.Actor(1, position.X, position.Y, true) with { ClassTag = 0 };
        // Position, loaded bit, camera cache, and call gate from the user's
        // 08:43:21 log. Actor20 +14C=0 was read in the earlier 02:09:36 capture.
        var occupant = FullGameNavigationTests.Actor(20, 9344, 1791) with
        { ClassTag = 5, VisualIndex = 80, ActivationEnabled = 0, ActivationBinding = 128, ScriptCallsEnabled = false };
        var field = FullGameNavigationTests.Field(131) with { LeadPlayer = pc, Actors = [pc, occupant], ActorCount = 56 };
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(field,
            OpenedMap() with { PlayerLayer = position.Layer }, new(7424, 1504, 11648, 4704), [],
            new FieldStoryState(21, true) { Globals = new Dictionary<int, int> { [0xFF] = 0x16, [0xFE] = 8 } });
    }

    internal static FieldMapSnapshot OpenedMap()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.cathedral-rear-live-0329.json")!;
        using var doc = JsonDocument.Parse(stream);
        var r = doc.RootElement;
        var planes = new[] { "collisionShapes", "terrainFlags", "collisionLayers" }
            .Select(k => r.GetProperty(k).GetBytesFromBase64()).ToArray();
        // Atel0179:0F73 opens the door: E4 3E 2C 3F 2F 15 02 3B.
        foreach (var plane in planes)
            for (var y = 0; y < 4; y++) for (var x = 0; x < 2; x++)
                plane[(y + 2) * 64 + x + 21] = plane[(y + 44) * 64 + x + 62];
        return new(64, 48, planes[0], planes[1], planes[2], 1, false, 64, 48,
            r.GetProperty("exitCells").GetBytesFromBase64());
    }

    private sealed class NoMemory : IReadableMemory
    { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
