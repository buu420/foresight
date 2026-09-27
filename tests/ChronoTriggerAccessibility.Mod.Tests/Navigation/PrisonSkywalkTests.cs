using System.Reflection;
using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Read-only scene 28 capture, 2026-09-26 19:07:13. The player stood at
/// (9094,2559), before Atel0409 actor1's first bridge contact. The native bridge
/// admits the foot at Y=2544..2559, between the old four-pixel routing rows.</summary>
public sealed class PrisonSkywalkTests
{
    [Fact]
    public void CapturedBridgeCanRouteBackToTheGuardroom()
    {
        var frame = Build();
        var target = Assert.Single(frame.Targets, t => t.Id == "exit:1");
        var path = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints);
        Assert.False(path.LimitReached);
        Assert.NotNull(path.Route);
        Assert.Equal(1, new FieldNavigationGraph(Fixture().Map).ExitAt(path.Route[^1].X, path.Route[^1].Y));
    }

    [Fact]
    public void FirstBridgeEventRoutesToTheLiveContactInsteadOfTheCastleExit()
    {
        var frame = Build();
        var target = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.Equal("Continue across the upper bridge", target.Label);
        Assert.NotEmpty(target.ApproachPoints);
        Assert.All(target.ApproachPoints, p => Assert.InRange(p.X, 6000, 6550));
        Assert.NotNull(NavigationPathfinder.Find(frame.Graph, frame.Player, target.ApproachPoints));
    }

    [Fact]
    public void SecondBridgeContactStartsTheBossBeforeTheCastleExitIsOffered()
    {
        var f = Fixture();
        var marker = f.Field.Actors.Single(a => a.Index == 1) with { TileX = 13, FineX = 3456 };
        var story = f.Story with { Globals = new Dictionary<int, int>(f.Story.Globals) { [0x198] = 0x15 } };
        var field = f.Field with { Actors = f.Field.Actors.Select(a => a.Index == 1 ? marker : a).ToArray() };
        var frame = Build(field, f.Map, story);
        var target = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.Equal("Continue toward the tank", target.Label);
        Assert.All(target.ApproachPoints, p => Assert.InRange(p.X, 3200, 3700));
        Assert.NotNull(NavigationPathfinder.Find(frame.Graph, frame.Player, target.ApproachPoints));
    }

    [Fact]
    public void MissingBridgeFlagsDoNotSkipTheEncounter()
    {
        var f = Fixture();
        var target = Assert.Single(Build(f.Field, f.Map, new(46, false)).Targets,
            t => t.Category == NavigationCategory.StoryEvents);
        Assert.True(target.IsStoryNote);
    }

    [Fact]
    public void AutoWalkKeepsMovingLeftUntilTheNativeBridgeContactTakesControl()
    {
        var frame = Build();
        var controller = new NavigationController();
        for (var i = 0; i < 3; i++) controller.Handle(NavigationCommand.NextCategory, frame, i);
        var result = controller.Handle(NavigationCommand.ToggleWalk, frame, 10);
        // Original 175E90 replay proves (-32,0) on this live row and contact2
        // before reaching the goal (6544 -> 6512). Simulate only those proven
        // straight moves, then let the game's event take away control.
        for (var x = frame.Player.X; x > 6512; x -= 32)
        {
            Assert.True(result.AutoWalking);
            Assert.Equal(NavigationDirection.West, result.Direction);
            frame = frame with { Player = frame.Player with { X = x - 32 } };
            result = controller.Update(frame, (9094 - x) * 2 + 30);
            Assert.DoesNotContain(result.Speech, s => s.Contains("blocked") || s.Contains("No route"));
        }
        Assert.False(controller.Update(frame with { CanNavigate = false }, 6000).AutoWalking);
    }

    [Theory]
    [InlineData(9, 0)]
    [InlineData(25, 2)]
    [InlineData(41, 4)]
    [InlineData(57, 6)]
    public void EachBridgeKeepsItsOwnOnwardExitAfterTheBoss(int row, int exit)
    {
        var f = Fixture();
        var story = f.Story with { Globals = new Dictionary<int, int>(f.Story.Globals) { [0x198] = 0x1D, [0x199] = 0x24 } };
        var pc = f.Field.LeadPlayer! with { TileY = row, FineY = row * 256 + 255 };
        var frame = Build(f.Field with { LeadPlayer = pc }, f.Map, story);
        var target = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.NotEmpty(target.ApproachPoints);
        Assert.All(target.ApproachPoints, p => Assert.Equal(exit, new FieldNavigationGraph(f.Map).ExitAt(p.X, p.Y)));
    }

    [Fact]
    public void NarrowTerrainAllowsASubGridTurnWithoutCrossingItsWall()
    {
        var graph = new FieldNavigationGraph(Fixture().Map);
        var start = new NavigationPoint(9094, 2559, 1);
        var goal = new NavigationPoint(6272, 2544, 1);
        var path = NavigationPathfinder.Find(graph, start, [goal]);
        Assert.NotNull(path);
        Assert.All(path, p => Assert.InRange(p.Y, 2544, 2559));
        Assert.Null(NavigationPathfinder.Find(graph, start, [goal with { Y = 2528 }]));
    }

    private static NavigationFrame Build()
    {
        var f = Fixture();
        return Build(f.Field, f.Map, f.Story);
    }
    private static NavigationFrame Build(FieldNavigationSnapshot field, FieldMapSnapshot map, FieldStoryState story) =>
        new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, map, new(0, 0, 16384, 16384), [], story);

    internal static Capture Fixture()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.prison-skywalk-live-0334.json")!;
        return JsonSerializer.Deserialize<Capture>(stream)!;
    }
    internal sealed record Capture(FieldNavigationSnapshot Field, FieldMapSnapshot Map, FieldStoryState Story);
    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
