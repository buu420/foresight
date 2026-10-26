using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>27D360 gives parked-vehicle boarding priority over entrance Confirm.
/// Use the captured opposing3324C/33254/3325C shapes, never the flight shapes.</summary>
internal static class WorldVehicleApproaches
{
    public static bool ClearForEntrance(NavigationPoint point, WorldVehicleState? vehicles, int margin = 1)
    {
        if (vehicles is not { Transport: 0, PartyBoardingShape: { } party }) return true;
        // World arrival permits one pixel of error. Keep that whole arrival box
        // outside boarding contact so Confirm still addresses the entrance.
        var arrival = margin == 0 ? party :
            new VehicleContactShape(party.Left + margin, party.Right + margin, party.Top + margin, party.Bottom + margin);
        return !(vehicles.EpochPresent && vehicles.EpochBoardingShape is { } epoch &&
                VehicleContactGeometry.Overlaps(point.X / 16, point.Y / 16, arrival,
                    new(vehicles.EpochX, vehicles.EpochY), epoch)) &&
            !(vehicles.DactylsPresent && vehicles.DactylBoardingShape is { } dactyl &&
                VehicleContactGeometry.Overlaps(point.X / 16, point.Y / 16, arrival,
                    new(vehicles.DactylX, vehicles.DactylY), dactyl));
    }

    public static WorldVehicleState? AtLanding(NavigationPoint point, VehicleKind kind, WorldVehicleState? vehicles) =>
        vehicles is null ? null : kind == VehicleKind.Epoch
            ? vehicles with { Transport = 0, EpochPresent = true, EpochX = point.X / 16, EpochY = point.Y / 16 }
            : vehicles with { Transport = 0, DactylsPresent = true, DactylX = point.X / 16, DactylY = point.Y / 16 };
}
