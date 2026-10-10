using System.Reflection;
using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class CathedralRearNavigationTests
{
    [Theory]
    [InlineData(8073, 10758)]
    [InlineData(8079, 10752)]
    [InlineData(8050, 10744)]
    [InlineData(8057, 10745)]
    public void FrontHallRouteFromReportedStallsUsesTheStairsInsteadOfOpposingTheFloor(int x, int y)
    {
        var frame = Frame(x, y);
        var exit = Assert.Single(frame.Targets, t => t.Id == "exit:0");
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, exit.ApproachPoints).Route;
        Assert.NotNull(route);
        Assert.Contains(route, p => p.X / 256 >= 36 && p.Layer != 1);
        Assert.True(frame.Graph.IsSameTerminal(route[^1], exit.ApproachPoints[0]));
        Assert.All(route.Zip(route.Skip(1)), edge =>
        {
            if (edge.Second.Y >= edge.First.Y) return;
            Assert.False(InMovingStrip(edge.First) || InMovingStrip(edge.Second));
        });
    }

    [Theory]
    [InlineData(12, 0, 64)]
    [InlineData(13, 0, -64)]
    [InlineData(14, 64, 0)]
    [InlineData(15, -64, 0)]
    public void StrongNativeFloorRejectsOpposingMovementButAllowsMovementWithItsForce(byte flag, int dx, int dy)
    {
        var map = FullGameNavigationTests.Map();
        Array.Fill(map.TerrainFlags, flag);
        var graph = new FieldNavigationGraph(map);
        var start = new NavigationPoint(768, 768, 1);
        Assert.DoesNotContain(start with { X = start.X + dx, Y = start.Y + dy }, graph.Neighbours(start));
        Assert.Contains(start with { X = start.X - dx, Y = start.Y - dy }, graph.Neighbours(start));
    }

    [Fact]
    public void OrganRemainsRoutableFromTheRightSwitchAndOffersOnlyItsUsableSide()
    {
        var frame = Frame(9225, 4982);
        var organ = Assert.Single(frame.Targets, t => t.Id == "story:inner-organ");
        Assert.False(organ.IsStoryNote);
        Assert.True(organ.GuideAvailable);
        Assert.False(organ.Discovered);
        Assert.NotEmpty(organ.ApproachPoints);
        Assert.All(organ.ApproachPoints, p => Assert.True(p.X + NavigationUnits.LocalStep / 8 < 18 * 256));
        Assert.NotNull(NavigationPathfinder.Search(frame.Graph, frame.Player, organ.ApproachPoints).Route);
    }

    [Fact]
    public void UnknownMechanismStateDoesNotInventAnOrganAction()
    {
        var frame = Frame(9225, 4982, unknownFlags: true);
        Assert.DoesNotContain(frame.Targets, t => t.Id == "actor:36:4:100");
        Assert.DoesNotContain(frame.Targets, t => t.Id == "story:inner-organ" && !t.IsStoryNote);
    }

    [Fact]
    public void ClosedNorthernPassageRemainsClosedAfterTheSwitch()
    {
        var frame = Frame(9225, 4982);
        var exit = Assert.Single(frame.Targets, t => t.Id == "exit:1");
        Assert.Null(NavigationPathfinder.Search(new FieldNavigationGraph(Map()), frame.Player, exit.ApproachPoints).Route);
    }

    [Theory]
    [InlineData(8073, 10758, "exit:0", NavigationCategory.Exits)]
    [InlineData(9225, 4982, "story:inner-organ", NavigationCategory.StoryEvents)]
    public void AutomaticSteeringCompletesTheCorrectedGraphRoute(int x, int y, string id, NavigationCategory category)
    {
        // Integration replay with independent native walking frames on the
        // captured collision map. This is not a live game or controller-hook test.
        var frame = Frame(x, y);
        var target = Assert.Single(frame.Targets, t => t.Id == id);
        frame = frame with { Targets = [target] };
        var map = Map();
        var controller = new NavigationController();
        for (var i = 0; i < (int)category; i++) controller.Handle(NavigationCommand.NextCategory, frame, 0);
        var result = controller.Handle(NavigationCommand.ToggleWalk, frame, 0);
        var speech = new List<string>(result.Speech);
        for (var tick = 1; tick <= 4000 && result.AutoWalking; tick++)
        {
            if (result.Direction != NavigationDirection.None)
            {
                var next = NativeWalkingReplay.Move(map, frame.Player, result.Direction);
                // A press into the organ turns the leader in place, as the native field does;
                // every step also turns them. A missing edge on the route itself still ends in
                // the "blocked" stop asserted below.
                frame = frame with { Player = next, PlayerFacing = result.Direction };
            }
            result = controller.Update(frame, tick * 32);
            speech.AddRange(result.Speech);
            // The replay keeps its first frame; refresh the target's native camera-scoped state
            // (not the graph or the player) when arrival is waiting on it, as the live capture does.
            if (result.Speech.Any(s => s.Contains("would not reach it yet")))
                frame = frame with { Targets = [Frame(frame.Player.X, frame.Player.Y).Targets.Single(t => t.Id == id)] };
        }
        Assert.False(result.AutoWalking);
        Assert.True(speech.Any(s => s.StartsWith("Arrived at ", StringComparison.Ordinal)),
            $"Stopped at {frame.Player}, facing {frame.PlayerFacing}: {string.Join(" | ", speech)}");
        Assert.DoesNotContain(speech, s => s.Contains("blocked", StringComparison.OrdinalIgnoreCase));
        if (category == NavigationCategory.Exits)
            Assert.True(frame.Graph.IsSameTerminal(frame.Player, target.ApproachPoints[0]));
        else Assert.True(frame.Player.X / 256 <= 17);
    }

    private static bool InMovingStrip(NavigationPoint p) => p.X / 256 is >= 31 and <= 33 && p.Y / 256 is >= 36 and <= 41;

    private static NavigationFrame Frame(int x, int y, bool unknownFlags = false)
    {
        var player = FullGameNavigationTests.Actor(1, x, y, true) with { ClassTag = 0 };
        // Loaded, drawn native actor from live-field-020936.json. Activation is
        // camera-scoped and zero, so this must use the script's interaction gate.
        // The per-frame pass 17A4D0 -> 17A6C0 sets its +0x20 byte to 0x80 once the drawn
        // organ is inside the camera (the viewport below), which the confirm scan requires.
        var onCamera = Math.Abs(4384 - x) < 400 && Math.Abs(4959 - y) < 400;
        var organ = FullGameNavigationTests.Actor(36, 4384, 4959) with
        { VisualIndex = 100, ActivationEnabled = 0, ActivationBinding = onCamera ? 0x80 : 0, Facing = 3 };
        var field = FullGameNavigationTests.Field(131) with
        { LeadPlayer = player, Actors = [player, organ], ActorCount = 56 };
        var state = new FieldStoryState(21, true)
        {
            Globals = unknownFlags ? new Dictionary<int, int>() : new Dictionary<int, int> { [0xFF] = 0x12, [0xFE] = 8 },
            Locals = new Dictionary<int, int> { [6] = x / 256, [7] = y / 256, [8] = 1 },
        };
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, Map(),
            new(x - 400, y - 400, x + 400, y + 400), [], state);
    }

    private static FieldMapSnapshot Map()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.cathedral-rear-live-0329.json")!;
        using var doc = JsonDocument.Parse(stream);
        var r = doc.RootElement;
        return new(r.GetProperty("width").GetInt32(), r.GetProperty("height").GetInt32(),
            r.GetProperty("collisionShapes").GetBytesFromBase64(), r.GetProperty("terrainFlags").GetBytesFromBase64(),
            r.GetProperty("collisionLayers").GetBytesFromBase64(), 1, false,
            r.GetProperty("exitWidth").GetInt32(), r.GetProperty("exitHeight").GetInt32(),
            r.GetProperty("exitCells").GetBytesFromBase64());
    }

    private sealed class NoMemory : IReadableMemory
    { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
