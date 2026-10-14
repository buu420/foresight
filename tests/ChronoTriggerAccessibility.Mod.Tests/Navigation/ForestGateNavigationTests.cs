using System.Reflection;
using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class ForestGateNavigationTests
{
    [Theory]
    [InlineData(NavigationCommand.Guide, false)]
    [InlineData(NavigationCommand.ToggleWalk, false)]
    [InlineData(NavigationCommand.Guide, true)]
    [InlineData(NavigationCommand.ToggleWalk, true)]
    public void DrawnForestGateBeforeFirstConfirmSupportsObjectsAndStoryInBothModes(
        NavigationCommand command, bool storyCategory)
    {
        var field = Field();
        Assert.False(field.Actors[1].IsActivationCandidate);
        Assert.Empty(GameNavigationCatalog.ActorInfo(20, field.Actors[1])!.Actions);
        var frame = Build(field);
        var gate = Assert.Single(frame.Targets, t => t.Id == "actor:9:4:151");
        Assert.Equal("Time Gate", gate.Label);
        Assert.Equal(NavigationCategory.Objects, gate.Category);
        Assert.True(gate.Visible);
        var story = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.False(story.IsStoryNote);
        Assert.Equal(gate.Position, story.Position);
        Assert.Equal(gate.ApproachPoints, story.ApproachPoints);
        Assert.DoesNotContain(frame.Targets, t => t.Label.Contains("Bangor") || t.Label.Contains("End of Time"));

        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        controller.Handle(NavigationCommand.NextCategory, frame, 10);
        if (storyCategory) controller.Handle(NavigationCommand.NextCategory, frame, 20);
        var started = controller.Handle(command, frame, 100);
        Assert.True(controller.IsActive);
        Assert.Equal(command == NavigationCommand.ToggleWalk, started.AutoWalking);
        Assert.DoesNotContain(started.Speech, s => s.Contains("No route") || s.Contains("not available"));
        var selected = storyCategory ? story : gate;
        var path = NavigationPathfinder.Find(frame.Graph, frame.Player, selected.ApproachPoints);
        Assert.NotNull(path);
        var goal = path[^1];
        var facings = selected.ConfirmAt(goal)!(goal);
        Assert.NotEmpty(facings);
        Assert.Equal(9, FieldInteractionRange.ConfirmWinner(
            field.Actors, field.LeadPlayerActorIndex, goal.X, goal.Y, facings[0]));
        var arrival = controller.Update(frame with { Player = goal, PlayerFacing = facings[0] },
            200, command == NavigationCommand.Guide);
        Assert.Contains(arrival.Speech, s => s.StartsWith("Arrived at ") &&
            s.Contains("Confirm", StringComparison.OrdinalIgnoreCase));
        Assert.False(arrival.AutoWalking);
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("removed")]
    [InlineData("unloaded")]
    [InlineData("calls disabled")]
    [InlineData("wrong model")]
    [InlineData("wrong actor")]
    [InlineData("unknown scene")]
    [InlineData("before reveal")]
    [InlineData("unknown story")]
    public void MissingOrUnverifiedGateNeverLeaksAGateTarget(string mutation)
    {
        var field = Field();
        var gate = field.Actors[1];
        gate = mutation switch
        {
            "hidden" => gate with { DrawMode = 0 },
            "removed" => gate with { ClassTag = 0x84 },
            "unloaded" => gate with { LoadedFlag = 0 },
            "calls disabled" => gate with { ScriptCallsEnabled = false },
            "wrong model" => gate with { VisualIndex = 87 },
            "wrong actor" => gate with { Index = 10 },
            _ => gate,
        };
        field = field with { Actors = [field.LeadPlayer!, gate] };
        if (mutation == "unknown scene") field = field with { SceneIdCoherent = false };
        FieldStoryState? state = mutation == "unknown story" ? null : new(mutation == "before reveal" ? 47 : 51, true);
        var frame = new FieldNavigationSource(new NoMemory(), _ => { })
            .Build(field, Map(), new(0, 0, 4224, 3200), [], state);
        Assert.DoesNotContain(frame.Targets, t => t.Id.StartsWith("actor:9:") ||
            t.Id == "story:forest-gate" || t.Label.Contains("Time Gate"));
    }

    [Fact]
    public void HidingTheGateAfterConfirmRetiresBothRows()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var field = Field();
        var first = source.Build(field, Map(), new(0, 0, 4224, 3200), [], new(51, true));
        Assert.Contains(first.Targets, t => t.Id == "actor:9:4:151");
        field = field with { Actors = [field.LeadPlayer!, field.Actors[1] with { DrawMode = 0 }] };
        var after = source.Build(field, Map(), new(0, 0, 4224, 3200), [], new(51, true));
        Assert.DoesNotContain(after.Targets, t => t.Id is "actor:9:4:151" or "story:forest-gate");
    }

    private static NavigationFrame Build(FieldNavigationSnapshot field) =>
        new FieldNavigationSource(new NoMemory(), _ => { })
            .Build(field, Map(), new(0, 0, 4224, 3200), [], new(51, true));

    // Actor positions/flags are from the tester's scene20 entry, log15207.
    // Terrain is installed MapTable82's initial planes, not a live capture.
    private static FieldNavigationSnapshot Field()
    {
        var lead = Actor(2, 1925, 2807, 0, 0) with { IsPartyMember = true };
        var gate = Actor(9, 2432, 2815, 4, 151);
        return new(0x11A3F750, 0x1000, 0x2000, 0x3000, 19, 20, true, 3, 0, 0, 2,
            lead, [lead, gate,
                Actor(12, 1874, 1823, 4, 7), Actor(13, 900, 1779, 4, 16),
                Actor(14, 1160, 1535, 4, 16), Actor(15, 1390, 1868, 4, 16),
                Actor(16, 2451, 1555, 4, 16), Actor(17, 1647, 1551, 4, 16),
                Actor(18, 3456, 1791, 4, 183)]);
    }

    private static FieldActorSnapshot Actor(int id, int x, int y, int kind, int visual) =>
        new(id, x >> 8, x, x & 255, y >> 8, y, y & 255, 0, 1, 1, visual, kind,
            0x2020, 0, 128, false, true, true, true);

    private static FieldMapSnapshot Map()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.guardia-gate-20-map-0360.json")!;
        return JsonSerializer.Deserialize<FieldMapSnapshot>(stream)!;
    }

    private sealed class NoMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination) => false;
    }
}
