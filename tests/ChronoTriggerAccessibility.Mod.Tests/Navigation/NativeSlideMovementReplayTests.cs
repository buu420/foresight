using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class NativeSlideMovementReplayTests
{
    [Theory]
    [InlineData(6272, 2047, 1, NavigationDirection.South, 16, 6272, 2063, 3)]
    [InlineData(6272, 2047, 1, NavigationDirection.South, 32, 6272, 2079, 3)]
    [InlineData(8928, 10512, 2, NavigationDirection.North, 32, 8928, 10480, 2)]
    [InlineData(8992, 10527, 2, NavigationDirection.North, 32, 8992, 10495, 2)]
    public void FrameOracleMatchesIndependentNativeTraces(int x, int y, int layer, NavigationDirection command,
        int speed, int toX, int toY, int toLayer)
    {
        Assert.Equal(new(toX, toY, toLayer), NativeWalkingReplay.Move(Map(282), new(x, y, layer), command, speed));
    }

    public static TheoryData<int, int, int, int, int, bool> Routes
    {
        get
        {
            var data = new TheoryData<int, int, int, int, int, bool>();
            (int Scene, int X, int Y, int Exit)[] entries =
            [
                (282, 2304, 767, 1), // Actual tester position, including its fine-coordinate phase.
                (282, 9 * 256 + 128, 2 * 256 + 128, 1),
                (282, 7 * 256 + 128, 46 * 256 + 128, 0),
                (290, 42 * 256 + 128, 59 * 256 + 128, 0),
                (196, 27 * 256 + 128, 44 * 256 + 128, 3),
                (196, 27 * 256 + 128, 60 * 256 + 128, 1),
                // Scene262's wind tiles require the native floor-push update;
                // its static route proofs remain in NativeCornerSlideTests.
                (264, 18 * 256 + 128, 28 * 256 + 128, 4),
            ];
            foreach (var (scene, x, y, exit) in entries)
            foreach (var speed in new[] { 16, 32 })
            foreach (var auto in new[] { false, true }) data.Add(scene, x, y, exit, speed, auto);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void GuidanceAndAutoWalkReachTheExitWithNativeWalkOrDashFrames(int scene, int x, int y, int exit, int speed, bool auto)
    {
        var map = Map(scene);
        var graph = new FieldNavigationGraph(map);
        Assert.True(graph.TryPosition(x, y, 1, out var start));
        var goals = Goals(graph, map, start, exit);
        var frame = new NavigationFrame("native-motion", true, start,
            [new("passage", "Passage", NavigationCategory.Exits, goals[0], goals, true, true)], graph, 256);
        var controller = new NavigationController();
        var timer = Stopwatch.StartNew();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        var result = controller.Handle(auto ? NavigationCommand.ToggleWalk : NavigationCommand.Guide, frame, 1);
        var speech = new List<string>(result.Speech);
        for (var tick = 1; tick <= 12000 && result.Guiding && graph.ExitAt(frame.Player.X, frame.Player.Y) != exit; tick++)
        {
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(20),
                $"Replay did not finish: scene={scene}, start=({x},{y}), speed={speed}, auto={auto}, " +
                $"tick={tick}, position={frame.Player}, {controller.DiagnosticState}; {string.Join(" | ", speech.TakeLast(12))}");
            var command = auto ? result.Direction : result.ManualLeg?.Direction ?? NavigationDirection.None;
            frame = frame with
            {
                Player = NativeWalkingReplay.Move(map, frame.Player, command, speed), PlayerFacing = command,
            };
            result = controller.Update(frame, tick * 16, !auto && command != NavigationDirection.None);
            speech.AddRange(result.Speech);
        }
        Assert.True(graph.ExitAt(frame.Player.X, frame.Player.Y) == exit,
            $"scene={scene}, speed={speed}, auto={auto}, stopped at {frame.Player}: {string.Join(" | ", speech.TakeLast(12))}");
        Assert.DoesNotContain(speech, s => s.Contains("blocked") || s.Contains("Off route") || s.Contains("No route"));
    }

    private static NavigationPoint[] Goals(FieldNavigationGraph graph, FieldMapSnapshot map, NavigationPoint start, int exit)
    {
        int[] offsets = [0, 64, 112, 128, 192, 240];
        var goals = new HashSet<NavigationPoint>();
        for (var y = 0; y < map.ExitHeight; y++)
        for (var x = 0; x < map.ExitWidth; x++)
        {
            if (map.ExitCells[y * map.ExitWidth + x] != exit) continue;
            foreach (var dx in offsets)
            foreach (var dy in offsets)
            for (var layer = 1; layer <= 3; layer++)
                if (graph.TryPosition(x * 256 + dx, y * 256 + dy, layer, out var p)) goals.Add(p);
        }
        return goals.OrderBy(p => Math.Abs((double)p.X - start.X) + Math.Abs((double)p.Y - start.Y)).Take(64).ToArray();
    }

    private static FieldMapSnapshot Map(int scene, [CallerFilePath] string source = "")
    {
        using var json = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(Path.GetDirectoryName(source)!, "native-slide-maps-0351.json")));
        var entry = json.RootElement.GetProperty("Maps").EnumerateArray().Single(m => m.GetProperty("Scene").GetInt32() == scene);
        return JsonSerializer.Deserialize<FieldMapSnapshot>(entry.GetProperty("Map").GetRawText())! with { PlayerLayer = 1 };
    }
}
