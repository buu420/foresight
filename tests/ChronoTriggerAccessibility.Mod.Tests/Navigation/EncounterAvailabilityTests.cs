using System.Reflection;
using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class EncounterAvailabilityTests
{
    [Theory]
    [InlineData(200, 14, 100)]
    [InlineData(251, 8, 112)]
    public void ObjectContactBattleKeepsItsContactRouteAlongsideDialogueOrSwitchEffects(int scene, int index, int visual)
    {
        // Native Confirm is empty in both cases. Touch talks before D8 (200/14)
        // or sets a switch before D8 (251/8). Open terrain isolates dispatch;
        // it is not a replay of these maps' changing scenery.
        var actor = FullGameNavigationTests.Actor(index, 5 * 256 + 128, 5 * 256 + 255)
            with { ClassTag = 4, VisualIndex = visual };
        var frame = new FieldNavigationSource(new NoMemory(), _ => { }).Build(
            FullGameNavigationTests.Field(scene, actor), FullGameNavigationTests.Map(), new(0, 0, 256, 256), [],
            new FieldStoryState(100, false));
        var target = Assert.Single(frame.Targets, t => t.Id == $"actor:{index}:4:{visual}");
        Assert.Equal(NavigationCategory.Objects, target.Category);
        Assert.NotNull(target.ContactPosition);
        Assert.Null(target.ArrivalInstruction);
        Assert.NotNull(NavigationPathfinder.Find(frame.Graph, frame.Player, target.ApproachPoints));
        Assert.DoesNotContain(frame.Targets, t => t.Id == $"actor-encounter:{index}");
    }

    [Fact]
    public void ConfirmRemainsAvailableWhenTerrainBlocksEveryTouchGoal()
    {
        // Forest 119 actor14 supports both native handlers. A blocked contact
        // footprint must not discard a still-reachable Confirm interaction.
        var actor = FullGameNavigationTests.Actor(14, 5 * 256 + 128, 5 * 256 + 255)
            with { ClassTag = 7, DrawMode = 0, LoadedFlag = 0 };
        var map = FullGameNavigationTests.Map();
        foreach (var (x, y) in new[] { (5, 5), (5, 6), (6, 6) })
            map.CollisionLayers[y * map.Width + x] = 0;
        var state = new FieldStoryState(50, false) { Locals = new Dictionary<int, int> { [9] = 0 } };
        var frame = new FieldNavigationSource(new NoMemory(), _ => { }).Build(
            FullGameNavigationTests.Field(119, actor), map, new(0, 0, 256, 256), [], state);
        var target = Assert.Single(frame.Targets, IsEncounter14);
        Assert.Null(target.ContactPosition);
        Assert.Contains("Confirm", target.ArrivalInstruction);
        Assert.NotNull(NavigationPathfinder.Find(frame.Graph, frame.Player, target.ApproachPoints));
    }

    private static bool IsEncounter14(NavigationTarget target) => target.Id == "actor-encounter:14";

    [Fact]
    public void StagedEnemyIsUnavailableUntilItsNativeArrival()
    {
        // Denadoro map 5, actor 12: 8B(7,21), 91 hides its sprite;
        // that staging position is blocked terrain. Actor11 calls function3,
        // which shows it (90) and moves it (96) to (8,27) before the fight.
        Assert.DoesNotContain(Build(Trigger()).Targets, IsEncounter);
        var frame = Build(Arrived());
        var target = Assert.Single(frame.Targets, t => t.Id == "actor-encounter:12");
        Assert.Equal(NavigationCategory.Enemies, target.Category);
        Assert.True(target.GuideAvailable);
        Assert.False(target.Visible);
        Assert.NotNull(target.ContactPosition);
        Assert.NotNull(NavigationPathfinder.Find(frame.Graph, frame.Player, target.ApproachPoints));
    }

    [Fact]
    public void EncounterNeedsAnActiveMatchingNativeScript()
    {
        Assert.DoesNotContain(Build(Arrived() with { ScriptCallsEnabled = false }).Targets, IsEncounter);
        Assert.DoesNotContain(Build(Arrived() with { ClassTag = 0x85 }).Targets, IsEncounter);
        Assert.DoesNotContain(Build(Arrived() with { VisualIndex = 52 }).Targets, IsEncounter);
        Assert.DoesNotContain(Build(Arrived(), signalled: true).Targets, IsEncounter);
    }

    private static bool IsEncounter(NavigationTarget target) => target.Id == "actor-encounter:12";
    private static FieldActorSnapshot Trigger() => FullGameNavigationTests.Actor(12, 7 * 256 + 128, 21 * 256 + 255)
        with { ClassTag = 5, VisualIndex = 51, DrawMode = 0, LoadedFlag = 1 };
    private static FieldActorSnapshot Arrived() => FullGameNavigationTests.Actor(12, 8 * 256 + 128, 27 * 256 + 255)
        with { ClassTag = 5, VisualIndex = 51, DrawMode = 1, LoadedFlag = 1 };

    private static NavigationFrame Build(FieldActorSnapshot trigger, bool signalled = false)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.denadoro-146-map.json")!;
        var map = JsonSerializer.Deserialize<FieldMapSnapshot>(stream)!;
        var player = FullGameNavigationTests.Actor(1, 384, 7040, true) with { ClassTag = 0 };
        var controller = FullGameNavigationTests.Actor(0, 0, 0) with { ClassTag = 7, DrawMode = 0, LoadedFlag = 0 };
        var field = FullGameNavigationTests.Field(146) with
        { LeadPlayer = player, Actors = [controller, player, trigger] };
        var locals = Enumerable.Range(0, 256).ToDictionary(i => i, _ => 0);
        if (signalled) locals[8] = 1;
        var state = new FieldStoryState(100, false) { Locals = locals,
            Globals = Enumerable.Range(1, 511).ToDictionary(i => i, _ => 0) };
        if (!signalled && trigger.ScriptCallsEnabled && trigger.ClassTag == 5 && trigger.VisualIndex == 51)
            Assert.Contains(GameNavigationCatalog.ActorInfo(146, trigger)!.Actions,
                action => action.Kind == "Encounter" && action.Available(state));
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, map, new(0, 0, 256, 256), [], state);
    }
    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
