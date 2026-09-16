using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Chooses the next native passage toward a scene. This never supplies
/// movement coordinates: those must come from the current collision-checked frame.</summary>
public sealed class SceneConnectionRouter
{
    private sealed record Link(int From, int To, string? Target, int Actor = -1, int Cost = 1,
        int NativeDestination = -1);
    private string? stateKey;
    private bool antiquitySkyAvailable;
    private Link[] links = [];
    private readonly Dictionary<string, IReadOnlyDictionary<int, int>> distanceCache = [];

    public IReadOnlyList<NavigationTarget> Next(int scene, FieldStoryState state,
        IEnumerable<int> goalScenes, IReadOnlyList<NavigationTarget> available,
        IReadOnlyDictionary<string, int>? liveWorldDestinations = null,
        IReadOnlyDictionary<int, int>? liveFieldDestinations = null, bool fieldOnly = false)
    {
        var goals = goalScenes.Distinct().Order().ToArray();
        if (goals.Length == 0 || goals.Contains(scene)) return [];
        Update(state);
        // A missing forward passage must not send the player out and back into
        // this room. Evaluate each continuation without traversing this scene.
        var distance = Distances(goals, fieldOnly, scene);
        var candidates = new List<(NavigationTarget Target, int Distance)>();
        if (liveWorldDestinations is not null)
        {
            foreach (var target in available)
                if (liveWorldDestinations.TryGetValue(target.Id, out var destination) && distance.TryGetValue(destination, out var remaining))
                    candidates.Add((target, remaining));
        }
        else
        {
            var current = links.Where(l => l.From == scene);
            if (liveFieldDestinations is not null)
                current = current.Where(l => l.Target?.StartsWith("exit:") != true)
                    .Concat(liveFieldDestinations.Where(p => Exists(p.Value))
                        .Select(p => new Link(scene, p.Value, $"exit:{p.Key}")));
            foreach (var link in current)
            {
                if (!distance.TryGetValue(link.To, out var remaining)) continue;
                // Look-ahead may ignore room-local values. The current passage
                // must satisfy every predicate and exist in the captured frame.
                if (!CurrentlyAllowed(scene, link, state)) continue;
                foreach (var target in available.Where(t => link.Target == t.Id || link.Actor >= 0 &&
                             (t.Id == $"landmark:{link.Actor}" || t.Id.StartsWith($"actor:{link.Actor}:"))))
                    candidates.Add((target, remaining + link.Cost));
            }
        }
        if (candidates.Count == 0) return [];
        var best = candidates.Min(c => c.Distance);
        return candidates.Where(c => c.Distance == best).Select(c => c.Target).DistinctBy(t => t.Id).ToArray();
    }

    public int? Distance(int scene, FieldStoryState state, params int[] goals)
    {
        Update(state);
        return Distances(goals).TryGetValue(scene, out var value) ? value : null;
    }

    /// <summary>Reach an interior without leaving this area through a world map,
    /// time gate, or vehicle cinematic. Antiquity's surface/sky Land Bridges are
    /// allowed because they do not change era. Used to associate destinations with eras.</summary>
    public int? DistanceWithinFields(int scene, FieldStoryState state, params int[] goals)
    {
        Update(state);
        return Distances(goals, true).TryGetValue(scene, out var value) ? value : null;
    }

