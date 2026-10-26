using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Publishes a visible world clue after area/movement/vehicle feedback.</summary>
public sealed class WorldNavigationObserver(WorldNavigationSource world, NavigationAreaAnnouncer areas, VehiclePromptAnnouncer? prompts = null)
{
    private NavigationFrame? worldFrame;
    public NavigationFrame? Field(NavigationFrame? frame)
    {
        if (frame is not null) world.ObserveField();
        worldFrame = null;
        return areas.Observe(frame);
    }
    public NavigationFrame? World(NavigationFrame? frame)
    {
        worldFrame = areas.Observe(frame);
        return worldFrame;
    }
    public void CompleteTick(nint context, WorldVehicleState? vehicles, int playerX = -1, int playerY = -1)
    {
        var observed = worldFrame;
        worldFrame = null;
        prompts?.Observe(context, vehicles, playerX, playerY);
        world.PublishDescriptions(observed);
    }
}
