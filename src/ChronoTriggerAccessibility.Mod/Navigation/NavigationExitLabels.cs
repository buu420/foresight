using ChronoTriggerAccessibility.Core.Navigation;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Distinguish repeated exit names without changing their targets or
/// routes. Remember aliases for the current scene so movement, native record
/// order and disappearing exits cannot rename a surviving destination.</summary>
internal sealed class NavigationExitLabels
{
    private readonly Dictionary<string, Dictionary<string, string>> aliases = new(StringComparer.OrdinalIgnoreCase);

    public void Reset() => aliases.Clear();

    public void Apply(List<NavigationTarget> targets)
    {
        foreach (var group in targets.Where(t => t.Category == NavigationCategory.Exits).GroupBy(t => t.Label, StringComparer.OrdinalIgnoreCase))
        {
            if (!aliases.TryGetValue(group.Key, out var names))
            {
                if (group.Select(t => t.Id).Distinct().Count() < 2) continue;
                aliases[group.Key] = names = new(StringComparer.Ordinal);
            }
            foreach (var target in group.OrderBy(t => t.Id, StringComparer.Ordinal))
            {
                if (!names.TryGetValue(target.Id, out var suffix)) names[target.Id] = suffix = Letters(names.Count);
                var label = string.Equals(group.Key, "Exit", StringComparison.OrdinalIgnoreCase)
                    ? $"Exit {suffix}" : $"{target.Label}, exit {suffix}";
                targets[targets.IndexOf(target)] = target with { Label = label };
            }
        }
    }

    private static string Letters(int index)
    {
        var result = "";
        do
        {
            result = (char)('A' + index % 26) + result;
            index = index / 26 - 1;
        } while (index >= 0);
        return result;
    }
}
