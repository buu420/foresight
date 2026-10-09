using ChronoTriggerAccessibility.Core.Navigation;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Stable scene-local aliases for repeated appearance labels.</summary>
internal sealed class NavigationPeopleLabels
{
    private readonly Dictionary<string, Dictionary<string, string>> aliases = new(StringComparer.OrdinalIgnoreCase);
    public void Reset() => aliases.Clear();

    public void Apply(List<NavigationTarget> targets, HashSet<string> genericIds)
    {
        foreach (var group in targets.Where(t => t.Category == NavigationCategory.People && genericIds.Contains(t.Id))
                     .GroupBy(t => t.Label, StringComparer.OrdinalIgnoreCase))
        {
            if (!aliases.TryGetValue(group.Key, out var names))
            {
                if (group.Select(t => t.Id).Distinct().Count() < 2) continue;
                aliases[group.Key] = names = new(StringComparer.Ordinal);
            }
            foreach (var target in group.OrderBy(t => ActorIndex(t.Id)).ThenBy(t => t.Id, StringComparer.Ordinal))
            {
                if (!names.TryGetValue(target.Id, out var suffix)) names[target.Id] = suffix = Letters(names.Count);
                targets[targets.IndexOf(target)] = target with { Label = $"{target.Label} {suffix}" };
            }
        }
    }

    private static int ActorIndex(string id) =>
        id.Split(':') is ["actor", var index, ..] && int.TryParse(index, out var value) ? value : int.MaxValue;

    private static string Letters(int index)
    {
        var result = "";
        do { result = (char)('A' + index % 26) + result; index = index / 26 - 1; } while (index >= 0);
        return result;
    }
}