    private void Update(FieldStoryState state)
    {
        var key = state.Point + ":" + string.Join(',', state.Globals.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}")) +
            ":" + string.Join(',', state.Extended.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}")) +
            ":" + (state.Inventory is null ? "unknown" : string.Join(',', state.Inventory.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}")));
        key += ":" + state.Gold + ":" + (state.Party is null ? "unknown" : string.Join(',', state.Party));
        if (key == stateKey) return;
        stateKey = key;
        antiquitySkyAvailable = state.Point < 204;
        distanceCache.Clear();
        var result = new List<Link>();
        foreach (var scene in GameNavigationCatalog.Scenes)
        {
            result.AddRange(scene.Exits.Where(e => Exists(e.Destination)).Select(e => new Link(scene.Id, e.Destination, $"exit:{e.Id}")));
            foreach (var actor in scene.Actors)
                result.AddRange(actor.Actions.Where(a => a.Kind == "Warp" && Exists(a.Destination) && a.Available(state, local: false))
                    .Select(a => new Link(scene.Id, ContinuedDestination(scene.Id, actor.Id, a.Destination, state),
                        null, actor.Id, 2, a.Destination)));
            result.AddRange(scene.Regions.Where(r => r.Kind == "Warp" && Exists(r.Destination) && r.Available(state, local: false))
                .Select(r => new Link(scene.Id, r.Destination, r.Id, Cost: 2)));
        }
        // These are look-ahead links only. Actual world-map targets always come
        // from the currently enabled native entrance table and connected land.
        result.AddRange(GameNavigationCatalog.Worlds.Where(w => GameNavigationCatalog.IsFieldScene(w.Destination))
            .Select(w => new Link(496 + w.WorldId, w.Destination, null, Cost: 4)));
        // Atel_0340 actor0 startup runs the conveyor battles, then E1 at
        // packet offset0249 transfers to scene250. There is no player-controlled
        // exit inside scene126: this is look-ahead only, never a walking target.
        result.Add(new Link(126, 250, null, Cost: 2));
        // Atel0224 actor11 finishes the mandatory elevator battles and DF at
        // packet05A5 enters418. Boarding still requires scene411's live actor10.
        result.Add(new Link(416, 418, null, Cost: 2));
        // Black Omen lift cinematics (Atel0456/0457 actor0, E0 at0275/0286)
        // return to99 after their battles. They change G1A7's landing bits,
        // so the eventual actor1 disembark edge cannot be tested against the
        // pre-ride flags. Include those proven continuations for look-ahead;
        // no link here creates a current-room target. Scene99's actual motor
        // and door still bind only through live actors and their native guards.
        result.Add(new Link(100, 99, null, Cost: 2));
        result.Add(new Link(101, 99, null, Cost: 2));
        result.Add(new Link(100, 313, null, Cost: 4)); // 01 ->02, actor1 PC01E0
        result.Add(new Link(100, 98, null, Cost: 4));  // 08 ->04, actor1 PC01F9
        result.Add(new Link(101, 98, null, Cost: 4));  // 02 ->01, actor1 PC01D2
        result.Add(new Link(101, 324, null, Cost: 4)); // 04 ->08, actor1 PC01EE
        // Atel0408 actor8 DF0319 and Atel0401 actor0 DF016B follow the
        // compulsory Zeal/Mammon battles. Spatial extraction stops at battle.
        result.Add(new Link(451, 422, null, Cost: 2));
        result.Add(new Link(422, 107, null, Cost: 2));
        links = result.Where(l => l.From != l.To).Distinct().ToArray();
    }

    private static bool CurrentlyAllowed(int scene, Link link, FieldStoryState state)
    {
        var info = GameNavigationCatalog.ForScene(scene);
        if (info is null) return false;
        if (link.Actor >= 0)
            return info.Actors.Any(a => a.Id == link.Actor && a.Actions.Any(action =>
                action.Kind == "Warp" && action.Destination == link.NativeDestination && action.Available(state)));
        if (link.Target?.StartsWith("script-region:") == true)
            return info.Regions.Any(r => r.Id == link.Target && r.Available(state));
        return true;
    }

    private static int ContinuedDestination(int scene, int actor, int destination, FieldStoryState state)
    {
        // Atel0206 actor8 is the portal, not Dalton (actor10). Confirm sets
        // G1F1:40 and transfers to500. Event_0004 then runs the underwater
        // cinematic and its mapjump at0370 enters404. Connecting500 itself
        // would incorrectly offer an Ocean Palace entrance anywhere outside.
        return scene == 334 && actor == 8 && destination == 500 && state.Point is >= 189 and < 204
            ? 404 : destination;
    }

    private IReadOnlyDictionary<int, int> Distances(IEnumerable<int> goals, bool fieldOnly = false,
        int? avoidScene = null)
    {
        var goalArray = goals.Distinct().Order().ToArray();
        var key = (fieldOnly ? "fields:" : "all:") + avoidScene + ":" + string.Join(',', goalArray);
        if (distanceCache.TryGetValue(key, out var cached)) return cached;
        bool WithinEra(int node) => GameNavigationCatalog.IsFieldScene(node) ||
            node is 500 or 501 && stateKey is not null && antiquitySkyAvailable;
        var eligible = fieldOnly ? links.Where(l =>
            WithinEra(l.From) && WithinEra(l.To) &&
            !CrossesTimeGate(l.From, l.To) &&
            l.From is not (464 or 432 or 472 or 477) &&
            (l.To is not (464 or 432 or 472 or 477) || goalArray.Contains(l.To))) : links;
        var reverse = eligible.Where(l => l.From != avoidScene).GroupBy(l => l.To)
            .ToDictionary(g => g.Key, g => g.ToArray());
        var distances = new Dictionary<int, int>();
        var pending = new PriorityQueue<int, int>();
        foreach (var goal in goalArray) { distances[goal] = 0; pending.Enqueue(goal, 0); }
        while (pending.TryDequeue(out var node, out var cost))
        {
            if (distances[node] != cost || !reverse.TryGetValue(node, out var incoming)) continue;
            foreach (var link in incoming)
            {
                var next = cost + link.Cost;
                if (distances.TryGetValue(link.From, out var prior) && prior <= next) continue;
                distances[link.From] = next;
                pending.Enqueue(link.From, next);
            }
        }
        distanceCache[key] = distances;
        return distances;
    }

    // Early story Gates travel directly before their End of Time pillars open.
    // Their old script branches must not assign an interior to a different era.
    private static bool CrossesTimeGate(int from, int to) => (from, to) is
        (8, 113) or (113, 8) or (20, 208) or (208, 20) or
        (307, 351) or (351, 307) or (83, 10) or (8, 467);

    private static bool Exists(int scene) => GameNavigationCatalog.IsFieldScene(scene) || scene is >= 496 and <= 503;
}
