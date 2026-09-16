using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Bind chapter/quest facts to the current native frame. A guide never
/// gets to invent a coordinate or bypass the local collision map.</summary>
public sealed class FullStoryTargets
{
    private readonly SceneConnectionRouter router = new();

    public IReadOnlyList<NavigationTarget> Build(int scene, FieldStoryState? state,
        IReadOnlyList<NavigationTarget> available, NavigationPoint player,
        IReadOnlyDictionary<string, int>? worldDestinations = null,
        IReadOnlyDictionary<int, int>? fieldDestinations = null, bool fieldOnly = false)
    {
        if (state is null || state.Point < FullStoryObjectives.FirstPoint) return [];
        var result = new List<NavigationTarget>();
        foreach (var chapter in FullStoryObjectives.Build(scene, state))
        {
            var objective = OceanPalaceRoutes.ForRoom(chapter, scene, player, state);
            // A pillar is an alternative way to reach its destination, not the
            // intended era itself. Vehicle look-ahead must evaluate that destination.
            var goals = fieldOnly && objective.Goals.Any(g => g.Scene != FullStoryObjectives.EndOfTime)
                ? objective.Goals.Where(g => g.Scene != FullStoryObjectives.EndOfTime).ToArray() : objective.Goals;
            var local = goals.Where(g => g.Scene == scene).ToArray();
            var candidates = available.Where(t => local.Any(g => g.Targets.Contains(t.Id) ||
                g.Actors.Any(actor => ActorTarget(t.Id, actor)))).ToArray();
            // An entering-scene objective can lead into a contact-triggered story
            // sequence. Its native spatial predicate supplies the next approach.
            if (candidates.Length == 0 && local.Any(g => g.Actors.Length == 0 && g.Targets.Length == 0))
                candidates = ProgressTargets(scene, state, available);
            if (candidates.Length == 0)
            {
                var onward = goals.Where(g => g.Scene != scene).Select(g => g.Scene);
                candidates = router.Next(scene, state, onward, available, worldDestinations, fieldDestinations, fieldOnly).ToArray();
            }
            if (candidates.Length == 0)
            {
                // Optional guide entries cannot become non-spatial items in the
                // people/objects lists. The native local targets remain listed.
                if (objective.Category == NavigationCategory.StoryEvents)
                    result.Add(StoryTarget.Note("story:" + objective.Id, objective.Label,
                        objective.Instruction ?? (local.Length != 0
                            ? "Continue the interaction here. Nearby people and objects are available in their navigation categories."
                            : "The next connection is not currently available.")));
                continue;
            }
            var instruction = objective.Instruction;
            if (local.Length == 0 && candidates[0].Label is { Length: > 0 } next)
                instruction = $"Next passage: {next}." + (instruction is null ? "" : " " + instruction);
            var bound = StoryTarget.BindAny(objective.Id, objective.Label, candidates,
                candidates.Select(t => t.Id).ToArray(), player, instruction);
            result.Add(bound with { Category = objective.Category });
        }
        return result;
    }

    private static bool ActorTarget(string id, int actor) => id == $"landmark:{actor}" ||
        id.StartsWith($"actor:{actor}:") || id.StartsWith($"script-region:{actor}:") ||
        id.StartsWith($"script-action:{actor}:") || id.StartsWith($"script-switch:{actor}:") ||
        id.StartsWith($"script-encounter:{actor}:") || id.StartsWith($"script-terrain:{actor}:");

    private static NavigationTarget[] ProgressTargets(int scene, FieldStoryState state,
        IReadOnlyList<NavigationTarget> available)
    {
        var metadata = GameNavigationCatalog.ForScene(scene);
        if (metadata is null) return [];
        var possibilities = new List<(NavigationTarget Target, int Value)>();
        foreach (var region in metadata.Regions.Where(r => r.Kind == "Progress" && r.Value > state.Point && r.Available(state)))
            possibilities.AddRange(available.Where(t => t.Id == region.Id).Select(t => (t, region.Value)));
        foreach (var actor in metadata.Actors)
        foreach (var action in actor.Actions.Where(a => a.Kind == "Progress" && a.Value > state.Point && a.Available(state)))
            possibilities.AddRange(available.Where(t => ActorTarget(t.Id, actor.Id)).Select(t => (t, action.Value)));
        if (possibilities.Count == 0)
            return metadata.Regions.Where(r => r.Kind == "Encounter" && r.Available(state))
                .SelectMany(r => available.Where(t => t.Id == r.Id)).DistinctBy(t => t.Id).ToArray();
        var next = possibilities.Min(p => p.Value);
        return possibilities.Where(p => p.Value == next).Select(p => p.Target).DistinctBy(t => t.Id).ToArray();
    }
}
