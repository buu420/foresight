using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class VortexWorldNavigationTests
{
    [Theory]
    [InlineData(NavigationCommand.Guide, 0, 0)]
    [InlineData(NavigationCommand.ToggleWalk, 0, 0)]
    [InlineData(NavigationCommand.Guide, 3, 5)]
    [InlineData(NavigationCommand.ToggleWalk, 3, 5)]
    public void NativeBoardingTableLeavesTheLoggedFutureVortexUsable(NavigationCommand command, int phaseX, int phaseY)
    {
        var world = World(phaseX, phaseY);
        //bankc2_half.bin324C=8,8,8,8;3254=0,0,0,8. Resource SHA256
        //fa0c1856d4a1794d0b2ee3ad77ff9aa6b4cfc23b4009f34d763f37446a3914d4.
        //Positions/entrances are log/catalog facts; the floor remains synthetic.
        world = world with { Vehicles = world.Vehicles! with { EpochBoardingShape = new(0, 0, 0, 8) } };
        var frame = Build(world);
        var target = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Exits);
        Assert.Null(target.GuidanceRestriction);
        Assert.NotEmpty(target.ApproachPoints);
        Assert.All(target.ApproachPoints, p =>
        {
            var x = p.X / 16 - (1136 + phaseX); var y = p.Y / 16 - (648 + phaseY);
            Assert.True(x <= -8 || x >= 8 || y <= -8 || y >= 16);
        });
        var control = new NavigationController();
        control.Handle(NavigationCommand.NextCategory, frame, 0);
        var result = control.Handle(command, frame, 16);
        Assert.True(result.Guiding);
        Assert.Equal(command == NavigationCommand.ToggleWalk, result.AutoWalking);
    }

    [Theory]
    [InlineData(NavigationCommand.Guide)]
    [InlineData(NavigationCommand.ToggleWalk)]
    public void ParkedEpochGuidanceAndWalkingReachItsNativeBoardingContact(NavigationCommand command)
    {
        var world = World(0, 0);
        world = world with
        {
            Motion = world.Motion with { PixelX = 1152, PixelY = 408 }, WalkingEndpoint = new(1152, 408),
            Vehicles = world.Vehicles! with { EpochX = 1120, EpochY = 408, BoardingPrompt = false },
        };
        var frame = Build(world);
        var epoch = Assert.Single(frame.Targets, t => t.Id == "vehicle:epoch");
        var path = NavigationPathfinder.Find(frame.Graph, frame.Player, epoch.ApproachPoints);
        Assert.NotNull(path);
        Assert.InRange(path[^1].X / 16, 1105, 1135);
        Assert.InRange(path[^1].Y / 16, 393, 423);
        var control = new NavigationController();
        control.Handle(NavigationCommand.NextCategory, frame, 0);
        control.Handle(NavigationCommand.NextCategory, frame, 0);
        var start = control.Handle(command, frame, 16);
        Assert.True(start.Guiding);
        Assert.Equal(command == NavigationCommand.ToggleWalk, start.AutoWalking);
        var result = control.Update(frame with { Player = path[^1] }, 32);
        Assert.Equal(NavigationDirection.None, result.Direction);
        Assert.Contains(result.Speech, s => s.Contains("Press Confirm to board"));
    }

    [Theory]
    [InlineData(NavigationCommand.Guide, 0, 0)]
    [InlineData(NavigationCommand.ToggleWalk, 0, 0)]
    [InlineData(NavigationCommand.Guide, 3, 5)]
    [InlineData(NavigationCommand.ToggleWalk, 3, 5)]
    public void VortexApproachFinishesOutsideTheParkedEpochsBoardingContact(
        NavigationCommand command, int phaseX, int phaseY)
    {
        // The four future-vortex records are native world2 entries20..23,
        // tiles70/71,41/42. The log lands at1136,648. Terrain and8px
        // opposing shapes here are explicit synthetic fixtures, not live captures.
        var frame = Build(World(phaseX, phaseY));
        var vortex = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Exits);
        Assert.NotEmpty(vortex.ApproachPoints);
        // With opposing8px extents, the first two rows would board the Epoch.
        // Leave one pixel for the controller's final arrival tolerance as well.
        Assert.All(vortex.ApproachPoints, p =>
        {
            Assert.True(p.Y >= (672 + phaseY) * 16);
            Assert.Equal(phaseX * 16, p.X % 128);
            Assert.Equal(phaseY * 16, p.Y % 128);
        });
        var path = NavigationPathfinder.Find(frame.Graph, frame.Player, vortex.ApproachPoints);
        Assert.NotNull(path);
        var control = new NavigationController();
        control.Handle(NavigationCommand.NextCategory, frame, 0);
        var start = control.Handle(command, frame, 16);
        Assert.True(start.Guiding);
        Assert.Equal(command == NavigationCommand.ToggleWalk, start.AutoWalking);
        var arrived = control.Update(frame with { Player = path[^1] }, 32);
        Assert.Contains(arrived.Speech, s => s.Contains("Arrived at Dimensional Vortex"));
        Assert.Equal(NavigationDirection.None, arrived.Direction);
        // The machine is still a separate, usable native interaction.
        var epoch = Assert.Single(frame.Targets, t => t.Id == "vehicle:epoch");
        Assert.NotEmpty(epoch.ApproachPoints);
    }

    [Theory]
    [InlineData(NavigationCommand.Guide)]
    [InlineData(NavigationCommand.ToggleWalk)]
    public void AnEntranceEntirelyCoveredByTheVehicleExplainsTheNativeRestriction(NavigationCommand command)
    {
        var world = World(0, 0);
        var frame = Build(world with
        {
            Entrances = world.Entrances.Where(e => e.TileX == 71 && e.TileY == 41).ToArray(),
            Vehicles = world.Vehicles! with { EpochY = 660 },
        });
        var target = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Exits);
        Assert.Empty(target.ApproachPoints);
        Assert.Contains("parked vehicle", target.GuidanceRestriction!, StringComparison.OrdinalIgnoreCase);
        var control = new NavigationController();
        control.Handle(NavigationCommand.NextCategory, frame, 0);
        var result = control.Handle(command, frame, 16);
        Assert.False(result.Guiding);
        Assert.False(result.AutoWalking);
        Assert.Contains(result.Speech, s => s.Contains("parked vehicle", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(NavigationCommand.Guide)]
    [InlineData(NavigationCommand.ToggleWalk)]
    public void AStrictlyClearBoundaryRowRemainsUsableWhenNoExtraMarginFits(NavigationCommand command)
    {
        var world = World(0, 0);
        var frame = Build(world with { Entrances = world.Entrances.Where(e => e.TileX == 71 && e.TileY == 41).ToArray() });
        var target = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Exits);
        Assert.Null(target.GuidanceRestriction);
        Assert.NotEmpty(target.ApproachPoints);
        Assert.All(target.ApproachPoints, p => Assert.Equal(664 * 16, p.Y));
        var control = new NavigationController();
        control.Handle(NavigationCommand.NextCategory, frame, 0);
        var result = control.Handle(command, frame, 16);
        Assert.True(result.Guiding);
        Assert.Equal(command == NavigationCommand.ToggleWalk, result.AutoWalking);
    }

    [Fact]
    public void DactylBoardingContactAlsoLeavesClearEntranceRows()
    {
        var world = World(0, 0);
        world = world with
        {
            Motion = world.Motion with { World = 3 },
            Vehicles = world.Vehicles! with
            {
                EpochPresent = false, DactylsPresent = true, DactylX = 1136, DactylY = 648,
                DactylBoardingShape = new(8, 8, 8, 8),
            },
        };
        var target = Assert.Single(Build(world).Targets, t => t.Category == NavigationCategory.Exits);
        Assert.NotEmpty(target.ApproachPoints);
        Assert.All(target.ApproachPoints, p => Assert.True(p.Y >= 672 * 16));
    }

    [Fact]
    public void FlightLandingLeavesAReachableEntranceContactOutsideTheParkedVehicle()
    {
        var world = World(0, 0);
        // Deliberately larger synthetic boarding shapes make all16 contacts
        // unusable if the vehicle lands immediately on the entrance.
        world = world with
        {
            Vehicle = new(1, 2, 3, 4, 2, VehicleKind.Epoch, 0xEF0, 3, 1112, 416, 0xE0),
            Vehicles = world.Vehicles! with { Transport = 2, PartyBoardingShape = new(24, 24, 24, 24), EpochBoardingShape = new(24, 24, 24, 24) },
        };
        var flight = new WorldNavigationSource(new EmptyMemory(), _ => { }).BuildFlight(world, _ => "Dimensional Vortex");
        var exit = Assert.Single(flight.Targets, t => t.Category == NavigationCategory.Exits);
        Assert.NotEmpty(exit.ApproachPoints);
        Assert.All(exit.ApproachPoints, landing =>
        {
            var grounded = world with
            {
                Vehicle = null, Motion = world.Motion with { PixelX = landing.X / 16, PixelY = landing.Y / 16 },
                WalkingEndpoint = new(landing.X / 16, landing.Y / 16),
                Vehicles = world.Vehicles with { Transport = 0, EpochX = landing.X / 16, EpochY = landing.Y / 16 },
            };
            var frame = Build(grounded);
            var entrance = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Exits);
            Assert.Null(entrance.GuidanceRestriction);
            Assert.NotEmpty(entrance.ApproachPoints);
            Assert.Contains(entrance.ApproachPoints, p => Math.Abs(p.X - landing.X) >= 48 * 16 || Math.Abs(p.Y - landing.Y) >= 48 * 16);
            Assert.NotNull(NavigationPathfinder.Find(frame.Graph, frame.Player, entrance.ApproachPoints));
        });
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("flying")]
    [InlineData("unknown")]
    public void UnprovenBoardingContactDoesNotRemoveNativeEntrancePoints(string state)
    {
        var world = World(0, 0);
        var vehicle = world.Vehicles!;
        vehicle = state switch
        {
            "absent" => vehicle with { EpochPresent = false },
            "flying" => vehicle with { Transport = 2 },
            _ => vehicle with { PartyBoardingShape = null },
        };
        var target = Assert.Single(Build(world with { Vehicles = vehicle }).Targets,
            t => t.Category == NavigationCategory.Exits);
        Assert.Equal(16, target.ApproachPoints.Count);
        Assert.Null(target.GuidanceRestriction);
    }

    private static NavigationFrame Build(WorldNavigationSnapshot world) =>
        new WorldNavigationSource(new EmptyMemory(), _ => { }).Build(world, _ => "Dimensional Vortex");

    private static WorldNavigationSnapshot World(int phaseX, int phaseY)
    {
        var exits = (from y in new[] { 41, 42 } from x in new[] { 70, 71 }
            select new WorldEntrance(20 + (y - 41) * 2 + x - 70, x, y, 92, 661, 0, 12, 18, true)
            {
                ContactPoints = [new(x * 16, y * 16), new(x * 16 + 8, y * 16),
                    new(x * 16, y * 16 + 8), new(x * 16 + 8, y * 16 + 8)],
            }).ToArray();
        return new(new(1, 2, 3, 4, 2, 3, 1136 + phaseX, 648 + phaseY),
            new byte[6144], new byte[512], exits, new(0, 0, 24576, 16384), null)
        {
            WalkingEndpoint = new(1136 + phaseX, 648 + phaseY),
            Vehicles = new(true, 1136 + phaseX, 648 + phaseY, true, false, 0, 0, 0, 1, true, false)
            { PartyBoardingShape = new(8, 8, 8, 8), EpochBoardingShape = new(8, 8, 8, 8) },
        };
    }

    private sealed class EmptyMemory : IReadableMemory
    { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
