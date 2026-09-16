using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>
/// Offers a parked vehicle as the next step when the current story objective cannot be
/// reached on foot. Field-only look-ahead distinguishes an interior reached from
/// this era's enabled entrance (fly there) from an interior in another era (use the
/// Epoch's time gauge). Optional objectives retain their native navigation category.
/// </summary>
public sealed class VehicleStoryRouting
{
    public const string EpochId = "vehicle:epoch", DactylId = "vehicle:dactyl";
    public const string EpochLabel = "Epoch", DactylLabel = "Dactyls";
    private readonly Func<int, FieldStoryState, int[], int?> distance;

    public VehicleStoryRouting()
    {
        var router = new SceneConnectionRouter();
        distance = (scene, state, goals) => router.DistanceWithinFields(scene, state, goals);
    }

    /// <summary>Scene-to-goals distance through the catalogue, or null when unreachable;
    /// injectable for callers supplying an equivalent bounded field topology.</summary>
    public VehicleStoryRouting(Func<int, FieldStoryState, int[], int?> distance) => this.distance = distance;

    /// <summary>Era names end in their own period ("600 A.D."); never double it.</summary>
    public static string Sentence(string text) => text.EndsWith('.') ? text : text + ".";

    public static int EraMessageIndex(int world) => world switch
    {
        0 => 106, 1 => 107, 2 => 108, 3 => 109, >= 4 and <= 6 => 110, _ => 0,
    };

    /// <summary>
    /// <paramref name="storyTargets"/> are the full-story results for this world map;
    /// notes whose objective is off this landmass or in another era are rebound to the
    /// nearest reachable vehicle in <paramref name="reachableVehicles"/>. Other targets
    /// are returned unchanged. Root may call this after its own binding step.
    /// </summary>
    public IReadOnlyList<NavigationTarget> Reroute(IReadOnlyList<NavigationTarget> storyTargets, int world,
        FieldStoryState? story, IReadOnlyList<NavigationTarget> reachableVehicles, bool epochWings,
        IReadOnlyDictionary<string, int> worldDestinations, NavigationPoint player, Func<int, string?> label)
    {
        if (story is null || reachableVehicles.Count == 0) return storyTargets;
        var nearest = reachableVehicles.Where(v => v.ApproachPoints.Count != 0)
            .OrderBy(v => Math.Abs((long)v.Position.X - player.X) + Math.Abs((long)v.Position.Y - player.Y)).ToArray();
        if (nearest.Length == 0) return storyTargets;
        var objectives = FullStoryObjectives.Build(496 + world, story);
        var result = storyTargets.ToList();
        foreach (var objective in objectives)
        {
            var index = result.FindIndex(t => t.Id == "story:" + objective.Id);
            if (index >= 0 && !result[index].IsStoryNote) continue;
            var goals = objective.Goals.Select(g => g.Scene).Distinct().ToArray();
            var bound = Bind(objective, goals, world, story, nearest, epochWings, worldDestinations, label);
            if (bound is null) continue;
            if (index >= 0) result[index] = bound;
            else result.Add(bound);
        }
        return result;
    }

    private NavigationTarget? Bind(FullStoryObjective objective, int[] goals, int world,
        FieldStoryState story, NavigationTarget[] nearest, bool epochWings,
        IReadOnlyDictionary<string, int> worldDestinations, Func<int, string?> label)
    {
        var epoch = nearest.FirstOrDefault(v => v.Id == EpochId);
        if (story.Point == 211 && goals.Contains(477) && epoch is not null && epochWings)
            return BindVehicle(epoch, "Board the Epoch and take off to continue the story.");

        // When an objective offers both an End of Time pillar and the destination,
        // the destination is the useful choice for an available time machine.
        var direct = goals.Where(g => g != FullStoryObjectives.EndOfTime).ToArray();
        if (direct.Length != 0) goals = direct;
        var flyer = nearest.FirstOrDefault(v => v.Id == DactylId || v.Id == EpochId && epochWings);
        // Only a currently enabled entrance counts as a local destination. Bounded
        // field look-ahead may not leave that entrance through a Gate and re-enter
        // a different era or a different landmass.
        if (flyer is not null && worldDestinations.Values.Distinct().Any(dest => distance(dest, story, goals) is not null))
        {
            return BindVehicle(flyer, $"The way there is not on this landmass. Board the {flyer.Label} and fly.");
        }
        // Boarding an unmodified Epoch opens the native time gauge automatically.
        // Wings are needed for map flight, never for changing era.
        if (epoch is null) return null;
        if (goals.Any(g => g is 464 or 473))
            return BindVehicle(epoch, "Board the Epoch and use its time gauge to travel to the End of Time.");
        if (goals.Any(g => g is 432 or 472))
            return BindVehicle(epoch, "Board the Epoch and use its time gauge to travel to 1999 A.D.");
        var eras = GameNavigationCatalog.Worlds.Where(e => GameNavigationCatalog.IsFieldScene(e.Destination) &&
                e.WorldId is >= 0 and <= 6 && EraMessageIndex(e.WorldId) != EraMessageIndex(world) &&
                (e.WorldId is not (4 or 5 or 6) || (story.Point <= 203 ? e.WorldId != 6 : e.WorldId == 6)))
            .Select(e => (World: e.WorldId, Distance: distance(e.Destination, story, goals)))
            .Where(e => e.Distance is not null).OrderBy(e => e.Distance).ThenBy(e => e.World).ToArray();
        if (eras.Length == 0) return null;
        var closest = eras.Where(e => e.Distance == eras[0].Distance).Select(e => e.World).ToArray();
        var names = closest.Select(w => label(EraMessageIndex(w))).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToArray();
        var era = names.Length == 0 ? "another era" : string.Join(" or ", names);
        return BindVehicle(epoch, Sentence($"The destination is in {era}") + " Board the Epoch and use its time gauge.");

        NavigationTarget BindVehicle(NavigationTarget vehicle, string instruction) => vehicle with
        {
            Id = "story:" + objective.Id, Label = objective.Label, Category = objective.Category,
            GuideAvailable = true, IsStoryNote = false,
            Instruction = instruction,
            ArrivalInstruction = $"Press Confirm to board the {vehicle.Label}.",
        };
    }
}
