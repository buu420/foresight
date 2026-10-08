using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Mod.Navigation;

public sealed class WorldNavigationSource(IReadableMemory memory, Action<string> diagnostic)
{
    private readonly NavigationTextCapture text = new(memory);
    private readonly Dictionary<string, NavigationTarget> discovered = new(StringComparer.Ordinal);
    private readonly WorldRegionIndex regions = new();
    private readonly FullStoryTargets fullStory = new();
    private readonly NavigationExitLabels exitLabels = new();
    private readonly VehicleStoryRouting vehicleRouting = new();
    private readonly VehicleFlightRegions flightRegions = new();
    private nuint imageBase;
    private string? scene, lastFailure, lastInventory, lastFlightFailure, lastFlightInventory;
    public string MotionStage { get; private set; } = "not sampled";
    public string FlightStage { get; private set; } = "not sampled";
    public void BindImageBase(nuint value) => imageBase = value;
    public void Reset() { discovered.Clear(); exitLabels.Reset(); scene = null; lastInventory = null; lastFlightInventory = null; }
    public FootstepFrame? CaptureMotion(nint context)
    {
        var motion = WorldNavigationCapture.Motion(memory, imageBase, (nuint)context, out var stage);
        MotionStage = stage;
        return motion is null ? null : new(((ulong)motion.Context << 32) | motion.ScriptData,
            496 + motion.World, motion.Actor, motion.PixelX * 16, motion.PixelY * 16, NavigationUnits.WorldStep);
    }
    public NavigationFrame? Capture(nint context)
    {
        var value = WorldNavigationCapture.Capture(memory, imageBase, (nuint)context, out var stage);
        if (value is null)
        {
            if (lastFailure != stage) diagnostic($"World navigation capture unavailable: {stage}.");
            lastFailure = stage; return null;
        }
        lastFailure = null;
        return Build(value, index => text.WorldName(imageBase, index));
    }

    /// <summary>Parked vehicles and the game's boarding/landing prompt flags, or null
    /// when the context is not the live world.</summary>
    public WorldVehicleState? CaptureVehicles(nint context) =>
        VehicleNavigationCapture.State(memory, imageBase, (nuint)context);

    /// <summary>True only at a boundary where this vehicle is the player's current flying
    /// transport. A parked or foreign vehicle task never qualifies.</summary>
    public bool IsVehicleActive(nint context, VehicleKind kind)
    {
        var motion = VehicleNavigationCapture.Motion(memory, imageBase, (nuint)context, kind, out var stage);
        FlightStage = stage;
        return motion is not null;
    }

    public NavigationFrame? CaptureFlight(nint context, VehicleKind kind)
    {
        var value = WorldNavigationCapture.CaptureFlight(memory, imageBase, (nuint)context, kind, out var stage);
        FlightStage = stage;
        if (value is null)
        {
            if (lastFlightFailure != stage) diagnostic($"Vehicle navigation capture unavailable: {stage}.");
            lastFlightFailure = stage; return null;
        }
        lastFlightFailure = null;
        return BuildFlight(value, index => text.WorldName(imageBase, index));
    }

