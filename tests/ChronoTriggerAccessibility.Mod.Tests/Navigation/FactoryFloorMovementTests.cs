using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class FactoryFloorMovementTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(1, 0, 8)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 2, 8)]
    [InlineData(2, 0, 0)]
    public void MovingAgainstTheLowerBeltUsesTheNativeRunSetting(int mode, int toggle, int dash)
    {
        var frame = Frame(new(1536, 1152, 1), 0x0B);
        var movement = FieldFloorMovement.Adjust(frame, new([], NavigationDirection.West, true, true),
            Floor(frame, mode, toggle));
        Assert.Null(movement.StopReason);
        Assert.Equal(0x200u | (uint)dash, movement.Pad);
    }

    [Fact]
    public void NativeWalkToggleThatCannotBeOverriddenRequestsThePlayersRunControl()
    {
        var frame = Frame(new(1536, 1152, 1), 0x0B);
        var movement = FieldFloorMovement.Adjust(frame, new([], NavigationDirection.West, true, true),
            Floor(frame, 0, 2));
        Assert.Equal(0u, movement.Pad);
        Assert.Contains("running", movement.StopReason);
    }

    [Theory]
    [InlineData(NavigationDirection.North)]
    [InlineData(NavigationDirection.South)]
    [InlineData(NavigationDirection.East)]
    public void LeavingOrFollowingTheBeltDoesNotRequireRunning(NavigationDirection direction)
    {
        var frame = Frame(new(1536, 1152, 1), 0x0B);
        var movement = FieldFloorMovement.Adjust(frame, new([], direction, true, true), Floor(frame, 0, 2));
        Assert.Null(movement.StopReason);
        Assert.Equal(FieldNavigationRuntime.DirectionBits(direction), movement.Pad);
    }

    [Theory]
    [InlineData(0x0B, 1, 2)] // east at 16; native held Dash can run
    [InlineData(0x0E, 1, 2)] // west at 32, on explicitly open test terrain
    public void AutomaticWalkingCrossesABeltWithoutStallingOnItsPerpendicularDrift(byte flags, int mode, int toggle)
    {
        // Open test terrain isolates the native floor force. These are not
        // invented Factory coordinates or a captured warehouse playthrough.
        var map = Map(flags);
        var graph = new FieldNavigationGraph(map);
        var player = new NavigationPoint(6 * 256, 5 * 256 + 128, 1);
        var goal = new NavigationPoint(6 * 256, 3 * 256 + 128, 1);
        var keys = new HashSet<int>();
        var speech = new List<string>();
        long now = 0;
        NavigationFrame Capture() => new("00001000:231", true, player,
            [new("goal", "Destination", NavigationCategory.People, goal, [goal], true, true)], graph, 256)
        { MovingFloor = FieldFloorMovement.FromFlags(map.TerrainFlags[player.Y / 256 * map.Width + player.X / 256]) };
        var runtime = new FieldNavigationRuntime(_ => Capture(), new(keys.Contains, () => true),
            () => true, () => now, speech.Add, _ => { },
            adjustFieldInput: (frame, result) => FieldFloorMovement.Adjust(frame, result, Floor(frame, mode, toggle)));
        runtime.Enable();
        runtime.OnInput(0x1000, 0); // establish the released-key baseline
        now += 16;
        keys.Add('P');
        for (var tick = 0; tick < 250; tick++)
        {
            var floor = Floor(Capture(), mode, toggle);
            var pad = runtime.OnInput(0x1000, 0);
            keys.Clear();
            var speed = mode == 1 && (pad & 8) != 0 ? 32 : 16; // selected cases from native 175C90
            var dx = (pad & 0x100) != 0 ? speed : (pad & 0x200) != 0 ? -speed : 0;
            var dy = (pad & 0x400) != 0 ? speed : (pad & 0x800) != 0 ? -speed : 0;
            player = player with { X = player.X + dx + floor.ForceX, Y = player.Y + dy + floor.ForceY };
            now += 16;
            Assert.InRange(player.X, 128, 15 * 256 - 128);
            Assert.InRange(player.Y, 128, 7 * 256 - 128);
            if (speech.Any(s => s.Contains("Arrived"))) break;
        }
        Assert.Contains(speech, s => s.Contains("Arrived"));
        Assert.DoesNotContain(speech, s => s.Contains("movement is blocked"));
        Assert.InRange(Math.Abs(player.X - goal.X), 0, 32);
        Assert.InRange(Math.Abs(player.Y - goal.Y), 0, 32);
    }

    [Fact]
    public void UnreadableOrForeignRunStateCannotDriveAnActiveBelt()
    {
        var frame = Frame(new(1536, 1152, 1), 0x0B);
        var result = new NavigationResult([], NavigationDirection.West, true, true);
        Assert.NotNull(FieldFloorMovement.Adjust(frame, result, null).StopReason);
        Assert.NotNull(FieldFloorMovement.Adjust(frame, result, Floor(frame, null, null)).StopReason);
        Assert.NotNull(FieldFloorMovement.Adjust(frame, result, Floor(frame, 1, 2) with { Scene = 229 }).StopReason);
        Assert.Equal(0x200u, FieldFloorMovement.Adjust(frame with { MovingFloor = null }, result, null).Pad);
    }

    private static NavigationFrame Frame(NavigationPoint player, byte flags) => new("00001000:231", true,
        player, [], new FieldNavigationGraph(Map(flags)), 256) { MovingFloor = FieldFloorMovement.FromFlags(flags) };
    private static FieldFloorSnapshot Floor(NavigationFrame frame, int? mode, int? toggle)
    {
        var floor = frame.MovingFloor;
        var speed = floor?.Speed ?? 0;
        var (x, y) = floor?.Direction switch
        {
            NavigationDirection.West => (-speed, 0), NavigationDirection.East => (speed, 0),
            NavigationDirection.North => (0, -speed), NavigationDirection.South => (0, speed), _ => (0, 0),
        };
        return new(0x1000, 0x4000, 231, 1, x, y, mode, toggle);
    }
    private static FieldMapSnapshot Map(byte flags)
    {
        var terrain = new byte[16 * 8];
        for (var x = 1; x < 15; x++) terrain[4 * 16 + x] = flags;
        return new(16, 8, new byte[128], terrain, Enumerable.Repeat((byte)1, 128).ToArray(),
            1, false, 16, 8, Enumerable.Repeat((byte)128, 128).ToArray());
    }
}
