using System.Text;
using ChronoTriggerAccessibility.Core.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.Navigation;

/// <summary>
/// Closed-loop motion replay. Unlike the scripted-position guidance tests, nothing here
/// tells the player where to go: each frame feeds the controller the current position,
/// takes <see cref="NavigationResult.Direction"/>, and moves the player by one native
/// frame step in that direction. The fixture refuses steps beyond its bounds, like
/// the final native layer-refusal branch at RVA 0x1791D3. Earlier native body checks
/// can slide along walls; this fixture deliberately tests a stricter room.
///
/// Units are the shipped ones: 256 fine units per tile, walk 16, dash 32. The 48-unit
/// increment is an additional stress case, not an observed live movement speed.
/// </summary>
public sealed class MotionReplayTests
{
    private const int Tile = 256;

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(48)]
    public void AutomaticWalkingFollowsLeftThreeThenDownTwoToTheDestination(int speed)
    {
        var replay = new Replay(speed);

        var start = replay.Controller.Handle(NavigationCommand.ToggleWalk, replay.Frame(), 0);
        Assert.Contains("Left 3 steps, then down 2 steps.", Say(start));

        var outcome = replay.Run();

        Assert.True(outcome.Arrived,
            $"speed {speed}: never arrived. {outcome}");
        Assert.DoesNotContain("blocked", outcome.Speech, StringComparison.OrdinalIgnoreCase);
        Assert.All(outcome.Directions, direction => Assert.True(IsCardinal(direction),
            $"speed {speed}: steering produced {direction}, which is not a cardinal move."));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(48)]
    public void AutomaticWalkingConvergesWhenThePlayerStartsOffTheRouteAxis(int speed)
    {
        // A live player is almost never exactly on a 64-unit graph vertex. An offset of
        // 24 units sits above the Axis deadzone (unitsPerTile / 16 = 16) and below the
        // AdvanceAlongRoute tolerance (unitsPerTile / 8 = 32), so the controller both
        // steers to correct it and still considers the player on route.
        var replay = new Replay(speed, new NavigationPoint(768, 24, 1));

        replay.Controller.Handle(NavigationCommand.ToggleWalk, replay.Frame(), 0);
        var outcome = replay.Run();

        Assert.True(outcome.Arrived,
            $"speed {speed}: never arrived from an off-axis start. {outcome}");
        Assert.False(outcome.Oscillated,
            $"speed {speed}: steering reversed on the same axis without progress. {outcome}");
    }

    [Fact]
    public void AutomaticWalkingNeverCutsTheCornerOutsideTheWalkableRegion()
    {
        var replay = new Replay(32);
        replay.Controller.Handle(NavigationCommand.ToggleWalk, replay.Frame(), 0);

        var outcome = replay.Run();

        Assert.True(outcome.Arrived, outcome.ToString());
        Assert.Empty(outcome.RefusedSteps);
    }

    [Fact]
    public void GuidanceOnlyModeNeverSteersAndStillReportsArrival()
    {
        // Guide must not drive the pad. The player is moved along the route by hand here,
        // exactly one native frame step at a time.
        var replay = new Replay(32);
        replay.Controller.Handle(NavigationCommand.Guide, replay.Frame(), 0);

        var outcome = replay.Run(manualRoute: true);

        Assert.True(outcome.Arrived, outcome.ToString());
        Assert.All(outcome.Directions, direction =>
            Assert.Equal(NavigationDirection.None, direction));
    }

    [Fact]
    public void RepeatDuringWalkingReportsTheRemainingLegsWithoutRestartingTheRoute()
    {
        var replay = new Replay(32);
        replay.Controller.Handle(NavigationCommand.ToggleWalk, replay.Frame(), 0);
        replay.Run(maximumFrames: 12);

        var repeat = replay.Controller.Handle(NavigationCommand.Repeat, replay.Frame(), replay.Now);

        Assert.DoesNotContain("Walking to", Say(repeat));
        Assert.Contains("step", Say(repeat), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCardinal(NavigationDirection direction) => direction is
        NavigationDirection.None or NavigationDirection.North or NavigationDirection.South or
        NavigationDirection.West or NavigationDirection.East;

    private static string Say(NavigationResult result) => string.Join(" ", result.Speech);

    /// <summary>An L-shaped room one tile wide, matching "left 3 steps, then down 2 steps".</summary>
    private static bool Walkable(NavigationPoint point) =>
        (point.Y >= 0 && point.Y < Tile && point.X >= 0 && point.X <= 3 * Tile) ||
        (point.X >= 0 && point.X < Tile && point.Y >= 0 && point.Y <= 2 * Tile);

    private sealed record Outcome(bool Arrived, bool Oscillated, string Speech,
        IReadOnlyList<NavigationDirection> Directions, IReadOnlyList<NavigationPoint> RefusedSteps,
        IReadOnlyList<NavigationPoint> Visited)
    {
        public override string ToString()
        {
            var text = new StringBuilder();
            text.Append($"frames={Visited.Count}; last={Visited[^1]}; refused={RefusedSteps.Count}; ");
            text.Append($"speech=[{Speech}]");
            return text.ToString();
        }
    }

    private sealed class Replay(int speed, NavigationPoint? origin = null)
    {
        private static readonly NavigationPoint Goal = new(0, 2 * Tile, 1);
        private NavigationPoint player = origin ?? new NavigationPoint(3 * Tile, 0, 1);

        public NavigationController Controller { get; } = new();
        public long Now { get; private set; }

        public NavigationFrame Frame() => new("room", true, player,
            [new("stairs", "Stairs", NavigationCategory.People, Goal, [Goal], true, true)],
            new LShapedRoom(), Tile);

        public Outcome Run(int maximumFrames = 400, bool manualRoute = false)
        {
            var speech = new StringBuilder();
            var directions = new List<NavigationDirection>();
            var refused = new List<NavigationPoint>();
            var visited = new List<NavigationPoint> { player };
            var arrived = false;
            var reversals = 0;
            var previousDirection = NavigationDirection.None;

            for (var frame = 0; frame < maximumFrames && !arrived; frame++)
            {
                Now += 16;
                var result = Controller.Update(Frame(), Now);
                foreach (var line in result.Speech) speech.Append(line).Append(' ');
                if (result.Speech.Any(line => line.Contains("Arrived", StringComparison.Ordinal)))
                {
                    arrived = true;
                    break;
                }

                directions.Add(result.Direction);
                var direction = manualRoute ? ManualDirection() : result.Direction;
                if (Opposite(direction, previousDirection)) reversals++;
                if (direction != NavigationDirection.None) previousDirection = direction;

                var next = Step(player, direction, speed);
                if (next == player) { if (direction != NavigationDirection.None) refused.Add(next); }
                else player = next;
                visited.Add(player);
            }

            return new(arrived, reversals > 4, speech.ToString().Trim(), directions, refused, visited);
        }

        /// <summary>Walks the reference route by hand: left along the arm, then down.</summary>
        private NavigationDirection ManualDirection() => player.X > 0
            ? NavigationDirection.West
            : player.Y < Goal.Y ? NavigationDirection.South : NavigationDirection.None;

        private static bool Opposite(NavigationDirection a, NavigationDirection b) =>
            (a, b) is (NavigationDirection.North, NavigationDirection.South) or
                (NavigationDirection.South, NavigationDirection.North) or
                (NavigationDirection.West, NavigationDirection.East) or
                (NavigationDirection.East, NavigationDirection.West);

        private static NavigationPoint Step(NavigationPoint current, NavigationDirection direction, int speed)
        {
            var next = direction switch
            {
                NavigationDirection.North => current with { Y = current.Y - speed },
                NavigationDirection.South => current with { Y = current.Y + speed },
                NavigationDirection.West => current with { X = current.X - speed },
                NavigationDirection.East => current with { X = current.X + speed },
                _ => current,
            };
            // Native refusal is a hard stop: 0x1791D3 zeroes the committed movement.
            return Walkable(next) ? next : current;
        }
    }

    private sealed class LShapedRoom : INavigationGraph
    {
        // Mirrors FieldNavigationGraph: snap to the 64-unit lattice from wherever the
        // player actually is, then accept the candidate if the cell is walkable. A live
        // player is rarely on a lattice vertex, so a graph that only linked vertices
        // would be unreachable from most real positions.
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point)
        {
            if (point.X < 0 || point.Y < 0) yield break;
            foreach (var next in Adjacent(point)) if (Walkable(next)) yield return next;
        }

        private static IEnumerable<NavigationPoint> Adjacent(NavigationPoint point)
        {
            yield return point with { Y = point.Y == 0 ? -1 : (point.Y - 1) / 64 * 64 };
            yield return point with { X = (point.X / 64 + 1) * 64 };
            yield return point with { Y = (point.Y / 64 + 1) * 64 };
            yield return point with { X = point.X == 0 ? -1 : (point.X - 1) / 64 * 64 };
        }
    }
}
