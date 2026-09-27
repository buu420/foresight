using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class TelepodPendantStoryTests
{
    [Theory]
    [InlineData(NavigationCommand.Guide, 1788, 2548)]
    [InlineData(NavigationCommand.ToggleWalk, 2188, 1044)]
    public void PendantAfterMarlesDisappearanceSupportsBothNavigationModes(
        NavigationCommand command, int playerX, int playerY)
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var field = Field(playerX, playerY);
        // Captured in scene 8 at 21:44-21:45 on September 13. The sprite is
        // drawn and has a script binding, but its normal activation byte is zero.
        Assert.False(field.Actors[1].IsActivationCandidate);
        var frame = source.Build(field, Map(), new(0, 224, 4224, 3424), [], State());

        var objective = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.Equal("Pick up Marle's pendant", objective.Label);
        Assert.False(objective.IsStoryNote);
        Assert.Equal(new NavigationPoint(1024, 1448, 1), objective.Position);
        var pendant = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Objects);
        Assert.Equal("Fallen pendant", pendant.Label);
        Assert.Equal(objective.Position, pendant.Position);
        Assert.NotEmpty(objective.ApproachPoints);

        var controller = new NavigationController();
        // Back twice from People: Enemies, then Story Events.
        controller.Handle(NavigationCommand.PreviousCategory, frame, 0);
        controller.Handle(NavigationCommand.PreviousCategory, frame, 0);
        var repeat = controller.Handle(NavigationCommand.Repeat, frame, 100);
        var route = controller.Handle(command, frame, 200);
        Assert.True(controller.IsActive);
        Assert.Equal(command == NavigationCommand.ToggleWalk, route.AutoWalking);
        Assert.DoesNotContain(repeat.Speech.Concat(route.Speech), s =>
            s.Contains("not active") || s.Contains("cannot locate") || s.Contains("No route") || s.Contains("not discovered"));
        var path = NavigationPathfinder.Find(frame.Graph, frame.Player, objective.ApproachPoints)!;
        // The pendant is picked up with Confirm, so arrival also needs the facing the native
        // handler tests. The captured leader faces down; the route ends beside the pendant.
        var there = frame with { Player = path[^1] };
        var turn = objective.ConfirmAt(path[^1])!(path[^1]);
        Assert.NotEmpty(turn);
        Assert.DoesNotContain(frame.PlayerFacing, turn);
        var arrival = controller.Update(there, 300);
        Assert.DoesNotContain(arrival.Speech, s => s.StartsWith("Arrived at "));
        if (command == NavigationCommand.ToggleWalk) Assert.Equal(turn[0], arrival.Direction);
        else Assert.Contains(arrival.Speech, s => s.Contains("to face"));
        arrival = controller.Update(there with { PlayerFacing = turn[0] }, 400, command == NavigationCommand.Guide);
        Assert.Contains(arrival.Speech, s => s.Contains("Arrived at ") && s.Contains("use confirm"));
    }

    [Fact]
    public void UnseenPendantCanBeReachedAndItsDiscoveredPositionIsRetiredAfterCollection()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var field = Field();
        var offscreen = new FieldViewport(2048, 2048, 4096, 4096);
        var unseen = source.Build(field, Map(), offscreen, [], State());
        var objective = Assert.Single(unseen.Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.False(objective.Visible);
        Assert.False(objective.Discovered);
        Assert.True(objective.GuideAvailable);
        Assert.NotNull(NavigationPathfinder.Find(unseen.Graph, unseen.Player, objective.ApproachPoints));

        source.Build(field, Map(), new(0, 0, 4096, 4096), [], State());
        // The pickup handler hides actor 11 before beginning the departure scene.
        var hidden = field with { Actors = [field.Actors[0], field.Actors[1] with { DrawMode = 0 }] };
        var collected = source.Build(hidden, Map(), offscreen, [], State());
        Assert.DoesNotContain(collected.Targets, t => t.Category == NavigationCategory.Objects);
        Assert.DoesNotContain(collected.Targets, t => t.Id == objective.Id && !t.IsStoryNote);

        var map = Map();
        map.ExitCells[10 * 64 + 5] = 0;
        // The native pickup keeps story point 12 across the jump to scene 113.
        var canyon = source.Build(hidden with { SceneId = 113, Actors = [field.Actors[0]] },
            map, offscreen, [], State());
        var next = Assert.Single(canyon.Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.Equal("Explore Truce Canyon", next.Label);
        Assert.False(next.IsStoryNote);
        Assert.NotNull(NavigationPathfinder.Find(canyon.Graph, canyon.Player, next.ApproachPoints));
        Assert.DoesNotContain(canyon.Targets, t => t.Label.Contains("pendant", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(8, 10, 11, 4, 99, 1, 128, true)]
    [InlineData(8, 13, 11, 4, 99, 1, 128, true)]
    [InlineData(7, 12, 11, 4, 99, 1, 128, true)]
    [InlineData(8, 12, 10, 4, 99, 1, 128, true)]
    [InlineData(8, 12, 11, 4, 98, 1, 128, true)]
    [InlineData(8, 12, 11, 0x84, 99, 1, 128, true)]
    [InlineData(8, 12, 11, 4, 99, 0, 128, true)]
    [InlineData(8, 12, 11, 4, 99, 1, 0, true)]
    [InlineData(8, 12, 11, 4, 99, 1, 128, false)]
    public void TelepodPickupDoesNotPromoteAnUnrelatedInactiveOrIncoherentActor(
        int scene, int point, int index, int actorClass, int visual, int draw, int callsEnabled, bool coherent)
    {
        var field = Field();
        field = field with
        {
            SceneId = scene, SceneIdCoherent = coherent,
            Actors = [field.Actors[0], field.Actors[1] with
            { Index = index, ClassTag = actorClass, VisualIndex = visual, DrawMode = draw, ActivationBinding = 0, ScriptCallsEnabled = callsEnabled != 0 }],
        };
        var frame = new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, Map(),
            new(0, 0, 4096, 4096), [], State() with { Point = point });
        Assert.DoesNotContain(frame.Targets, t => t.Category == NavigationCategory.Objects);
        Assert.DoesNotContain(frame.Targets, t => t.Id == "story:follow-marle" && !t.IsStoryNote);
    }

    [Fact]
    public void TelepodPickupUsesItsNativeStoryGateWithoutRequiringUnrelatedFairFlags()
    {
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var known = source.Build(Field(), Map(), new(0, 0, 4096, 4096), [], new(12, false));
        Assert.Contains(known.Targets, t => t.Id == "story:follow-marle" && !t.IsStoryNote);
        var unknown = source.Build(Field(), Map(), new(0, 0, 4096, 4096), [], null);
        Assert.DoesNotContain(unknown.Targets, t => t.Category is NavigationCategory.Objects or NavigationCategory.StoryEvents);
    }

    private static FieldStoryState State() => new(12, false)
    { Globals = new Dictionary<int, int> { [0x54] = 8, [0x55] = 0x84, [0x56] = 2 } };

    private static FieldNavigationSnapshot Field(int x = 1788, int y = 2548)
    {
        var lead = Actor(3, x, y, 0, 0) with { IsPartyMember = true };
        var pendant = Actor(11, 1024, 1448, 4, 99);
        return new(0x1000, 0x4000, 0x2000, 0x20000, 2, 8, true, 3, 0, 0, 0, lead, [lead, pendant]);
    }

    private static FieldActorSnapshot Actor(int index, int x, int y, int actorClass, int visual) =>
        new(index, x >> 8, x, x & 255, y >> 8, y, y & 255, 0, 1, 1, visual, actorClass,
            0x2020, 0, 128, false, true, true, true);

    private static FieldMapSnapshot Map() => new(64, 64, new byte[4096], new byte[4096],
        Enumerable.Repeat((byte)1, 4096).ToArray(), 1, false, 64, 64,
        Enumerable.Repeat((byte)128, 4096).ToArray());

    private sealed class NoMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination) => false;
    }
}
