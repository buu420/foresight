using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;

if (args.Length != 2) throw new ArgumentException("Usage: NavigationAudit fixtures.jsonl output.jsonl");
using var output = new StreamWriter(args[1]);
var scenes = 0;
var scenesWithSearches = 0;
var searches = 0;
var failures = 0;
foreach (var line in File.ReadLines(args[0]))
{
    var fixture = JsonSerializer.Deserialize<Fixture>(line)!;
    if (fixture.Seeds.Length == 0) continue;
    var searchesBeforeScene = searches;
    var map = fixture.Map;
    var graph = new FieldNavigationGraph(map);
    // One representative per reachable entry neighborhood. This is not a
    // proof of every directed conveyor state or every dynamic scene variant.
    var covered = new HashSet<NavigationPoint>();
    foreach (var seed in fixture.Seeds)
    foreach (var layer in new[] { 1, 2 })
    {
        if (!graph.TryPosition(seed[0], seed[1], layer, out var start) ||
            graph.IsTerminal(start) || covered.Contains(start)) continue;
        var seen = new HashSet<NavigationPoint> { start };
        var queue = new Queue<NavigationPoint>(); queue.Enqueue(start);
        while (queue.TryDequeue(out var at))
        {
            if (graph.IsTerminal(at)) continue;
            foreach (var next in graph.Neighbours(at))
                if (seen.Add(next)) queue.Enqueue(next);
        }
        covered.UnionWith(seen);
        if (seen.Count < 16) continue;
        var player = new FieldActorSnapshot(1, start.X / 256, start.X, start.X % 256,
            start.Y / 256, start.Y, start.Y % 256, 0, 1, 1, 0, 0, 0, 0, 0, true, true, true, true);
        var field = new FieldNavigationSnapshot(0x1000, 0x4000, 0x2000, 0x20000, 1,
            fixture.Scene, true, 1, 0, 1, 1, player, [player]);
        var frame = new FieldNavigationSource(new NoMemory(), _ => { }).Build(field,
            map with { PlayerLayer = start.Layer }, new(0, 0, map.Width * 256, map.Height * 256), []);
        var reachableExits = seen.Select(p => graph.ExitAt(p.X, p.Y)).Where(id => id >= 0).Distinct().ToArray();
        foreach (var id in reachableExits)
        {
            searches++;
            var target = frame.Targets.FirstOrDefault(t => t.Id == $"exit:{id}");
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var result = target is null ? new NavigationSearchResult(null, false) :
                NavigationPathfinder.Search(frame.Graph, start, target.ApproachPoints);
            timer.Stop();
            if (result.Route is null) failures++;
            output.WriteLine(JsonSerializer.Serialize(new { fixture.Scene, fixture.Name, Start = start,
                Exit = id, ReachableNodes = seen.Count, Goals = target?.ApproachPoints.Count ?? 0,
                Steps = result.Route?.Count, result.LimitReached, Milliseconds = timer.Elapsed.TotalMilliseconds,
                GoalInReachableSet = target?.ApproachPoints.Any(seen.Contains) ?? false }));
        }
    }
    scenes++;
    if (searches > searchesBeforeScene) scenesWithSearches++;
    if (scenes % 50 == 0) { output.Flush(); Console.WriteLine($"{scenes} scenes; {searches} reachable exit searches; {failures} failures"); }
}
Console.WriteLine($"Finished: {scenes} scenes examined; {scenesWithSearches} scenes with exit searches; {searches} reachable exit searches; {failures} failures.");
return failures == 0 ? 0 : 1;

sealed record Fixture(int Scene, string? Name, int[][] Seeds, FieldMapSnapshot Map);
sealed class NoMemory : IReadableMemory
{ public bool TryRead(nuint address, Span<byte> destination) => false; }