    public NavigationFrame Build(WorldNavigationSnapshot world, Func<int, string?> label)
    {
        var identity = $"world:{world.Motion.Context:X8}:{world.Motion.World}";
        if (scene != identity) { Reset(); scene = identity; }
        var player = new NavigationPoint(world.Motion.PixelX * 16, world.Motion.PixelY * 16, 1);
        NavigationPoint? walkingEndpoint = world.WalkingEndpoint is { } end ? new(end.X * 16, end.Y * 16, 1) : null;
        var graph = new WorldNavigationGraph(world.Map, world.Properties, walkingEndpoint, walkingEndpoint);
        if (world.StoryPoint is not null || world.Vehicles is not null) regions.Update(world.Map, world.Properties, graph);
        var targets = new List<NavigationTarget>();
        var storyCandidates = new List<NavigationTarget>();
        var active = new HashSet<string>(StringComparer.Ordinal);
        var destinations = new Dictionary<string, int>(StringComparer.Ordinal);
        var groups = world.Entrances.Where(e => e.Available && e.NameIndex is > 0 and < 106 &&
                e.TileX is >= 0 and < 96 && e.TileY is >= 0 and < 64 && GameNavigationCatalog.IsFieldScene(e.Destination))
            .GroupBy(e => (e.NameIndex, e.Destination, e.Facing, e.DestinationX, e.DestinationY));
        foreach (var group in groups)
        {
            var key = $"world-exit:{group.Key.NameIndex}:{group.Key.Destination}:{group.Key.Facing}:{group.Key.DestinationX}:{group.Key.DestinationY}";
            active.Add(key); destinations[key] = group.Key.Destination;
            var visible = group.Any(e => world.IsVisible((e.TileX * 16 + 8) * 16, e.TileY * 16 * 16));
            discovered.TryGetValue(key, out var known);
            var points = group.SelectMany(e => e.ContactPoints).Select(p => new NavigationPoint(p.X * 16, p.Y * 16, 1))
                .Select(graph.ContactPoint).Where(graph.CanStand).Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).Take(64).ToArray();
            var name = label(group.Key.NameIndex);
            if (string.IsNullOrWhiteSpace(name)) continue;
            var first = group.First();
            var position = points.Length == 0 ? new NavigationPoint((first.TileX * 16 + 8) * 16, first.TileY * 256, 1) :
                points.MinBy(p => Math.Abs((long)p.X - player.X) + Math.Abs((long)p.Y - player.Y));
            var target = new NavigationTarget(key, name, NavigationCategory.Exits, position, points, visible, visible || known is not null)
            { Instruction = "Press Confirm to enter.", ArrivalInstruction = "Press Confirm to enter." };
            storyCandidates.Add(target);
            if (OptionalGuideAreas.WorldDestination(world.Motion.World, group.Key.Destination, world.StoryPoint))
            {
                var reachable = points.Where(p => regions.Connected(player, p)).ToArray();
                if (reachable.Length != 0)
                {
                    target = target with { GuideAvailable = true, ApproachPoints = reachable,
                        Position = reachable.MinBy(p => Math.Abs((long)p.X - player.X) + Math.Abs((long)p.Y - player.Y)) };
                    if (visible) discovered[key] = target;
                    targets.Add(target);
                    continue;
                }
            }
            if (!visible)
            {
                if (known is null) continue;
                var retained = known!.ApproachPoints.Where(points.Contains).ToArray();
                if (retained.Length == 0) { discovered.Remove(key); continue; }
                targets.Add(known with { Visible = false, Discovered = true, ApproachPoints = retained });
                continue;
            }
            discovered[key] = target; targets.Add(target);
        }
        foreach (var key in discovered.Keys.Where(key => !active.Contains(key)).ToArray()) discovered.Remove(key);
        var vehicles = ParkedVehicles(world, player, graph);
        targets.AddRange(vehicles);
        AddStory();
        var inventory = $"world={world.Motion.World}; exits={targets.Count(t => t.Category == NavigationCategory.Exits)}; " +
            $"storyEvents={targets.Count(t => t.Category == NavigationCategory.StoryEvents)}; vehicles={vehicles.Count}; " +
            $"nativeExits={world.Entrances.Count}; story={world.StoryPoint}";
        if (inventory != lastInventory)
        {
            lastInventory = inventory;
            diagnostic($"World navigation: {inventory}; context=0x{world.Motion.Context:X}; viewport={world.Viewport}.");
        }
        var era = world.EraMessageIndex > 0 ? label(world.EraMessageIndex) : null;
        if (era?.All(c => char.IsWhiteSpace(c) || c == '?') == true) era = "unknown era";
        exitLabels.Apply(targets);
        return new(identity, true, player, targets.AsReadOnly(), graph, NavigationUnits.WorldStep)
        { AreaName = string.IsNullOrWhiteSpace(era) ? "World map" : $"World map, {era}" };

