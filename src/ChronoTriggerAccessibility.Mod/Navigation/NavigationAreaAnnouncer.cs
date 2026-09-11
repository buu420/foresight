using ChronoTriggerAccessibility.Core.Navigation;

namespace ChronoTriggerAccessibility.Mod.Navigation;

public sealed class NavigationAreaAnnouncer(Action<string> speak, Action<string> diagnostic)
{
    private string? scene;
    public void Reset() => scene = null;
    public NavigationFrame? Observe(NavigationFrame? frame)
    {
        if (frame is not { CanNavigate: true } || string.IsNullOrWhiteSpace(frame.AreaName) || scene == frame.Scene) return frame;
        scene = frame.Scene;
        var name = frame.AreaName.Trim().TrimEnd('.');
        diagnostic($"Navigation area entered: {frame.Scene}; {name}.");
        speak(name + ".");
        return frame;
    }
}
