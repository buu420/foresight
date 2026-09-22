using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Mod.Navigation;

public sealed class FieldNavigationSource(IReadableMemory memory, Action<string> diagnostic,
    Func<int, string?>? areaName = null)
{
    private readonly Dictionary<string, NavigationTarget> discovered = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NavigationTarget> guideGeometry = new(StringComparer.Ordinal);
    private readonly FullStoryTargets fullStory = new();
    private string? scene;
    private string? lastFailure;
    private string? lastInventory;
    private bool storyUnavailable;

    public void Reset() { discovered.Clear(); guideGeometry.Clear(); scene = null; lastInventory = null; }

    public NavigationFrame? Capture(nint engine)
    {
        if (!FieldNavigationCapture.TryCapture(memory, (nuint)engine, out var field, out var error)) return Failed(error);
        if (!FieldMapCapture.TryCapture(memory, field, out var map, out error)) return Failed(error);
        if (!FieldEnvironmentCapture.TryViewport(memory, field, out var viewport)) return Failed("Native camera bounds are unavailable.");
        var story = FieldStoryCapture.Capture(memory, field);
        if (!FieldEnvironmentCapture.TryTreasures(memory, field, map, out var treasures,
            includeGuidePickups: field.SceneIdCoherent && GameNavigationCatalog.IsFieldScene(field.SceneId) && story is { Point: >= 3 }))
            return Failed("Native treasure state is unavailable.");
        lastFailure = null;
        if (story is null && !storyUnavailable) diagnostic("Navigation story context is unavailable; field targets remain usable.");
        storyUnavailable = story is null;
        return Build(field, map, viewport, treasures, story);
    }

    public NavigationFrame Build(FieldNavigationSnapshot field, FieldMapSnapshot map, FieldViewport viewport,
        IReadOnlyList<FieldTreasure> treasures, FieldStoryState? story = null)
    {
        var identity = $"{field.Engine:X8}:{field.SceneId}";
        if (scene != identity)
        {
            Reset(); scene = identity;
            diagnostic($"Navigation field: scene={field.SceneId}; engine=0x{field.Engine:X}; map={map.Width}x{map.Height}; layer={map.PlayerLayer}; actors={field.ActorCount}; viewport={viewport}.");
        }
        var graph = new FieldNavigationGraph(map);
        var player = new NavigationPoint(field.LeadPlayer?.FineX ?? 0, field.LeadPlayer?.FineY ?? 0, map.PlayerLayer);
        var targets = new List<NavigationTarget>();
        var storyAnchors = new List<NavigationTarget>();
        var storyCandidates = new List<NavigationTarget>();
        var scriptTerminals = new List<(string Id, int Left, int Top, int Right, int Bottom)>();
        var activeIds = new HashSet<string>();
        var guideActive = field.SceneIdCoherent && GameNavigationCatalog.IsFieldScene(field.SceneId) && story is { Point: >= 3 };
        foreach (var actor in field.Actors)
        {
            if (!actor.IsUsable || !InsideMap(actor) || !actor.IsDrawn || !actor.ClassTagKnown || actor.IsPartyMember || actor.Index == 0 ||
                (actor.ClassTag & FieldNavigationCapture.ClassTagRemovedBit) != 0) continue;
            var description = FieldVisualLabels.Describe(actor);
            var scriptInfo = field.SceneIdCoherent ? GameNavigationCatalog.ActorInfo(field.SceneId, actor) : null;
            var scriptedAction = story is not null && actor.ScriptCallsEnabled &&
                scriptInfo?.Actions.Any(a => a.Available(story)) == true;
            var scriptedContact = scriptedAction &&
                scriptInfo?.Actions.Any(a => a.Touch && a.Available(story) && a.Kind is "Item" or "Warp" or "Progress" or "Switch" or "Terrain") == true;
            // These pickups have an initialization-time gate in addition to the
            // contact handler. Preserve the audited gate when the sprite lingers.
            if ((field.SceneId, actor.Index) is (8, 11) or (439, 15))
                scriptedAction = scriptedContact = field.SceneIdCoherent && EarlyStoryTargets.IsScriptedPickupAvailable(field.SceneId, story, actor);
            // People includes visible characters without a talk action, such as
            // Crono's cat. Objects require activation or an audited script pickup gate.
            if (description.Category == NavigationCategory.Objects && (!actor.ScriptCallsEnabled ||
                !actor.IsActivationCandidate && !scriptedAction &&
                !(field.SceneIdCoherent && EarlyStoryTargets.IsScriptedPickupAvailable(field.SceneId, story, actor)))) continue;
            var position = Position(actor.FineX, actor.FineY);
            var id = $"actor:{actor.Index}:{actor.ClassTag}:{actor.VisualIndex}";
            var label = field.SceneIdCoherent ? OpeningStoryTargets.ActorLabel(field.SceneId, actor) ??
                FutureAreaLabels.ActorLabel(field.SceneId, actor, story) ?? OptionalGuideTargets.ActorLabel(field.SceneId, actor) : null;
            // A stand-in with no name of its own is the same destination twice over: the
            // actor whose script it runs is already offered, and this would arrive at the
            // same spot under a name that tells the player nothing.
            if (label is null && field.SceneIdCoherent && FieldActorProxies.IsProxy(field.SceneId, actor.Index) &&
                field.Actors.Any(owner => InteractionProxy(owner)?.Index == actor.Index)) continue;
            var touchOnly = scriptedContact && scriptInfo?.Actions.Any(a => !a.Touch && a.Available(story)) != true &&
                (field.SceneId, actor.Index) is not ((8, 11) or (439, 15));
            if (scriptedContact && scriptInfo?.Actions.Any(a => a.Touch && a.Kind == "Warp" && a.Available(story)) == true)
                ProtectContactPassage(id, actor);
            Add(id, label ?? description.Label, description.Category, position, touchOnly ? TouchApproach(actor) : ActorApproach(actor, position), viewport.Contains(position.X, position.Y),
                guideAvailable: guideActive && (label is not null || actor.IsActivationCandidate || scriptedAction));
        }
        foreach (var chest in treasures)
        {
            var position = Position(chest.FineX, chest.FineY);
            Add($"chest:{chest.Index}", chest.IsChest ? "Treasure chest" : "Item pickup", NavigationCategory.Objects, position,
                Approach(position), chest.IsChest && TileVisible(chest.FineX / 256, chest.FineY / 256),
                guideAvailable: guideActive);
        }
        if (field.SceneIdCoherent)
        foreach (var actor in field.Actors)
        {
            // These exact script slots are interaction markers for scenery drawn in
            // the map. Their class 7 intentionally has no sprite. Never promote other
            // hidden actors, and use their live coordinates rather than guide positions.
            if (!InsideMap(actor)) continue;
            var label = EarlyStoryTargets.Landmark(field.SceneId, story, actor, field.Actors) ??
                FutureAreaLabels.Landmark(field.SceneId, story, actor);
            var knownTouch = EarlyStoryTargets.IsTouchLandmark(field.SceneId, actor.Index) ||
                FutureAreaLabels.IsTouchLandmark(field.SceneId, actor.Index);
            if (knownTouch && label is null) continue;
            var metadata = GameNavigationCatalog.ActorInfo(field.SceneId, actor);
            var scripted = story is not null && metadata is { Marker: true } && actor.IsUsable && !actor.IsPartyMember && actor.ScriptCallsEnabled
                ? metadata.Actions.Where(a => a.Available(story)).ToArray() : [];
            if (label is null && scripted.Length != 0)
                label = scripted.Any(a => a.Kind == "Item") ? "Item pickup" :
                    scripted.FirstOrDefault(a => a.Kind == "Warp") is { } warp
                        ? GameNavigationCatalog.DestinationLabel(warp.Destination) : "Interactable scenery";
            if (label is null) continue;
            var position = Position(actor.FineX, actor.FineY);
            var touch = knownTouch || metadata is { Touch: true };
            var approaches = touch
                ? TouchApproach(actor)
                : ActorApproach(actor, position);
            if (scripted.Any(a => a.Touch && a.Kind == "Terrain" && a.Copy is not null))
                approaches = FieldTerrainGraph.Contacts(actor, map.PlayerLayer)
                    .Where(p => graph.TryPosition(p.X, p.Y, p.Layer, out var at) && at == p && !graph.IsTerminal(p)).ToArray();
            var scriptedExit = scripted.Any(a => a.Kind == "Warp");
            if (scripted.Any(a => a.Touch && a.Kind == "Warp"))
                ProtectContactPassage($"landmark:{actor.Index}", actor);
            Add($"landmark:{actor.Index}", label, scriptedExit ? NavigationCategory.Exits : NavigationCategory.Objects, position, approaches,
                TileVisible(actor.TileX, actor.TileY), storyOnly: knownTouch || touch && scripted.Length == 0,
                guideAvailable: guideActive && (!touch || scripted.Length != 0));
        }
        if (guideActive && GameNavigationCatalog.ForScene(field.SceneId) is { } sceneInfo)
        foreach (var group in sceneInfo.Regions.Where(r => r.Available(story) &&
                     (r.Kind != "Progress" || r.Value > story!.Point) &&
                     field.Actors.Any(a => a.Index == r.Actor && a.IsUsable && !a.IsPartyMember &&
                         (a.ClassTag & FieldNavigationCapture.ClassTagRemovedBit) == 0)).GroupBy(r => r.Id))
        {
            var region = group.First();
            var goals = new List<NavigationPoint>();
            for (var y = Math.Max(0, region.Top); y <= Math.Min(map.Height - 1, region.Bottom); y++)
            for (var x = Math.Max(0, region.Left); x <= Math.Min(map.Width - 1, region.Right); x++)
                goals.AddRange(At(x * 256 + 128, y * 256 + 128).Where(p => !graph.IsTerminal(p)));
            var points = goals.OrderBy(Distance).Distinct().Take(64).ToArray();
            if (points.Length == 0) continue;
            if (region.Kind == "Warp") scriptTerminals.Add((region.Id, region.Left, region.Top, region.Right, region.Bottom));
            // Encounters are the script's own battle triggers (native D8 under a
            // leader-coordinate guard), so they carry the trigger's location but no
            // party, name or reward. Offer the trigger and leave the fight to the
            // player; the guard that the script sets when it is over removes it.
            var (regionLabel, regionCategory) = region.Kind switch
            {
                "Warp" => (GameNavigationCatalog.DestinationLabel(region.Destination), NavigationCategory.Exits),
                "Switch" => ("Floor trigger", NavigationCategory.Objects),
                "Encounter" => ("Encounter", NavigationCategory.Enemies),
                _ => ("Story event", NavigationCategory.Exits),
            };
            Add(region.Id, regionLabel, regionCategory,
                points[0], points, points.Any(p => TileVisible(p.X / 256, p.Y / 256)),
                storyOnly: region.Kind is not ("Warp" or "Encounter"), guideAvailable: true);
        }
        var exitGroups = new Dictionary<int, List<NavigationPoint>>();
        var exitCellCount = 0;
        var visibleExitCellCount = 0;
        for (var y = 0; y < map.ExitHeight; y++)
        for (var x = 0; x < map.ExitWidth; x++)
        {
            var id = map.ExitCells[y * map.ExitWidth + x];
            if (id >= 128) continue;
            exitCellCount++;
            if (!exitGroups.TryGetValue(id, out var points)) exitGroups[id] = points = [];
            var px = x * 256 + 128; var py = y * 256 + 128;
            points.Add(Position(px, py));
            if (TileVisible(x, y)) visibleExitCellCount++;
        }
        foreach (var (id, points) in exitGroups.OrderBy(entry => entry.Key))
        {
            var key = $"exit:{id}";
            var label = FutureAreaLabels.ExitLabel(field.SceneId, id) ?? OpeningStoryTargets.ExitLabel(field.SceneId, id);
            var optionalLabel = field.SceneIdCoherent ? OptionalGuideAreas.ExitLabel(field.SceneId, id) : null;
            if (label == "Exit" && optionalLabel is not null) label = optionalLabel;
            var catalogLabel = field.SceneIdCoherent ? map.ExitDestinations?.TryGetValue(id, out var destination) == true
                ? GameNavigationCatalog.DestinationLabel(destination) : GameNavigationCatalog.ExitLabel(field.SceneId, id) : null;
            if (label == "Exit" && catalogLabel is not null) label = catalogLabel;
            var optionalGuide = guideActive && (optionalLabel is not null || catalogLabel is not null);
            // Story objectives use the entire current native exit, including tiles
            // outside the camera. Retain goals to keep an active route stable.
            var retainedGuide = guideGeometry.TryGetValue(key, out var priorGuide)
                ? priorGuide.ApproachPoints.Where(p => StillExitGoal(p, id)).ToArray() : [];
            var guideGoals = retainedGuide.Concat(points.SelectMany(ExitApproach).Distinct().OrderBy(Distance))
                .Distinct().Take(64).OrderBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Layer).ToArray();
            // Visibility discovers the destination. Its connected native footprint
            // supplies usable entry points even when the camera clips the stairs.
            var visible = points.Where(p => TileVisible(p.X / 256, p.Y / 256)).ToArray();
            var guide = new NavigationTarget(key, label, NavigationCategory.Exits,
                (visible.Length != 0 ? visible : points.ToArray()).MinBy(Distance), guideGoals,
                visible.Length != 0, discovered.ContainsKey(key) || visible.Length != 0);
            guideGeometry[key] = guide;
            storyCandidates.Add(guide);
            if (visible.Length == 0)
            {
                activeIds.Add(key);
                if (optionalGuide) { targets.Add(guide with { GuideAvailable = true }); continue; }
                if (discovered.TryGetValue(key, out var old)) targets.Add(old with
                {
                    Visible = false, Discovered = true,
                    ApproachPoints = old.ApproachPoints.Where(p => StillExitGoal(p, id)).ToArray(),
                });
                continue;
            }
            var position = visible.OrderBy(Distance).First();
            // Preserve discovered approaches while the exit still owns their cells.
            // Re-ranking a large exit by the moving player evicts a routed goal.
            var retained = discovered.TryGetValue(key, out var knownExit)
                ? knownExit.ApproachPoints.Where(p => StillExitGoal(p, id)).ToArray() : [];
            var footprint = ConnectedExitFootprint(points, visible.Concat(retained));
            var approaches = retained.Concat(footprint.SelectMany(ExitApproach).Distinct().OrderBy(Distance)).Distinct().Take(64)
                .OrderBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Layer).ToArray();
            Add(key, label, NavigationCategory.Exits, position, approaches, true, storyCandidate: false, guideAvailable: optionalGuide);
        }
        if (field.SceneIdCoherent)
        foreach (var passage in FutureScriptedPassage.ForScene(field.SceneId, story, field.Actors))
        {
            // The script predicate supplies the full region for the story route;
            // ordinary Exits still discover the rendered portion independently.
            var goals = new List<NavigationPoint>();
            var allGoals = new List<NavigationPoint>();
            for (var y = Math.Max(0, passage.Top); y <= Math.Min(map.Height - 1, passage.Bottom); y++)
            for (var x = Math.Max(0, passage.Left); x <= Math.Min(map.Width - 1, passage.Right); x++)
            {
                var tileGoals = At(x * 256 + 128, y * 256 + 128).Where(p => !graph.IsTerminal(p)).Distinct().ToArray();
                allGoals.AddRange(tileGoals);
                if (TileVisible(x, y)) goals.AddRange(tileGoals);
            }
            activeIds.Add(passage.Id);
            var retainedGuide = guideGeometry.TryGetValue(passage.Id, out var priorGuide)
                ? priorGuide.ApproachPoints.Where(allGoals.Contains).ToArray() : [];
            var guideGoals = retainedGuide.Concat(allGoals.OrderBy(Distance)).Distinct().Take(64)
                .OrderBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Layer).ToArray();
            if (guideGoals.Length != 0)
            {
                var guide = new NavigationTarget(passage.Id, passage.Label, NavigationCategory.Exits,
                    guideGoals.MinBy(Distance), guideGoals, goals.Count != 0, discovered.ContainsKey(passage.Id) || goals.Count != 0);
                guideGeometry[passage.Id] = guide;
                storyCandidates.Add(guide);
            }
            else guideGeometry.Remove(passage.Id);
            var retained = discovered.TryGetValue(passage.Id, out var previous)
                ? previous.ApproachPoints.Where(p => graph.TryPosition(p.X, p.Y, p.Layer, out var current) &&
                    current == p && !graph.IsTerminal(p)).ToArray() : [];
            var visible = goals.Count != 0;
            var approaches = retained.Concat(goals.OrderBy(Distance)).Distinct().Take(64)
                .OrderBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Layer).ToArray();
            if (approaches.Length == 0) { discovered.Remove(passage.Id); continue; }
            var position = goals.Count != 0 ? goals.MinBy(Distance) : previous!.Position;
            var target = new NavigationTarget(passage.Id, passage.Label, NavigationCategory.Exits, position,
                approaches, visible, true);
            discovered[passage.Id] = target;
            (passage.StoryOnly ? storyAnchors : targets).Add(target);
        }
        foreach (var id in discovered.Keys.Where(id => !activeIds.Contains(id)).ToArray()) discovered.Remove(id);
        foreach (var id in guideGeometry.Keys.Where(id => !activeIds.Contains(id)).ToArray()) guideGeometry.Remove(id);
        if (field.SceneIdCoherent)
        {
            var available = storyCandidates.ToArray();
            targets.AddRange(OpeningStoryTargets.Build(field.SceneId, story, available));
            targets.AddRange(EarlyStoryTargets.Build(field.SceneId, story, available, player));
            if (story is not { Point: >= FullStoryObjectives.FirstPoint })
                targets.AddRange(FutureStoryTargets.Build(field.SceneId, story, available, player));
            targets.AddRange(fullStory.Build(field.SceneId, story, available, player, fieldDestinations: map.ExitDestinations));
        }
        ReportInventory();
        return new(identity, field.SceneIdCoherent && field.ControlFlag != 0 && field.InputMode == 0 &&
            field.LeadPlayer is { IsUsable: true, IsDrawn: true } && !map.TransitionPending,
            player, targets.AsReadOnly(), guideActive
                ? FieldLandingGraph.Create(
                    FieldTerrainGraph.Create(map, field.Actors, story!, GameNavigationCatalog.ForScene(field.SceneId)!, scriptTerminals),
                    map, GameNavigationCatalog.ForScene(field.SceneId))
                : scriptTerminals.Count == 0 ? graph : new ScriptPassageGraph(graph, scriptTerminals), NavigationUnits.LocalStep)
            { AreaName = areaName?.Invoke(field.SceneId) ?? GameNavigationCatalog.AreaName(field.SceneId) };

        void ReportInventory()
        {
            var inventory = $"people={targets.Count(t => t.Category == NavigationCategory.People)}; " +
                $"exits={targets.Count(t => t.Category == NavigationCategory.Exits)}; " +
                $"objects={targets.Count(t => t.Category == NavigationCategory.Objects)}; " +
                $"storyEvents={targets.Count(t => t.Category == NavigationCategory.StoryEvents)}; " +
                $"enemies={targets.Count(t => t.Category == NavigationCategory.Enemies)}; " +
                $"actors={field.Actors.Count}; usable={field.Actors.Count(a => a.IsUsable)}; " +
                $"drawn={field.Actors.Count(a => a.IsDrawn)}; activationCandidates={field.Actors.Count(a => a.IsActivationCandidate)}; " +
                $"exitCells={exitCellCount}; visibleExitCells={visibleExitCellCount}; " +
                $"renderedChests={treasures.Count(t => t.IsChest)}; guidePickups={treasures.Count(t => !t.IsChest)}; storyPoint={story?.Point.ToString() ?? "unknown"}; " +
                $"motherIntroduced={story?.MotherIntroducedFriend.ToString() ?? "unknown"}; " +
                $"storyFlags={string.Join(",", FieldStoryCapture.ObjectiveGlobalIndices.Select(index =>
                    $"{index:X}={story?.Global(index)?.ToString("X2") ?? "?"}"))}";
            if (inventory == lastInventory) return;
            lastInventory = inventory;
            diagnostic($"Navigation inventory: scene={field.SceneId}; {inventory}; viewport={viewport}.");
            // Raw field facts stay in the diagnostic log. Bound the detail and only
            // emit it when inventory changes, including the first read of an area.
            diagnostic("Navigation actor facts: " + string.Join(" | ", field.Actors.Take(32).Select(a =>
                $"id={a.Index},class=0x{a.ClassTag:X},visual=0x{a.VisualIndex:X},draw=0x{a.DrawMode:X}," +
                $"loaded={a.LoadedFlag},usable={a.IsUsable},party={a.IsPartyMember}," +
                $"flag152={a.ActivationEnabled},field20={a.ActivationBinding},scriptCalls={a.ScriptCallsEnabled},pos=({a.FineX},{a.FineY})")) + ".");
        }

        // Scripts park retired actors at tile FF,FF without necessarily clearing
        // their draw mode. Such actors must not keep their last discovered position.
        bool InsideMap(FieldActorSnapshot actor) => actor.FineX >= 0 && actor.FineY >= 0 &&
            actor.FineX / 256 < map.Width && actor.FineY / 256 < map.Height;

        NavigationPoint Position(int x, int y)
        {
            if (x < 0 || y < 0 || x / 256 >= map.Width || y / 256 >= map.Height) return new(x, y, player.Layer);
            var index = y / 256 * map.Width + x / 256;
            var region = FieldCollisionRules.Region(map.CollisionShapes[index], map.CollisionLayers[index], x, y);
            return new(x, y, region.NeutralMask == 0 && region.Layer != 0 ? region.Layer : player.Layer);
        }
        bool TileVisible(int x, int y) => viewport.Intersects(x * 256, y * 256, (x + 1) * 256, (y + 1) * 256);
        double Distance(NavigationPoint p) => Math.Abs((double)p.X - player.X) + Math.Abs((double)p.Y - player.Y);
        bool StillExitGoal(NavigationPoint p, int id) => graph.ExitAt(p.X, p.Y) == id &&
            graph.TryPosition(p.X, p.Y, p.Layer, out var current) && current == p;
        IEnumerable<NavigationPoint> At(int x, int y)
        {
            for (var layer = 1; layer <= 3; layer++)
                if (graph.TryPosition(x, y, layer, out var point)) yield return point;
        }
        /// <summary>Resolve an audited forwarding actor only while both native actors
        /// match their identities and permit their scripts to run.</summary>
        FieldActorSnapshot? InteractionProxy(FieldActorSnapshot actor)
        {
            if (!field.SceneIdCoherent || FieldActorProxies.ProxyFor(field.SceneId, actor.Index) is not { } id ||
                !actor.IsUsable || !actor.IsDrawn || !InsideMap(actor) || actor.IsPartyMember || !actor.ScriptCallsEnabled ||
                GameNavigationCatalog.ActorInfo(field.SceneId, actor) is null) return null;
            return field.Actors.FirstOrDefault(a => a.Index == id && a.IsUsable && a.IsDrawn && InsideMap(a) &&
                !a.IsPartyMember && a.ScriptCallsEnabled && (a.ClassTag & FieldNavigationCapture.ClassTagRemovedBit) == 0 &&
                GameNavigationCatalog.ActorInfo(field.SceneId, a)?.Id == id);
        }
        /// <summary>One step away may be on the inaccessible side of a counter.
        /// Add in-range standing positions for actors; chests keep their separate rule.</summary>
        IReadOnlyList<NavigationPoint> ActorApproach(FieldActorSnapshot actor, NavigationPoint p)
        {
            var direct = Approach(p);
            // The sprite the player recognises is not always the one the game lets them
            // act on. Where another actor exists only to run this one's script, its own
            // standing room counts too, so long as it is really there and really itself.
            var proxy = InteractionProxy(actor);
            // Fenced on some side, so one step out may all be the wrong side of a
            // counter or hedge. Widen to everywhere the confirm handler would still
            // accept, and let the search pick whichever of them it can actually walk
            // to. An actor with all four steps open and no stand-in pays nothing.
            if (direct.Count == 4 && proxy is null) return direct;
            var widened = direct.Concat(ConfirmApproach(actor.FineX, actor.FineY));
            if (proxy is not null) widened = widened.Concat(ConfirmApproach(proxy.FineX, proxy.FineY));
            return widened.Distinct()
                .OrderBy(point => Math.Abs(point.X - p.X) + Math.Abs(point.Y - p.Y)).Take(64).ToArray();
        }
        IReadOnlyList<NavigationPoint> Approach(NavigationPoint p)
        {
            var x = (p.X + 32) / 64 * 64; var y = (p.Y + 32) / 64 * 64;
            return new[] { (x - 256, y), (x + 256, y), (x, y - 256), (x, y + 256) }
                .SelectMany(pair => At(pair.Item1, pair.Item2)).Where(point => !graph.IsTerminal(point))
                .Distinct().ToArray();
        }
        /// <summary>Lattice points from which the game would accept a confirm on an actor
        /// at these exact coordinates, allowing for the controller calling the walk over as
        /// soon as the player is inside its arrival box. Judging a rounded position instead
        /// would hand out goals up to a rounding and two tolerances too far.</summary>
        IReadOnlyList<NavigationPoint> ConfirmApproach(int actorX, int actorY)
        {
            var found = new List<NavigationPoint>();
            var reach = FieldInteractionRange.Along + NavigationUnits.LocalStep / 8;
            for (var py = (actorY - reach) / 64 * 64; py <= actorY + reach; py += 64)
            for (var px = (actorX - reach) / 64 * 64; px <= actorX + reach; px += 64)
            {
                if (px < 0 || py < 0) continue;
                if (!FieldInteractionRange.ReachesWithin(px, py, actorX, actorY, NavigationUnits.LocalStep / 8)) continue;
                found.AddRange(At(px, py).Where(point => !graph.IsTerminal(point)));
            }
            return found;
        }
        IReadOnlyList<NavigationPoint> TouchApproach(FieldActorSnapshot actor)
        {
            // Scene 8's left pod marker sits at the bottom of a blocked map tile.
            // Its native contact reaches the floor below it. Aim eight pixels
            // below the live marker, aligned to the four-pixel routing lattice.
            // Other touch landmarks retain their separately audited tile goals.
            var (x, y) = (field.SceneId, actor.Index) == (8, 12)
                ? ((actor.FineX + 32) / 64 * 64, (actor.FineY + 128 + 32) / 64 * 64)
                : (actor.TileX * 256 + 128, actor.TileY * 256 + 128);
            return At(x, y).Where(p => !graph.IsTerminal(p)).Distinct().ToArray();
        }
        void ProtectContactPassage(string id, FieldActorSnapshot actor)
        {
            foreach (var point in TouchApproach(actor))
            {
                var (x, y) = (point.X / 256, point.Y / 256);
                var footprint = (id, x, y, x, y);
                if (!scriptTerminals.Contains(footprint)) scriptTerminals.Add(footprint);
            }
        }
        IEnumerable<NavigationPoint> ExitApproach(NavigationPoint p)
        {
            // Every lattice node of the cell, its own boundary included. A one-tile
            // doorway leaves that boundary node as the only one the leading-corner
            // probes allow, so skipping it made the goals unreachable by construction
            // and five of Manoria's six doors reported no route. Stopping short of the
            // cell is prevented by the arrival rule instead, which asks the graph
            // whether the player is actually in it.
            for (var x = p.X / 256 * 256; x < p.X / 256 * 256 + 256; x += 64)
            for (var y = p.Y / 256 * 256; y < p.Y / 256 * 256 + 256; y += 64)
                foreach (var point in At(x, y)) yield return point;
        }
        void Add(string id, string label, NavigationCategory category, NavigationPoint position,
            IReadOnlyList<NavigationPoint> approaches, bool visible, bool storyOnly = false, bool storyCandidate = true,
            bool guideAvailable = false)
        {
            activeIds.Add(id);
            if (storyCandidate) storyCandidates.Add(new(id, label, category, position, approaches, visible,
                visible || discovered.ContainsKey(id)));
            var output = storyOnly ? storyAnchors : targets;
            if (visible || guideAvailable)
            {
                var target = new NavigationTarget(id, label, category, position, approaches, visible,
                    visible || discovered.ContainsKey(id)) { GuideAvailable = guideAvailable };
                if (visible) discovered[id] = target;
                output.Add(target);
            }
            else if (discovered.TryGetValue(id, out var known)) output.Add(known with { Visible = false, Discovered = true });
        }
    }

    private static IEnumerable<NavigationPoint> ConnectedExitFootprint(IEnumerable<NavigationPoint> cells,
        IEnumerable<NavigationPoint> discoveredPoints)
    {
        // A disconnected reuse of an exit id must be seen separately. Flood only
        // through cardinally adjacent cells belonging to this same native exit.
        var remaining = cells.ToDictionary(p => (p.X / 256, p.Y / 256));
        var pending = new Queue<(int X, int Y)>(discoveredPoints.Select(p => (p.X / 256, p.Y / 256)).Distinct());
        while (pending.TryDequeue(out var tile))
        {
            if (!remaining.Remove(tile, out var point)) continue;
            yield return point;
            pending.Enqueue((tile.X - 1, tile.Y));
            pending.Enqueue((tile.X + 1, tile.Y));
            pending.Enqueue((tile.X, tile.Y - 1));
            pending.Enqueue((tile.X, tile.Y + 1));
        }
    }

    private NavigationFrame? Failed(string message)
    {
        if (lastFailure != message) diagnostic($"Navigation capture unavailable: {message}");
        lastFailure = message;
        return null;
    }
}