        void Bind(string id, string name, params int[] sceneIds)
        {
            var eligible = storyCandidates.Where(t => t.Category == NavigationCategory.Exits &&
                destinations.TryGetValue(t.Id, out var destination) && sceneIds.Contains(destination)).ToArray();
            eligible = eligible.Select(t => t with { ApproachPoints = t.ApproachPoints
                    .Where(p => regions.Connected(player, p)).ToArray() }).Where(t => t.ApproachPoints.Count != 0)
                    .Select(t => t with { Position = t.ApproachPoints.MinBy(p => Math.Abs((long)p.X - player.X) + Math.Abs((long)p.Y - player.Y)) })
                    .ToArray();
            if (eligible.Length == 0) return;
            var anchor = eligible.MinBy(t => Math.Abs((long)t.Position.X - player.X) + Math.Abs((long)t.Position.Y - player.Y))!;
            targets.Add(anchor with { Id = "world-story:" + id, Label = name, Category = NavigationCategory.StoryEvents,
                GuideAvailable = true,
                ApproachPoints = eligible.SelectMany(t => t.ApproachPoints).Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).Take(64).ToArray() });
        }
        void AddStory()
        {
            if (world.StoryPoint is not { } progress) return;
            if (progress >= FullStoryObjectives.FirstPoint)
            {
                var usable = storyCandidates.Select(t => t with
                    { ApproachPoints = t.ApproachPoints.Where(p => regions.Connected(player, p)).ToArray() })
                    .Where(t => t.ApproachPoints.Count != 0).ToArray();
                var story = world.Story ?? new FieldStoryState(progress, false);
                var hasVehicle = vehicles.Any(v => v.ApproachPoints.Count != 0);
                var bound = fullStory.Build(496 + world.Motion.World, story, usable, player, destinations,
                    fieldOnly: hasVehicle);
                // The Omen has a native vehicle contact task rather than a ground
                // entrance. Its live presence permits a same-era flight binding.
                if (world.Vehicles?.BlackOmen is not null) destinations["vehicle:black-omen"] = 449;
                // A parked vehicle is the fallback when no native passage reaches the
                // objective from this landmass or from this era.
                var routed = vehicleRouting.Reroute(bound, world.Motion.World, story, vehicles,
                    world.Vehicles?.EpochWings == true, destinations, player, label).ToList();
                if (hasVehicle && !vehicles.Any(v => v.Id == VehicleStoryRouting.EpochId && v.ApproachPoints.Count != 0))
                {
                    // Dactyls can solve a local flight leg but cannot change era.
                    // Keep a real Gate route when no local walk/flight bound it.
                    foreach (var fallback in fullStory.Build(496 + world.Motion.World, story, usable, player, destinations))
                    {
                        var index = routed.FindIndex(t => t.Id == fallback.Id);
                        if (index < 0) routed.Add(fallback);
                        else if (routed[index].IsStoryNote) routed[index] = fallback;
                    }
                }
                targets.AddRange(routed);
                return;
            }
            if (world.Motion.World == 2 && progress is >= 51 and < 72)
            {
                if (progress < 55)
                {
                    Bind("future-site16", "Cross Site 16 toward Arris Dome", 212);
                    Bind("future-arris", "Visit Arris Dome", 214);
                }
                else if (progress < 60)
                {
                    Bind("future-site32", "Cross Site 32 toward Proto Dome", 223);
                    Bind("future-proto", "Visit Proto Dome", 226);
                }
                else if (progress < 63) Bind("future-factory", "Restore power at the Derelict Factory", 228);
                else Bind("future-return-proto", "Return to Proto Dome", 226);
                return;
            }
            if (world.Motion.World == 0)
            {
                if (progress is >= 3 and < 12) Bind("fair", "Go to the fair at Leene Square", 5);
                if (progress is >= 39 and < 42)
                {
                    Bind("forest", "Go through Guardia Forest", 19);
                    Bind("castle", "Take Marle to Guardia Castle", 21);
                }
            }
            if (world.Motion.World != 1) return;
            if (progress is >= 12 and < 18 || progress is >= 27 and < 33)
            {
                Bind("forest", "Go through Guardia Forest", 119);
                Bind("castle", progress >= 27 ? "Return to Guardia Castle" : "Visit Guardia Castle", 120);
            }
            if (progress is >= 18 and < 27) Bind("cathedral", "Investigate the cathedral", 129);
            if (progress is >= 33 and < 39) Bind("canyon", "Return to Truce Canyon", 112);
        }
    }

    /// <summary>Parked vehicles in this era as Interactable Objects at their exact native
    /// pixel positions. Approach points use the game's boarding shapes, including
    /// when the party's attainable lattice does not pass through the sprite center.</summary>
    private List<NavigationTarget> ParkedVehicles(WorldNavigationSnapshot world, NavigationPoint player, WorldNavigationGraph graph)
    {
        var result = new List<NavigationTarget>();
        if (world.Vehicles is not { } vehicles || vehicles.Transport != 0) return result;
        if (vehicles.EpochPresent) Add(VehicleStoryRouting.EpochId, VehicleStoryRouting.EpochLabel, vehicles.EpochX, vehicles.EpochY, vehicles.EpochBoardingShape);
        if (vehicles.DactylsPresent) Add(VehicleStoryRouting.DactylId, VehicleStoryRouting.DactylLabel, vehicles.DactylX, vehicles.DactylY, vehicles.DactylBoardingShape);
        return result;

        void Add(string id, string name, int pixelX, int pixelY, VehicleContactShape? shape)
        {
            var position = new NavigationPoint(pixelX * 16, pixelY * 16, 1);
            var lattice = graph.ContactPoint(position);
            var candidates = vehicles.PartyBoardingShape is { } party && shape is not null
                ? VehicleContactGeometry.BoardingApproaches(new(pixelX, pixelY), party, shape, lattice)
                : position == lattice ? [position] : Enumerable.Empty<NavigationPoint>();
            var approach = candidates.Where(graph.CanStand).Where(p => regions.Connected(player, p)).ToArray();
            result.Add(new(id, name, NavigationCategory.Objects, position, approach,
                world.IsVisible(position.X, position.Y), true)
            {
                GuideAvailable = approach.Length != 0,
                Instruction = "Press Confirm to board.", ArrivalInstruction = "Press Confirm to board.",
            });
        }
    }

    /// <summary>A flight frame. Destinations are the same enabled entrances, but each
    /// approach point is a legal landing site walk-connected to that entrance.</summary>
    public NavigationFrame BuildFlight(WorldNavigationSnapshot world, Func<int, string?> label)
    {
        if (world.Vehicle is not { } vehicle) throw new ArgumentException("A flight frame requires the vehicle motion.", nameof(world));
        var kind = vehicle.Kind;
        var identity = $"world:{world.Motion.Context:X8}:{world.Motion.World}:{(kind == VehicleKind.Epoch ? "epoch" : "dactyl")}";
        if (scene != identity) { Reset(); scene = identity; }
        var player = new NavigationPoint(vehicle.PixelX * 16, vehicle.PixelY * 16, 1);
        var walking = new WorldNavigationGraph(world.Map, world.Properties);
        var endpoint = vehicle.SegmentEnd is { } end ? new NavigationPoint(end.X * 16, end.Y * 16, 1) : player;
        VehicleFlightGraph graph = kind == VehicleKind.Epoch ? new EpochFlightGraph(endpoint, endpoint) :
            new DactylFlightGraph(world.Map, world.Properties, endpoint, endpoint);
        if (kind == VehicleKind.Dactyl) flightRegions.Update(world.Map, world.Properties, graph);
        WorldPixelPoint? other = kind == VehicleKind.Epoch
            ? world.Vehicles is { DactylsPresent: true } d ? new WorldPixelPoint(d.DactylX, d.DactylY) : null
            : world.Vehicles is { EpochPresent: true } e ? new WorldPixelPoint(e.EpochX, e.EpochY) : null;
        var movingShape = kind == VehicleKind.Epoch ? world.Vehicles?.EpochShape : world.Vehicles?.DactylShape;
        var parkedShape = kind == VehicleKind.Epoch ? world.Vehicles?.DactylShape : world.Vehicles?.EpochShape;
        var landable = VehicleLandingSites.Predicate(kind, world.Motion.World, world.Map, world.Properties, other, movingShape, parkedShape);
        bool CanLand(NavigationPoint point) => landable(point) && graph.CanFly(point) &&
            (kind == VehicleKind.Epoch || flightRegions.Connected(endpoint, point)) && !(kind == VehicleKind.Epoch && movingShape is not null &&
            world.Vehicles?.BlackOmen is { } omen && VehicleContactGeometry.BlackOmenContact(point.X / 16, point.Y / 16, movingShape, omen));
        var targets = new List<NavigationTarget>();
        var destinations = new Dictionary<string, int>(StringComparer.Ordinal);
        var groups = world.Entrances.Where(e => e.Available && e.NameIndex is > 0 and < 106 &&
                e.TileX is >= 0 and < 96 && e.TileY is >= 0 and < 64 && GameNavigationCatalog.IsFieldScene(e.Destination))
            .GroupBy(e => (e.NameIndex, e.Destination, e.Facing, e.DestinationX, e.DestinationY));
        var unreachable = 0;
        foreach (var group in groups)
        {
            var name = label(group.Key.NameIndex);
            if (string.IsNullOrWhiteSpace(name)) continue;
            var contacts = group.SelectMany(e => e.ContactPoints).Select(p => new NavigationPoint(p.X * 16, p.Y * 16, 1)).Distinct().ToArray();
            var sites = VehicleLandingSites.Near(contacts, walking, CanLand, endpoint)
                .Where(graph.CanFly).Distinct().Take(64).ToArray();
            if (sites.Length == 0) { unreachable++; continue; }
            var key = $"world-exit:{group.Key.NameIndex}:{group.Key.Destination}:{group.Key.Facing}:{group.Key.DestinationX}:{group.Key.DestinationY}";
            destinations[key] = group.Key.Destination;
            var visible = group.Any(e => world.IsVisible((e.TileX * 16 + 8) * 16, e.TileY * 16 * 16));
            var position = sites.MinBy(p => Math.Abs((long)p.X - player.X) + Math.Abs((long)p.Y - player.Y));
            targets.Add(new(key, name, NavigationCategory.Exits, position, sites, visible, true)
            {
                GuideAvailable = true,
                Instruction = "Fly there and land nearby, then walk in.",
                ArrivalInstruction = $"Press Confirm to land, then walk to {name}.",
            });
        }
        if (kind == VehicleKind.Epoch && world.Vehicles is { BlackOmen: { } blackOmen, EpochShape: { } epochShape } &&
            label(76) is { Length: > 0 } omenName)
        {
            var sites = VehicleContactGeometry.BlackOmenApproaches(blackOmen, epochShape, endpoint).Where(graph.CanFly)
                .OrderBy(p => Math.Abs((long)p.X - player.X) + Math.Abs((long)p.Y - player.Y)).Take(64).ToArray();
            if (sites.Length != 0)
            {
                const string key = "vehicle:black-omen";
                destinations[key] = 449;
                targets.Add(new(key, omenName, NavigationCategory.Exits, sites[0], sites,
                    world.IsVisible(blackOmen.X * 16, blackOmen.Y * 16), true)
                {
                    GuideAvailable = true,
                    Instruction = $"Fly to {omenName} and open its boarding choice.",
                    ArrivalInstruction = $"Press Confirm to open the {omenName} boarding choice.",
                });
            }
        }
        if (world.StoryPoint is { } progress && progress >= FullStoryObjectives.FirstPoint)
        {
            var story = world.Story ?? new FieldStoryState(progress, false);
            targets.AddRange(fullStory.Build(496 + world.Motion.World, story, targets.Where(t => t.Category == NavigationCategory.Exits).ToArray(),
                player, destinations, fieldOnly: true));
        }
        var inventory = $"world={world.Motion.World}; vehicle={kind}; landable={targets.Count(t => t.Category == NavigationCategory.Exits)}; " +
            $"unlandable={unreachable}; storyEvents={targets.Count(t => t.Category == NavigationCategory.StoryEvents)}; story={world.StoryPoint}";
        if (inventory != lastFlightInventory)
        {
            lastFlightInventory = inventory;
            diagnostic($"Vehicle navigation: {inventory}; context=0x{world.Motion.Context:X}; actor=0x{vehicle.ActorOffset:X}.");
        }
        var era = world.EraMessageIndex > 0 ? label(world.EraMessageIndex) : null;
        if (era?.All(c => char.IsWhiteSpace(c) || c == '?') == true) era = "unknown era";
        var ride = kind == VehicleKind.Epoch ? "flying the Epoch" : "riding the Dactyls";
        exitLabels.Apply(targets);
        return new(identity, true, player, targets.AsReadOnly(), graph, NavigationUnits.WorldStep)
        { AreaName = string.IsNullOrWhiteSpace(era) ? $"World map, {ride}" : $"World map, {era}, {ride}" };
    }
}
