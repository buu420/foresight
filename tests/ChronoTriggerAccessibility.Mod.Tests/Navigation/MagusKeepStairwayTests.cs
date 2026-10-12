using System.Runtime.CompilerServices;
using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Magus's Keep Exterior Stairways 176 and 446 on the installed initial maps
/// (magus-keep-stairways-0356.json). Player and creature positions are from the 2026-10-12
/// tester log at each "No route" announcement: the terrain route to (6256,1008) exists, and
/// one wandering enemy inside the stair corridor closes it under native contact (178980).
/// Removing any other logged actor leaves the route closed.</summary>
public sealed class MagusKeepStairwayTests
{
    private static readonly Dictionary<int, FieldMapSnapshot> Installed = Load();
    private static readonly NavigationPoint Goal = new(6256, 1008, 1);

    public static TheoryData<int, int, int, int, int, int> LoggedBlocks => new()
    {
        // scene, player x/y, creature actor, creature x/y (class 5 enemies 0x10 and 0xF2)
        { 176, 4688, 7423, 17, 4137, 7415 },
        { 176, 4016, 7423, 16, 6045, 5115 },
        { 176, 5056, 4319, 16, 5597, 4145 },
        { 446, 4048, 8959, 18, 5212, 6131 },
        { 446, 3488, 6911, 18, 3974, 4604 },
        { 446, 4336, 4543, 18, 4880, 2127 },
        { 446, 4544, 4415, 18, 6082, 3707 },
    };

    [Theory]
    [MemberData(nameof(LoggedBlocks))]
    public void AWanderingCreatureClosesOnlyTheLiveRoute(int scene, int x, int y, int creature, int cx, int cy)
    {
        var graph = Graph(scene, Creature(creature, scene == 176 ? 0x10 : 0xF2, cx, cy));
        Assert.Null(Search(graph, x, y));
        var open = Assert.IsAssignableFrom<INavigationGraph>(graph.WithoutCreatures);
        Assert.NotNull(Search(open, x, y));
    }

    [Fact]
    public void APersonStandingInTheSameCorridorIsNotTreatedAsAPassingCreature()
    {
        var graph = Graph(446, Creature(18, 0x52, 3974, 4604) with { ClassTag = 4 });
        Assert.Null(Search(graph, 3488, 6911));
        Assert.Null(graph.WithoutCreatures);
    }

    [Fact]
    public void ScriptPassagesAndLandingsKeepTheCreatureFreeView()
    {
        var graph = Graph(446, Creature(18, 0xF2, 3974, 4604));
        var wrapped = FieldTransitionGraph.Create(FieldLandingGraph.Create(
            new ScriptPassageGraph(graph, [("script:1", 0, 0, 0, 0)]), Installed[446] with { PlayerLayer = 1 },
            GameNavigationCatalog.ForScene(446)), []);
        Assert.Null(Search(wrapped, 3488, 6911));
        Assert.NotNull(Search(Assert.IsAssignableFrom<INavigationGraph>(wrapped.WithoutCreatures), 3488, 6911));
    }

    [Fact]
    public void WalkingWaitsAtTheLoggedBlockAndNamesTheCreatureInTheWay()
    {
        var creature = Creature(18, 0xF2, 3974, 4604);
        var player = new NavigationPoint(3488, 6911, 1);
        var stairs = new NavigationTarget("story:full:keep-ozzie", "Follow the entrance-hall warp", NavigationCategory.StoryEvents,
            Goal, [Goal], true, true);
        var blocker = new NavigationTarget("actor-encounter:18", "Encounter", NavigationCategory.Enemies,
            new(creature.FineX, creature.FineY, 1), [], true, true) { ActorSlot = 18 };
        var frame = new NavigationFrame("keep-446", true, player, [stairs, blocker], Graph(446, creature), NavigationUnits.LocalStep);
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        var result = controller.Handle(NavigationCommand.ToggleWalk, frame, 0);

        Assert.DoesNotContain(result.Speech, s => s.Contains("No route", StringComparison.Ordinal));
        Assert.Contains("Encounter is blocking the way. Guidance continues when the way clears.", result.Speech);
        Assert.True(result.AutoWalking);

        // The creature wanders off the stairs; the walk resumes from the same position.
        var moved = frame with { Targets = [stairs], Graph = Graph(446, creature with { FineX = 7552, TileX = 29, FineY = 6911, TileY = 26 }) };
        result = controller.Update(moved, 600);
        Assert.Contains("The way is clear.", result.Speech);
        Assert.NotEqual(NavigationDirection.None, result.Direction);
    }

