using ChronoTriggerAccessibility.Core.Navigation;

namespace ChronoTriggerAccessibility.Mod.Navigation;

public static class StoryTarget
{
    public static NavigationTarget BindAny(string key, string label, IReadOnlyList<NavigationTarget> available,
        IReadOnlyList<string> ids, NavigationPoint player, string? instruction = null)
    {
        var choices = available.Where(t => ids.Contains(t.Id)).ToArray();
        // Prefer the currently rendered part of a multi-floor scene. Previously visited
        // floors retain their discovery, but cannot displace the current staircase.
        if (choices.Any(t => t.Visible)) choices = choices.Where(t => t.Visible).ToArray();
        choices = choices.OrderBy(t => Math.Abs((double)t.Position.X - player.X) +
            Math.Abs((double)t.Position.Y - player.Y)).ToArray();
        var nearest = choices.FirstOrDefault();
        if (nearest is null) return Note("story:" + key, label,
            (instruction is null ? "" : instruction + " ") +
            "The mod cannot locate this destination yet.");
        return nearest with { Id = "story:" + key, Label = label, Category = NavigationCategory.StoryEvents,
            GuideAvailable = true,
            // Share the search budget across alternatives. A large exit on another
            // floor must not consume all 64 goals before the current floor is added.
            ApproachPoints = choices.SelectMany(t => t.ApproachPoints.Select((point, rank) => (point, rank)))
                .OrderBy(p => p.rank).Select(p => p.point).Distinct().Take(64).ToArray(), Instruction = instruction };
    }

    public static NavigationTarget Bind(string id, string label, IReadOnlyList<NavigationTarget> available,
        string? instruction = null)
    {
        var target = available.FirstOrDefault(t => t.Id == id);
        return target is null
            ? Note("story:" + id, label, (instruction is null ? "" : instruction + " ") +
                "The mod cannot locate this destination yet.")
            : target with { Id = "story:" + id, Label = label, Category = NavigationCategory.StoryEvents,
                GuideAvailable = true, Instruction = instruction };
    }

    public static NavigationTarget Note(string id, string label, string instruction) =>
        new(id, label, NavigationCategory.StoryEvents, default, [], false, false)
        { IsStoryNote = true, Instruction = instruction };
}
