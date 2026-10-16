using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class FrogHideawayTests
{
    private const string Reveal = "hideaway:reveal";

    [Theory]
    [InlineData(2304, 895, NavigationCommand.Guide)]
    [InlineData(2304, 895, NavigationCommand.ToggleWalk)]
    [InlineData(2944, 1423, NavigationCommand.Guide)]
    [InlineData(2944, 1423, NavigationCommand.ToggleWalk)]
    public void EntryListsTheVisibleRoomApproachAndBindsBothStoryNavigationModes(int x, int y, NavigationCommand command)
    {
        var frame = Frame(x, y);
        var approach = Assert.Single(frame.Targets, t => t.Id == Reveal);
        Assert.Equal(NavigationCategory.Objects, approach.Category);
        Assert.Contains("left", approach.Label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Frog", approach.Label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(frame.Targets, t => t.Category == NavigationCategory.People);
        var story = Assert.Single(frame.Targets, t => t.Id == "story:full:frog-medal");
        Assert.False(story.IsStoryNote);
        Assert.Equal(approach.ApproachPoints, story.ApproachPoints);
        foreach (var target in new[] { approach, story })
        {
            Assert.NotEmpty(target.ApproachPoints);
            // Atel0186 raw0739 compares the leader's tile X with 8, strictly less.
            Assert.All(target.ApproachPoints, p => Assert.InRange(p.X, 0, 2047));
            Assert.All(target.ApproachPoints, p => Assert.False(frame.Graph.IsTerminal(p)));
            Assert.Null(target.ConfirmFacings);
            Assert.All(target.ApproachPoints, p => Assert.Null(target.ConfirmAt(p)));
            var route = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route;
            Assert.NotNull(route);
            Assert.True(route[^1].X < 2048);
            var selected = frame with { Targets = [target] };
            var controller = new NavigationController();
            for (var i = 0; i < (int)target.Category; i++) controller.Handle(NavigationCommand.NextCategory, selected, 0);
            var result = controller.Handle(command, selected, 1);
            Assert.True(result.Guiding);
            Assert.Equal(command == NavigationCommand.ToggleWalk, result.AutoWalking);
            Assert.DoesNotContain(result.Speech, s => s.Contains("Press Confirm", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Theory]
    [InlineData(NavigationCommand.Guide)]
    [InlineData(NavigationCommand.ToggleWalk)]
    public void GuidanceCrossesTheStrictThresholdAndStopsWhenTheGameTakesControl(NavigationCommand command)
    {
        var frame = Frame(2304, 895);
        var target = Assert.Single(frame.Targets, t => t.Id == Reveal);
        var selected = frame with { Targets = [target] };
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, selected, 0);
        controller.Handle(NavigationCommand.NextCategory, selected, 0);
        controller.Handle(command, selected, 1);
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route!;
        var firstInside = 1;
        // Follow real floor nodes: a direct west step from the entrance is a
        // wall, so teleporting there would test recovery from an invalid pose.
        while (route[firstInside].X >= 2048)
        {
            var result = controller.Update(selected with { Player = route[firstInside] }, firstInside * 100);
            Assert.True(result.Guiding);
            Assert.DoesNotContain(result.Speech, s => s.StartsWith("Arrived", StringComparison.Ordinal));
            if (command == NavigationCommand.ToggleWalk) Assert.NotEqual(NavigationDirection.None, result.Direction);
            firstInside++;
        }
        var cutscene = controller.Update(selected with { Player = route[firstInside], CanNavigate = false }, firstInside * 100);
        Assert.False(cutscene.Guiding);
        Assert.False(cutscene.AutoWalking);
        Assert.Equal(NavigationDirection.None, cutscene.Direction);
    }

    [Theory]
    [InlineData("revealed")]
    [InlineData("already-spoken")]
    [InlineData("later-mode")]
    [InlineData("unknown-mode")]
    [InlineData("unknown-flag")]
    [InlineData("paused-controller")]
    [InlineData("missing-controller")]
    [InlineData("removed-controller")]
    [InlineData("wrong-controller")]
    [InlineData("wrong-frog")]
    [InlineData("frog-calls-off")]
    [InlineData("frog-processing-off")]
    [InlineData("frog-busy")]
    [InlineData("frog-priority-unknown")]
    [InlineData("party-frog")]
    [InlineData("other-scene")]
    [InlineData("incoherent-scene")]
    public void OnlyTheAvailableNativeRevealProducesTheRoomApproach(string variant) =>
        Assert.DoesNotContain(Frame(2304, 895, variant).Targets, t => t.Id == Reveal);

    [Fact]
    public void NativeRevealReplacesTheApproachWithFrogAtHisCurrentPosition()
    {
        var source = Source();
        Assert.Contains(Frame(2304, 895, source: source).Targets, t => t.Id == Reveal);
        var shown = Frame(1909, 2555, "revealed", source);
        Assert.DoesNotContain(shown.Targets, t => t.Id == Reveal);
        var frog = Assert.Single(shown.Targets, t => t.Category == NavigationCategory.People);
        Assert.Equal("Frog", frog.Label);
        Assert.Equal(745, frog.Position.X);
        Assert.Equal(2016, frog.Position.Y);
        var story = Assert.Single(shown.Targets, t => t.Id == "story:full:frog-medal");
        Assert.False(story.IsStoryNote);
        Assert.Equal(frog.ApproachPoints, story.ApproachPoints);
        Assert.All(story.ApproachPoints, p => Assert.NotNull(story.ConfirmAt(p)));
    }

    private static NavigationFrame Frame(int x, int y, string variant = "entry", FieldNavigationSource? source = null)
    {
        var player = Actor(1, 0, 0, x, y, party: true);
        var frog = Actor(4, 3, 4, 1152, 255) with { DrawMode = 0, ActivationBinding = 0, ScriptAddress = 0x480, CurrentScriptOpcode = 0x11 };
        var controller = Actor(8, 7, 0, 65280, 65280) with { ScriptAddress = 0x744, CurrentScriptOpcode = 0x11 };
        var globals = Enumerable.Range(0, 512).ToDictionary(i => i, _ => 0);
        globals[0xFF] = 0x16;
        var locals = Enumerable.Range(0, 256).ToDictionary(i => i, _ => 0);
        switch (variant)
        {
            case "revealed": frog = frog with { FineX = 745, TileX = 2, FractionX = 233, FineY = 2016, TileY = 7, FractionY = 224, DrawMode = 1, ActivationBinding = 128 }; controller = controller with { ScriptProcessingEnabled = false }; break;
            case "already-spoken": globals[0xFF] |= 0x80; break;
            case "later-mode": locals[8] = 1; break;
            case "unknown-mode": locals.Remove(8); break;
            case "unknown-flag": globals.Remove(0xFF); break;
            case "paused-controller": controller = controller with { ScriptProcessingEnabled = false }; break;
            case "removed-controller": controller = controller with { ClassTag = 0x87 }; break;
            case "wrong-controller": controller = controller with { ClassTag = 4 }; break;
            case "wrong-frog": frog = frog with { VisualIndex = 3 }; break;
            case "frog-calls-off": frog = frog with { ScriptCallsEnabled = false }; break;
            case "frog-processing-off": frog = frog with { ScriptProcessingEnabled = false }; break;
            case "frog-busy": frog = frog with { ScriptPriority = 2 }; break;
            case "frog-priority-unknown": frog = frog with { ScriptPriority = null }; break;
            case "party-frog": frog = frog with { IsPartyMember = true }; break;
        }
        FieldActorSnapshot[] actors = [Actor(0, 7, 0, 65280, 65280), player,
            Actor(2, 2, 1, x, y, party: true), frog, Actor(5, 1, 3, x, y, party: true)];
        if (variant != "missing-controller") actors = [.. actors, controller];
        var field = new FieldNavigationSnapshot(0x26B957D8, 0x4000, 0x2000, 0x20000, 15,
            variant == "other-scene" ? 140 : 141, variant != "incoherent-scene", 1, 0, 1, 1,
            player, actors, [1, 5, 2]);
        var state = new FieldStoryState(102, true) {
            Globals = globals, Locals = locals, Party = [0, 3, 1, 130, 129, 2, 128, 128, 128],
        };
        using var stream = typeof(FrogHideawayTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.frog-hideaway-map-0364.json")!;
        var map = JsonSerializer.Deserialize<FieldMapSnapshot>(stream)!;
        return (source ?? Source()).Build(field, map, new(0, 0, 4224, 3200), [], state);
    }

    private static FieldNavigationSource Source() => new(new NoMemory(), _ => { },
        characterName: id => id == 4 ? "Frog" : null);

    private static FieldActorSnapshot Actor(int id, int kind, int visual, int x, int y, bool party = false) =>
        new(id, x >> 8, x, x & 255, y >> 8, y, y & 255, 0, 1, kind == 7 ? 0 : 1, visual, kind,
            0x2020, 0, 128, party, true, true, true);
    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
