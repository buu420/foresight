using System.Reflection;
using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

// Actor identities, local gates and 8B positions come from Atel0113/0116.
// Map planes are the installed resources, not an open-room approximation.
public sealed class ForestEncounterTests
{
    [Theory]
    [InlineData(14, 37, 17, 9)]
    [InlineData(20, 27, 32, 10)]
    [InlineData(25, 7, 37, 11)]
    [InlineData(30, 8, 12, 12)]
    public void PastForestContactBattlesHaveRoutes(int actor, int x, int y, int flag)
    {
        var frame = Build(119, Marker(actor, x, y));
        var target = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Enemies);
        Assert.True(target.GuideAvailable);
        Assert.NotNull(NavigationPathfinder.Find(frame.Graph, frame.Player, target.ApproachPoints));
        Assert.DoesNotContain(Build(119, Marker(actor, x, y), flag).Targets,
            t => t.Category == NavigationCategory.Enemies);
    }

    [Fact]
    public void TrapAndItemKeepTheSameVisibleAppearanceUntilInteractedWith()
    {
        var trap = Marker(35, 40, 43) with { ClassTag = 4, VisualIndex = 112, DrawMode = 1 };
        var frame = Build(119, trap);
        var target = Assert.Single(frame.Targets, t => t.Id == "actor:35:4:112");
        Assert.Equal("Sparkle", target.Label);
        Assert.Equal(NavigationCategory.Objects, target.Category);
        Assert.DoesNotContain(frame.Targets, t => t.Category == NavigationCategory.Enemies);
        Assert.NotNull(NavigationPathfinder.Find(frame.Graph, frame.Player, target.ApproachPoints));
        var item = Marker(63, 40, 39) with { ClassTag = 4, VisualIndex = 112, DrawMode = 1 };
        var itemFrame = Build(119, item);
        Assert.DoesNotContain(itemFrame.Targets, t => t.Category == NavigationCategory.Enemies);
        Assert.Equal("Sparkle", Assert.Single(itemFrame.Targets, t => t.Id == "actor:63:4:112").Label);
    }

    [Fact]
    public void ConfirmOnlyAmbushHasAReachableConfirmPosition()
    {
        var frame = Build(119, Marker(52, 23, 18));
        var target = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Enemies);
        Assert.NotNull(NavigationPathfinder.Find(frame.Graph, frame.Player, target.ApproachPoints));
        Assert.Contains("Confirm", target.ArrivalInstruction);
    }

    [Theory]
    [InlineData(18, 37, 43)]
    [InlineData(21, 7, 37)]
    [InlineData(30, 8, 7)]
    public void PresentForestCreatureSignalsItsControllerBattle(int actor, int x, int y)
    {
        var creature = Marker(actor, x, y) with { ClassTag = 5, VisualIndex = 94, DrawMode = 1, LoadedFlag = 1 };
        var frame = Build(19, creature);
        var target = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Enemies);
        Assert.NotNull(NavigationPathfinder.Find(frame.Graph, frame.Player, target.ApproachPoints));
        Assert.DoesNotContain(frame.Targets, t => t.Category == NavigationCategory.People && t.Id == target.Id);
    }

    [Fact]
    public void DisabledContactCannotOfferAnEncounter()
    {
        Assert.DoesNotContain(Build(119, Marker(14, 37, 17) with { ScriptCallsEnabled = false }).Targets,
            t => t.Category == NavigationCategory.Enemies);
    }

    [Fact]
    public void EveryTrapApproachFinishesInsideTheNativeContactDetector()
    {
        var actor = Marker(35, 40, 43) with { ClassTag = 4, VisualIndex = 112, DrawMode = 1, LoadedFlag = 1 };
        var frame = Build(119, actor);
        var target = Assert.Single(frame.Targets, t => t.Id == "actor:35:4:112");
        Assert.DoesNotContain(frame.Targets, t => t.Category == NavigationCategory.Enemies);
        Assert.NotNull(target.ContactPosition);
        var native = new FieldActorCollisionRules(FullGameNavigationTests.Field(119, actor));
        foreach (var goal in target.ApproachPoints)
        foreach (var dx in new[] { -32, 0, 32 })
        foreach (var dy in new[] { -32, 0, 32 })
        {
            // Exercise the finish input at every corner of the arrival box,
            // independently of the path that brought the player into that box.
            var player = goal with { X = goal.X + dx, Y = goal.Y + dy };
            var arrivedFrame = frame with { Player = player, Targets = [target with { ApproachPoints = [player] }] };
            var controller = new NavigationController();
            controller.Handle(NavigationCommand.NextCategory, arrivedFrame, 0);
            controller.Handle(NavigationCommand.NextCategory, arrivedFrame, 0);
            var result = controller.Handle(NavigationCommand.ToggleWalk, arrivedFrame, 1);
            Assert.True(result.AutoWalking);
            var (mx, my) = result.Direction switch
            {
                NavigationDirection.North => (0, -32), NavigationDirection.South => (0, 32),
                NavigationDirection.West => (-32, 0), NavigationDirection.East => (32, 0), _ => (0, 0),
            };
            Assert.True(native.BlocksMove(player.X, player.Y, player.X + mx, player.Y + my),
                $"Native contact missed at {player}, toward {result.Direction}, goal {goal}.");
        }
    }

    [Fact]
    public void StoppedControllerCannotReceiveAnEncounterSignal()
    {
        var creature = Marker(18, 37, 43) with { ClassTag = 5, VisualIndex = 94, DrawMode = 1 };
        Assert.DoesNotContain(Build(19, creature, controllerRunning: false).Targets,
            t => t.Category == NavigationCategory.Enemies);
    }

    private static FieldActorSnapshot Marker(int index, int x, int y) =>
        FullGameNavigationTests.Actor(index, x * 256 + 128, y * 256 + 255) with
        { ClassTag = 7, VisualIndex = 0, DrawMode = 0, LoadedFlag = 0, ActivationEnabled = 0 };

    private static NavigationFrame Build(int scene, FieldActorSnapshot actor, int cleared = -1, bool controllerRunning = true)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            $"ChronoTriggerAccessibility.Mod.Tests.Navigation.guardia-forest-{scene}-map.json")!;
        var map = JsonSerializer.Deserialize<FieldMapSnapshot>(stream)!;
        // Inside the walkable middle of the south exit span (15..26,47).
        var player = FullGameNavigationTests.Actor(0, 20 * 256 + 128, 46 * 256 + 128, true) with { ClassTag = 0 };
        var controller = Marker(1, 0, 0) with { ScriptProcessingEnabled = controllerRunning };
        var field = FullGameNavigationTests.Field(scene) with { LeadPlayer = player, Actors = [player, controller, actor] };
        var locals = Enumerable.Range(0, 256).ToDictionary(i => i, _ => 0);
        if (cleared >= 0) locals[cleared] = 1;
        var state = new FieldStoryState(50, false) { Locals = locals,
            Globals = Enumerable.Range(1, 511).ToDictionary(i => i, _ => 0) };
        var frame = new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, map, new(0, 0, 256, 256), [], state);
        Assert.NotEmpty(frame.Graph.Neighbours(frame.Player));
        return frame;
    }
    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
