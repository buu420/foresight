using System.Runtime.CompilerServices;
using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class ForestMazeNavigationSourceTests
{
    [Theory]
    [InlineData(NavigationCommand.Guide)]
    [InlineData(NavigationCommand.ToggleWalk)]
    public void LoggedMazeEntryCanNavigateToTheForwardStoryPassage(NavigationCommand command)
    {
        var frame = Frame();
        var story = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.Contains("Outside, south side", story.Instruction!);
        var graph = new FieldNavigationGraph(Map());
        Assert.All(story.ApproachPoints, p => Assert.Equal(1, graph.ExitAt(p.X, p.Y)));
        var controller = new NavigationController();
        for (var i = 0; i < 3; i++) controller.Handle(NavigationCommand.NextCategory, frame, 0);
        var result = controller.Handle(command, frame, 1);
        Assert.True(result.Guiding);
        Assert.Equal(command == NavigationCommand.ToggleWalk, result.AutoWalking);
        Assert.DoesNotContain(result.Speech, s => s.Contains("No route"));
    }

    [Fact]
    public void InstalledMazeCrossingsHaveDistinctLabelsAndNativeApproaches()
    {
        var frame = Frame();
        var crossings = frame.Targets.Where(t => t.Id.StartsWith("level-crossing:")).ToArray();
        Assert.NotEmpty(crossings);
        Assert.Equal(crossings.Length, crossings.Select(t => t.Label).Distinct().Count());
        var graph = new FieldNavigationGraph(Map());
        Assert.All(crossings, t =>
        {
            Assert.Equal(NavigationCategory.Objects, t.Category);
            Assert.True(t.GuideAvailable);
            Assert.NotEmpty(t.ApproachPoints);
            Assert.All(t.ApproachPoints, p => Assert.True(
                graph.TryPosition(p.X, p.Y, p.Layer, out var native) && p == native));
        });
    }

    private static NavigationFrame Frame()
    {
        // Position and story point are from the October 9 Forest Maze tester log.
        // Actor blockers and camera culling are excluded to isolate its map failure.
        var player = FullGameNavigationTests.Actor(1, 2304, 767) with { IsPartyMember = true };
        var field = FullGameNavigationTests.Field(282, player) with { LeadPlayer = player, LeadPlayerActorIndex = 1 };
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, Map(),
            new(0, 0, 64 * 256, 48 * 256), [], new(123, false));
    }

    private static FieldMapSnapshot Map([CallerFilePath] string source = "")
    {
        using var json = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(Path.GetDirectoryName(source)!, "native-slide-maps-0351.json")));
        var entry = json.RootElement.GetProperty("Maps").EnumerateArray().Single(m => m.GetProperty("Scene").GetInt32() == 282);
        return JsonSerializer.Deserialize<FieldMapSnapshot>(entry.GetProperty("Map").GetRawText())! with { PlayerLayer = 1 };
    }

    private sealed class NoMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination) => false;
    }
}
