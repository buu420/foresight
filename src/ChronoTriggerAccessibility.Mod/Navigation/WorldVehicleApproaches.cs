using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>27D360 gives parked-vehicle boarding priority over entrance Confirm.
/// Use the captured opposing3324C/33254/3325C shapes, never the flight shapes.</summary>
internal static class WorldVehicleApproaches
{
    public static bool ClearForEntrance(NavigationPoint point, WorldVehicleState? vehicles)
    {
        if (vehicles is not { Transport: 0, PartyBoardingShape: { } party }) return true;
        // World arrival permits one pixel of error. Keep that whole arrival box
        // outside boarding contact so Confirm still addresses the entrance.
        var arrival = new VehicleContactShape(party.Left + 1, party.Right + 1, party.Top + 1, party.Bottom + 1);
        return !(vehicles.EpochPresent && vehicles.EpochBoardingShape is { } epoch &&
                VehicleContactGeometry.Overlaps(point.X / 16, point.Y / 16, arrival,
                    new(vehicles.EpochX, vehicles.EpochY), epoch)) &&
            !(vehicles.DactylsPresent && vehicles.DactylBoardingShape is { } dactyl &&
                VehicleContactGeometry.Overlaps(point.X / 16, point.Y / 16, arrival,
                    new(vehicles.DactylX, vehicles.DactylY), dactyl));
    }
}
