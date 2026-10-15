using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class FactoryEntranceTests
{
    [Theory]
    [InlineData(57)]
    [InlineData(59)]
    public void EarlyFactoryVisitDoesNotNameAnUnintroducedCharacter(int point)
    {
        var frame = Frame(2560, 6399, point: point, roster: [0, 1, 2, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80]);
        var terminal = Assert.Single(frame.Targets, t => t.Id == "actor:8:4:127");
        Assert.DoesNotContain("Robo", terminal.Instruction ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Robo", terminal.ArrivalInstruction ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(frame.Targets, t => t.Id == "story:future:factory-security");
    }

    [Fact]
    public void CapturedNativeRosterBindsTheConsoleWithItsControllerPresent()
    {
        var roster = StoryMemoryCapture.Party(new RosterMemory([0, 3, 1, 2, 0x80, 0x80, 0x80, 0x80, 0x80]), 0x4000);
        Assert.NotNull(roster);
        var state = new FieldStoryState(60, false) { Party = roster };
        Assert.True(new GameNavigationCatalog.Requirement("ActiveParty", 3, 0, 1, true).Allows(state));
        var frame = Frame(2560, 6399, roster: roster.ToArray());
        var terminal = Assert.Single(frame.Targets, t => t.Id == "actor:8:4:127");
        var story = Assert.Single(frame.Targets, t => t.Id == "story:future:factory-security");
        Assert.False(story.IsStoryNote);
        Assert.Equal(terminal.ApproachPoints, story.ApproachPoints);
        Assert.NotNull(NavigationPathfinder.Search(frame.Graph, frame.Player, story.ApproachPoints).Route);
        Assert.DoesNotContain(frame.Targets, t => t.Id == "actor-encounter:8" || t.Id == "actor-encounter:15");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void SharedMapStairOutsideTheEntranceDoesNotBecomeAnEntranceDestination(int flags)
    {
        var frame = Frame(2560, 6399, flags);
        Assert.DoesNotContain(frame.Targets, t => t.Id == "level-crossing:11:52");
    }

    [Theory]
    [InlineData(2560, 6399, NavigationCommand.Guide)]
    [InlineData(2560, 6399, NavigationCommand.ToggleWalk)]
    [InlineData(3264, 5247, NavigationCommand.Guide)]
    [InlineData(3264, 5247, NavigationCommand.ToggleWalk)]
    [InlineData(1296, 4959, NavigationCommand.Guide)]
    [InlineData(1296, 4959, NavigationCommand.ToggleWalk)]
    public void UnavailableRosterDoesNotHideTheDrawnNativeSecurityTerminal(int x, int y, NavigationCommand command)
    {
        var frame = Frame(x, y);
        var terminal = Assert.Single(frame.Targets, t => t.Id == "actor:8:4:127");
        Assert.Equal("Entrance security terminal", terminal.Label);
        Assert.Equal(NavigationCategory.Objects, terminal.Category);
        Assert.NotEmpty(terminal.ApproachPoints);
        var story = Assert.Single(frame.Targets, t => t.Id == "story:future:factory-security");
        Assert.False(story.IsStoryNote);
        Assert.Equal(terminal.ApproachPoints, story.ApproachPoints);
        Assert.All(story.ApproachPoints, p => Assert.NotNull(story.ConfirmAt(p)));
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, story.ApproachPoints);
        Assert.NotNull(route.Route);
        Assert.True(FieldInteractionRange.ReachesWithin(route.Route[^1].X, route.Route[^1].Y, 2432, 4864, 32));
        foreach (var target in new[] { terminal, story })
        {
            var controller = new NavigationController();
            var selected = frame with { Targets = [target] };
            for (var i = 0; i < (int)target.Category; i++) controller.Handle(NavigationCommand.NextCategory, selected, 0);
            var result = controller.Handle(command, selected, 1);
            Assert.True(result.Guiding);
            Assert.Equal(command == NavigationCommand.ToggleWalk, result.AutoWalking);
            Assert.False(controller.AllowsConfirmWhileFollowing(selected));
        }
    }

    [Theory]
    [InlineData("landmark:9", NavigationCommand.Guide)]
    [InlineData("landmark:9", NavigationCommand.ToggleWalk)]
    [InlineData("landmark:10", NavigationCommand.Guide)]
    [InlineData("landmark:10", NavigationCommand.ToggleWalk)]
    public void SecurityBlockedLiftsExplainTheirPrerequisite(string id, NavigationCommand command)
    {
        var frame = Frame(2560, 6399);
        var lift = Assert.Single(frame.Targets, t => t.Id == id);
        Assert.Contains("security", lift.GuidanceRestriction, StringComparison.OrdinalIgnoreCase);
        var selected = frame with { Targets = [lift] };
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, selected, 0);
        var result = controller.Handle(command, selected, 1);
        Assert.False(result.Guiding);
        Assert.False(result.AutoWalking);
        Assert.Equal(NavigationDirection.None, result.Direction);
        Assert.Contains(result.Speech, s => s.Contains("security", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Speech, s => s.StartsWith("No route", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(0x64)]
    public void NativeSecurityCompletionRemovesLiftRestriction(int flags)
    {
        var frame = Frame(2560, 6399, flags);
        Assert.All(frame.Targets.Where(t => t.Id is "landmark:9" or "landmark:10"), t => Assert.Null(t.GuidanceRestriction));
        Assert.DoesNotContain(frame.Targets, t => t.Id == "story:future:factory-security");
    }

    [Theory]
    [InlineData("landmark:9", NavigationCommand.Guide)]
    [InlineData("landmark:9", NavigationCommand.ToggleWalk)]
    [InlineData("landmark:10", NavigationCommand.Guide)]
    [InlineData("landmark:10", NavigationCommand.ToggleWalk)]
    public void BothLiftsRouteAfterTheNativeSecurityCopy(string id, NavigationCommand command)
    {
        var frame = Frame(2560, 6399, 4);
        var lift = Assert.Single(frame.Targets, t => t.Id == id);
        var path = NavigationPathfinder.Search(frame.Graph, frame.Player, lift.ApproachPoints);
        Assert.NotNull(path.Route);
        var controller = new NavigationController();
        var selected = frame with { Targets = [lift] };
        controller.Handle(NavigationCommand.NextCategory, selected, 0);
        var result = controller.Handle(command, selected, 1);
        Assert.True(result.Guiding);
        Assert.Equal(command == NavigationCommand.ToggleWalk, result.AutoWalking);
    }

    [Theory]
    [InlineData(2560, 6399)]
    [InlineData(3264, 5247)]
    [InlineData(1296, 4959)]
    public void SecurityDoesNotBlockTheReturnToTheWorldMap(int x, int y)
    {
        var frame = Frame(x, y);
        var outside = Assert.Single(frame.Targets, t => t.Id == "exit:0");
        Assert.Null(outside.GuidanceRestriction);
        Assert.NotNull(NavigationPathfinder.Search(frame.Graph, frame.Player, outside.ApproachPoints).Route);
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("removed")]
    [InlineData("calls-off")]
    [InlineData("wrong-visual")]
    [InlineData("wrong-class")]
    [InlineData("off-map")]
    public void TerminalRepairDoesNotPromoteAnInvalidNativeSubject(string change)
    {
        FieldActorSnapshot Mutate(FieldActorSnapshot actor) => change switch {
            "hidden" => actor with { DrawMode = 0 },
            "removed" => actor with { ClassTag = 0x84 },
            "calls-off" => actor with { ScriptCallsEnabled = false },
            "wrong-visual" => actor with { VisualIndex = 126 },
            "wrong-class" => actor with { ClassTag = 7 },
            _ => actor with { FineX = 65280, TileX = 255 },
        };
        var frame = Frame(2560, 6399, mutate: Mutate);
        Assert.DoesNotContain(frame.Targets, t => t.Id == "actor:8:4:127");
        Assert.True(Assert.Single(frame.Targets, t => t.Id == "story:future:factory-security").IsStoryNote);
    }

    private static NavigationFrame Frame(int x, int y, int flags = 0, Func<FieldActorSnapshot, FieldActorSnapshot>? mutate = null,
        int point = 60, int[]? roster = null)
    {
        // Positions, class, draw and activation gates are from the October15
        // log. Roster words and terrain planes were NOT logged. Null Party
        // reproduces the missing-terminal boundary; this map is installed
        // MapTable0124's initial resource terrain, not a captured live map.
        var player = Actor(1, 0, 0, x, y, true);
        var terminal = Actor(8, 4, 127, 2432, 4864);
        var party = roster is null
            ? new[] { player, Actor(4, 1, 3, x, y, true), Actor(2, 2, 1, x, y, true) }
            : roster.Take(3).Select((character, slot) => Actor(character + 1, slot, character, x, y, true)).ToArray();
        FieldActorSnapshot[] actors = [Actor(0, 7, 0, 65280, 65280), .. party,
            mutate?.Invoke(terminal) ?? terminal, Actor(9, 7, 0, 1152, 1535), Actor(10, 7, 0, 3968, 1535),
            Actor(11, 5, 85, 2176, 2559), Actor(12, 5, 85, 2688, 2559), Actor(13, 5, 85, 3200, 2559),
            Actor(14, 7, 0, 2432, 2559), Actor(16, 7, 0, 1152, 4351), Actor(17, 7, 0, 1152, 4863)];
        var field = new FieldNavigationSnapshot(0x266F8958, 0x4000, 0x2000, 0x20000, 18, 228, true,
            1, 0, 1, 1, player, actors, party.Select(a => a.Index).ToArray());
        var story = new FieldStoryState(point, true) {
            Globals = new Dictionary<int, int> { [0x58] = flags },
            Locals = new Dictionary<int, int> { [8] = 0, [9] = 0, [10] = 0 },
            Party = roster,
        };
        using var stream = typeof(FactoryEntranceTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.factory-entrance-map-0363.json")!;
        var map = JsonSerializer.Deserialize<FieldMapSnapshot>(stream)!;
        if ((flags & 4) != 0)
        {
            // Atel0039 Robo fn5 removes the three barrier cells. This is an
            // offline preview of that native copy, never a write to the game.
            // MapTable0124 source cells50..52,59 have these exact shape/layer
            // bytes; terrain flags are zero both before and after the copy.
            foreach (var (column, shape, layer) in new[] { (14, 96, 129), (15, 0, 1), (16, 96, 136) })
            {
                map.CollisionShapes[18 * map.Width + column] = (byte)shape;
                map.CollisionLayers[18 * map.Width + column] = (byte)layer;
            }
        }
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, map, new(512, 3840, 4736, 7040), [], story);
    }
    private static FieldActorSnapshot Actor(int id, int kind, int visual, int x, int y, bool party = false) =>
        new(id, x >> 8, x, x & 255, y >> 8, y, y & 255, 0, 1, kind == 7 ? 0 : 1, visual, kind,
            0x2020, 0, id is 8 or 16 or 17 || party ? 128 : 0, party, true, true, true);
    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
    private sealed class RosterMemory(int[] values) : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination)
        {
            if (address != 0x1724C || destination.Length != 36) return false;
            for (var i = 0; i < values.Length; i++)
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(destination[(i * 4)..], values[i]);
            return true;
        }
    }
}