    [Fact]
    public void InTheCentralChamberOzzieIsNamedAsTheOneBlockingTheCorridor()
    {
        // Log 01:52:52-01:53:48: from (10880,1945) both the warp and the Trap 1 exit had no
        // route. Actor 10 (load_enemy A6) stood at (10112,1791) until his talk handler ran.
        var ozzie = Creature(10, 0xA6, 10112, 1791);
        var goal = new NavigationPoint(10112, 1264, 1);
        var target = new NavigationTarget("story:full:keep-ozzie", "Follow the entrance-hall warp", NavigationCategory.StoryEvents,
            goal, [goal], true, true);
        var person = new NavigationTarget("actor:10:5:166", "Ozzie", NavigationCategory.People, new(10112, 1791, 1), [], true, true)
            { ActorSlot = 10 };
        var graph = Graph(170, ozzie);
        Assert.Null(NavigationPathfinder.Search(graph, new(10880, 1945, 1), [goal]).Route);
        var frame = new NavigationFrame("keep-170", true, new(10880, 1945, 1), [target, person], graph, NavigationUnits.LocalStep);
        var controller = new NavigationController();
        for (var i = 0; i < 3; i++) controller.Handle(NavigationCommand.NextCategory, frame, 0);
        var result = controller.Handle(NavigationCommand.ToggleWalk, frame, 0);
        Assert.Contains("Ozzie is blocking the way. Guidance continues when the way clears.", result.Speech);
        Assert.True(result.AutoWalking);
    }

    [Fact]
    public void TheLiveGraphReportsTheExactNativeContactSlotAtTheCut()
    {
        var graph = Graph(446, Creature(18, 0xF2, 3974, 4604));
        var through = Assert.IsAssignableFrom<IReadOnlyList<NavigationPoint>>(Search(graph.WithoutCreatures!, 3488, 6911));
        var cut = Enumerable.Range(0, through.Count - 1).First(i => !graph.Neighbours(through[i]).Contains(through[i + 1]));
        Assert.Equal(18, ((INavigationGraph)graph).BlockingActor(through[cut], through[cut + 1]));
        Assert.Null(graph.WithoutCreatures!.BlockingActor(through[cut], through[cut + 1]));
    }

    [Fact]
    public void AnUndrawnBlockerOnTheStairsIsNotRevealed()
    {
        // Native contact ignores the draw flag, so the wait stays; the speech must not
        // name or even call it a creature.
        var hidden = Creature(18, 0xF2, 3974, 4604) with { DrawMode = 0 };
        var result = Walk(Graph(446, hidden), [new("actor-encounter:18", "Encounter", NavigationCategory.Enemies,
            new(hidden.FineX, hidden.FineY, 1), [], false, true) { ActorSlot = 18 }]);
        Assert.Contains("The way is temporarily blocked. Guidance continues when it clears.", result.Speech);
        Assert.DoesNotContain(result.Speech, s => s.Contains("Encounter", StringComparison.Ordinal) ||
            s.Contains("creature", StringComparison.OrdinalIgnoreCase));
        Assert.True(result.AutoWalking);
    }

    [Fact]
    public void AVisibleNeighbourOfTheBlockerIsNotBlamed()
    {
        var creature = Creature(18, 0xF2, 3974, 4604);
        var result = Walk(Graph(446, creature), [new("actor:12:4:52", "Person in a blue cap", NavigationCategory.People,
            new(creature.FineX + 128, creature.FineY, 1), [], true, true) { ActorSlot = 12 }]);
        Assert.Contains("The way is temporarily blocked. Guidance continues when it clears.", result.Speech);
        Assert.DoesNotContain(result.Speech, s => s.Contains("blue cap", StringComparison.Ordinal));
    }

    private static NavigationResult Walk(INavigationGraph graph, IReadOnlyList<NavigationTarget> others)
    {
        var stairs = new NavigationTarget("story:full:keep-ozzie", "Follow the entrance-hall warp", NavigationCategory.StoryEvents,
            Goal, [Goal], true, true);
        var frame = new NavigationFrame("keep-446", true, new(3488, 6911, 1), [stairs, .. others], graph, NavigationUnits.LocalStep);
        var controller = new NavigationController();
        for (var i = 0; i < 3; i++) controller.Handle(NavigationCommand.NextCategory, frame, 0);
        return controller.Handle(NavigationCommand.ToggleWalk, frame, 0);
    }

    private static IReadOnlyList<NavigationPoint>? Search(INavigationGraph graph, int x, int y) =>
        NavigationPathfinder.Search(graph, new NavigationPoint(x, y, 1), [Goal]).Route;

    private static FieldNavigationGraph Graph(int scene, params FieldActorSnapshot[] actors)
    {
        var player = FullGameNavigationTests.Actor(1, 0, 0, true) with { ClassTag = 0 };
        var field = FullGameNavigationTests.Field(scene) with { LeadPlayer = player, Actors = [player, .. actors] };
        return new FieldNavigationGraph(Installed[scene] with { PlayerLayer = 1 }, new FieldActorCollisionRules(field));
    }

    /// <summary>The logged live words: loaded, camera cache +20 = 128, +152 = 0.</summary>
    private static FieldActorSnapshot Creature(int index, int visual, int x, int y) =>
        new(index, x >> 8, x, x & 255, y >> 8, y, y & 255, 0, 1, 1, visual, 5, 0x2020, 0, 128, false, true, true, true);

    private static Dictionary<int, FieldMapSnapshot> Load([CallerFilePath] string source = "")
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(source)!, "magus-keep-stairways-0356.json")));
        return json.RootElement.GetProperty("Maps").EnumerateArray().ToDictionary(e => e.GetProperty("Scene").GetInt32(),
            e => JsonSerializer.Deserialize<FieldMapSnapshot>(e.GetProperty("Map").GetRawText())!);
    }
}
