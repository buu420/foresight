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
        var frame = Build(world with { Entrances = world.Entrances.Where(e => e.TileX == 71 && e.TileY == 41).ToArray() });
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
