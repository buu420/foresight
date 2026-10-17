using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Live map/actor/story failure packets from lines12469,12490 and12518
/// of the October16 keep report. Engine addresses and viewport are reconstructed;
/// the captured terrain, actors and story words are preserved.</summary>
public sealed class KeepEntranceDoorTests
{
    [Theory]
    [InlineData(12469, NavigationCommand.Guide)]
    [InlineData(12469, NavigationCommand.ToggleWalk)]
    [InlineData(12490, NavigationCommand.Guide)]
    [InlineData(12490, NavigationCommand.ToggleWalk)]
    [InlineData(12518, NavigationCommand.Guide)]
    [InlineData(12518, NavigationCommand.ToggleWalk)]
    public void CapturedClosedDoorStagesBothExitAndStoryGuidance(int line, NavigationCommand command)
    {
        var capture = Fixture(line);
        var frame = Frame(capture);
        var door = Assert.Single(frame.Targets, t => t.Id == "landmark:14");
        Assert.Equal(NavigationCategory.Objects, door.Category);
        foreach (var id in new[] { "exit:1", "story:full:keep-west", "landmark:14" })
        {
            var target = Assert.Single(frame.Targets, t => t.Id == id);
            Assert.NotEmpty(target.ApproachPoints);
            var path = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints);
            Assert.NotNull(path.Route);
            Assert.Equal("landmark:14", path.IntermediateId);
            Assert.DoesNotContain(path.Route, p => new FieldNavigationGraph(capture.Map).ExitAt(p.X, p.Y) >= 0);
            var controller = new NavigationController();
            var selected = frame with { Targets = [target] };
            for (var i = 0; i < (int)target.Category; i++) controller.Handle(NavigationCommand.NextCategory, selected, 0);
            var result = controller.Handle(command, selected, 1);
            Assert.True(result.Guiding, string.Join(" | ", result.Speech));
            Assert.Equal(command == NavigationCommand.ToggleWalk, result.AutoWalking);
            Assert.False(controller.AllowsConfirmWhileFollowing(selected));
            var opening = controller.Update(selected with { CanNavigate = false }, 20);
            Assert.False(opening.AutoWalking);
            Assert.Equal(NavigationDirection.None, opening.Direction);
        }
        Assert.Equal("Keep entrance door", door.Label);
    }

    [Theory]
    [InlineData(12469)]
    [InlineData(12490)]
    [InlineData(12518)]
    public void OnlyTheNativeOpenCopyProvidesTheContinuation(int line)
    {
        var c = Fixture(line);
        var open = Frame(c with { Map = DoorCopy(c.Map, 4) });
        var exit = Assert.Single(open.Targets, t => t.Id == "exit:1");
        var route = NavigationPathfinder.Search(open.Graph, open.Player, exit.ApproachPoints);
        Assert.NotNull(route.Route);
        Assert.Null(route.IntermediateId);
        Assert.Equal(1, new FieldNavigationGraph(c.Map).ExitAt(route.Route[^1].X, route.Route[^1].Y));
    }

    [Theory]
    [InlineData("handled")]
    [InlineData("unknown")]
    [InlineData("disabled")]
    [InlineData("paused")]
    [InlineData("busy")]
    [InlineData("unknown-priority")]
    [InlineData("misplaced")]
    [InlineData("wrong-marker")]
    public void ClosedFloorWithNoVerifiedOpeningCannotGainAThroughRoute(string variant)
    {
        var c = Fixture(12518);
        var locals = new Dictionary<int, int>(c.Story.Locals);
        if (variant == "handled") locals[7] = 1;
        if (variant == "unknown") locals.Remove(7);
        var actors = c.Actors.Select(a => a.Index != 14 ? a : variant switch {
            "disabled" => a with { ScriptCallsEnabled = false },
            "paused" => a with { ScriptProcessingEnabled = false },
            "busy" => a with { ScriptPriority = 2 },
            "unknown-priority" => a with { ScriptPriority = null },
            "misplaced" => a with { FineX = 2176, TileX = 8 },
            "wrong-marker" => a with { ClassTag = 4 }, _ => a,
        }).ToArray();
        var frame = Frame(c with { Actors = actors, Story = c.Story with { Locals = locals } });
        var exit = Assert.Single(frame.Targets, t => t.Id == "exit:1");
        Assert.Null(NavigationPathfinder.Search(frame.Graph, frame.Player, exit.ApproachPoints).Route);
        // Fixed positive goals force the negative graph through its staged
        // search; an empty negative exit goal list alone cannot test that guard.
        var positiveGoals = Frame(c).Targets.Single(t => t.Id == "exit:1").ApproachPoints;
        Assert.NotEmpty(positiveGoals);
        Assert.Null(NavigationPathfinder.Search(frame.Graph, frame.Player, positiveGoals).Route);
        if (frame.Targets.FirstOrDefault(t => t.Id == "landmark:14") is { } door)
            Assert.Null(NavigationPathfinder.Search(frame.Graph, frame.Player, door.ApproachPoints).Route);
    }

    private static NavigationFrame Frame(Capture c)
    {
        var leader = c.Actors.Single(a => a.Index == 1);
        var field = new FieldNavigationSnapshot(0x1000, 0x4000, 0x2000, 0x20000, c.Actors.Length,
            165, true, 1, 0, 1, 1, leader, c.Actors, [1, 5, 4]) { LastPartySlotRaw = 8 };
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, c.Map,
            new(0, 6656, 4224, 9856), [], c.Story);
    }

    private static Capture Fixture(int line)
    {
        using var stream = typeof(KeepEntranceDoorTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.keep-entrance-captures-0366.json")!;
        using var json = JsonDocument.Parse(stream);
        var raw = json.RootElement.EnumerateArray().Single(e => e.GetProperty("Line").GetInt32() == line).GetProperty("Capture");
        var state = raw.GetProperty("story");
        var story = new FieldStoryState(state.GetProperty("point").GetInt32(), true) {
            Globals = state.GetProperty("globals").Deserialize<Dictionary<int, int>>()!,
            Locals = state.GetProperty("locals").Deserialize<Dictionary<int, int>>()!,
            Extended = state.GetProperty("extended").Deserialize<Dictionary<int, int>>()!,
        };
        return new(raw.GetProperty("map").Deserialize<FieldMapSnapshot>()!,
            raw.GetProperty("actors").Deserialize<FieldActorSnapshot[]>()!, story);
    }

    // Native Atel0246 actor14: E5 copies tiles (left,39)..(left+3,43) to (6,26),
    // flags3B. Left0 is the first animation frame, left4 the final open frame.
    private static FieldMapSnapshot DoorCopy(FieldMapSnapshot map, int left)
    {
        var opened = map with { CollisionShapes = (byte[])map.CollisionShapes.Clone(),
            TerrainFlags = (byte[])map.TerrainFlags.Clone(), CollisionLayers = (byte[])map.CollisionLayers.Clone() };
        foreach (var (source, target) in new[] { (map.CollisionShapes, opened.CollisionShapes),
                     (map.TerrainFlags, opened.TerrainFlags), (map.CollisionLayers, opened.CollisionLayers) })
            for (var y = 0; y < 5; y++)
            for (var x = 0; x < 4; x++) target[(26 + y) * map.Width + 6 + x] = source[(39 + y) * map.Width + left + x];
        return opened;
    }
    private sealed record Capture(FieldMapSnapshot Map, FieldActorSnapshot[] Actors, FieldStoryState Story);
    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
