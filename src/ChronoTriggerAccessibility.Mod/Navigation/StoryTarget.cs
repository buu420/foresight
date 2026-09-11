using ChronoTriggerAccessibility.Core.Navigation;

namespace ChronoTriggerAccessibility.Mod.Navigation;

public static class StoryTarget
{
    public static NavigationTarget BindAny(string key, string label, IReadOnlyList<NavigationTarget> available,
        IReadOnlyList<string> ids, NavigationPoint player, string? instruction = null)
    {
        var choices = available.Where(t => ids.Contains(t.Id) && (t.Visible || t.Discovered)).ToArray();
        // Prefer the currently rendered part of a multi-floor scene. Previously visited
        // floors retain their discovery, but cannot displace the current staircase.
        if (choices.Any(t => t.Visible)) choices = choices.Where(t => t.Visible).ToArray();
        var nearest = choices.OrderBy(t => Math.Abs((double)t.Position.X - player.X) +
            Math.Abs((double)t.Position.Y - player.Y)).FirstOrDefault();
        if (nearest is null) return Note("story:" + key, label,
            (instruction is null ? "" : instruction + " ") +
            "The destination has not been discovered in this area yet. Explore the area and press K again.");
        return nearest with { Id = "story:" + key, Label = label, Category = NavigationCategory.StoryEvents,
            ApproachPoints = choices.SelectMany(t => t.ApproachPoints).Distinct().ToArray(), Instruction = instruction };
    }

    public static NavigationTarget Bind(string id, string label, IReadOnlyList<NavigationTarget> available,
        string? instruction = null)
    {
        var target = available.FirstOrDefault(t => t.Id == id && (t.Visible || t.Discovered));
        return target is null
            ? Note("story:" + id, label, (instruction is null ? "" : instruction + " ") +
                "The destination has not been discovered in this area yet. Explore the area and press K again.")
            : target with { Id = "story:" + id, Label = label, Category = NavigationCategory.StoryEvents,
                Instruction = instruction };
    }

    public static NavigationTarget Note(string id, string label, string instruction) =>
        new(id, label, NavigationCategory.StoryEvents, default, [], false, false)
        { IsStoryNote = true, Instruction = instruction };
}
