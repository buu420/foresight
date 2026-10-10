using ChronoTriggerAccessibility.Core.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Core.Tests.Navigation;

/// <summary>Held Dash moves 32 units a frame, but the graph proves legs at the
/// 16-unit walking step. Forest Maze 282 shows both consequences: at (8928,10512,2)
/// a dash ends a turn 16 units beside the proven line and meets a one-pixel layer
/// sliver there; at (6272,2079,3) it passes a turn onto a level crossing by 31 units,
/// and steering back would leave the crossing.</summary>
public sealed class DashStallRecoveryTests
{
    private static readonly NavigationPoint Goal = new(240, 48, 1);

    [Fact]
    public void DashStalledBesideAProvenLegReplansFromItsPositionAndArrives()
    {
        var run = Walk(new Corridor(new(96, 32, 1)), 32);
        Assert.Contains("Arrived at Passage.", run.Speech);
        Assert.DoesNotContain(run.Speech, s => s.Contains("blocked", StringComparison.Ordinal));
        Assert.Equal(2, run.Plans);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    public void OpenParallelLineKeepsOrdinarySteeringWithoutReplanning(int speed)
    {
        var run = Walk(new Corridor(null), speed);
        Assert.Contains("Arrived at Passage.", run.Speech);
        Assert.Equal(1, run.Plans);
        Assert.All(run.Directions, d => Assert.True(d is NavigationDirection.South or NavigationDirection.East));
    }

    [Fact]
    public void WallTheGraphCannotSeeStillStopsWithinItsBlockedTimeout()
    {
        // Every eastward frame past x = 64 is refused, on any line.
        var run = Walk(new Corridor(new(96, 32, 1), wallX: 64), 32);
        Assert.Contains("Navigation stopped: movement is blocked.", run.Speech);
        Assert.DoesNotContain("Arrived at Passage.", run.Speech);
        Assert.InRange(run.StoppedAt, 1500, 3500);
        Assert.InRange(run.Plans, 1, 2);
    }

    [Fact]
    public void ManualInputStillCancelsDuringAStall()
    {
        var run = Walk(new Corridor(new(96, 32, 1)), 32, cancelAt: 200);
        Assert.Contains("Navigation stopped: manual control.", run.Speech);
        Assert.Equal(200, run.StoppedAt);
        Assert.Equal(1, run.Plans);
    }

    [Fact]
    public void ManualDashPressStalledBesideAProvenLegGetsNewDirections()
    {
        var run = Guide(new Corridor(new(96, 32, 1)), 32);
        Assert.Contains("Arrived at Passage.", run.Speech);
        Assert.Contains("Route updated.", run.Speech);
        Assert.DoesNotContain(run.Speech, s => s.Contains("Off route", StringComparison.Ordinal));
        Assert.Equal(2, run.Plans);
    }

    [Fact]
    public void ManualPressIntoAWallTheGraphCannotSeeRepeatsDirectionsOncePerTile()
    {
        var run = Guide(new Corridor(new(96, 32, 1), wallX: 64), 32);
        Assert.DoesNotContain("Arrived at Passage.", run.Speech);
        Assert.Equal(2, run.Plans);
    }

    [Fact]
    public void ManualGuidanceWithoutAStallKeepsItsSingleRoute()
    {
        var run = Guide(new Corridor(null), 32);
        Assert.Contains("Arrived at Passage.", run.Speech);
        Assert.Equal(1, run.Plans);
    }

    [Fact]
    public void HeldButtonWhileFacingAwayFromTheLegIsNotAGuidanceStall()
    {
        // Any held button except Dash reports manual input; standing still while
        // holding it away from the spoken leg must not produce new directions.
        var world = new Corridor(null);
        var frame = new NavigationFrame("dash-stall", true, world.Start,
            [new("passage", "Passage", NavigationCategory.Exits, world.Goal, [world.Goal], true, true)], world, 256)
            { PlayerFacing = NavigationDirection.West };
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        var speech = new List<string>(controller.Handle(NavigationCommand.Guide, frame, 0).Speech);
        for (var tick = 1; tick <= 60; tick++) speech.AddRange(controller.Update(frame, tick * 1000L / 60, true).Speech);
        Assert.DoesNotContain("Route updated.", speech);
        Assert.Contains("plan=1;", controller.DiagnosticState);
    }

    [Fact]
    public void DashPastATurnOntoALevelCrossingDoesNotSteerBackOffIt()
    {
        var run = Walk(new Crossing(), 32);
        Assert.Contains("Arrived at Passage.", run.Speech);
        Assert.Equal(1, run.Plans);
        Assert.DoesNotContain(NavigationDirection.North, run.Directions);
    }

    [Fact]
    public void WalkingOntoTheSameCrossingStillTurnsWithoutCorrection()
    {
        var run = Walk(new Crossing(), 16);
        Assert.Contains("Arrived at Passage.", run.Speech);
        Assert.Equal(1, run.Plans);
    }

    private sealed record Run(List<string> Speech, List<NavigationDirection> Directions, int Plans, long StoppedAt);

    private static Run Walk(World world, int speed, long? cancelAt = null)
    {
        var frame = new NavigationFrame("dash-stall", true, world.Start,
            [new("passage", "Passage", NavigationCategory.Exits, world.Goal, [world.Goal], true, true)], world, 256);
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        var result = controller.Handle(NavigationCommand.ToggleWalk, frame, 0);
        var speech = new List<string>(result.Speech);
        var directions = new List<NavigationDirection>();
        var player = world.Start;
        long stoppedAt = -1;
        for (var tick = 1; tick <= 600 && result.AutoWalking; tick++)
        {
            directions.Add(result.Direction);
            player = world.Move(player, result.Direction, speed) ?? player;
            var now = tick * 1000L / 60;
            if (cancelAt is { } cancel && now >= cancel) now = cancel;
            result = controller.Update(frame with { Player = player, PlayerFacing = result.Direction }, now, now == cancelAt);
            speech.AddRange(result.Speech);
            if (!result.AutoWalking) stoppedAt = now;
        }
        var plans = int.Parse(controller.DiagnosticState.Split("plan=")[1].Split(';')[0]);
        return new(speech, directions, plans, stoppedAt);
    }

    /// <summary>A player holds the spoken leg's direction, and nothing between legs.</summary>
    private static Run Guide(World world, int speed)
    {
        var frame = new NavigationFrame("dash-stall", true, world.Start,
            [new("passage", "Passage", NavigationCategory.Exits, world.Goal, [world.Goal], true, true)], world, 256);
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        var result = controller.Handle(NavigationCommand.Guide, frame, 0);
        var speech = new List<string>(result.Speech);
        var directions = new List<NavigationDirection>();
        var player = world.Start;
        for (var tick = 1; tick <= 600 && result.Guiding; tick++)
        {
            var held = result.ManualLeg?.Direction ?? NavigationDirection.None;
            directions.Add(held);
            player = world.Move(player, held, speed) ?? player;
            result = controller.Update(frame with { Player = player, PlayerFacing = held }, tick * 1000L / 60,
                held != NavigationDirection.None);
            speech.AddRange(result.Speech);
        }
        var plans = int.Parse(controller.DiagnosticState.Split("plan=")[1].Split(';')[0]);
        return new(speech, directions, plans, -1);
    }

    private static NavigationPoint Step(NavigationPoint p, NavigationDirection direction, int distance) => direction switch
    {
        NavigationDirection.North => p with { Y = p.Y - distance },
        NavigationDirection.South => p with { Y = p.Y + distance },
        NavigationDirection.West => p with { X = p.X - distance },
        NavigationDirection.East => p with { X = p.X + distance },
        _ => p,
    };

    private abstract class World : INavigationGraph
    {
        public abstract NavigationPoint Start { get; }
        public abstract NavigationPoint Goal { get; }
        protected abstract bool Open(NavigationPoint p);
        protected virtual int Layer(NavigationPoint p) => 1;
        private NavigationPoint Placed(NavigationPoint p) => p with { Layer = Layer(p) };

        /// <summary>One native frame: the foot commit tests only the frame's end, so a
        /// dash skips the pixel between.</summary>
        public virtual NavigationPoint? Move(NavigationPoint p, NavigationDirection direction, int speed) =>
            Placed(Step(p, direction, speed)) is var next && Open(next) ? next : null;

        /// <summary>Walking proof: the next 16-unit boundary in each direction.</summary>
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point) => new[]
        {
            point with { Y = (point.Y - 1) / 16 * 16 }, point with { X = (point.X / 16 + 1) * 16 },
            point with { Y = (point.Y / 16 + 1) * 16 }, point with { X = (point.X - 1) / 16 * 16 },
        }.Where(p => p.X >= 0 && p.Y >= 0 && p != point).Select(Placed).Where(Open);
    }

