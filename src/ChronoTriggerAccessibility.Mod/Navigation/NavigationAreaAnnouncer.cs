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
        // Native unknown-name marks carry meaning even when punctuation is not spoken.
        var spokenName = name.Any(c => c is '?' or '？' or '¿') &&
            name.All(c => char.IsWhiteSpace(c) || c is '?' or '？' or '¿' or '.' or '…')
            ? "Unidentified area" : name;
        speak(spokenName + "." + (frame.Targets.Any(t => t.Id == DeathPeakClimbAssist.TargetId && t.GuideAvailable)
            ? " Shelter assist is first in Exits." : ""));
        return frame;
    }
}
