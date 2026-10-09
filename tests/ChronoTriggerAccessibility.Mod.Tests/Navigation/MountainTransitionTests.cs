using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class MountainTransitionTests
{
    [Fact]
    public void DenadoroLedgeIsAnIdentifiableObjectWithAnUpperSideApproach()
    {
        var frame = Mountain(new(384, 1151, 1));
        var drop = Assert.Single(frame.Targets, t => t.Id == "transition:9");
        Assert.Equal(NavigationCategory.Objects, drop.Category);
        Assert.Contains("Drop", drop.Label);
        Assert.Contains("down", drop.Instruction, StringComparison.OrdinalIgnoreCase);
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, drop.ApproachPoints);
        Assert.NotNull(route.Route);
        Assert.InRange(route.Route[^1].Y, 3760, 3839);
    }

    [Fact]
    public void ReportedUpperPathCanReachTheBossRoomByStagingTheNativeDrop()
    {
        var frame = Mountain(new(384, 1151, 1));
        var exit = Assert.Single(frame.Targets, t => t.Id == "exit:3");
        var ordinary = NavigationPathfinder.Search(new FieldNavigationGraph(Map()), frame.Player, exit.ApproachPoints);
        Assert.Null(ordinary.Route);
        var staged = NavigationPathfinder.Search(frame.Graph, frame.Player, exit.ApproachPoints);
        Assert.NotNull(staged.Route);
        Assert.Equal("transition:9", staged.IntermediateId);
        Assert.InRange(staged.Route[^1].Y, 3760, 3839);
        // Every first-leg edge is ordinary floor; the animation is never a walking shortcut.
        var live = new FieldNavigationGraph(Map()).ForGoals([staged.Route[^1]]);
        for (var i = 1; i < staged.Route.Count; i++)
            Assert.Contains(staged.Route[i], live.Neighbours(staged.Route[i - 1]));
    }

    [Fact]
    public void LowerPathCannotUseTheOneWayDropToReturnToTheUpperExit()
    {
        var frame = Mountain(new(1408, 4863, 1));
        var exit = Assert.Single(frame.Targets, t => t.Id == "exit:2");
        Assert.Null(NavigationPathfinder.Search(frame.Graph, frame.Player, exit.ApproachPoints).Route);
    }

    [Theory]
    [InlineData(NavigationCommand.Guide)]
    [InlineData(NavigationCommand.ToggleWalk)]
    public void BothModesDescribeTheDropWaitWithoutInputAndContinueFromTheActualLanding(NavigationCommand command)
    {
        var first = Mountain(new(384, 1151, 1));
        var controller = new NavigationController();
        var exit = first.Targets.Single(t => t.Id == "exit:3");
        first = first with { Targets = [exit] };
        controller.Handle(NavigationCommand.NextCategory, first, 0);
        Assert.True(controller.Handle(command, first, 16).Guiding);
        var approach = Mountain(new(1408, 3808, 1)) with { Targets = [exit] };
        var contact = controller.Update(approach, 32);
        Assert.True(contact.Guiding);
        Assert.Contains(contact.Speech, s => s.Contains("drop", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(command == NavigationCommand.ToggleWalk ? NavigationDirection.South : NavigationDirection.None, contact.Direction);
        var airborne = Mountain(new(1392, 4256, 1), progress: true);
        var waiting = controller.Update(airborne, 700);
        Assert.True(waiting.Guiding);
        Assert.Equal(command == NavigationCommand.ToggleWalk, waiting.AutoWalking);
        Assert.Equal(NavigationDirection.None, waiting.Direction);
        Assert.Null(waiting.ManualLeg);
        var landed = Mountain(new(1392, 4675, 1)); // actual log position, not the preview centre
        var continued = controller.Update(landed, 1500);
        Assert.True(continued.Guiding, string.Join(" ", continued.Speech));
        Assert.Equal(command == NavigationCommand.ToggleWalk, continued.AutoWalking);
        Assert.Contains(continued.Speech, s => s.Contains("continu", StringComparison.OrdinalIgnoreCase));
        if (command == NavigationCommand.ToggleWalk) Assert.NotEqual(NavigationDirection.None, continued.Direction);
    }

    [Fact]
    public void ManualGuidanceReturnsToTheApproachWhenThePlayerStepsAwayFromTheDrop()
    {
        var first = Mountain(new(1408, 3808, 1));
        var exit = first.Targets.Single(t => t.Id == "exit:3");
        first = first with { Targets = [exit] };
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, first, 0);
        var contact = controller.Handle(NavigationCommand.Guide, first, 16);
        Assert.Contains(contact.Speech, s => s.Contains("take the drop"));
        var backedAway = Mountain(new(1408, 3296, 1)) with { Targets = [exit] };
        var directions = controller.Update(backedAway, 32, manualInput: true);
        Assert.True(directions.Guiding);
        Assert.NotNull(directions.ManualLeg);
        Assert.Equal(NavigationDirection.South, directions.ManualLeg.Direction);
        Assert.InRange(directions.ManualLeg.End.Y, 3760, 3839);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("unreadable")]
    [InlineData("scene")]
    [InlineData("landing")]
    [InlineData("timeout")]
    public void NativeHandoffCannotResumeOnUnknownControlOrAnUnexpectedLanding(string cause)
    {
        var frame = Mountain(new(1408, 3808, 1));
        var exit = frame.Targets.Single(t => t.Id == "exit:3");
        frame = frame with { Targets = [exit] };
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        controller.Handle(NavigationCommand.ToggleWalk, frame, 16);
        var airborne = Mountain(new(1392, 4256, 1), progress: true);
        if (cause is "foreign" or "unreadable") airborne = airborne with { ActiveTransitions = new HashSet<string>() };
        if (cause == "scene") airborne = airborne with { Scene = "another area" };
        var result = controller.Update(airborne, 700);
        if (cause is "landing" or "timeout")
        {
            Assert.True(result.Guiding);
            result = cause == "landing"
                ? controller.Update(Mountain(new(1408, 4352, 1)), 900)
                : controller.Update(airborne, 21000);
        }
        Assert.False(result.Guiding);
        Assert.False(result.AutoWalking);
        Assert.Equal(NavigationDirection.None, result.Direction);
    }

    [Fact]
    public void SelectingTheDropFinishesAfterLandingRatherThanAtTheLedge()
    {
        var frame = Mountain(new(1408, 3808, 1));
        var drop = frame.Targets.Single(t => t.Id == "transition:9");
        frame = frame with { Targets = [drop] };
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        controller.Handle(NavigationCommand.NextCategory, frame, 1);
        var started = controller.Handle(NavigationCommand.ToggleWalk, frame, 16);
        Assert.True(started.AutoWalking);
        Assert.DoesNotContain(started.Speech, s => s.Contains("Arrived"));
        Assert.Equal(NavigationDirection.South, started.Direction);
        controller.Update(Mountain(new(1392, 4256, 1), progress: true), 300);
        var landed = Mountain(new(1392, 4675, 1));
        var done = controller.Update(landed, 900);
        Assert.False(done.Guiding);
        Assert.Contains(done.Speech, s => s.Contains("Arrived at Drop"));
        Assert.Equal(NavigationDirection.None, done.Direction);
    }

    [Theory]
    [InlineData(8576, 4479, 1)]
    [InlineData(13179, 2228, 2)]
    [InlineData(16000, 6143, 1)]
    public void WaterfallExitStagesFromReportedPositions(int x, int y, int layer)
    {
        var frame = Waterfalls(new(x, y, layer));
        var exit = frame.Targets.Single(t => t.Id == "exit:2");
        Assert.Null(NavigationPathfinder.Search(new FieldNavigationGraph(WaterfallMap()), frame.Player, exit.ApproachPoints).Route);
        var search = NavigationPathfinder.Search(frame.Graph, frame.Player, exit.ApproachPoints);
        Assert.NotNull(search.Route);
        Assert.Contains(search.IntermediateId, new[] { "transition:16", "transition:18", "transition:19" });
    }

    [Fact]
    public void WaterfallDescriptionsAreDistinctAndAConsumedSlideIsUnavailable()
    {
        var frame = Waterfalls(new(8576, 4479, 1));
        var drops = frame.Targets.Where(t => t.Id.StartsWith("transition:")).ToArray();
        Assert.Equal(4, drops.Length);
        Assert.Equal(4, drops.Select(t => t.Label).Distinct().Count());
        var consumed = Waterfalls(new(13179, 2228, 2), used: true);
        Assert.DoesNotContain(consumed.Targets, t => t.Id is "transition:16" or "transition:18" or "transition:19");
        var exit = consumed.Targets.Single(t => t.Id == "exit:2");
        Assert.Null(NavigationPathfinder.Search(consumed.Graph, consumed.Player, exit.ApproachPoints).Route);
    }

    [Fact]
    public void NativeWalkwayLevelCrossingHasAReadableTargetAndKeepsOrdinaryWalkingRules()
    {
        // The tester crosses here at (11675,2228,3) between layers 1 and 2.
        var frame = Waterfalls(new(11451, 2228, 1));
        var crossing = Assert.Single(frame.Targets, t => t.Id == "level-crossing:45:8");
        Assert.Contains("Level crossing", crossing.Label);
        Assert.Equal(NavigationCategory.Objects, crossing.Category);
        Assert.All(crossing.ApproachPoints, p => Assert.Equal(3, p.Layer));
        var search = NavigationPathfinder.Search(frame.Graph, frame.Player, crossing.ApproachPoints);
        Assert.NotNull(search.Route);
        Assert.Null(search.IntermediateId);
        var after = NavigationPathfinder.Search(new FieldNavigationGraph(WaterfallMap()), search.Route[^1], [new(11915, 2228, 2)]);
        Assert.NotNull(after.Route);
        var labels = frame.Targets.Where(t => t.Id.StartsWith("level-crossing:")).Select(t => t.Label).ToArray();
        Assert.Equal(labels.Length, labels.Distinct().Count());
    }

    [Fact]
    public void WaterfallAProvesTwoDropsButWalksOnlyTheFirstLegThenRecapturesThePocket()
    {
        var first = Waterfalls(new(8576, 4479, 1), onlyA: true);
        var exit = first.Targets.Single(t => t.Id == "exit:2");
        var stage = NavigationPathfinder.Search(first.Graph, first.Player, exit.ApproachPoints);
        Assert.NotNull(stage.Route);
        Assert.Equal("transition:16", stage.IntermediateId);
        Assert.Equal(new NavigationPoint(10368, 7168, 3), stage.Route[^1]);
        var pocket = Waterfalls(new(9856, 10800, 3), used: true, onlyA: true);
        exit = pocket.Targets.Single(t => t.Id == "exit:2");
        var continuation = NavigationPathfinder.Search(pocket.Graph, pocket.Player, exit.ApproachPoints);
        Assert.NotNull(continuation.Route);
        Assert.Equal("transition:17", continuation.IntermediateId);
        Assert.Equal(new NavigationPoint(10016, 11088, 1), continuation.Route[^1]);
    }

    [Fact]
    public void WaterfallLandingWaitsForTheHandlerToClearItsBusyFlagBeforeContinuing()
    {
        var first = Waterfalls(new(10368, 7168, 3), onlyA: true);
        var exit = first.Targets.Single(t => t.Id == "exit:2");
        first = first with { Targets = [exit] };
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, first, 0);
        controller.Handle(NavigationCommand.ToggleWalk, first, 16);
        // E3 01 returns control before AD 01 yields and 77 64 clears the busy flag.
        var waiting = controller.Update(Waterfalls(new(9856, 10800, 3), used: true,
            onlyA: true, activeActor: 16, busy: true), 4000);
        Assert.True(waiting.Guiding, string.Join(" ", waiting.Speech));
        Assert.Equal(NavigationDirection.None, waiting.Direction);
        Assert.DoesNotContain(waiting.Speech, s => s.Contains("No route"));
        var ready = controller.Update(Waterfalls(new(9856, 10800, 3), used: true, onlyA: true), 4016);
        Assert.True(ready.Guiding, string.Join(" ", ready.Speech));
        Assert.True(controller.HasPendingTransition);
        Assert.Contains(ready.Speech, s => s.Contains("Continuing to"));
    }

    [Fact]
    public void StrongSouthFloorCanCarryThePlayerPastTheExactApproachBetweenTicks()
    {
        var first = Waterfalls(new(10368, 7008, 2), onlyA: true);
        var exit = first.Targets.Single(t => t.Id == "exit:2");
        first = first with { Targets = [exit] };
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, first, 0);
        controller.Handle(NavigationCommand.ToggleWalk, first, 16);
        var pastApproach = Waterfalls(new(10368, 7208, 3), onlyA: true) with { Targets = [exit] };
        var contact = controller.Update(pastApproach, 32);
        Assert.True(contact.Guiding, string.Join(" ", contact.Speech));
        Assert.Equal(NavigationDirection.South, contact.Direction);
        Assert.Contains(contact.Speech, s => s.Contains("take the drop"));
        Assert.DoesNotContain(contact.Speech, s => s.Contains("No route"));
    }

    [Fact]
    public void ManualGuidanceKeepsTheArmedContactAsTheChuteCarriesThePlayerForward()
    {
        var first = Waterfalls(new(13440, 5888, 2));
        var exit = first.Targets.Single(t => t.Id == "exit:2");
        first = first with { Targets = [exit] };
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, first, 0);
        controller.Handle(NavigationCommand.Guide, first, 16);
        var carried = Waterfalls(new(13440, 5952, 2)) with { Targets = [exit] };
        var result = controller.Update(carried, 32);
        Assert.True(result.Guiding, string.Join(" ", result.Speech));
        Assert.True(controller.HasPendingTransition);
        Assert.DoesNotContain(result.Speech, s => s.Contains("No route"));
    }

    [Fact]
    public void ManualMovementAfterAnObservedLandingDoesNotUndoTheCompletedDrop()
    {
        var first = Mountain(new(1408, 3808, 1));
        var exit = first.Targets.Single(t => t.Id == "exit:3");
        first = first with { Targets = [exit] };
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, first, 0);
        controller.Handle(NavigationCommand.Guide, first, 16);
        controller.Update(Mountain(new(1392, 4256, 1), progress: true), 700);
        var onLanding = Mountain(new(1392, 4675, 1), progress: true) with { CanNavigate = true };
        Assert.True(controller.Update(onLanding, 1500).Guiding);
        var moving = Mountain(new(1392, 4870, 1), progress: true) with { CanNavigate = true };
        Assert.True(controller.Update(moving, 1600, manualInput: true).Guiding);
        var finished = controller.Update(Mountain(new(1392, 4900, 1)), 1640, manualInput: true);
        Assert.True(finished.Guiding, string.Join(" ", finished.Speech));
        Assert.Contains(finished.Speech, s => s.Contains("Continuing to"));
        Assert.NotNull(finished.ManualLeg);
        Assert.Equal(NavigationDirection.None, finished.Direction);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("class")]
    [InlineData("position")]
    [InlineData("calls")]
    [InlineData("processing")]
    [InlineData("locals")]
    [InlineData("loaded")]
    [InlineData("offset")]
    public void UnverifiedOrDisabledTriggersCannotAuthorizeADrop(string cause)
    {
        var frame = Mountain(new(384, 1151, 1), cause);
        Assert.DoesNotContain(frame.Targets, t => t.Id == "transition:9");
        var exit = Assert.Single(frame.Targets, t => t.Id == "exit:3");
        Assert.Null(NavigationPathfinder.Search(frame.Graph, frame.Player, exit.ApproachPoints).Route);
    }

    internal static FieldMapSnapshot Map()
    {
        using var stream = typeof(MountainTransitionTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.denadoro-143-map-0349.json")!;
        return JsonSerializer.Deserialize<FieldMapSnapshot>(stream)!;
    }

    internal static NavigationFrame Mountain(NavigationPoint position, string? cause = null, bool progress = false)
    {
        var player = Actor(1, 0, position.X, position.Y, true);
        var marker = Actor(9, cause == "class" ? 5 : 7, cause == "position" ? 1664 : 1408, 3968, false) with
        { ScriptCallsEnabled = cause != "calls", ScriptProcessingEnabled = cause != "processing",
            ScriptPriority = progress ? 2 : 7, ScriptAddress = progress ? 0x55A : null,
            LoadedFlag = cause == "loaded" ? 1 : 0, CollisionOffsetX = cause == "offset" ? 5 : 0 };
        var actors = new List<FieldActorSnapshot> { Actor(0, 7, 0, 0, false), player };
        if (cause != "missing") actors.Add(marker);
        var field = new FieldNavigationSnapshot(0x1000, 0x4000, 0x2000, 0x20000, 29,
            143, true, progress ? 0 : 1, 0, 1, 1, player, actors, [1, -1, -1]);
        var state = new FieldStoryState(93, false)
        { Locals = cause == "locals" ? new Dictionary<int, int>() : new Dictionary<int, int> { [6] = position.X / 256, [7] = position.Y / 256, [19] = progress ? 1 : 0 } };
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, Map() with { PlayerLayer = position.Layer },
            new(0, 0, 8192, 8192), [], state);
    }

    internal static FieldMapSnapshot WaterfallMap()
    {
        using var stream = typeof(MountainTransitionTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.denadoro-144-map-0349.json")!;
        return JsonSerializer.Deserialize<FieldMapSnapshot>(stream)!;
    }

    internal static NavigationFrame Waterfalls(NavigationPoint position, bool used = false, bool onlyA = false,
        int? activeActor = null, bool busy = false, bool control = true)
    {
        var player = Actor(1, 0, position.X, position.Y, true);
        var actors = new List<FieldActorSnapshot> { player, Actor(16, 7, 10112, 7423, false),
            Actor(17, 7, 10112, 11263, false), Actor(21, 7, 10368, 7423, false) };
        if (!onlyA) actors.AddRange([Actor(18, 7, 13440, 6143, false), Actor(19, 7, 12672, 5631, false)]);
        actors = actors.Select(a => a.Index == activeActor ? a with
        { ScriptPriority = a.Index == 16 ? 5 : 2, ScriptAddress = a.Index == 16 ? 0x52D : 0x575 } : a).ToList();
        var field = new FieldNavigationSnapshot(0x1000, 0x4000, 0x2000, 0x20000, 22,
            144, true, control ? 1 : 0, 0, 1, 1, player, actors, [1, -1, -1]);
        var state = new FieldStoryState(93, false)
        { Locals = new Dictionary<int, int> { [100] = busy ? 1 : 0, [101] = used ? 1 : 0 } };
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(field,
            WaterfallMap() with { PlayerLayer = position.Layer }, new(0, 0, 16384, 16384), [], state);
    }

    internal static FieldActorSnapshot Actor(int id, int kind, int x, int y, bool party) =>
        new(id, x / 256, x, x % 256, y / 256, y, y % 256, 0, party ? 1 : 0,
            party ? 1 : 0, 0, kind, 0, 0, 1, party, true, true, true);
    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