    /// <summary>A short column down from the start, then three rows to the goal.
    /// The graph proves the row through the goal.</summary>
    private sealed class Corridor(NavigationPoint? sliver, int? wallX = null) : World
    {
        public override NavigationPoint Start => new(0, 0, 1);
        public override NavigationPoint Goal => DashStallRecoveryTests.Goal;
        protected override bool Open(NavigationPoint p) => p != sliver &&
            (p.X == 0 && p.Y is >= 0 and <= 48 || p.Y is >= 32 and <= 64 && p.X is >= 0 and <= 256);
        public override NavigationPoint? Move(NavigationPoint p, NavigationDirection direction, int speed) =>
            base.Move(p, direction, speed) is { } next && (wallX is null || next.X <= wallX || next.X <= p.X) ? next : null;
    }

    /// <summary>Rows from y = 64 are a level crossing (layer 3). Starting 31 units into
    /// a 32-unit stride, a dash stands 1 unit short of the turn on layer 1, then 31
    /// units past it on the crossing.</summary>
    private sealed class Crossing : World
    {
        public override NavigationPoint Start => new(0, 31, 1);
        public override NavigationPoint Goal => new(240, 64, 3);
        protected override int Layer(NavigationPoint p) => p.Y >= 64 ? 3 : 1;
        protected override bool Open(NavigationPoint p) =>
            p.X == 0 && p.Y is >= 0 and < 128 || p.Y is >= 64 and < 128 && p.X is >= 0 and <= 256;
    }
}
