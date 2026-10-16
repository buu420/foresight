using System.Diagnostics;
using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Installed initial Map186 plus actor facts logged at lines 11942 and
/// 12150 of the October 16 report. This is a reconstruction, not a live map
/// capture: unlogged script priority, facing, render flags and story bytes are
/// synthetic. All four failure poses are copied exactly; the earlier poses
/// reuse the 11942 actor frame rather than claiming an exact actor capture.</summary>
public sealed class MountainOfWoeRouteTests
{
    [Theory]
    [InlineData(5649)]
    [InlineData(9142)]
    [InlineData(11942)]
    [InlineData(12150)]
    public void LoggedPositionsCanReachStationFiveAndTheReturnExit(int line)
    {
        var frame = Frame(line);
        var story = Assert.Single(frame.Targets, t => t.Id == "story:full:woe-summit");
        var onward = Assert.Single(frame.Targets, t => t.Id == "exit:1");
        Assert.Equal(onward.ApproachPoints, story.ApproachPoints);
        Assert.False(story.IsStoryNote);
        foreach (var id in new[] { "exit:0", "exit:1" })
        {
            var target = Assert.Single(frame.Targets, t => t.Id == id);
            var path = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints);
            Assert.False(path.LimitReached);
            Assert.NotNull(path.Route);
            Assert.Contains(path.Route[^1], target.ApproachPoints);
            var terrain = new FieldNavigationGraph(Fixture().Map);
            Assert.Equal(id == "exit:1" ? 1 : 0, terrain.ExitAt(path.Route[^1].X, path.Route[^1].Y));
            Assert.DoesNotContain(path.Route.Skip(1).SkipLast(1), p => terrain.ExitAt(p.X, p.Y) >= 0);
        }
    }

    [Theory]
    [InlineData(5649, "exit:1", NavigationCommand.Guide)]
    [InlineData(5649, "exit:1", NavigationCommand.ToggleWalk)]
    [InlineData(5649, "story:full:woe-summit", NavigationCommand.Guide)]
    [InlineData(5649, "story:full:woe-summit", NavigationCommand.ToggleWalk)]
    [InlineData(9142, "exit:1", NavigationCommand.Guide)]
    [InlineData(9142, "exit:1", NavigationCommand.ToggleWalk)]
    [InlineData(9142, "story:full:woe-summit", NavigationCommand.Guide)]
    [InlineData(9142, "story:full:woe-summit", NavigationCommand.ToggleWalk)]
    [InlineData(11942, "exit:1", NavigationCommand.Guide)]
    [InlineData(11942, "exit:1", NavigationCommand.ToggleWalk)]
    [InlineData(11942, "story:full:woe-summit", NavigationCommand.Guide)]
    [InlineData(11942, "story:full:woe-summit", NavigationCommand.ToggleWalk)]
    [InlineData(12150, "exit:1", NavigationCommand.Guide)]
    [InlineData(12150, "exit:1", NavigationCommand.ToggleWalk)]
    [InlineData(12150, "story:full:woe-summit", NavigationCommand.Guide)]
    [InlineData(12150, "story:full:woe-summit", NavigationCommand.ToggleWalk)]
    public void BothNavigationModesStartForTheExitAndStoryTarget(int line, string id, NavigationCommand command)
    {
        var frame = Frame(line);
        var target = Assert.Single(frame.Targets, t => t.Id == id);
        frame = frame with { Targets = [target] };
        var controller = new NavigationController();
        for (var i = 0; i < (int)target.Category; i++) controller.Handle(NavigationCommand.NextCategory, frame, 0);
        var result = controller.Handle(command, frame, 1);
        Assert.True(result.Guiding, string.Join(" | ", result.Speech));
        Assert.Equal(command == NavigationCommand.ToggleWalk, result.AutoWalking);
        Assert.False(controller.AllowsConfirmWhileFollowing(frame));
        Assert.DoesNotContain(result.Speech, s => s.Contains("search limit", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(5649, false)]
    [InlineData(5649, true)]
    [InlineData(9142, false)]
    [InlineData(9142, true)]
    [InlineData(11942, false)]
    [InlineData(11942, true)]
    [InlineData(12150, false)]
    [InlineData(12150, true)]
    public void BothModesFollowNativeWalkingFramesAcrossTheMountain(int line, bool auto)
    {
        var frame = Frame(line);
        var map = Fixture().Map;
        var terrain = new FieldNavigationGraph(map);
        var target = Assert.Single(frame.Targets, t => t.Id == "story:full:woe-summit");
        frame = frame with { Targets = [target] };
        var controller = new NavigationController();
        for (var i = 0; i < (int)target.Category; i++) controller.Handle(NavigationCommand.NextCategory, frame, 0);
        var result = controller.Handle(auto ? NavigationCommand.ToggleWalk : NavigationCommand.Guide, frame, 1);
        var speech = new List<string>(result.Speech);
        var timer = Stopwatch.StartNew();
        for (var tick = 1; tick <= 20000 && result.Guiding && terrain.ExitAt(frame.Player.X, frame.Player.Y) != 1; tick++)
        {
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(30), $"Replay timed out at {frame.Player}: {controller.DiagnosticState}");
            var command = auto ? result.Direction : result.ManualLeg?.Direction ?? NavigationDirection.None;
            frame = frame with { Player = NativeWalkingReplay.Move(map, frame.Player, command), PlayerFacing = command };
            Assert.True(terrain.ExitAt(frame.Player.X, frame.Player.Y) is < 0 or 1, $"Unexpected exit at {frame.Player}");
            result = controller.Update(frame, tick * 16, !auto && command != NavigationDirection.None);
            speech.AddRange(result.Speech);
        }
        Assert.Equal(1, terrain.ExitAt(frame.Player.X, frame.Player.Y));
        Assert.DoesNotContain(speech, s => s.Contains("search limit", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("No route", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(5649)]
    [InlineData(9142)]
    [InlineData(11942)]
    [InlineData(12150)]
    public void ExplicitSmallBudgetsStillStopTheSearch(int line)
    {
        var frame = Frame(line);
        var target = Assert.Single(frame.Targets, t => t.Id == "exit:1");
        var result = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints, 100);
        Assert.True(result.LimitReached);
        Assert.Null(result.Route);
    }

    private static NavigationFrame Frame(int line)
    {
        var fixture = Fixture();
        var actors = fixture.Frames.Single(f => f.Line == (line is 5649 or 9142 ? 11942 : line)).Actors;
        var leader = actors.Single(a => a.Index == 8);
        var pose = line switch { 5649 => new NavigationPoint(5472, 13055, 1),
            9142 => new NavigationPoint(5248, 12415, 1), _ => new(leader.FineX, leader.FineY, 1) };
        leader = leader with { TileX = pose.X >> 8, FineX = pose.X, FractionX = pose.X & 255,
            TileY = pose.Y >> 8, FineY = pose.Y, FractionY = pose.Y & 255 };
        actors = actors.Select(a => a.Index == 8 ? leader : a).ToArray();
        var field = new FieldNavigationSnapshot(0x13AFB020, 0x4000, 0x2000, 0x20000, 27, 392, true,
            1, 0, 8, 8, leader, actors, [8, 10, 9]);
        var story = new FieldStoryState(180, true) {
            Globals = Enumerable.Range(0, 512).ToDictionary(i => i, _ => 0),
            Locals = Enumerable.Range(0, 256).ToDictionary(i => i, _ => 0),
            Extended = Enumerable.Range(0, 80).ToDictionary(i => i, _ => 0),
            Party = [0, 2, 1, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80],
        };
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, fixture.Map,
            new(0, 0, 16384, 16384), [], story);
    }

    private static MountainFixture Fixture()
    {
        using var stream = typeof(MountainOfWoeRouteTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.mountain-woe-392-map-0365.json")!;
        return JsonSerializer.Deserialize<MountainFixture>(stream)!;
    }
    private sealed record MountainFixture(FieldMapSnapshot Map, ActorFrame[] Frames);
    private sealed record ActorFrame(int Line, FieldActorSnapshot[] Actors);
    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
