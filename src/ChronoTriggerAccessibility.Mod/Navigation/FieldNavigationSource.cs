using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Mod.Navigation;

public sealed class FieldNavigationSource(IReadableMemory memory, Action<string> diagnostic,
    Func<int, string?>? areaName = null, Func<int, string?>? characterName = null,
    Action<FieldNavigationSnapshot, FieldMapSnapshot, FieldStoryState?, FieldViewport>? fieldObserver = null)
{
    private readonly Dictionary<string, NavigationTarget> discovered = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NavigationTarget> guideGeometry = new(StringComparer.Ordinal);
    private readonly FullStoryTargets fullStory = new();
    private readonly NavigationExitLabels exitLabels = new();
    private readonly NavigationPeopleLabels peopleLabels = new();
    private readonly FieldLevelCrossings levelCrossings = new();
    private readonly FieldRegionIndex regions = new();
    private string? scene;
    private string? lastFailure;
    private string? lastInventory;
    private bool storyUnavailable;

    public void Reset() { discovered.Clear(); guideGeometry.Clear(); exitLabels.Reset(); peopleLabels.Reset(); scene = null; lastInventory = null; }

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
        var capturedMap = map;
        map = BlackOmenTargets.Map(field, OceanPalaceTargets.Map(field, map));
        var identity = $"{field.Engine:X8}:{field.SceneId}";
        if (scene != identity)
        {
            Reset(); scene = identity;
            diagnostic($"Navigation field: scene={field.SceneId}; engine=0x{field.Engine:X}; map={map.Width}x{map.Height}; layer={map.PlayerLayer}; actors={field.ActorCount}; viewport={viewport}.");
        }
        fieldObserver?.Invoke(field, map, map.TransitionPending ? null : story, viewport);
        var genoObstacles=field.SceneIdCoherent?GenoDomeObstacles.Read(field.SceneId,map,story):[];
        var collisions = new FieldActorCollisionRules(field);
        var touchGoals = new List<(int Actor, IReadOnlyList<NavigationPoint> Goals)>();
        var graph = new FieldNavigationGraph(map, collisions, touchGoals, treasureGrid:
            field.SceneIdCoherent && SunkenDesertNavigation.IsScene(field.SceneId) ? treasureGrid : null);
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
        var keepEntranceCopy = guideActive ? KeepFieldTargets.EntranceOpening(field, story) : null;
        var openedKeepEntrance = keepEntranceCopy is not null && FieldTerrainGraph.Preview(map, keepEntranceCopy) is { } keepOpen
            ? new FieldNavigationGraph(keepOpen, collisions, touchGoals) : null;
        var omenDoorCopy = guideActive && map.PlayerLayer == 1 ? BlackOmenTargets.DoorOpening(field, story) : null;
        var omenDoorOpening = guideActive && map.PlayerLayer == 1 && field.Actors.Any(a =>
            BlackOmenTargets.OpeningFinalDoor(field.SceneId, a, story));
        var openedOmenDoor = omenDoorCopy is not null && FieldTerrainGraph.Preview(map, omenDoorCopy) is { } omenOpen
            ? new FieldNavigationGraph(omenOpen, collisions, touchGoals) : null;
        var openedSanctumDoors = new Dictionary<int, FieldNavigationGraph>();
        if (guideActive)
            foreach (var actor in field.Actors.Where(a => a.IsUsable && a.ScriptCallsEnabled && !a.IsPartyMember))
                if ((LostSanctumTargets.DoorExit(field.SceneId, actor) ?? TemporalLabTargets.DoorExit(field.SceneId, actor)) is { } exit &&
                    GameNavigationCatalog.ActorInfo(field.SceneId, actor) is { } door)
                    foreach (var action in door.Actions.Where(a => a.Touch && a.Kind == "Terrain" && a.Copy is not null &&
                        FieldTerrainGraph.ActionAvailable(field.SceneId, actor.Index, a, story!)))
                        if (FieldTerrainGraph.Preview(map, action.Copy!) is { } opened)
                            openedSanctumDoors[exit] = new(opened, collisions, touchGoals);
        // A save point's sparkle and checker are offered once, as the save point itself; the
        // engine briefly makes the checker an activation candidate while the leader is on it.
        var savePoints = FieldSavePoints.Find(field, story).Where(s => KeepFieldTargets.SaveIsObservable(field, story, s)).ToArray();
        var savePointActors = savePoints.SelectMany(s => s.Actors).ToHashSet();
        foreach (var actor in field.Actors)
        {
            if (!actor.IsUsable || !InsideMap(actor) || !actor.IsDrawn || !actor.ClassTagKnown ||
                actor.IsPartyMember && !PartyTalk(actor) || actor.Index == field.LeadPlayer?.Index || actor.Index == 0 ||
                (actor.ClassTag & FieldNavigationCapture.ClassTagRemovedBit) != 0 || savePointActors.Contains(actor.Index)) continue;
            if (field.SceneIdCoherent && ZealFieldLabels.IsBookEffect(field.SceneId, actor)) continue;
            if (field.SceneIdCoherent && GenoDomeTargets.IsElectricalEffect(field.SceneId, actor)) continue;
            if (field.SceneIdCoherent && GenoDomeTargets.IsTerminalDisplay(field.SceneId, actor)) continue;
            if (field.SceneIdCoherent && (OzzieFortScenery.IsBladePart(field.SceneId,actor) ||
                OzzieFortScenery.ConcealedActor(field.SceneId,actor,map,story))) continue;
            if (field.SceneIdCoherent && (OceanPalaceTargets.ConcealedSprite(field.SceneId, actor) ||
                OceanPalaceTargets.ParkedCast(field.SceneId, actor, story))) continue;
            if (field.SceneIdCoherent && KeepFieldTargets.IsRegionLightSprite(field.SceneId, actor)) continue;
            if (field.SceneIdCoherent && KeepFieldTargets.LightLabel(field.SceneId, actor) is { } light)
            {
                scriptTerminals.Add((KeepFieldTargets.LightId(actor.Index), actor.TileX, actor.TileY, actor.TileX, actor.TileY));
                Add(KeepFieldTargets.LightId(actor.Index), light, NavigationCategory.Objects,
                    Position(actor.FineX, actor.FineY), SaveApproach(actor.TileX, actor.TileY),
                    TileVisible(actor.TileX, actor.TileY), guideAvailable: guideActive && actor.ScriptProcessingEnabled,
                    instruction: "Walk onto the glowing point.", actorSlot: actor.Index);
                continue;
            }
            // The bridge switch is the drawn panel; the knob beneath it runs the switch on
            // contact. One row at the panel, finished at the knob while its contact is available.
            if (field.SceneIdCoherent && OceanPalaceTargets.BridgePanel(field.SceneId, actor))
            {
                var knob = field.Actors.FirstOrDefault(a => OceanPalaceTargets.BridgeKnob(field.SceneId, a) && a.IsUsable &&
                    a.IsDrawn && InsideMap(a) && a.ScriptCallsEnabled && (a.ClassTag & FieldNavigationCapture.ClassTagRemovedBit) == 0);
                GameNavigationCatalog.Action[] contact = knob is null || story is null ? [] :
                    GameNavigationCatalog.ActorInfo(field.SceneId, knob)?.Actions.Where(a => a.Touch && a.Available(story)).ToArray() ?? [];
                IReadOnlyList<NavigationPoint> goals = contact.Length == 0 ? [] : InteractionPositions(TouchApproach(knob!), contact, true);
                if (goals.Count != 0) touchGoals.Add((knob!.Index, goals));
                var inactive = contact.Length == 0 ? OceanPalaceTargets.InactiveSwitch : null;
                var panel = Position(actor.FineX, actor.FineY);
                var shown = viewport.Contains(panel.X, panel.Y);
                Add($"actor:{actor.Index}:{actor.ClassTag}:{actor.VisualIndex}", "Floor switch", NavigationCategory.Objects,
                    panel, goals, shown, guideAvailable: guideActive && inactive is null,
                    instruction: inactive, guidanceRestriction: inactive, actorSlot: actor.Index);
                // The bridge objective names the knob's own slot.
                if (inactive is null)
                    Add($"actor:{knob!.Index}:{knob.ClassTag}:{knob.VisualIndex}", "Floor switch", NavigationCategory.Objects,
                        panel, goals, shown, storyOnly: true, guideAvailable: guideActive, actorSlot: knob.Index);
                continue;
            }
            var book = field.SceneIdCoherent ? ZealFieldLabels.Book(field.SceneId, actor, story) : null;
            var questObject = field.SceneIdCoherent ? QuestObjectLabels.Actor(field.SceneId, actor) : null;
            var description = FieldVisualLabels.Describe(actor);
            if (field.SceneIdCoherent && GenoDomeTargets.Actor(field.SceneId, actor) is { } genoActor)
                description = genoActor;
            // Verified switch and control slots keep their native finish and availability.
            if (field.SceneIdCoherent && OceanPalaceTargets.ObjectLabel(field.SceneId, actor) is { } oceanObject)
                description = (oceanObject, NavigationCategory.Objects);
            // A lair hole is a Confirm passage drawn as an open pit; a parked one has no contact.
            var lairHole = field.SceneIdCoherent && ReptiteLairTargets.IsHole(field.SceneId, actor);
            if (lairHole && ReptiteLairTargets.IsParked(actor)) continue;
            if (lairHole) description = (ReptiteLairTargets.HoleLabel(field.SceneId, actor), NavigationCategory.Exits);
            var scriptInfo = field.SceneIdCoherent ? GameNavigationCatalog.ActorInfo(field.SceneId, actor) : null;
            var labWarp = field.SceneIdCoherent ? TemporalLabTargets.Pad(field.SceneId, actor, story, field.Actors) : null;
            // Atel_0114 actor9's Confirm sets local9; actor1 performs the warp.
            // The catalog cannot infer that handoff from actor9's handler alone.
            var forestGate = field.SceneIdCoherent && EarlyStoryTargets.IsForestGateAvailable(field.SceneId, story, actor);
            var entranceTerminal = field.SceneIdCoherent && FutureAreaLabels.IsEntranceSecurityTerminal(field.SceneId, actor);
            var touchFigure = field.SceneIdCoherent && KeepFieldTargets.IsTouchFigure(field.SceneId, actor, story);
            var genoMobile = field.SceneIdCoherent && GenoDomeTargets.MobileDroid(field.SceneId, actor, story);
            var genoMeeting = field.SceneIdCoherent ? GenoDomeTargets.DroidMeeting(field.SceneId, actor, story) : null;
            var scriptedAction = genoMobile || forestGate || entranceTerminal || book is not null || story is not null && actor.ScriptCallsEnabled &&
                scriptInfo?.Actions.Any(a => a.Available(story)) == true;
            var scriptedContact = genoMobile || touchFigure || scriptedAction &&
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
            var currentName = actor.IsPlayerClass && actor.VisualIndex is >= 0 and < 7
                ? characterName?.Invoke(actor.VisualIndex) : null;
            var nu = field.SceneIdCoherent ? NuNpcLabels.ActorLabel(field.SceneId, actor, story) ??
                LostSanctumTargets.ActorLabel(field.SceneId, actor, story) : null;
            var capeEncounter = field.SceneIdCoherent ? NorthCapeTargets.EncounterLabel(field.SceneId, actor, story, characterName) : null;
            var recurring = field.SceneIdCoherent ? RecurringNpcLabels.Resolve(field.SceneId, actor, story) : default;
            var familiarName = recurring.Handled ? recurring.Name :
                field.SceneIdCoherent ? OpeningStoryTargets.ActorLabel(field.SceneId, actor) ??
                StoryCharacterLabels.ActorLabel(field.SceneId, actor, story) ??
                FutureAreaLabels.ActorLabel(field.SceneId, actor, story) ?? OptionalGuideTargets.ActorLabel(field.SceneId, actor) ??
                ZealFieldLabels.Actor(field.SceneId, actor) ?? nu ?? (IsCathedralOrgan(actor) ? "Organ" : null) : null;
            var label = labWarp?.Label ?? book ?? capeEncounter ?? questObject?.Label ?? (!string.IsNullOrWhiteSpace(currentName) ? currentName :
                familiarName);
            if (field.SceneIdCoherent && DeathPeakTargets.IsDrawnDoll(field.SceneId, actor)) label = "Doll";
            // These names identify a character the player can see; alone they are not a destination.
            var onScreenName = recurring.Handled ? recurring.Name :
                field.SceneIdCoherent ? StoryCharacterLabels.OnScreenName(field.SceneId, actor, story) : null;
            var conveyorRobot = field.SceneIdCoherent && field.SceneId == 231 &&
                actor.Index is >= 12 and <= 15 && actor.ClassTag == 4 && actor.VisualIndex == 170;
            var touchOnly = labWarp is not null || genoMobile || scriptedContact && scriptInfo?.Actions.Any(a => !a.Touch && a.Available(story)) != true &&
                (field.SceneId, actor.Index) is not ((8, 11) or (439, 15));
            if (scriptedContact && scriptInfo?.Actions.Any(a => a.Touch && a.Kind == "Warp" && a.Available(story)) == true)
                ProtectContactPassage(id, actor);
            var approaches = touchOnly ? TouchApproach(actor) : ActorApproach(actor, position);
            if (!genoMobile) approaches = InteractionPositions(approaches, scriptInfo?.Actions ?? [], touchOnly);
            // A sprite rises from its feet: Mune's and Masa's heads show before their feet enter the window.
            var inView = viewport.Contains(position.X, position.Y) ||
                field.SceneIdCoherent && OceanPalaceSight.TwinHeadShown(field.SceneId, actor, viewport);
            label ??= field.SceneIdCoherent ? TemporalLabTargets.RobotLabel(field.SceneId, actor, inView) : null;
            var seen = inView || discovered.ContainsKey(id);
            var npcRegion = guideActive ? genoMeeting ?? OceanPalaceTargets.NpcRegion(field, actor, story, seen) ??
                OceanPalaceStoryTargets.NpcRegion(field, actor, story) : null;
            if (npcRegion is not null) approaches = RegionPoints(npcRegion);
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
                (field.SceneIdCoherent ? ReptiteLairTargets.CreatureAppearance(field.SceneId, actor) : null) ??
                description.Label, description.Category, position, approaches, inView,
                storyOnly: encounterOnly,
                storyCandidate: !conveyorRobot,
                guideAvailable: !conveyorRobot && guideActive && (!recurring.Handled || recurring.Name is null || seen) &&
                    (!lairHole || actor.ScriptCallsEnabled) &&
                    (label is not null && label != onScreenName || actor.IsActivationCandidate || scriptedAction || npcRegion is not null),
                instruction: labWarp is not null ? $"Step onto the pad to warp to the {labWarp.Landing}. Restart guidance from the new landing." :
                    capeEncounter is not null ? NorthCapeTargets.EncounterInstruction :
                    conveyorRobot ? "Moving hazard on the conveyor. Getting caught can start the robot inspection ride." : questObject?.Description,
                guidanceRestriction: genoMobile && story?.Local(12) == 1
                    ? "The mobile Sentinel is following you. Choose another destination to lead it there." :
                    conveyorRobot ? "Conveyor robot is a moving hazard. Choose another destination for guidance or automatic walking." :
                    lairHole && !actor.ScriptCallsEnabled ? "Hole interaction is currently unavailable." : null,
                arrivalInstruction: genoMeeting is not null ? "Wait here for the following Sentinel." :
                    book is not null ? "Press Confirm to open or close the book." :
                    entranceTerminal ? "Press Confirm to use the entrance security terminal." :
                    forestGate ? "Press Confirm to enter the Time Gate." :
                    lairHole || appearanceBattle && !battleTouch ? "Press Confirm to interact." : null,
                contactPosition: labWarp is not null || battleTouch || touchFigure || genoMobile ? Position(actor.FineX - actor.CollisionOffsetX * 16 - 1,
                    actor.FineY - (field.LastPartySlotRaw > actor.Index * 2 ? 1 : 0)) : null,
                // Confirm reaches the actor or any stand-in that runs its script (the same
                // set ActorApproach routes to); touch pickups finish by contact instead.
                confirm: touchOnly || conveyorRobot || npcRegion is not null ? null : ActorConfirm(Anchors(actor)),
                followUntilInteraction: field.SceneIdCoherent && field.SceneId == 221 &&
                    actor.Index is 12 or 13 && actor.ClassTag == 5 && actor.VisualIndex == 134 &&
                    story is { Point: >= 51 and < 54 } && story.Flag(0xEC, 0x10) == true && story.Flag(0xEC, 0x40) == false,
                actorSlot: actor.Index);
            if (label is null || nu is not null) genericIds.Add(id);
            foreach (var (standIn, same) in InteractionProxies(actor))
                if (same) standInsOf.Add((id, standIn.ClassTag == 7 ? $"landmark:{standIn.Index}"
                    : $"actor:{standIn.Index}:{standIn.ClassTag}:{standIn.VisualIndex}"));
        }
        if(OzzieFortScenery.VisibleBlade(field,viewport) is{} blade)
            targets.Add(new("scenery:fort-blade","Hanging blade",NavigationCategory.Objects,
                Position(blade.FineX,blade.FineY),[],true,true)
            {
                Instruction="A metal blade hangs on a chain above a chest.",
                GuidanceRestriction="This is visible scenery with no interaction.",
            });
        foreach (var chest in treasures)
        {
            if(field.SceneIdCoherent&&OzzieFortScenery.ConcealedTreasure(field.SceneId,chest,map,story))continue;
            var sandChest = field.SceneIdCoherent && chest.IsChest && SunkenDesertNavigation.ChestRoom(field.SceneId);
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
            if ((!below || sandChest) && !treasureAt(chestTileX, chestTileY + 1))
                chestGoals.AddRange(At(chestTileX * 256 + 128, (chestTileY + 2) * 256 + 128).Where(p => !graph.IsTerminal(p)));
            var preferred = sandChest ? chestGoals.Where(p =>
            {
                var floor = FieldFloorMovement.FromFlags(map.TerrainFlags[p.Y / 256 * map.Width + p.X / 256]);
                return floor is null || floor.Value.Speed == 16 &&
                    FieldInteractionRange.TreasureFacings(p.X, p.Y, chestTileX, chestTileY, treasureAt)
                        .Any(f => FieldFloorMovement.Opposes(f, floor.Value.Direction));
            }).Distinct().ToArray() : [];
            var chestLabel = field.SceneIdCoherent && chest.IsChest ? FrozenCliffsTargets.ChestLabel(field.SceneId, chest.Index) : null;
            Add($"chest:{chest.Index}", chestLabel ?? (chest.IsChest ? "Treasure chest" : "Item pickup"), NavigationCategory.Objects, position,
                chestGoals.Distinct().ToArray(), chest.IsChest && TileVisible(chest.FineX / 256, chest.FineY / 256),
                guideAvailable: guideActive,
                instruction: sandChest ? SunkenDesertNavigation.ChestInstruction : null,
                arrivalAnnouncement: sandChest ? SunkenDesertNavigation.ChestArrival : null,
                keepInteractionReach: sandChest,
                preferredApproaches: preferred,
                confirm: new(point => FieldInteractionRange.TreasureFacings(point.X, point.Y, chestTileX, chestTileY, treasureAt), null));
        }
        foreach (var save in savePoints)
        {
            // Saving is enabled by standing in the checker's tile, so the goals are the lattice
            // nodes that keep the controller's arrival box inside it.
            var lightLabel = KeepFieldTargets.LightLabel(field.SceneId, save.Checker);
            if (lightLabel is not null)
                scriptTerminals.Add((KeepFieldTargets.LightId(save.Checker), save.TileX, save.TileY, save.TileX, save.TileY));
            Add(lightLabel is not null ? KeepFieldTargets.LightId(save.Checker) : $"save:{save.Checker}:{save.TileX}:{save.TileY}",
                lightLabel is not null ? lightLabel + ", save point" : FieldSavePoints.Label, NavigationCategory.Objects,
                Position(save.TileX * 256 + 128, save.TileY * 256 + 128), SaveApproach(save.TileX, save.TileY),
                TileVisible(save.TileX, save.TileY), guideAvailable: guideActive,
                instruction: FieldSavePoints.Instruction, arrivalInstruction: FieldSavePoints.ArrivalInstruction);
        }
        if (field.SceneIdCoherent && SealedFieldTargets.PyramidClosed(field.SceneId, story) && map.Width >= 12 && map.Height > 18)
        {
            // Atel0423 draws the pyramid in tiles 4..11,6..13. It has no actor
            // or Confirm handler: its loop watches the leader's tile Y == 18.
            // Offer real floor in front of the visible structure; the native
            // story guard decides whether walking there starts any sequence.
            var approaches = Enumerable.Range(4, 8).SelectMany(x => At(x * 256 + 128, 18 * 256 + 128))
                .Where(p => !graph.IsTerminal(p)).Distinct().ToArray();
            Add(SealedFieldTargets.PyramidId, "Pyramid", NavigationCategory.Objects,
                Position(8 * 256, 10 * 256), approaches, viewport.Intersects(4 * 256, 6 * 256, 12 * 256, 14 * 256),
                guideAvailable: guideActive, instruction: "Approach the front of the pyramid.");
        }
        if (field.SceneIdCoherent)
        foreach (var actor in field.Actors)
        {
            // These exact script slots are interaction markers for scenery drawn in
            // the map. Their class 7 intentionally has no sprite. Never promote other
            // hidden actors, and use their live coordinates rather than guide positions.
            if (!InsideMap(actor) || savePointActors.Contains(actor.Index)) continue;
            if (VortexRoomTargets.ConcealedPassage(field.SceneId, actor, story)) continue;
            if (KeepFieldTargets.ConcealedActor(field.SceneId, actor.Index)) continue;
            if (OzzieFortScenery.ConcealedMarker(field.SceneId,actor)) continue;
            if (BlackbirdDuctTargets.ConcealedLandmark(field.SceneId, actor.Index, map)) continue;
            // Actor36's native A4 branch has an operand the older catalog missed.
            // Its boarding choice is described only by the verified dock provider.
            if (field.SceneId == 464 && actor.Index == EpochDockTargets.Actor) continue;
            var sealedDoor = SealedFieldTargets.IsDoor(field.SceneId, actor.Index);
            var marker = SealedFieldTargets.DoorMarker(field.SceneId, actor.Index);
            if (sealedDoor && (!SealedFieldTargets.DoorClosed(field.SceneId, actor, story) ||
                actor.Index != marker && field.Actors.Any(a => a.Index == marker && InsideMap(a) &&
                    SealedFieldTargets.DoorClosed(field.SceneId, a, story)))) continue;
            var landmarkId = $"landmark:{marker}";
            var book = actor.ClassTag == 7 ? ZealFieldLabels.Book(field.SceneId, actor, story) : null;
            var teleportPad = ZealFieldLabels.TeleportPad(field.SceneId, actor);
            var omenDoor = field.SceneIdCoherent && BlackOmenTargets.IsFinalDoor(field.SceneId, actor);
            var labConfirmDoor = field.SceneIdCoherent && TemporalLabTargets.ConfirmDoor(field.SceneId, actor);
            var vortexDoor = VortexRoomTargets.Door(field.SceneId, actor);
            var confirmDoor = labConfirmDoor || vortexDoor;
            var keepDoor = keepEntranceCopy is not null && actor.Index == 14;
            var genoMarker = GenoDomeTargets.Marker(field.SceneId, actor);
            var genoPodOpen = GenoDomeTargets.PodOpen(field.SceneId, actor, map, story);
            var genoDoorOpen = GenoDomeTargets.ConveyorDoorOpen(field.SceneId, actor, map, story);
            var label = book ?? teleportPad ?? genoMarker ??
                (omenDoor ? "Northern door" : null) ?? (keepDoor ? KeepFieldTargets.EntranceDoorLabel : null) ??
                EarlyStoryTargets.Landmark(field.SceneId, story, actor, field.Actors) ??
                FutureAreaLabels.Landmark(field.SceneId, story, actor);
            var knownTouch = EarlyStoryTargets.IsTouchLandmark(field.SceneId, actor.Index) ||
                FutureAreaLabels.IsTouchLandmark(field.SceneId, actor.Index);
            var metadata = GameNavigationCatalog.ActorInfo(field.SceneId, actor);
            var scripted = story is not null && metadata is { Marker: true } && actor.IsUsable && !actor.IsPartyMember && actor.ScriptCallsEnabled
                ? metadata.Actions.Where(a => FieldTerrainGraph.ActionAvailable(field.SceneId, actor.Index, a, story)).ToArray() : [];
            if (TemporalLabTargets.TouchDoor(field.SceneId, actor))
                label ??= TemporalLabTargets.DoorLabel(field.SceneId, actor);
            if (scripted.Length != 0) label ??= (vortexDoor ? VortexRoomTargets.DoorLabel : null) ??
                TemporalLabTargets.DoorLabel(field.SceneId, actor) ?? TemporalLabTargets.ControlLabel(field.SceneId, actor) ??
                LostSanctumTargets.DoorLabel(field.SceneId, actor) ??
                TyrannoLairLabels.ActorLabel(field.SceneId, actor.Index) ??
                OceanPalaceTargets.MarkerLabel(field.SceneId, actor) ?? BlackbirdDuctTargets.MarkerLabel(field.SceneId, actor.Index) ??
                QuestObjectLabels.Marker(field.SceneId, actor) ??
                (field.SceneId == 643 && actor.Index == 8 ? "Door console" : null);
            if (knownTouch && label is null && !scripted.Any(a => a.Touch && a.Kind == "Terrain" && a.Copy is not null)) continue;
            if (label is null && scripted.Any(a => a.Kind == "Encounter") &&
                !scripted.Any(a => a.Kind is "Item" or "Warp" or "Menu" or "Talk" or "Terrain")) continue;
            var generic = label is null;
            if (label is null && scripted.Length != 0)
                label = FieldContentFacts.Service(field.SceneId, actor.Index) ??
                    (scripted.Any(a => a.Kind == "Item") ? "Item pickup" :
                    scripted.FirstOrDefault(a => a.Kind == "Warp") is { } warp
                        ? VortexRoomTargets.PassageLabel(field.SceneId) ?? GameNavigationCatalog.DestinationLabel(warp.Destination) : "Interactable scenery");
            if (label is null) continue;
            if (generic) genericIds.Add(landmarkId);
            var position = Position(actor.FineX, actor.FineY);
            var touch = !confirmDoor && (knownTouch || genoDoorOpen || metadata is { Touch: true });
            var approaches = touch
                ? TouchApproach(actor)
                : ActorApproach(actor, position);
            if (sealedDoor) approaches = Anchors(actor).Where(a => SealedFieldTargets.DoorClosed(field.SceneId, a, story))
                .SelectMany(a => ActorApproach(a, Position(a.FineX, a.FineY))).Distinct().ToArray();
            var terrainContact = !confirmDoor && (field.SceneId, actor.Index) != (28, 1) &&
                scripted.Any(a => a.Touch && a.Kind == "Terrain" && a.Copy is not null);
            var labDoor = TemporalLabTargets.TouchDoor(field.SceneId, actor);
            var labContact = labDoor && scripted.Any(a => a.Touch);
            if (terrainContact || omenDoor || labDoor)
            {
                // A door marker can lie beyond its own closed floor. Publish
                // goals on its guarded native copy as well as the live floor;
                // the staged graph must still reach an opening contact on the
                // live map before any movement through that door is possible.
                var opened = scripted.Where(a => a.Touch && a.Kind == "Terrain" && a.Copy is not null &&
                        FieldTerrainGraph.CanContinueContact(field.SceneId, actor.Index, metadata!.Actions))
                    .Select(a => FieldTerrainGraph.Preview(map, a.Copy!)).OfType<FieldMapSnapshot>()
                    .Select(m => new FieldNavigationGraph(m, collisions, touchGoals)).ToArray();
                approaches = FieldTerrainGraph.Contacts(actor, map.PlayerLayer, field.SceneId)
                    .Where(p => !graph.IsTerminal(p) &&
                        (graph.TryPosition(p.X, p.Y, p.Layer, out var at) && at == p ||
                         opened.Any(g => g.TryPosition(p.X, p.Y, p.Layer, out var goal) && goal == p))).ToArray();
            }
            approaches = InteractionPositions(approaches, metadata?.Actions ?? [], touch);
            if (touch && actor.ScriptCallsEnabled) touchGoals.Add((actor.Index, approaches));
            var scriptedExit = scripted.Any(a => a.Kind == "Warp");
            var bike = FutureAreaLabels.IsBike(field.SceneId, actor.Index);
            if (scripted.Any(a => a.Touch && a.Kind == "Warp"))
                ProtectContactPassage(landmarkId, actor);
            var restriction = FutureAreaLabels.FactoryLiftRestriction(field.SceneId, actor.Index, story) ??
                OceanPalaceTargets.WallSwitchBlocked(field, actor) ??
                (GenoDomeTargets.ChargingContactAccepted(field, actor, story) ? "At the charging pod. Step away before entering it again." : null) ??
                (genoPodOpen == false ? "The pod is closed." : null) ??
                (omenDoor ? BlackOmenTargets.DoorRestriction(actor, story) : null);
            Add(landmarkId, label, !bike && teleportPad is null && scriptedExit ? NavigationCategory.Exits : NavigationCategory.Objects, position, approaches,
                TileVisible(actor.TileX, actor.TileY), storyOnly: !confirmDoor && !omenDoor && genoMarker is null && (knownTouch || touch && scripted.Length == 0),
                guideAvailable: guideActive && (omenDoor || genoMarker is not null || !touch || scripted.Length != 0),
                instruction: vortexDoor ? VortexRoomTargets.DoorInstruction : labConfirmDoor ? "Press Confirm to open the security chamber door." :
                    restriction ?? GenoDomeObstacles.ControlInstruction(field.SceneId,actor,map,viewport,genoPodOpen,genoObstacles) ??
                    (genoDoorOpen ? "The door to the main room is open. Continue up through the doorway." : null) ??
                    (omenDoor && story?.Local(10) == 1 ? "The northern door is open." :
                    omenDoor || keepDoor ? "Walk up to the door to open it." :
                    sealedDoor ? SealedFieldTargets.DoorInstruction(field.SceneId, story) :
                    teleportPad is not null && !scripted.Any(a => a.Kind == "Warp")
                    ? "Teleportation is currently unavailable." : null),
                guidanceRestriction: restriction,
                passageOpening: omenDoor && omenDoorOpening,
                arrivalAnnouncement: genoDoorOpen ? "The door to the main room is open. Continue up through the doorway." :
                    omenDoor && story?.Local(10) == 1 ? "The northern door is open." : null,
                arrivalInstruction: vortexDoor ? VortexRoomTargets.DoorInstruction : labConfirmDoor ? "Press Confirm to open the security chamber door." :
                    genoDoorOpen ? "Continue up through the doorway." :
                    book is not null ? "Press Confirm to open or close the book." :
                    sealedDoor ? "Press Confirm to examine the sealed door." :
                    bike ? "Press Confirm to interact with the jet bike." :
                    field.SceneId == 465 && actor.Index == 15 ? "Continue down through the doorway." :
                    BlackbirdDuctTargets.ArrivalInstruction(field.SceneId, actor.Index),
                contactPosition: field.SceneId == 256 && actor.Index == 25 && GenoDomeTargets.IsPod(field.SceneId, actor) ||
                    field.SceneId == 642 && actor.Index is 14 or 15 ||
                    field.SceneId == 600 && actor.Index == 21 && terrainContact ||
                    labContact || terrainContact && LostSanctumTargets.DoorExit(field.SceneId, actor) is not null
                    ? Position(actor.FineX - actor.CollisionOffsetX * 16 - 1, actor.FineY) : null,
                confirm: touch || terrainContact || labDoor ? null : ActorConfirm(Anchors(actor)));
            if (!touch && !terrainContact && !labDoor) markerAnchors[landmarkId] = Anchors(actor);
        }
        // A counter that only runs its keeper's script is the keeper's destination, already
        // offered under the keeper's name with the counter's reach. A visible character that
        // happens to stand in (the Truce ferry office cat) keeps its own row. Repeated markers
        // of one map feature (a two-tile sign) are one row with every marker's standing room.
        targets.RemoveAll(t => t.Category != NavigationCategory.People && genericIds.Contains(t.Id) &&
            standInsOf.Any(s => s.StandIn == t.Id && targets.Any(owner => owner.Id == s.Owner)));
        if (field.SceneIdCoherent)
        foreach (var group in FieldContentFacts.MarkerGroups(field.SceneId)
                     .Concat(VortexRoomTargets.MarkerGroups(field))
                     // Atel0642's two side-by-side contacts run the same guarded
                     // warp. They are one passage, not two distinct doorways.
                     .Concat(field.SceneId == 642 ? new[] { new[] { 14, 15 } } : []))
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
            if (KeepFieldTargets.ConcealedEncounter(field.SceneId, actor)) continue;
            if (field.SceneId == 600 && actor.Index == 21 && actor.ClassTag == 7) continue;
            if (OceanPalaceTargets.ConcealedSprite(field.SceneId, actor)) continue;
            if (field.SceneId == 464 && actor.Index == EpochDockTargets.Actor) continue;
            var metadata = GameNavigationCatalog.ActorInfo(field.SceneId, actor);
            // These verified switches are scenery. Their concealed battle
            // outcomes must not create a second, revealing Enemies row.
            if (metadata is { Marker: true } && TyrannoLairLabels.ActorLabel(field.SceneId, actor.Index) is not null) continue;
            if (metadata is not { Marker: true } && FieldVisualLabels.Describe(actor).Category == NavigationCategory.Objects) continue;
            var encounters = FieldEncounters.Available(field, actor, story, metadata);
            if (encounters.Length == 0) continue;
            var position = Position(actor.FineX, actor.FineY);
            var approaches = EncounterApproaches(actor, encounters, out var touch);
            if (approaches.Count == 0) continue;
            if (touch) touchGoals.Add((actor.Index, approaches));
            Add($"actor-encounter:{actor.Index}", ReptiteLairTargets.CreatureAppearance(field.SceneId, actor) ?? "Encounter", NavigationCategory.Enemies,
                position, approaches, actor.IsDrawn && viewport.Contains(position.X, position.Y) ||
                    metadata is { Marker: true } && TileVisible(actor.TileX, actor.TileY),
                guideAvailable: guideActive, storyCandidate: true,
                arrivalInstruction: touch ? null : "Press Confirm to interact.",
                contactPosition: touch ? Position(actor.FineX - actor.CollisionOffsetX * 16 - 1,
                    actor.FineY - (field.LastPartySlotRaw > actor.Index * 2 ? 1 : 0)) : null,
                confirm: touch ? null : ActorConfirm([actor]), actorSlot: actor.Index);
        }
        if (guideActive && GameNavigationCatalog.ForScene(field.SceneId) is { } sceneInfo)
        foreach (var group in sceneInfo.Regions.Where(r => !DeathPeakTargets.KnockbackRegion(field.SceneId, r) &&
                     !TemporalLabTargets.CaptureRegion(field.SceneId, r) &&
                     !(field.SceneId == 600 && r.Kind == "Encounter") &&
                     !KeepFieldTargets.ConcealedRegion(field.SceneId, r) &&
                     FrozenCliffsTargets.WatcherAvailable(field, r) &&
                     VortexRoomTargets.WatcherAvailable(field, r) &&
                     KeepFieldTargets.RegionLightVisible(field, r) && !OceanPalaceTargets.SpentRegion(field, r) &&
                     (BlackOmenTargets.IsPad(field.SceneId, r) ||
                      (FutureAreaLabels.IsPillar(field.SceneId, r) ? FutureAreaLabels.PillarVisible(r, story) : VortexRoomTargets.RegionAvailable(field.SceneId, r, map, story))) &&
                     (r.Kind != "Progress" || r.Value > story!.Point) &&
                     field.Actors.Any(a => a.Index == r.Actor && a.IsUsable && !a.IsPartyMember &&
                         (a.ClassTag & FieldNavigationCapture.ClassTagRemovedBit) == 0 &&
                         (!(FutureAreaLabels.IsPillar(field.SceneId, r) || BlackOmenTargets.IsPad(field.SceneId, r) ||
                            field.SceneId == 633 && r.Kind == "Warp") ||
                          a.ClassTag == 7 && a.ScriptProcessingEnabled))).GroupBy(r => r.Id))
        {
            var region = group.First();
            var cliffCave = FrozenCliffsTargets.IsCave(field.SceneId, region);
            var omenPad = BlackOmenTargets.IsPad(field.SceneId, region);
            var padRestriction = omenPad ? BlackOmenTargets.PadRestriction(field.SceneId, region, story) : null;
            var pillar = FutureAreaLabels.IsPillar(field.SceneId, region);
            var keepLight = KeepFieldTargets.RegionLightLabel(field.SceneId, region);
            var points = RegionPoints(region);
            if (points.Length == 0) continue;
            // A shaft grate warps only on Confirm, so a route may cross it.
            var grate = BlackbirdDuctTargets.IsGrate(field.SceneId, region);
            if (region.Kind == "Warp" && !pillar && !grate && (!omenPad || padRestriction is null))
                scriptTerminals.Add((region.Id, region.Left, region.Top, region.Right, region.Bottom));
            // Encounters are the script's own battle triggers (native D8 under a
            // leader-coordinate guard), so they carry the trigger's location but no
            // party, name or reward. Offer the trigger and leave the fight to the
            // player; the guard that the script sets when it is over removes it.
            var (regionLabel, regionCategory) = region.Kind switch
            {
                "Warp" => (pillar ? FutureAreaLabels.PillarLabel(region) : VortexRoomTargets.PassageLabel(field.SceneId) ?? GameNavigationCatalog.DestinationLabel(region.Destination),
                    pillar ? NavigationCategory.Objects : NavigationCategory.Exits),
                "Switch" => ("Floor trigger", NavigationCategory.Objects),
                "Encounter" => ("Encounter", NavigationCategory.Enemies),
                _ => ("Story event", NavigationCategory.Exits),
            };
            if (keepLight is not null)
            {
                regionLabel = keepLight;
                regionCategory = field.SceneId == 166 ? NavigationCategory.Objects : NavigationCategory.Exits;
            }
            if (grate) (regionLabel, regionCategory) = (BlackbirdDuctTargets.GrateLabel(field.SceneId, region, map), NavigationCategory.Objects);
            if (omenPad) (regionLabel, regionCategory) =
                ("Teleport pad to " + GameNavigationCatalog.AreaName(region.Destination), NavigationCategory.Objects);
            if (cliffCave) regionLabel = FrozenCliffsTargets.CaveLabel(field.SceneId, region)!;
            var castlePassage = VortexRoomTargets.DreamscapePassage(field.SceneId, region);
            var castleOpen = castlePassage && VortexRoomTargets.DreamscapeOpened(field.SceneId, map, story);
            if (castlePassage) (regionLabel, regionCategory) = (castleOpen ? "Open castle passage" : "Castle passage", NavigationCategory.Exits);
            Add(region.Id, regionLabel, regionCategory,
                points[0], points, points.Any(p => TileVisible(p.X / 256, p.Y / 256)),
                storyOnly: !castlePassage && region.Kind is not ("Warp" or "Encounter") ||
                    OceanPalaceStoryTargets.CinematicRegion(field.SceneId, region), guideAvailable: true,
                instruction: cliffCave ? FrozenCliffsTargets.CaveInstruction : padRestriction ?? (grate ? BlackbirdDuctTargets.GrateInstruction : null),
                guidanceRestriction: padRestriction,
                arrivalInstruction: cliffCave ? FrozenCliffsTargets.CaveInstruction : omenPad ? "Step onto the pad to teleport." :
                    pillar ? FutureAreaLabels.PillarInstruction(story!) : grate ? BlackbirdDuctTargets.GrateArrival : null,
                contactDirection: cliffCave ? NavigationDirection.North : NavigationDirection.None,
                passageOpening: castlePassage && VortexRoomTargets.DreamscapeOpening(field.SceneId, map, story),
                openingCompleteAnnouncement: castleOpen ? "The castle passage is open." : null);
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
            // Added PC rooms retain a destination-zero placeholder at (0,0).
            // It is not a native doorway. A current positive destination takes
            // precedence over the static placeholder when the game replaces it.
            if (map.ExitDestinations?.TryGetValue(id, out var liveDestination) == true
                ? liveDestination == 0
                : GameNavigationCatalog.ForScene(field.SceneId)?.Exits.Any(e => e.Id == id && e.Destination == 0) == true)
                continue;
            if (field.SceneIdCoherent && ZealFieldLabels.ConcealedExit(field.SceneId, id) &&
                !ZealFieldLabels.PassageOpen(story)) continue;
            var key = $"exit:{id}";
            var sanctumRestriction = field.SceneIdCoherent ? LostSanctumTargets.ExitRestriction(field.SceneId, id, story) : null;
            var labRestriction = field.SceneIdCoherent ? TemporalLabTargets.ExitRestriction(field.SceneId, id, story) : null;
            var label = (field.SceneIdCoherent && ZealFieldLabels.ConcealedExit(field.SceneId, id)
                ? "Opening behind the bookcase" : null) ??
                (field.SceneIdCoherent ? TyrannoLairLabels.ExitLabel(field.SceneId, id, story) : null) ??
                (field.SceneIdCoherent ? BlackbirdDuctTargets.ExitLabel(field.SceneId, id, map) : null) ??
                (field.SceneIdCoherent ? BlackOmenTargets.ExitLabel(field.SceneId, id) : null) ??
                FutureAreaLabels.ExitLabel(field.SceneId, id) ?? OpeningStoryTargets.ExitLabel(field.SceneId, id);
            var optionalLabel = field.SceneIdCoherent ? OptionalGuideAreas.ExitLabel(field.SceneId, id) : null;
            if (label == "Exit" && optionalLabel is not null) label = optionalLabel;
            var catalogLabel = field.SceneIdCoherent ? map.ExitDestinations?.TryGetValue(id, out var destination) == true
                ? GameNavigationCatalog.DestinationLabel(destination) : GameNavigationCatalog.ExitLabel(field.SceneId, id) : null;
            if (label == "Exit" && catalogLabel is not null) label = catalogLabel;
            if (sanctumRestriction is not null) label = "Unfinished bridge";
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
                visible.Length != 0, discovered.ContainsKey(key) || visible.Length != 0)
                { PassageOpening = omenDoorOpening && id == 1,
                    GuidanceRestriction = sanctumRestriction ?? labRestriction, Instruction = sanctumRestriction ?? labRestriction };
            guideGeometry[key] = guide;
            storyCandidates.Add(guide);
            if (visible.Length == 0)
            {
                activeIds.Add(key);
                if (optionalGuide) { targets.Add(guide with { GuideAvailable = true }); continue; }
                if (discovered.TryGetValue(key, out var old)) targets.Add(old with
                {
                    Visible = false, Discovered = true,
                    PassageOpening = omenDoorOpening && id == 1,
                    GuidanceRestriction = sanctumRestriction ?? labRestriction, Instruction = sanctumRestriction ?? labRestriction,
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
            Add(key, label, NavigationCategory.Exits, position, approaches, true, storyCandidate: false, guideAvailable: optionalGuide,
                passageOpening: omenDoorOpening && id == 1,
                instruction: sanctumRestriction ?? labRestriction, guidanceRestriction: sanctumRestriction ?? labRestriction);
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
        foreach (var dock in EpochDockTargets.Build(field, map, viewport, story))
        {
            touchGoals.Add((EpochDockTargets.Actor, dock.ApproachPoints));
            Add(dock.Id, dock.Label, dock.Category, dock.Position, dock.ApproachPoints, dock.Visible,
                guideAvailable: dock.GuideAvailable, instruction: dock.Instruction,
                arrivalInstruction: dock.ArrivalInstruction, contactPosition: dock.ContactPosition,
                actorSlot: dock.ActorSlot);
        }
        if (!map.TransitionPending && FrogHideawayTargets.Available(field, story))
        {
            // Actor8 has no sprite or usable position. Its automatic X<8 test
            // describes the room's left side; use actual walkable floor there,
            // never the concealed Frog actor's off-floor coordinates.
            var points = Enumerable.Range(0, map.Height).SelectMany(y =>
                    Enumerable.Range(0, Math.Min(8, map.Width)).SelectMany(x => At(x * 256 + 128, y * 256 + 128)))
                .Where(p => !graph.IsTerminal(p)).Distinct().OrderBy(Distance).Take(64).ToArray();
            if (points.Length != 0)
                Add(FrogHideawayTargets.Id, "Left side of the room", NavigationCategory.Objects,
                    points[0], points, points.Any(p => TileVisible(p.X / 256, p.Y / 256)),
                    guideAvailable: true, instruction: "Walk into the left side of the room.");
        }
        foreach (var shelter in DeathPeakTargets.Build(field, map, viewport, story, graph))
            Add(shelter.Id, shelter.Label, shelter.Category, shelter.Position, shelter.ApproachPoints, shelter.Visible,
                storyCandidate: false, guideAvailable: shelter.GuideAvailable, instruction: shelter.Instruction,
                arrivalInstruction: shelter.ArrivalInstruction, arrivalXRange: shelter.ArrivalXRange,
                arrivalAnnouncement: shelter.ArrivalAnnouncement, arrivalHold: shelter.ArrivalHold);
        foreach (var id in discovered.Keys.Where(id => !activeIds.Contains(id)).ToArray()) discovered.Remove(id);
        foreach (var id in guideGeometry.Keys.Where(id => !activeIds.Contains(id)).ToArray()) guideGeometry.Remove(id);
        if (field.SceneIdCoherent) storyCandidates.AddRange(TyrannoLairTargets.Build(field, map, story));
        var transitions = guideActive ? FieldTransitions.Find(field, map, story) : [];
        var routeGraph = guideActive
            ? FieldTransitionGraph.Create(FieldLandingGraph.Create(
                FieldTerrainGraph.Create(map, field.Actors, story!, GameNavigationCatalog.ForScene(field.SceneId)!, scriptTerminals, collisions, touchGoals),
                map, GameNavigationCatalog.ForScene(field.SceneId)), transitions)
            : scriptTerminals.Count == 0 ? graph : new ScriptPassageGraph(graph, scriptTerminals);
        if (guideActive) routeGraph = TemporalLabWarpGraph.Create(routeGraph, field, map, story, storyCandidates);
        var cliffSlide = FrozenCliffsTargets.SlideAvailable(field, map, story);
        if (cliffSlide)
        {
            var bodyMap = map with { TerrainFlags = map.TerrainFlags.Select(f => (byte)(f & ~12)).ToArray() };
            routeGraph = new FrozenCliffsSlideGraph(routeGraph, map,
                goals => new FieldNavigationGraph(bodyMap, collisions, touchGoals, goals, treasureGrid),
                targets.Where(t => t.Id.StartsWith("script-region:9:", StringComparison.Ordinal)).ToArray());
        }
        regions.Update(map);
        // A reachable native door, landing or drop can bridge a real floor gap.
        // Preserve its full staged proof. A marker in another disconnected room
        // cannot make every guide target in that room available from here.
        var stagedPart = CanReachStagedPart();
        var labWarpPart = routeGraph is TemporalLabWarpGraph warps && warps.HasConnectedPad(player);
        bool Disconnected(NavigationTarget t, bool storyCandidate = false) =>
            field.SceneIdCoherent && OceanPalaceTargets.DifferentRoom(field.SceneId, player, t) ||
            !stagedPart && !t.IsStoryNote && t.ApproachPoints.Count != 0 &&
            t.ApproachPoints.All(p => regions.Connected(player, p) == false) &&
            // Native warp look-ahead can bind a story goal and route to seen
            // destinations. It cannot disclose unseen pads, items or creatures.
            !(labWarpPart && (storyCandidate || t.Category == NavigationCategory.StoryEvents || t.Visible || t.Discovered));
        // Atel_0164 gates these two exterior doorways through scene303's
        // Confirm wall switches. No touch-opening copy covers either exit.
        // Other maps can have automatic doors whose staged goals need their
        // own proof; an empty approach alone must not classify those doors.
        bool BlockedExit(NavigationTarget t) => field.SceneIdCoherent && field.SceneId == 298 &&
            t.Id is "exit:5" or "exit:8" && t.ApproachPoints.Count == 0;
        storyCandidates.RemoveAll(t => Disconnected(t, true) || BlockedExit(t));
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
        peopleLabels.Apply(targets, genericIds);
        if (guideActive)
            targets.AddRange(levelCrossings.Find(field.SceneId, map)
                // Map124 also holds other Factory rooms. Its distant stair is
                // not part of scene228, even after the entrance lasers open.
                .Where(t => field.SceneId != 228 || t.ApproachPoints.Any(p => regions.Connected(player, p) != false))
                .Select(t => t with
                { Visible = t.ApproachPoints.Any(p => TileVisible(p.X / 256, p.Y / 256)) }));
        foreach (var entry in transitions)
        {
            var t = entry.Transition;
            targets.Add(new(t.Id, t.Label, NavigationCategory.Objects, t.Approach, [t.Approach],
                TileVisible(t.Approach.X / 256, t.Approach.Y / 256), false)
            {
                GuideAvailable = entry.Available || entry.Active, Transition = t,
                Instruction = "Continue down at this point to take the drop. This drop is one-way.",
            });
        }
        const string otherPart = "This destination is in another part of this map. Reach that section first.";
        for (var i = targets.Count - 1; i >= 0; i--)
        {
            var target = targets[i];
            var blockedExit = BlockedExit(target);
            if (!blockedExit && !Disconnected(target)) continue;
            // Seen information remains identifiable. A guide must not promote
            // an unseen destination on another floor component as usable here.
            if (!target.Visible && !target.Discovered) { targets.RemoveAt(i); continue; }
            var restriction = blockedExit ? "This passage is currently blocked." : otherPart;
            targets[i] = target with
            {
                GuideAvailable = false,
                GuidanceRestriction = target.GuidanceRestriction ?? restriction,
                Instruction = restriction + (string.IsNullOrWhiteSpace(target.Instruction) ? "" : " " + target.Instruction),
            };
        }
        if (DeathPeakTargets.Climb(field, map, viewport, story, graph, targets) is { } climb)
            targets.Insert(0, climb);
        ReportInventory();
        var floor = player.X >= 0 && player.Y >= 0 && player.X / 256 < map.Width && player.Y / 256 < map.Height
            ? FieldFloorMovement.FromFlags(map.TerrainFlags[player.Y / 256 * map.Width + player.X / 256]) : null;
        var compensateFloor = cliffSlide || field.SceneIdCoherent && (SunkenDesertNavigation.IsScene(field.SceneId) || field.SceneId == 256 ||
            LostSanctumTargets.NarrowCave(field.SceneId));
        return new(identity, field.SceneIdCoherent && field.ControlFlag != 0 && field.InputMode == 0 &&
            field.LeadPlayer is { IsUsable: true, IsDrawn: true } && !map.TransitionPending,
            player, targets.AsReadOnly(), routeGraph, NavigationUnits.LocalStep)
            {
                AreaName = CharacterRoomLabels.AreaName(field.SceneId) ?? OzzieFortLabels.AreaName(field.SceneId) ?? TemporalLabTargets.AreaName(field.SceneId) ??
                    (field.SceneId is 337 or 464 ? GameNavigationCatalog.AreaName(field.SceneId)
                    : areaName?.Invoke(field.SceneId) ?? GameNavigationCatalog.AreaName(field.SceneId)),
                PlayerFacing = field.LeadPlayer is { FacingValid: true } lead
                    ? FieldInteractionRange.NativeFacing(lead.Facing) : NavigationDirection.None,
                FieldMovementOwner = (field.ActorBase, field.LeadPlayerActorIndex),
                ManualRecoveryHoldDirection = cliffSlide && floor is { Direction: NavigationDirection.South, Speed: 32 }
                    ? NavigationDirection.North : NavigationDirection.None,
                ManualRecoveryInstruction = cliffSlide && floor is { Direction: NavigationDirection.South, Speed: 32 }
                    ? "Off route. Hold Up while running to stop sliding for new directions." : null,
                MovingFloor = floor,
                CompensateMovingFloor = compensateFloor,
                CanTraverseFloorStep = compensateFloor ? graph.CanTraverseFloorStep : null,
                MovingFloorGuidance = compensateFloor && floor is { Speed:16 } moving
                    ? (direction,goal)=>FieldFloorMovement.ManualDirection(player,direction,goal,moving,graph.CanTraverseFloorStep) : null,
                ActiveTransitions = transitions.Where(t => t.Active).Select(t => t.Transition.Id).ToHashSet(),
                RouteEvidence = RouteFailureEvidence.For(field, capturedMap, story, targets.AsReadOnly(), treasures),
                RouteFailureExplanation = genoObstacles.Any(o=>!o.Open)
                    ? (from,target)=>GenoDomeObstacles.Explain(map,from,target,genoObstacles,collisions,touchGoals) : null,
            };

        bool CanReachStagedPart()
        {
            if (routeGraph is not IStagedNavigationGraph || GameNavigationCatalog.ForScene(field.SceneId) is not { } metadata)
                return false;
            if (routeGraph is TemporalLabWarpGraph) return false;
            if (transitions.Any(t => (t.Available || t.Active) && regions.Connected(player, t.Transition.Approach) == true)) return true;
            var landingIds = metadata.Exits.Where(e =>
                (map.ExitDestinations?.TryGetValue(e.Id, out var current) == true ? current : e.Destination) == field.SceneId)
                .Select(e => $"exit:{e.Id}").ToHashSet();
            if (storyCandidates.Any(t => landingIds.Contains(t.Id) &&
                t.ApproachPoints.Any(p => regions.Connected(player, p) == true))) return true;
            foreach (var actor in field.Actors.Where(a => a.ClassTag == 7 && a.IsUsable && !a.IsPartyMember &&
                         a.ScriptCallsEnabled && InsideMap(a)))
            {
                var actions = metadata.Actors.FirstOrDefault(a => a.Matches(actor))?.Actions;
                if (actions?.Any(a => a.Touch && a.Kind == "Terrain" && a.Copy is not null && story is not null &&
                    FieldTerrainGraph.ActionAvailable(field.SceneId, actor.Index, a, story) &&
                    FieldTerrainGraph.Preview(map, a.Copy) is not null) != true) continue;
                // A permissive neighbourhood covers native contact probes and
                // closed-door edge approaches. This only retains eligibility;
                // the exact staged search still controls every movement command.
                for (var dy = -512; dy <= 512; dy += 256)
                for (var dx = -512; dx <= 512; dx += 256)
                for (var layer = 1; layer <= 3; layer++)
                    if (regions.Connected(player, new(actor.FineX - actor.CollisionOffsetX * 16 + dx,
                        actor.FineY + dy, layer)) == true) return true;
            }
            return false;
        }

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
                $"storyParty={(story?.Party is { } roster ? string.Join(",", roster) : "unavailable")}; " +
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
                $"flag152={a.ActivationEnabled},field20={a.ActivationBinding},scriptCalls={a.ScriptCallsEnabled}," +
                $"scriptPc={a.ScriptAddress?.ToString("X4") ?? "?"},opcode={a.CurrentScriptOpcode?.ToString("X2") ?? "?"}," +
                $"collisionOffsetX={a.CollisionOffsetX},pos=({a.FineX},{a.FineY})")) + ".");
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
        NavigationPoint[] RegionPoints(GameNavigationCatalog.Region region)
        {
            if (VortexRoomTargets.DreamscapePassage(field.SceneId, region) && VortexRoomTargets.DreamscapeOpened(field.SceneId, map, story))
                region = GameNavigationCatalog.ForScene(645)!.Regions.First(r => VortexRoomTargets.DreamscapeEncounter(645, r));
            if (FrozenCliffsTargets.IsCave(field.SceneId, region))
                return At(region.Left * 256 + 128, region.Top * 256 + 192).ToArray();
            var points = BlackOmenTargets.PadPoints(field.SceneId, region, graph, map.PlayerLayer).ToList();
            if (field.SceneId is 578 or 599 or 600 or 638 or 643 && region.Kind == "Warp")
            {
                // These native half-tile doorways have no standing room at a
                // tile centre. Search the same fine lattice as map exits, keeping
                // the arrival box inside the script's region. Visit nearby cells
                // first and retain only 64 points, rather than expanding every
                // point of a long coordinate guard on every capture.
                var best = new SortedSet<NavigationPoint>(Comparer<NavigationPoint>.Create((a, b) =>
                {
                    var order = Distance(a).CompareTo(Distance(b));
                    if (order != 0) return order;
                    order = a.X.CompareTo(b.X); if (order != 0) return order;
                    order = a.Y.CompareTo(b.Y); return order != 0 ? order : a.Layer.CompareTo(b.Layer);
                }));
                foreach (var point in points) best.Add(point);
                // The hall's north floor does not exist until its live marker
                // opens it. Publish native-copy goals for staged proof only;
                // FindStage must still reach that marker on the current map.
                var previews = field.SceneId == 600 && story is not null
                    ? field.Actors.Where(a => a.Index == 21 && a.ClassTag == 7 && a.IsUsable && a.ScriptCallsEnabled)
                        .SelectMany(a => GameNavigationCatalog.ActorInfo(600, a)?.Actions.Where(c =>
                            c.Touch && c.Kind == "Terrain" && c.Copy is not null &&
                            FieldTerrainGraph.ActionAvailable(600, 21, c, story)) ?? [])
                        .Select(c => FieldTerrainGraph.Preview(map, c.Copy!)).OfType<FieldMapSnapshot>()
                        .Select(m => new FieldNavigationGraph(m)).ToArray() : [];
                IEnumerable<NavigationPoint> DoorPoints(int x, int y)
                {
                    foreach (var point in At(x, y)) yield return point;
                    foreach (var preview in previews)
                    for (var layer = 1; layer <= 3; layer++)
                        if (preview.TryPosition(x, y, layer, out var point)) yield return point;
                }
                var cells = from y in Enumerable.Range(Math.Max(0, region.Top),
                        Math.Max(0, Math.Min(map.Height - 1, region.Bottom) - Math.Max(0, region.Top) + 1))
                    from x in Enumerable.Range(Math.Max(0, region.Left),
                        Math.Max(0, Math.Min(map.Width - 1, region.Right) - Math.Max(0, region.Left) + 1))
                    let distance = Math.Max(0L, Math.Max((long)x * 256 - player.X, player.X - ((long)x * 256 + 240))) +
                        Math.Max(0L, Math.Max((long)y * 256 - player.Y, player.Y - ((long)y * 256 + 240)))
                    orderby distance, y, x
                    select (x, y, distance);
                foreach (var (x, y, distance) in cells)
                {
                    if (best.Count == 64 && distance > Distance(best.Max)) break;
                    foreach (var oy in new[] { 0, 64, 112, 128, 192, 240 })
                    foreach (var ox in new[] { 0, 64, 112, 128, 192, 240 })
                    {
                        var px = x * 256 + ox; var py = y * 256 + oy;
                        if (px < region.Left * 256 + 32 || px > (region.Right + 1) * 256 - 33 ||
                            py < region.Top * 256 + 32 || py > (region.Bottom + 1) * 256 - 33) continue;
                        foreach (var point in DoorPoints(px, py).Where(p => !graph.IsTerminal(p)))
                        {
                            best.Add(point);
                            if (best.Count > 64) best.Remove(best.Max);
                        }
                    }
                }
                return best.ToArray();
            }
            for (var y = Math.Max(0, region.Top); y <= Math.Min(map.Height - 1, region.Bottom); y++)
            for (var x = Math.Max(0, region.Left); x <= Math.Min(map.Width - 1, region.Right); x++)
                points.AddRange(At(x * 256 + 128, y * 256 + 128).Where(p => !graph.IsTerminal(p)));
            return points.OrderBy(Distance).Distinct().Take(64).ToArray();
        }
        /// <summary>Resolve the audited and script-proven actors that receive Confirm for this
        /// one, only while both native actors match their identities and permit their scripts
        /// to run. A generated stand-in may be an interaction marker, which is never drawn.</summary>
        IReadOnlyList<(FieldActorSnapshot Actor, bool SameDestination)> InteractionProxies(FieldActorSnapshot actor)
        {
            if (!field.SceneIdCoherent || !actor.IsUsable || !actor.IsDrawn || !InsideMap(actor) ||
                actor.IsPartyMember && !PartyTalk(actor) ||
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
        // 1760B0's activation list excludes the party, but Confirm's 17D230
        // scan skips only the leader. A loaded companion with its own native
        // Confirm dialogue (such as the Ioka feast) is a person to speak with.
        // Calls can be disabled temporarily during idle animations; that gates
        // Confirm readiness, not the visible companion's People entry.
        bool PartyTalk(FieldActorSnapshot actor) => field.SceneIdCoherent && actor.IsPlayerClass &&
            actor.VisualIndex is >= 0 and < 7 && (actor.LoadedFlag & 1) != 0 && actor.ScriptProcessingEnabled &&
            actor.Index != field.LeadPlayerActorIndex && actor.CurrentScriptOpcode is >= 0 and not 0xB0 &&
            GameNavigationCatalog.ActorInfo(field.SceneId, actor)?.Actions.Any(a =>
                !a.Touch && a.Kind == "Talk" && a.Available(story)) == true;
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
            if (field.SceneIdCoherent && LostSanctumTargets.PreciseRow(field.SceneId, actor))
                for (var dx = -384; dx <= 384; dx += 64)
                    if (FieldInteractionRange.FacingsWithin(actorX + dx, actorY, actor.FineX, actor.FineY,
                            actor.CollisionOffsetX, NavigationUnits.LocalStep / 8).Count != 0)
                        found.AddRange(At(actorX + dx, actorY).Where(point => !graph.IsTerminal(point)));
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
            if (GenoDomeTargets.IsPod(field.SceneId, actor))
            {
                // Atel0355 uses precise half-tile centres for all four pods.
                // Rounding them to a tile centre puts the goal in a pod wall.
                // This floor point below the live centre is inside 178980's
                // leading contact probe, including the arrival tolerance.
                return At((actor.FineX + 32) / 64 * 64, (actor.FineY + (actor.Index == 25 ? 288 : 160)) / 64 * 64)
                    .Where(p => !graph.IsTerminal(p)).Distinct().ToArray();
            }
            if (field.SceneId == 642 && actor.Index is 14 or 15)
            {
                // Atel0642 0320/0397 places equivalent warp contacts on both
                // sides of a narrow corridor. Their tile centres are walls.
                // The midpoint one native pixel below their head row is real
                // standing room. A south approach meets slot15; starting here
                // and facing either live centre meets that same passage.
                var peer = field.Actors.FirstOrDefault(a => a.Index == (actor.Index == 14 ? 15 : 14) &&
                    a.ClassTag == 7 && a.IsUsable && a.ScriptCallsEnabled &&
                    GameNavigationCatalog.ActorInfo(642,a)?.Actions.Any(c => c.Touch && c.Kind == "Warp" && c.Available(story)) == true);
                if (peer is null || Math.Abs((long)actor.FineX-peer.FineX) != 256 || actor.FineY != peer.FineY)
                    return [];
                var sx = (actor.FineX + peer.FineX) / 2;
                var sy = (actor.FineY + 32) / 64 * 64 - 64;
                return At(sx,sy).Where(p => !graph.IsTerminal(p)).Distinct().ToArray();
            }
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
                if (openedKeepEntrance is not null && graph.ExitAt(px, py) == 1)
                    for (var layer = 1; layer <= 3; layer++)
                        if (openedKeepEntrance.TryPosition(px, py, layer, out var point)) yield return point;
                if (openedOmenDoor is not null && graph.ExitAt(px, py) == 1 &&
                    openedOmenDoor.TryPosition(px, py, 1, out var omenPoint)) yield return omenPoint;
                if (openedSanctumDoors.TryGetValue(graph.ExitAt(px, py), out var sanctumDoor))
                    for (var layer = 1; layer <= 3; layer++)
                        if (sanctumDoor.TryPosition(px, py, layer, out var sanctumPoint)) yield return sanctumPoint;
            }
        }
        void Add(string id, string label, NavigationCategory category, NavigationPoint position,
            IReadOnlyList<NavigationPoint> approaches, bool visible, bool storyOnly = false, bool storyCandidate = true,
            bool guideAvailable = false, string? instruction = null, string? arrivalInstruction = null,
            NavigationPoint? contactPosition = null, ConfirmRule? confirm = null, bool followUntilInteraction = false,
            string? guidanceRestriction = null, int? actorSlot = null,
            (int Minimum, int Maximum)? arrivalXRange = null, string? arrivalAnnouncement = null,
            NavigationHold? arrivalHold = null, bool passageOpening = false, bool keepInteractionReach = false,
            IReadOnlyList<NavigationPoint>? preferredApproaches = null, NavigationDirection contactDirection = NavigationDirection.None,
            string? openingCompleteAnnouncement = null)
        {
            activeIds.Add(id);
            if (storyCandidate) storyCandidates.Add(new(id, label, category, position, approaches, visible,
                visible || discovered.ContainsKey(id))
                {
                    Instruction = instruction, ArrivalInstruction = arrivalInstruction, ContactPosition = contactPosition,
                    ContactDirection = contactDirection,
                    ArrivalXRange = arrivalXRange,
                    ArrivalAnnouncement = arrivalAnnouncement,
                    ArrivalHold = arrivalHold, PassageOpening = passageOpening, OpeningCompleteAnnouncement = openingCompleteAnnouncement,
                    GuidanceRestriction = guidanceRestriction, ActorSlot = actorSlot,
                    ConfirmFacings = confirm?.Ready, ConfirmPending = confirm?.Pending,
                    FollowUntilInteraction = followUntilInteraction,
                    KeepInteractionReach = keepInteractionReach,
                    PreferredApproachPoints = preferredApproaches ?? [],
                });
            var output = storyOnly ? storyAnchors : targets;
            if (visible || guideAvailable)
            {
                var target = new NavigationTarget(id, label, category, position, approaches, visible,
                    visible || discovered.ContainsKey(id))
                {
                    GuideAvailable = guideAvailable, Instruction = instruction, ArrivalInstruction = arrivalInstruction,
                    ContactDirection = contactDirection,
                    ArrivalXRange = arrivalXRange,
                    ArrivalAnnouncement = arrivalAnnouncement,
                    ArrivalHold = arrivalHold, PassageOpening = passageOpening, OpeningCompleteAnnouncement = openingCompleteAnnouncement,
                    GuidanceRestriction = guidanceRestriction, ActorSlot = actorSlot,
                    ContactPosition = contactPosition, ConfirmFacings = confirm?.Ready, ConfirmPending = confirm?.Pending,
                    FollowUntilInteraction = followUntilInteraction,
                    KeepInteractionReach = keepInteractionReach,
                    PreferredApproachPoints = preferredApproaches ?? [],
                };
                if (visible) discovered[id] = target;
                output.Add(target);
            }
            // A remembered target keeps its discovered geometry, but its Confirm readiness is
            // always this capture's (current position, scan gate, contact), never the old one's.
            else if (discovered.TryGetValue(id, out var known)) output.Add(known with
            {
                Visible = false, Discovered = true,
                Label = TemporalLabTargets.RememberedLabel(field.SceneId, id) ?? known.Label,
                ContactDirection = contactDirection,
                ArrivalHold = arrivalHold, PassageOpening = passageOpening, OpeningCompleteAnnouncement = openingCompleteAnnouncement,
                GuidanceRestriction = guidanceRestriction, ActorSlot = actorSlot,
                ConfirmFacings = confirm?.Ready, ConfirmPending = confirm?.Pending,
                FollowUntilInteraction = followUntilInteraction,
                KeepInteractionReach = keepInteractionReach,
                PreferredApproachPoints = preferredApproaches ?? [],
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
        /// actor the scan tests first could be accepted from any point of that box with
        /// the same facing (outward, exact carry-aware bounds). Planning assumes the target's own
        /// scan gate, a camera-cull state, will be on once the player is there; arrival does not.
        /// Every scanned actor can win, including busy companions and followers;
        /// their positions and dispatch gates are checked again live at arrival.</summary>
        bool ConfirmGoal(NavigationPoint goal, FieldActorSnapshot[] anchors)
        {
            const int tolerance = NavigationUnits.LocalStep / 8;
            var leader = field.LeadPlayerActorIndex;
            foreach (var anchor in anchors)
            foreach (var facing in FieldInteractionRange.FacingsWithin(goal.X, goal.Y, anchor.FineX, anchor.FineY,
                         anchor.CollisionOffsetX, tolerance))
                if (!field.Actors.Any(other => anchors.All(a => a.Index != other.Index) &&
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
