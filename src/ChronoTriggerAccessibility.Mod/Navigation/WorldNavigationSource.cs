using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Mod.Navigation;

public sealed class WorldNavigationSource(IReadableMemory memory, Action<string> diagnostic)
{
    private readonly NavigationTextCapture text = new(memory);
    private readonly Dictionary<string, NavigationTarget> discovered = new(StringComparer.Ordinal);
    private readonly WorldRegionIndex regions = new();
    private nuint imageBase;
    private string? scene, lastFailure, lastInventory;
    public string MotionStage { get; private set; } = "not sampled";
    public void BindImageBase(nuint value) => imageBase = value;
    public void Reset() { discovered.Clear(); scene = null; lastInventory = null; }
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

    public NavigationFrame Build(WorldNavigationSnapshot world, Func<int, string?> label)
    {
        var identity = $"world:{world.Motion.Context:X8}:{world.Motion.World}";
        if (scene != identity) { Reset(); scene = identity; }
        var player = new NavigationPoint(world.Motion.PixelX * 16, world.Motion.PixelY * 16, 1);
        var graph = new WorldNavigationGraph(world.Map, world.Properties);
        if (world.StoryPoint is not null) regions.Update(world.Map, world.Properties, graph);
        var targets = new List<NavigationTarget>();
        var storyCandidates = new List<NavigationTarget>();
        var active = new HashSet<string>(StringComparer.Ordinal);
        var destinations = new Dictionary<string, int>(StringComparer.Ordinal);
        var groups = world.Entrances.Where(e => e.Available && e.NameIndex is > 0 and < 106 &&
                e.TileX is >= 0 and < 96 && e.TileY is >= 0 and < 64 && e.Destination is >= 0 and <= 511)
            .GroupBy(e => (e.NameIndex, e.Destination, e.Facing, e.DestinationX, e.DestinationY));
        foreach (var group in groups)
        {
            var key = $"world-exit:{group.Key.NameIndex}:{group.Key.Destination}:{group.Key.Facing}:{group.Key.DestinationX}:{group.Key.DestinationY}";
            active.Add(key); destinations[key] = group.Key.Destination;
            var visible = group.Any(e => world.IsVisible((e.TileX * 16 + 8) * 16, e.TileY * 16 * 16));
            discovered.TryGetValue(key, out var known);
            var points = group.SelectMany(e => e.ContactPoints).Select(p => new NavigationPoint(p.X * 16, p.Y * 16, 1))
                .Where(graph.CanStand).Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).Take(64).ToArray();
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
        AddStory();
        var inventory = $"world={world.Motion.World}; exits={targets.Count(t => t.Category == NavigationCategory.Exits)}; " +
            $"storyEvents={targets.Count(t => t.Category == NavigationCategory.StoryEvents)}; nativeExits={world.Entrances.Count}; story={world.StoryPoint}";
        if (inventory != lastInventory)
        {
            lastInventory = inventory;
            diagnostic($"World navigation: {inventory}; context=0x{world.Motion.Context:X}; viewport={world.Viewport}.");
        }
        var era = world.EraMessageIndex > 0 ? label(world.EraMessageIndex) : null;
        if (era?.All(c => char.IsWhiteSpace(c) || c == '?') == true) era = "unknown era";
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
}
