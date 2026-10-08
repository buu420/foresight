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
    private readonly NavigationExitLabels exitLabels = new();
    private string? scene;
    private string? lastFailure;
    private string? lastInventory;
    private bool storyUnavailable;

    public void Reset() { discovered.Clear(); guideGeometry.Clear(); exitLabels.Reset(); scene = null; lastInventory = null; }

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
        return Build(field, map, viewport, treasures, story, TreasureGrid(field));
    }

    /// <summary>The whole chest grid 179690 builds at engine+E44 (end E48, width E50, height E54):
    /// 179D90 treats any byte under 0x80 as a treasure, opened or not. Same bounds as the
    /// treasure capture; null when any part is unreadable or inconsistent.</summary>
    private Func<int, int, bool>? TreasureGrid(FieldNavigationSnapshot field)
    {
        Span<byte> word = stackalloc byte[4];
        int Read(uint offset, Span<byte> buffer) =>
            memory.TryRead((nuint)(field.Engine + offset), buffer) ? BitConverter.ToInt32(buffer) : int.MinValue;
        var begin = Read(0xE44, word); var end = Read(0xE48, word);
        var width = Read(0xE50, word); var height = Read(0xE54, word);
        if (begin == int.MinValue || end == int.MinValue || width is < 1 or > 256 || height is < 1 or > 256 ||
            (long)(uint)end - (uint)begin != width * height) return null;
        var cells = new byte[width * height];
        if (!memory.TryRead((nuint)(uint)begin, cells)) return null;
        return (x, y) => x >= 0 && y >= 0 && x < width && y < height && cells[y * width + x] < 0x80;
    }

    /// <param name="treasureGrid">True where the live chest grid (engine+E44) holds any treasure
    /// record, opened or not. Null when it could not be read.</param>
    public NavigationFrame Build(FieldNavigationSnapshot field, FieldMapSnapshot map, FieldViewport viewport,
        IReadOnlyList<FieldTreasure> treasures, FieldStoryState? story = null, Func<int, int, bool>? treasureGrid = null)
    {
        var identity = $"{field.Engine:X8}:{field.SceneId}";
        if (scene != identity)
        {
            Reset(); scene = identity;
            diagnostic($"Navigation field: scene={field.SceneId}; engine=0x{field.Engine:X}; map={map.Width}x{map.Height}; layer={map.PlayerLayer}; actors={field.ActorCount}; viewport={viewport}.");
        }
        var collisions = new FieldActorCollisionRules(field);
        var touchGoals = new List<(int Actor, IReadOnlyList<NavigationPoint> Goals)>();
        var graph = new FieldNavigationGraph(map, collisions, touchGoals);
        var player = new NavigationPoint(field.LeadPlayer?.FineX ?? 0, field.LeadPlayer?.FineY ?? 0, map.PlayerLayer);
        var targets = new List<NavigationTarget>();
        var storyAnchors = new List<NavigationTarget>();
        var storyCandidates = new List<NavigationTarget>();
        var scriptTerminals = new List<(string Id, int Left, int Top, int Right, int Bottom)>();
        var activeIds = new HashSet<string>();
        // Rows named only by their appearance, and the counters standing in for a keeper.
        var genericIds = new HashSet<string>();
        var standInsOf = new List<(string Owner, string StandIn)>();
        // The live marker actors behind each Confirm landmark row, for merging equivalent markers.
        var markerAnchors = new Dictionary<string, FieldActorSnapshot[]>(StringComparer.Ordinal);
        var guideActive = field.SceneIdCoherent && GameNavigationCatalog.IsFieldScene(field.SceneId) && story is { Point: >= 3 };
        FieldNavigationGraph? openedEndOfTimeDoor = null;
        if (guideActive && field.SceneId == 464 &&
            field.Actors.Any(a => a.Index == 25 && a.ClassTag == 7 && a.IsUsable &&
                a.ScriptCallsEnabled && a.ScriptProcessingEnabled))
        {
            var copy = GameNavigationCatalog.ForScene(464)?.Actors.FirstOrDefault(a => a.Id == 25)?.Actions
                .FirstOrDefault(a => a.Touch && a.Kind == "Terrain" && a.Copy is not null && FieldTerrainGraph.ActionAvailable(464, 25, a, story!))?.Copy;
            if (copy is not null && FieldTerrainGraph.Preview(map, copy) is { } opened)
                openedEndOfTimeDoor = new(opened, collisions, touchGoals);
        }
        // A save point's sparkle and checker are offered once, as the save point itself; the
        // engine briefly makes the checker an activation candidate while the leader is on it.
        var savePoints = FieldSavePoints.Find(field, story);
        var savePointActors = savePoints.SelectMany(s => s.Actors).ToHashSet();
        foreach (var actor in field.Actors)
        {
            if (!actor.IsUsable || !InsideMap(actor) || !actor.IsDrawn || !actor.ClassTagKnown || actor.IsPartyMember || actor.Index == 0 ||
                (actor.ClassTag & FieldNavigationCapture.ClassTagRemovedBit) != 0 || savePointActors.Contains(actor.Index)) continue;
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
                FutureAreaLabels.ActorLabel(field.SceneId, actor, story) ?? OptionalGuideTargets.ActorLabel(field.SceneId, actor) ??
                (IsCathedralOrgan(actor) ? "Organ" : null) : null;
            var conveyorRobot = field.SceneIdCoherent && field.SceneId == 231 &&
                actor.Index is >= 12 and <= 15 && actor.ClassTag == 4 && actor.VisualIndex == 170;
            var touchOnly = scriptedContact && scriptInfo?.Actions.Any(a => !a.Touch && a.Available(story)) != true &&
                (field.SceneId, actor.Index) is not ((8, 11) or (439, 15));
            if (scriptedContact && scriptInfo?.Actions.Any(a => a.Touch && a.Kind == "Warp" && a.Available(story)) == true)
                ProtectContactPassage(id, actor);
            var approaches = touchOnly ? TouchApproach(actor) : ActorApproach(actor, position);
            approaches = InteractionPositions(approaches, scriptInfo?.Actions ?? [], touchOnly);
            var battles = FieldEncounters.Available(field, actor, story, scriptInfo);
            // A trap drawn as an ordinary object keeps its appearance. Its outcome is
            // learned by interacting, not announced by putting it in Enemies in advance.
            var appearanceBattle = description.Category == NavigationCategory.Objects && battles.Length != 0 &&
                (battles.Any(a => !a.Touch) || !scriptInfo!.Actions.Any(a => !a.Touch && a.Available(story)));
            var battleTouch = false;
            if (appearanceBattle) approaches = EncounterApproaches(actor, battles, out battleTouch);
            touchOnly |= battleTouch;
            if (touchOnly) touchGoals.Add((actor.Index, approaches));
            // A creature with a proven battle activation belongs in Enemies.
            // Retain its original identity for existing story bindings while
            // omitting the duplicate row. Named challengers and service keepers keep both.
            var encounterOnly = label is null && description.Label == "Creature" &&
                FieldContentFacts.Service(field.SceneId, actor.Index) is null && battles.Length != 0 &&
                EncounterApproaches(actor, battles, out _).Count != 0;
            Add(id, label ?? (field.SceneIdCoherent ? FieldContentFacts.ActorLabel(field.SceneId, actor, description.Category) : null) ??
                description.Label, description.Category, position, approaches, viewport.Contains(position.X, position.Y),
                storyOnly: encounterOnly,
                storyCandidate: !conveyorRobot,
                guideAvailable: !conveyorRobot && guideActive && (label is not null || actor.IsActivationCandidate || scriptedAction),
                instruction: conveyorRobot ? "Moving hazard on the conveyor. Getting caught can start the robot inspection ride." : null,
                guidanceRestriction: conveyorRobot ? "Conveyor robot is a moving hazard. Choose another destination for guidance or automatic walking." : null,
                arrivalInstruction: appearanceBattle && !battleTouch ? "Press Confirm to interact." : null,
                contactPosition: battleTouch ? Position(actor.FineX - actor.CollisionOffsetX * 16 - 1,
                    actor.FineY - (field.LastPartySlotRaw > actor.Index * 2 ? 1 : 0)) : null,
                // Confirm reaches the actor or any stand-in that runs its script (the same
                // set ActorApproach routes to); touch pickups finish by contact instead.
                confirm: touchOnly || conveyorRobot ? null : ActorConfirm(Anchors(actor)),
                followUntilInteraction: field.SceneIdCoherent && field.SceneId == 221 &&
                    actor.Index is 12 or 13 && actor.ClassTag == 5 && actor.VisualIndex == 134 &&
                    story is { Point: >= 51 and < 54 } && story.Flag(0xEC, 0x10) == true && story.Flag(0xEC, 0x40) == false);
            if (label is null) genericIds.Add(id);
            foreach (var (standIn, same) in InteractionProxies(actor))
                if (same) standInsOf.Add((id, standIn.ClassTag == 7 ? $"landmark:{standIn.Index}"
                    : $"actor:{standIn.Index}:{standIn.ClassTag}:{standIn.VisualIndex}"));
        }
        foreach (var chest in treasures)
        {
            var position = Position(chest.FineX, chest.FineY);
            // Approach(position) is the centre of each neighbouring tile, and the one-eighth-tile
            // arrival box never leaves that tile, so the position already satisfies 179940; the
            // facing is the rest of the native test.
            var (chestTileX, chestTileY) = (chest.FineX >> 8, chest.FineY >> 8);
            // 179D90 finds any grid record, opened or not, so only the live chest grid can prove
            // that nothing shadows the up probe's second row. Without it, row - 2 never counts.
            Func<int, int, bool> treasureAt = treasureGrid ?? ((_, _) => true);
            var chestGoals = Approach(position).ToList();
            // 179C30 also accepts the chest two rows above when the row between holds no record.
            // Offer that spot only when the tile directly below cannot be stood on, so an
            // adjacent goal stays the one chosen whenever it exists.
            var below = At(chestTileX * 256 + 128, (chestTileY + 1) * 256 + 128).Any();
            if (!below && !treasureAt(chestTileX, chestTileY + 1))
                chestGoals.AddRange(At(chestTileX * 256 + 128, (chestTileY + 2) * 256 + 128).Where(p => !graph.IsTerminal(p)));
            Add($"chest:{chest.Index}", chest.IsChest ? "Treasure chest" : "Item pickup", NavigationCategory.Objects, position,
                chestGoals.Distinct().ToArray(), chest.IsChest && TileVisible(chest.FineX / 256, chest.FineY / 256),
                guideAvailable: guideActive,
                confirm: new(point => FieldInteractionRange.TreasureFacings(point.X, point.Y, chestTileX, chestTileY, treasureAt), null));
        }
        foreach (var save in savePoints)
        {
            // Saving is enabled by standing in the checker's tile, so the goals are the lattice
            // nodes that keep the controller's arrival box inside it.
            Add($"save:{save.Checker}:{save.TileX}:{save.TileY}", FieldSavePoints.Label, NavigationCategory.Objects,
                Position(save.TileX * 256 + 128, save.TileY * 256 + 128), SaveApproach(save.TileX, save.TileY),
                TileVisible(save.TileX, save.TileY), guideAvailable: guideActive,
                instruction: FieldSavePoints.Instruction, arrivalInstruction: FieldSavePoints.ArrivalInstruction);
        }
        if (field.SceneIdCoherent)
        foreach (var actor in field.Actors)
        {
            // These exact script slots are interaction markers for scenery drawn in
            // the map. Their class 7 intentionally has no sprite. Never promote other
            // hidden actors, and use their live coordinates rather than guide positions.
            if (!InsideMap(actor) || savePointActors.Contains(actor.Index)) continue;
            var label = EarlyStoryTargets.Landmark(field.SceneId, story, actor, field.Actors) ??
                FutureAreaLabels.Landmark(field.SceneId, story, actor);
            var knownTouch = EarlyStoryTargets.IsTouchLandmark(field.SceneId, actor.Index) ||
                FutureAreaLabels.IsTouchLandmark(field.SceneId, actor.Index);
            if (knownTouch && label is null) continue;
            var metadata = GameNavigationCatalog.ActorInfo(field.SceneId, actor);
            var scripted = story is not null && metadata is { Marker: true } && actor.IsUsable && !actor.IsPartyMember && actor.ScriptCallsEnabled
                ? metadata.Actions.Where(a => FieldTerrainGraph.ActionAvailable(field.SceneId, actor.Index, a, story)).ToArray() : [];
            if (label is null && scripted.Any(a => a.Kind == "Encounter") &&
                !scripted.Any(a => a.Kind is "Item" or "Warp" or "Menu" or "Talk" or "Terrain")) continue;
            var generic = label is null;
            if (label is null && scripted.Length != 0)
                label = FieldContentFacts.Service(field.SceneId, actor.Index) ??
                    (scripted.Any(a => a.Kind == "Item") ? "Item pickup" :
                    scripted.FirstOrDefault(a => a.Kind == "Warp") is { } warp
                        ? GameNavigationCatalog.DestinationLabel(warp.Destination) : "Interactable scenery");
            if (label is null) continue;
            if (generic) genericIds.Add($"landmark:{actor.Index}");
            var position = Position(actor.FineX, actor.FineY);
            var touch = knownTouch || metadata is { Touch: true };
            var approaches = touch
                ? TouchApproach(actor)
                : ActorApproach(actor, position);
            var terrainContact = (field.SceneId, actor.Index) != (28, 1) &&
                scripted.Any(a => a.Touch && a.Kind == "Terrain" && a.Copy is not null);
            if (terrainContact)
                approaches = FieldTerrainGraph.Contacts(actor, map.PlayerLayer, field.SceneId)
                    .Where(p => graph.TryPosition(p.X, p.Y, p.Layer, out var at) && at == p && !graph.IsTerminal(p)).ToArray();
            approaches = InteractionPositions(approaches, metadata?.Actions ?? [], touch);
            if (touch && actor.ScriptCallsEnabled) touchGoals.Add((actor.Index, approaches));
            var scriptedExit = scripted.Any(a => a.Kind == "Warp");
            var bike = FutureAreaLabels.IsBike(field.SceneId, actor.Index);
            if (scripted.Any(a => a.Touch && a.Kind == "Warp"))
                ProtectContactPassage($"landmark:{actor.Index}", actor);
            Add($"landmark:{actor.Index}", label, !bike && scriptedExit ? NavigationCategory.Exits : NavigationCategory.Objects, position, approaches,
                TileVisible(actor.TileX, actor.TileY), storyOnly: knownTouch || touch && scripted.Length == 0,
                guideAvailable: guideActive && (!touch || scripted.Length != 0),
                arrivalInstruction: bike ? "Press Confirm to interact with the jet bike." : null,
                confirm: touch || terrainContact ? null : ActorConfirm(Anchors(actor)));
            if (!touch && !terrainContact) markerAnchors[$"landmark:{actor.Index}"] = Anchors(actor);
        }
        // A counter that only runs its keeper's script is the keeper's destination, already
        // offered under the keeper's name with the counter's reach. A visible character that
        // happens to stand in (the Truce ferry office cat) keeps its own row. Repeated markers
        // of one map feature (a two-tile sign) are one row with every marker's standing room.
        targets.RemoveAll(t => t.Category != NavigationCategory.People && genericIds.Contains(t.Id) &&
            standInsOf.Any(s => s.StandIn == t.Id && targets.Any(owner => owner.Id == s.Owner)));
        if (field.SceneIdCoherent)
        foreach (var group in FieldContentFacts.MarkerGroups(field.SceneId))
        {
            var members = targets.Where(t => genericIds.Contains(t.Id) && group.Any(index => t.Id == $"landmark:{index}")).ToList();
            if (members.Count < 2) continue;
            var first = members[0];
            members = members.Where(m => m.Label == first.Label && m.Category == first.Category).ToList();
            if (members.Count < 2) continue;
            var merged = first with
            {
                ApproachPoints = members.SelectMany(m => m.ApproachPoints).Distinct()
                    .OrderBy(p => Math.Abs((long)p.X - first.Position.X) + Math.Abs((long)p.Y - first.Position.Y)).Take(64).ToArray(),
                Visible = members.Any(m => m.Visible), Discovered = members.Any(m => m.Discovered),
                GuideAvailable = members.Any(m => m.GuideAvailable),
            };
            // Equivalent markers run the same script, so Confirm reaching any of them is the
            // interaction. Next to one marker the scan may well pick its higher-slot sibling.
            if (members.All(m => markerAnchors.ContainsKey(m.Id)))
            {
                var rule = ActorConfirm(members.SelectMany(m => markerAnchors[m.Id]).ToArray());
                merged = merged with { ConfirmFacings = rule.Ready, ConfirmPending = rule.Pending, ApproachConfirms = null };
            }
            else
            {
                // Mixed finishes: each goal keeps the rule of the marker it came from.
                merged = merged with
                {
                    ConfirmFacings = null,
                    ApproachConfirms = members.SelectMany(m => m.ApproachPoints.Where(merged.ApproachPoints.Contains)
                            .Select(p => (Point: p, Rule: m.ConfirmFacings)))
                        .Where(p => p.Rule is not null).DistinctBy(p => p.Point).ToDictionary(p => p.Point, p => p.Rule!),
                    ConfirmPending = members.Any(m => m.ConfirmPending is not null)
                        ? point => members.Any(m => m.ConfirmPending?.Invoke(point) == true) : null,
                };
            }
            targets[targets.IndexOf(first)] = merged;
            targets.RemoveAll(t => members.Skip(1).Contains(t));
        }
        if (field.SceneIdCoherent)
        foreach (var actor in field.Actors)
        {
            if (!InsideMap(actor)) continue;
            var metadata = GameNavigationCatalog.ActorInfo(field.SceneId, actor);
            if (metadata is not { Marker: true } && FieldVisualLabels.Describe(actor).Category == NavigationCategory.Objects) continue;
            var encounters = FieldEncounters.Available(field, actor, story, metadata);
            if (encounters.Length == 0) continue;
            var position = Position(actor.FineX, actor.FineY);
            var approaches = EncounterApproaches(actor, encounters, out var touch);
            if (approaches.Count == 0) continue;
            if (touch) touchGoals.Add((actor.Index, approaches));
            Add($"actor-encounter:{actor.Index}", "Encounter", NavigationCategory.Enemies,
                position, approaches, actor.IsDrawn && viewport.Contains(position.X, position.Y) ||
                    metadata is { Marker: true } && TileVisible(actor.TileX, actor.TileY),
                guideAvailable: guideActive, storyCandidate: true,
                arrivalInstruction: touch ? null : "Press Confirm to interact.",
                contactPosition: touch ? Position(actor.FineX - actor.CollisionOffsetX * 16 - 1,
                    actor.FineY - (field.LastPartySlotRaw > actor.Index * 2 ? 1 : 0)) : null,
                confirm: touch ? null : ActorConfirm([actor]));
        }
        if (guideActive && GameNavigationCatalog.ForScene(field.SceneId) is { } sceneInfo)
        foreach (var group in sceneInfo.Regions.Where(r =>
                     (FutureAreaLabels.IsPillar(field.SceneId, r) ? FutureAreaLabels.PillarVisible(r, story) : r.Available(story)) &&
                     (r.Kind != "Progress" || r.Value > story!.Point) &&
                     field.Actors.Any(a => a.Index == r.Actor && a.IsUsable && !a.IsPartyMember &&
                         (a.ClassTag & FieldNavigationCapture.ClassTagRemovedBit) == 0 &&
                         (!FutureAreaLabels.IsPillar(field.SceneId, r) || a.ClassTag == 7 && a.ScriptProcessingEnabled))).GroupBy(r => r.Id))
        {
            var region = group.First();
            var pillar = FutureAreaLabels.IsPillar(field.SceneId, region);
            var goals = new List<NavigationPoint>();
            for (var y = Math.Max(0, region.Top); y <= Math.Min(map.Height - 1, region.Bottom); y++)
            for (var x = Math.Max(0, region.Left); x <= Math.Min(map.Width - 1, region.Right); x++)
                goals.AddRange(At(x * 256 + 128, y * 256 + 128).Where(p => !graph.IsTerminal(p)));
            var points = goals.OrderBy(Distance).Distinct().Take(64).ToArray();
            if (points.Length == 0) continue;
            if (region.Kind == "Warp" && !pillar) scriptTerminals.Add((region.Id, region.Left, region.Top, region.Right, region.Bottom));
            // Encounters are the script's own battle triggers (native D8 under a
            // leader-coordinate guard), so they carry the trigger's location but no
            // party, name or reward. Offer the trigger and leave the fight to the
            // player; the guard that the script sets when it is over removes it.
            var (regionLabel, regionCategory) = region.Kind switch
            {
                "Warp" => (pillar ? FutureAreaLabels.PillarLabel(region) : GameNavigationCatalog.DestinationLabel(region.Destination), NavigationCategory.Exits),
                "Switch" => ("Floor trigger", NavigationCategory.Objects),
                "Encounter" => ("Encounter", NavigationCategory.Enemies),
                _ => ("Story event", NavigationCategory.Exits),
            };
            Add(region.Id, regionLabel, regionCategory,
                points[0], points, points.Any(p => TileVisible(p.X / 256, p.Y / 256)),
                storyOnly: region.Kind is not ("Warp" or "Encounter"), guideAvailable: true,
                arrivalInstruction: pillar ? FutureAreaLabels.PillarInstruction(story!) : null);
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
        exitLabels.Apply(targets);
        ReportInventory();
        return new(identity, field.SceneIdCoherent && field.ControlFlag != 0 && field.InputMode == 0 &&
            field.LeadPlayer is { IsUsable: true, IsDrawn: true } && !map.TransitionPending,
            player, targets.AsReadOnly(), guideActive
                ? FieldLandingGraph.Create(
                    FieldTerrainGraph.Create(map, field.Actors, story!, GameNavigationCatalog.ForScene(field.SceneId)!, scriptTerminals, collisions, touchGoals),
                    map, GameNavigationCatalog.ForScene(field.SceneId))
                : scriptTerminals.Count == 0 ? graph : new ScriptPassageGraph(graph, scriptTerminals), NavigationUnits.LocalStep)
            {
                AreaName = areaName?.Invoke(field.SceneId) ?? GameNavigationCatalog.AreaName(field.SceneId),
                PlayerFacing = field.LeadPlayer is { FacingValid: true } lead
                    ? FieldInteractionRange.NativeFacing(lead.Facing) : NavigationDirection.None,
                MovingFloor = player.X >= 0 && player.Y >= 0 && player.X / 256 < map.Width && player.Y / 256 < map.Height
                    ? FieldFloorMovement.FromFlags(map.TerrainFlags[player.Y / 256 * map.Width + player.X / 256]) : null,
            };

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
                $"renderedChests={treasures.Count(t => t.IsChest)}; guidePickups={treasures.Count(t => !t.IsChest)}; " +
                $"savePoints={string.Join(",", savePoints.Select(s => $"{s.Checker}@{s.TileX}:{s.TileY}"))}; " +
                $"storyPoint={story?.Point.ToString() ?? "unknown"}; " +
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
                $"flag152={a.ActivationEnabled},field20={a.ActivationBinding},scriptCalls={a.ScriptCallsEnabled},collisionOffsetX={a.CollisionOffsetX},pos=({a.FineX},{a.FineY})")) + ".");
        }

        // Scripts park retired actors at tile FF,FF without necessarily clearing
        // their draw mode. Such actors must not keep their last discovered position.
        bool InsideMap(FieldActorSnapshot actor) => actor.FineX >= 0 && actor.FineY >= 0 &&
            actor.FineX / 256 < map.Width && actor.FineY / 256 < map.Height;

        bool IsCathedralOrgan(FieldActorSnapshot actor) => field.SceneIdCoherent && field.SceneId == 131 &&
            actor.Index == 36 && actor.ClassTag == 4 && actor.VisualIndex == 100;

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
        /// <summary>Resolve the audited and script-proven actors that receive Confirm for this
        /// one, only while both native actors match their identities and permit their scripts
        /// to run. A generated stand-in may be an interaction marker, which is never drawn.</summary>
        IReadOnlyList<(FieldActorSnapshot Actor, bool SameDestination)> InteractionProxies(FieldActorSnapshot actor)
        {
            if (!field.SceneIdCoherent || !actor.IsUsable || !actor.IsDrawn || !InsideMap(actor) || actor.IsPartyMember ||
                !actor.ScriptCallsEnabled || GameNavigationCatalog.ActorInfo(field.SceneId, actor) is null) return [];
            var bindings = FieldContentFacts.StandInsFor(field.SceneId, actor.Index, story).Select(s => (s.Actor, s.SameDestination, AllowMarker: true));
            if (FieldActorProxies.ProxyFor(field.SceneId, actor.Index) is { } audited) bindings = bindings.Append((audited, true, false));
            var result = new List<(FieldActorSnapshot, bool)>();
            foreach (var (id, same, allowMarker) in bindings)
                if (field.Actors.FirstOrDefault(a => a.Index == id && a.IsUsable && (a.IsDrawn || allowMarker && a.ClassTag == 7) &&
                    InsideMap(a) && !a.IsPartyMember && a.ScriptCallsEnabled && (a.ClassTag & FieldNavigationCapture.ClassTagRemovedBit) == 0 &&
                    GameNavigationCatalog.ActorInfo(field.SceneId, a)?.Id == id) is { } proxy && result.All(r => r.Item1 != proxy))
                    result.Add((proxy, same));
            return result;
        }
        /// <summary>One step away may be on the inaccessible side of a counter.
        /// Add in-range standing positions for actors; chests keep their separate rule.</summary>
        IReadOnlyList<NavigationPoint> InteractionPositions(IReadOnlyList<NavigationPoint> points,
            IReadOnlyList<GameNavigationCatalog.Action> actions, bool touch)
        {
            var current = actions.Where(a => a.Touch == touch && a.Available(story)).ToArray();
            if (!current.Any(a => a.Guards.Any(g => g.Source is "X" or "Y"))) return points;
            return points.Where(p => current.Any(a => a.AcceptsPosition(p.X, p.Y, NavigationUnits.LocalStep / 8))).ToArray();
        }

        IReadOnlyList<NavigationPoint> EncounterApproaches(FieldActorSnapshot actor, GameNavigationCatalog.Action[] encounters, out bool touch)
        {
            touch = encounters.Any(a => a.Touch);
            // 8B places feet at fraction FF. Keep the exact foot row, plus
            // native contact positions around it when the object occupies wall
            // terrain (Guardia Forest's trap sparkle does). Only this selected
            // actor is allowed to be bumped by the route's collision graph.
            var approaches = touch
                ? At((actor.FineX - actor.CollisionOffsetX * 16 - 1) / 16 * 16, actor.FineY / 16 * 16)
                    .Concat(FieldTerrainGraph.Contacts(actor, player.Layer)
                        .SelectMany(p => At(p.X - actor.CollisionOffsetX * 16, p.Y)))
                    .Where(p => !graph.IsTerminal(p)).Distinct().ToArray()
                : ActorApproach(actor, Position(actor.FineX, actor.FineY));
            approaches = InteractionPositions(approaches, encounters, touch);
            if (approaches.Count == 0 && touch && encounters.Any(a => !a.Touch))
            {
                touch = false;
                approaches = InteractionPositions(ActorApproach(actor, Position(actor.FineX, actor.FineY)), encounters, false);
            }
            return approaches;
        }

        IReadOnlyList<NavigationPoint> ActorApproach(FieldActorSnapshot actor, NavigationPoint p)
        {
            // The sprite the player recognises is not always the one the game lets them
            // act on. Where another actor exists only to run this one's script, its own
            // standing room counts too, so long as it is really there and really itself.
            var proxies = InteractionProxies(actor);
            var anchors = Anchors(actor);
            // Confirm (17D230) tests X - 16 * [actor+0x14C], not the sprite X, so the four
            // one-step goals are placed around that native point, and every goal must pass the
            // same facing test the arrival uses, over the whole arrival box.
            var direct = Approach(Position(actor.FineX - actor.CollisionOffsetX * 16, actor.FineY))
                .Where(goal => ConfirmGoal(goal, anchors)).ToArray();
            // A nearby goal can be blocked by a counter, hedge, or the actor's
            // own body. Widen to everywhere the confirm handler would still
            // accept, and let the search pick whichever of them it can actually walk
            // to. An actor with all four steps open and no stand-in pays nothing.
            if (direct.Length == 4 && proxies.Count == 0 && (actor.LoadedFlag & 1) == 0) return direct;
            var widened = direct.Concat(ConfirmApproach(actor));
            foreach (var (proxy, _) in proxies) widened = widened.Concat(ConfirmApproach(proxy));
            // Rank by the nearest place the game accepts Confirm, so a keeper's own floor
            // behind the counter cannot crowd out every customer-side goal.
            var sources = anchors.Select(a => (X: a.FineX - a.CollisionOffsetX * 16, Y: a.FineY)).ToArray();
            return widened.Distinct().Where(goal => ConfirmGoal(goal, anchors))
                .OrderBy(point => sources.Min(s => Math.Abs(point.X - s.X) + Math.Abs(point.Y - s.Y))).Take(64).ToArray();
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
        IReadOnlyList<NavigationPoint> ConfirmApproach(FieldActorSnapshot actor)
        {
            var found = new List<NavigationPoint>();
            var reach = FieldInteractionRange.Along + NavigationUnits.LocalStep / 8;
            var actorX = actor.FineX - actor.CollisionOffsetX * 16;
            var actorY = actor.FineY;
            for (var py = (actorY - reach) / 64 * 64; py <= actorY + reach; py += 64)
            for (var px = (actorX - reach) / 64 * 64; px <= actorX + reach; px += 64)
            {
                if (px < 0 || py < 0) continue;
                // The side-aware test the arrival uses, over the whole arrival box.
                if (FieldInteractionRange.FacingsWithin(px, py, actor.FineX, actor.FineY, actor.CollisionOffsetX,
                        NavigationUnits.LocalStep / 8).Count == 0) continue;
                found.AddRange(At(px, py).Where(point => !graph.IsTerminal(point)));
            }
            return found;
        }
        IReadOnlyList<NavigationPoint> TouchApproach(FieldActorSnapshot actor)
        {
            if (field.SceneId == 28 && actor.Index == 1)
            {
                // Atel0409's upper bridge contact shares confirm and touch code.
                // The floor is the bottom pixel of this half-tile strip; the tile
                // centre is not a reachable standing position. Keep the live foot
                // coordinates at pixel precision, inside the native touch radius.
                return At(actor.FineX / 16 * 16, actor.FineY / 16 * 16)
                    .Where(p => !graph.IsTerminal(p)).Distinct().ToArray();
            }
            if (field.SceneId == 465 && actor.Index is >= 16 and <= 20)
            {
                // The clockwise lesson markers are native touch contacts, not
                // tile-centre goals. In particular the west marker is in a wall.
                // These standing points plus FutureStoryTargets' final directions
                // satisfy 178980's scene465 radius224, including arrival tolerance.
                var sx = (actor.FineX + 32) / 64 * 64;
                var sy = (actor.FineY + 32) / 64 * 64;
                if (actor.Index == 17) { sx += 256; sy -= 128; }
                if (actor.Index == 18) sy += 64;
                var dx = actor.Index == 17 ? -1 : actor.Index == 19 ? 1 : 0;
                var dy = actor.Index == 20 ? 1 : dx == 0 ? -1 : 0;
                // Check the live center/offset, strict native bounds and every
                // corner of the arrival box. Do not inherit a static offset if
                // the captured actor no longer satisfies the audited contact.
                foreach (var ox in new[] { -32, 32 })
                foreach (var oy in new[] { -32, 32 })
                foreach (var step in new[] { 0, 32 })
                {
                    var px = sx + ox + dx * (step + 112);
                    var py = sy + oy + dy * step - (dx != 0 ? 64 : dy < 0 ? 112 : 0);
                    var carry = field.LastPartySlotRaw > actor.Index * 2 ? 1 : 0;
                    if (Math.Abs((long)actor.FineX - px - actor.CollisionOffsetX * 16 - 1) >= field.ActorCollisionRadius ||
                        Math.Abs((long)actor.FineY - py - carry) >= (actor.ActivationEnabled == 0 ? field.ActorCollisionRadius : 224))
                        return [];
                }
                return At(sx, sy).Where(p => !graph.IsTerminal(p)).Distinct().ToArray();
            }
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
        /// <summary>Interior nodes of one tile: with the one-eighth-tile arrival box every
        /// arrival still has the leader's native tile (FineX >> 8, FineY >> 8) equal to it.</summary>
        IReadOnlyList<NavigationPoint> SaveApproach(int tileX, int tileY)
        {
            var goals = new List<NavigationPoint>();
            for (var y = tileY * 256 + 64; y <= tileY * 256 + 192; y += 64)
            for (var x = tileX * 256 + 64; x <= tileX * 256 + 192; x += 64)
                goals.AddRange(At(x, y).Where(point => !graph.IsTerminal(point)));
            return goals.Distinct().ToArray();
        }
        IEnumerable<NavigationPoint> ExitApproach(NavigationPoint p)
        {
            // Every lattice node of the cell, its own boundary included. A one-tile
            // doorway leaves that boundary node as the only one the leading-corner
            // probes allow, so skipping it made the goals unreachable by construction
            // and five of Manoria's six doors reported no route. Stopping short of the
            // cell is prevented by the arrival rule instead, which asks the graph
            // whether the player is actually in it.
            // Half-tile corridors plus the native seven-pixel leading corners
            // also leave standing rows/columns at offsets 112 and 240. Preserve
            // those pixel boundaries alongside the ordinary four-pixel lattice.
            foreach (var x in new[] { 0, 64, 112, 128, 192, 240 })
            foreach (var y in new[] { 0, 64, 112, 128, 192, 240 })
            {
                var px = p.X / 256 * 256 + x; var py = p.Y / 256 * 256 + y;
                foreach (var point in At(px, py)) yield return point;
                // This doorway has no standing points until its native touch
                // script opens the floor. Keep its exit goals so staged search
                // can prove the contact and continuation. The live graph still
                // controls every step and must be captured again after contact.
                if (openedEndOfTimeDoor is not null && graph.ExitAt(px, py) == 0)
                    for (var layer = 1; layer <= 3; layer++)
                        if (openedEndOfTimeDoor.TryPosition(px, py, layer, out var point)) yield return point;
            }
        }
        void Add(string id, string label, NavigationCategory category, NavigationPoint position,
            IReadOnlyList<NavigationPoint> approaches, bool visible, bool storyOnly = false, bool storyCandidate = true,
            bool guideAvailable = false, string? instruction = null, string? arrivalInstruction = null,
            NavigationPoint? contactPosition = null, ConfirmRule? confirm = null, bool followUntilInteraction = false,
            string? guidanceRestriction = null)
        {
            activeIds.Add(id);
            if (storyCandidate) storyCandidates.Add(new(id, label, category, position, approaches, visible,
                visible || discovered.ContainsKey(id))
                {
                    Instruction = instruction, ArrivalInstruction = arrivalInstruction, ContactPosition = contactPosition,
                    GuidanceRestriction = guidanceRestriction,
                    ConfirmFacings = confirm?.Ready, ConfirmPending = confirm?.Pending,
                    FollowUntilInteraction = followUntilInteraction,
                });
            var output = storyOnly ? storyAnchors : targets;
            if (visible || guideAvailable)
            {
                var target = new NavigationTarget(id, label, category, position, approaches, visible,
                    visible || discovered.ContainsKey(id))
                {
                    GuideAvailable = guideAvailable, Instruction = instruction, ArrivalInstruction = arrivalInstruction,
                    GuidanceRestriction = guidanceRestriction,
                    ContactPosition = contactPosition, ConfirmFacings = confirm?.Ready, ConfirmPending = confirm?.Pending,
                    FollowUntilInteraction = followUntilInteraction,
                };
                if (visible) discovered[id] = target;
                output.Add(target);
            }
            // A remembered target keeps its discovered geometry, but its Confirm readiness is
            // always this capture's (current position, scan gate, contact), never the old one's.
            else if (discovered.TryGetValue(id, out var known)) output.Add(known with
            {
                Visible = false, Discovered = true,
                GuidanceRestriction = guidanceRestriction,
                ConfirmFacings = confirm?.Ready, ConfirmPending = confirm?.Pending,
                FollowUntilInteraction = followUntilInteraction,
            });
        }
        /// <summary>The actor and every audited stand-in that runs its script (the same set
        /// ActorApproach routes to).</summary>
        FieldActorSnapshot[] Anchors(FieldActorSnapshot actor) =>
            InteractionProxies(actor).Select(p => p.Actor).Prepend(actor).Concat(EquivalentMarkers(actor)).DistinctBy(a => a.Index).ToArray();

        /// <summary>The live markers curated as repeats of this marker's map feature (the same
        /// groups merged into one row below). They run the same script, so the scan choosing
        /// any of them is this interaction, when planning as well as on arrival.</summary>
        IEnumerable<FieldActorSnapshot> EquivalentMarkers(FieldActorSnapshot actor) =>
            !field.SceneIdCoherent || (actor.ClassTag & 0xFF) != 7 ? [] :
            FieldContentFacts.MarkerGroups(field.SceneId).Where(g => g.Contains(actor.Index)).SelectMany(g => g)
                .Where(index => index != actor.Index)
                .Select(index => field.Actors.FirstOrDefault(a => a.Index == index && (a.ClassTag & 0xFF) == 7 && InsideMap(a)))
                .OfType<FieldActorSnapshot>();

        /// <summary>Readiness exactly as Confirm (17D0C0) would resolve it with the leader standing at
        /// the live point. While fieldState+0x20C4 holds a touched actor (bit 7 clear), 17D0C0 skips the
        /// facing scan and 17FA20 runs the actor at actorBase+0x13174, whatever the facing: ready (any
        /// facing) only when that is one of these actors. Otherwise the scan 17D230 takes the highest
        /// eligible slot its exact routine accepts, so only facings whose winner is one of these actors
        /// are ready. Unknown native state, no leader, a scan gate that is currently off for all of
        /// these actors, or a touched actor that is someone else, is "pending" (in reach, not ready);
        /// a facing another actor wins is simply not offered.</summary>
        ConfirmRule ActorConfirm(FieldActorSnapshot[] anchors)
        {
            var ids = anchors.Select(a => a.Index).ToHashSet();
            var leader = field.LeadPlayerActorIndex;
            var actors = field.Actors;
            var (contact, selected) = (field.ContactActorRaw, field.ConfirmActorRaw);
            var gated = anchors.Any(a => FieldInteractionRange.ConfirmScanned(a, leader));
            NavigationDirection[] all = [NavigationDirection.North, NavigationDirection.South, NavigationDirection.West, NavigationDirection.East];
            IReadOnlyList<NavigationDirection> Geometric(NavigationPoint p) => anchors
                .SelectMany(a => FieldInteractionRange.Facings(p.X, p.Y, a.FineX, a.FineY, a.CollisionOffsetX)).Distinct().ToArray();
            // 17FA20 runs the selected actor only while its persistent call gates are open and
            // its script priority at +E4 exceeds 1. Busy/unknown actors still win the scan;
            // only dispatch is blocked, so never remove them from competing actor selection.
            bool Runs(int slot) => anchors.Any(a => a.Index == slot && a.ScriptCallsEnabled &&
                a.ScriptPriority is > 1 && (a.ClassTag & FieldNavigationCapture.ClassTagRemovedBit) == 0);
            IReadOnlyList<NavigationDirection> Ready(NavigationPoint p)
            {
                if (leader < 0 || contact is not int touched) return [];
                var preferred = Geometric(p).Concat(all).Distinct();
                if ((touched & 0x80) == 0)
                    return selected is int chosen && (chosen & 0x80) == 0 && Runs((chosen & 0xFF) >> 1)
                        ? preferred.ToArray() : [];
                return preferred.Where(f => Runs(FieldInteractionRange.ConfirmWinner(actors, leader, p.X, p.Y, f))).ToArray();
            }
            // Not ready but in reach is "pending" unless every in-reach facing is simply won by some
            // other actor; that is a bad standing point, which the controller replans away from.
            bool Pending(NavigationPoint p)
            {
                var geometric = Geometric(p);
                if (geometric.Count == 0 || Ready(p).Count != 0) return false;
                if (leader < 0 || contact is not int touched || (touched & 0x80) == 0 || !gated) return true;
                return geometric.Any(f => FieldInteractionRange.ConfirmWinner(actors, leader, p.X, p.Y, f) is var winner &&
                    (winner < 0 || ids.Contains(winner)));
            }
            return new(Ready, Pending);
        }

        /// <summary>A route goal for a confirmed actor: some facing reaches one of its anchors
        /// from the whole arrival box (inward, carry-independent bounds), and no other eligible,
        /// non-party actor the scan tests first could be accepted from any point of that box with
        /// the same facing (outward, exact carry-aware bounds). Planning assumes the target's own
        /// scan gate, a camera-cull state, will be on once the player is there; arrival does not.
        /// Party followers move with the leader, so they are judged live at arrival.</summary>
        bool ConfirmGoal(NavigationPoint goal, FieldActorSnapshot[] anchors)
        {
            const int tolerance = NavigationUnits.LocalStep / 8;
            var leader = field.LeadPlayerActorIndex;
            foreach (var anchor in anchors)
            foreach (var facing in FieldInteractionRange.FacingsWithin(goal.X, goal.Y, anchor.FineX, anchor.FineY,
                         anchor.CollisionOffsetX, tolerance))
                if (!field.Actors.Any(other => anchors.All(a => a.Index != other.Index) && !other.IsPartyMember &&
                        other.Index > anchor.Index && FieldInteractionRange.ConfirmScanned(other, leader) &&
                        FieldInteractionRange.MayReach(goal.X, goal.Y, other, leader, tolerance, facing)))
                    return true;
            return false;
        }
    }

    /// <summary>A destination's Confirm finish: facings ready now, and where it is in reach but
    /// the game would not yet give it Confirm.</summary>
    private sealed record ConfirmRule(Func<NavigationPoint, IReadOnlyList<NavigationDirection>> Ready,
        Func<NavigationPoint, bool>? Pending);

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
