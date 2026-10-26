using ChronoTriggerAccessibility.Core.Navigation;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Orders area speech before a visible world clue and observes field re-entry.</summary>
public sealed class WorldNavigationObserver(WorldNavigationSource world, NavigationAreaAnnouncer areas)
{
    public NavigationFrame? Field(NavigationFrame? frame)
    {
        if (frame is not null) world.ObserveField();
        return areas.Observe(frame);
    }
    public NavigationFrame? World(NavigationFrame? frame)
    {
        var observed = areas.Observe(frame);
        world.PublishDescriptions(observed);
        return observed;
    }
}
