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
        // Integration replay on the captured graph. Each commanded edge must
        // exist, including layer changes. This is not a live movement replay.
        var frame = Frame(x, y);
        var target = Assert.Single(frame.Targets, t => t.Id == id);
        frame = frame with { Targets = [target] };
        var controller = new NavigationController();
        for (var i = 0; i < (int)category; i++) controller.Handle(NavigationCommand.NextCategory, frame, 0);
        var result = controller.Handle(NavigationCommand.ToggleWalk, frame, 0);
        var speech = new List<string>(result.Speech);
        for (var tick = 1; tick <= 4000 && result.AutoWalking; tick++)
        {
            if (result.Direction != NavigationDirection.None)
            {
                var next = frame.Graph.Neighbours(frame.Player).Where(p => result.Direction switch
                {
                    NavigationDirection.North => p.X == frame.Player.X && p.Y < frame.Player.Y,
                    NavigationDirection.South => p.X == frame.Player.X && p.Y > frame.Player.Y,
                    NavigationDirection.West => p.Y == frame.Player.Y && p.X < frame.Player.X,
                    NavigationDirection.East => p.Y == frame.Player.Y && p.X > frame.Player.X,
                    _ => false,
                }).ToArray();
                Assert.Single(next);
                frame = frame with { Player = next[0] };
            }
            result = controller.Update(frame, tick * 32);
            speech.AddRange(result.Speech);
        }
        Assert.False(result.AutoWalking);
        Assert.Contains(speech, s => s.StartsWith("Arrived at ", StringComparison.Ordinal));
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
        var organ = FullGameNavigationTests.Actor(36, 4384, 4959) with
        { VisualIndex = 100, ActivationEnabled = 0, ActivationBinding = 0, Facing = 3 };
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
