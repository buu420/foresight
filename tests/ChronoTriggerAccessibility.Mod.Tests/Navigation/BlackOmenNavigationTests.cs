using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class BlackOmenNavigationTests
{
    [Theory]
    [InlineData(97, 98)]
    [InlineData(98, 97)]
    [InlineData(320, 321)]
    [InlineData(321, 320)]
    public void NativeWarpPadsAreIdentifiableObjectsRatherThanOrdinaryExits(int scene, int destination)
    {
        var frame = Frame(scene);
        var pad = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Objects && t.Label.StartsWith("Teleport pad"));
        Assert.StartsWith($"script-region:0:{destination}:", pad.Id);
        Assert.True(pad.Visible);
        Assert.Contains("to ", pad.Label);
        Assert.DoesNotContain(frame.Targets, t => t.Id == pad.Id && t.Category == NavigationCategory.Exits);
    }

    [Fact]
    public void NarrowUpperLandingPadHasAReachableGoalOnTheNativeTileBoundary()
    {
        var frame = Frame(97);
        var pad = Assert.Single(frame.Targets, t => t.Id == "script-region:0:98:7:6:8:6");
        var path = NavigationPathfinder.Search(frame.Graph, frame.Player, pad.ApproachPoints).Route;
        Assert.NotNull(path);
        Assert.All(path!, p => Assert.InRange(p.Y, 0, 16 * 256 - 1));
        Assert.Equal(8 * 256, path![^1].X);
        Assert.Equal(6, path[^1].Y / 256);
    }

    [Theory]
    [InlineData(97, 0x20)]
    [InlineData(98, 0x10)]
    [InlineData(320, 0x80)]
    [InlineData(321, 0x40)]
    public void ArrivalGuardKeepsThePadIdentifiableAndExplainsHowToReactivateIt(int scene, int arrivalMask)
    {
        var frame = Frame(scene, flags: 4 | arrivalMask);
        var pad = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Objects && t.Label.StartsWith("Teleport pad"));
        Assert.Contains(scene == 320 ? "south" : scene == 321 ? "north or south" : "Step away", pad.GuidanceRestriction);
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        while (controller.CurrentCategoryLabel != "Interactable Objects") controller.Handle(NavigationCommand.NextCategory, frame, 0);
        Assert.Equal(pad.Id, controller.TargetId);
        var result = controller.Handle(NavigationCommand.ToggleWalk, frame, 16);
        Assert.False(result.AutoWalking);
        Assert.Contains(pad.GuidanceRestriction!, result.Speech);
    }

    [Fact]
    public void UnknownPadGuardDoesNotAuthorizeAutomaticTeleporting()
    {
        var frame = Frame(320, flags: null);
        var pad = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Objects && t.Label.StartsWith("Teleport pad"));
        Assert.Contains("unavailable", pad.GuidanceRestriction);
    }

    [Theory]
    [InlineData(97, false)]
    [InlineData(97, true)]
    [InlineData(98, false)]
    [InlineData(98, true)]
    [InlineData(320, false)]
    [InlineData(320, true)]
    [InlineData(321, false)]
    [InlineData(321, true)]
    public void BothNavigationModesReachTheNativePadTrigger(int scene, bool automatic)
    {
        var position = scene switch { 98 => new NavigationPoint(6144, 6144, 1), 321 => new(6144, 5823, 1), _ => (NavigationPoint?)null };
        var frame = Frame(scene, position: position);
        var pad = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Objects && t.Label.StartsWith("Teleport pad"));
        var controller = new NavigationController();
        while (controller.CurrentCategoryLabel != "Interactable Objects") controller.Handle(NavigationCommand.NextCategory, frame, 0);
        Assert.Equal(pad.Id, controller.TargetId);
        var result = controller.Handle(automatic ? NavigationCommand.ToggleWalk : NavigationCommand.Guide, frame, 16);
        Assert.True(result.Guiding);
        Assert.Equal(automatic, result.AutoWalking);
        if (automatic) Assert.NotEqual(NavigationDirection.None, result.Direction);
        else { Assert.Equal(NavigationDirection.None, result.Direction); Assert.NotNull(result.ManualLeg); }
        Assert.NotNull(NavigationPathfinder.Search(frame.Graph, frame.Player, pad.ApproachPoints).Route);
    }

    [Fact]
    public void TheLowerLandingExitUsesTheSameNativeWrappedFloor()
    {
        var frame = Frame(98, position: new(6144, 6144, 1));
        var exit = Assert.Single(frame.Targets, t => t.Id == "exit:0");
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, exit.ApproachPoints).Route;
        Assert.NotNull(route);
        Assert.Equal(30, route![^1].Y / 256);
    }

    [Fact]
    public void WrappedLandingDoesNotCreateAGroundConnectionToTheOtherLanding()
    {
        var frame = Frame(98, position: new(6144, 6144, 1));
        Assert.Null(NavigationPathfinder.Search(frame.Graph, frame.Player, [new(2048, 3327, 1)]).Route);
    }

    [Fact]
    public void TheClosedFinalDoorHasAUsableLiveApproachBeforeItsNativeOpeningContact()
    {
        var frame = FinalFrame();
        var door = Assert.Single(frame.Targets, t => t.Id == "landmark:20");
        Assert.Contains("door", door.Label, StringComparison.OrdinalIgnoreCase);
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, door.ApproachPoints);
        Assert.NotNull(route.Route);
        Assert.Equal("landmark:20", route.IntermediateId);
        Assert.Equal(new NavigationPoint(3072, 1488, 1), route.IntermediateContact);
        Assert.Equal(new NavigationPoint(3072, 1728, 1), route.Route![^1]);
        var live = new FieldNavigationGraph(Map(324));
        Assert.All(route.Route, p => Assert.True(live.TryPosition(p.X, p.Y, p.Layer, out var at) && at == p));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothNavigationModesCanApproachTheFinalDoor(bool automatic)
    {
        var frame = FinalFrame();
        var controller = new NavigationController();
        while (controller.CurrentCategoryLabel != "Interactable Objects") controller.Handle(NavigationCommand.NextCategory, frame, 0);
        Assert.Equal("landmark:20", controller.TargetId);
        var result = controller.Handle(automatic ? NavigationCommand.ToggleWalk : NavigationCommand.Guide, frame, 16);
        Assert.True(result.Guiding);
        Assert.Equal(automatic, result.AutoWalking);
    }

    [Fact]
    public void TheLocalStoryRouteUsesTheFinalDoorInsteadOfTheReturnElevator()
    {
        var frame = FinalFrame();
        var story = Assert.Single(frame.Targets, t => t.Id == "story:full:lavos");
        Assert.DoesNotContain("End of Time", story.Instruction ?? "");
        Assert.DoesNotContain("Elevator", story.Instruction ?? "");
        Assert.Contains("Black Omen", story.Label);
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, story.ApproachPoints);
        Assert.NotNull(route.Route);
        Assert.Equal("landmark:20", route.IntermediateId);
    }

    [Fact]
    public void TheFinalDoorPassageUsesItsVisibleLocationRatherThanADebugBossName()
    {
        var exit = Assert.Single(FinalFrame().Targets, t => t.Id == "exit:1");
        Assert.Equal("Passage through the northern door", exit.Label);
    }

    [Fact]
    public void AutomaticDoorApproachFinishesWithNativeUpAndContinuesOnlyAfterAnOpenedCapture()
    {
        var frame = FinalFrame();
        var controller = new NavigationController();
        while (controller.CurrentCategoryLabel != "Story Events") controller.Handle(NavigationCommand.NextCategory, frame, 0);
        Assert.Equal("story:full:lavos", controller.TargetId);
        Assert.True(controller.Handle(NavigationCommand.ToggleWalk, frame, 16).AutoWalking);
        var contact = new NavigationPoint(3072, 1728, 1);
        var pushing = controller.Update(FinalFrame(position: contact), 32);
        Assert.True(pushing.AutoWalking);
        Assert.Equal(NavigationDirection.North, pushing.Direction);
        var open = FinalFrame(position: contact, opened: true);
        var continuing = controller.Update(open, 48);
        Assert.True(continuing.AutoWalking);
        Assert.Equal(NavigationDirection.North, continuing.Direction);
        var passage = Assert.Single(open.Targets, t => t.Id == "story:full:lavos");
        Assert.NotEmpty(passage.ApproachPoints);
        Assert.All(passage.ApproachPoints, p => Assert.Equal(5, p.Y / 256));
    }

    [Theory]
    [InlineData(false, 7)]
    [InlineData(true, null)]
    [InlineData(true, 2)]
    public void MissingNativeDoorCallAuthorityDoesNotAllowTheAssistedContact(bool calls, int? priority)
    {
        var frame = FinalFrame(doorCalls: calls, priority: priority);
        var target = Assert.Single(frame.Targets, t => t.Id == "landmark:20");
        Assert.Contains("unavailable", target.GuidanceRestriction);
        var controller = new NavigationController();
        while (controller.CurrentCategoryLabel != "Interactable Objects") controller.Handle(NavigationCommand.NextCategory, frame, 0);
        Assert.False(controller.Handle(NavigationCommand.ToggleWalk, frame, 16).AutoWalking);
    }

    [Theory]
    [InlineData(97, "script-region:0:98:7:6:8:6")]
    [InlineData(98, "exit:0")]
    [InlineData(320, "script-region:0:321:0:38:255:38")]
    [InlineData(321, "exit:0")]
    public void PadRoomStoryGuidanceFollowsTheLocalForwardPassage(int scene, string forward)
    {
        var frame = Frame(scene);
        var story = Assert.Single(frame.Targets, t => t.Id == "story:full:lavos");
        var passage = Assert.Single(frame.Targets, t => t.Id == forward);
        Assert.Contains("Black Omen", story.Label);
        Assert.Equal(passage.ApproachPoints, story.ApproachPoints);
    }

    [Theory]
    [InlineData("landmark:20", false)]
    [InlineData("landmark:20", true)]
    [InlineData("story:full:lavos", false)]
    [InlineData("story:full:lavos", true)]
    [InlineData("exit:1", false)]
    [InlineData("exit:1", true)]
    public void NativeOpeningFramesKeepBothModesActiveUntilTheDoorScriptCompletes(string id, bool automatic)
    {
        NavigationFrame Select(NavigationFrame frame) => frame with { Targets = [frame.Targets.Single(t => t.Id == id)] };
        var contact = new NavigationPoint(3072, 1728, 1);
        var frame = Select(FinalFrame(position: contact));
        var controller = new NavigationController();
        for (var i = 0; i < (int)frame.Targets[0].Category; i++) controller.Handle(NavigationCommand.NextCategory, frame, 0);
        Assert.True(controller.Handle(automatic ? NavigationCommand.ToggleWalk : NavigationCommand.Guide, frame, 16).Guiding);
        // E8 yields to PC558, E5 yields to PC560. Local10 is still zero in both.
        for (var n = 1; n <= 5; n++)
        {
            var opening = Select(FinalFrame(position: contact with { Y = contact.Y - 16 * n }, priority: 2,
                scriptAddress: n < 3 ? 0x558 : 0x560, copied: n >= 3));
            var result = controller.Update(opening, 16 + n * 16);
            Assert.True(result.Guiding, string.Join(" | ", result.Speech));
            Assert.Equal(automatic, result.AutoWalking);
            Assert.Equal(NavigationDirection.None, result.Direction);
            Assert.DoesNotContain(result.Speech, s => s.Contains("stopped") || s.Contains("unavailable") || s.Contains("No route"));
        }
        var opened = Select(FinalFrame(position: contact with { Y = contact.Y - 80 }, opened: true));
        var continuing = controller.Update(opened, 128);
        Assert.DoesNotContain(continuing.Speech, s => s.Contains("stopped") || s.Contains("unavailable") || s.Contains("No route"));
        var goal = opened.Targets[0].ApproachPoints.OrderBy(p => Math.Abs(p.X - opened.Player.X) + Math.Abs(p.Y - opened.Player.Y)).First();
        var finish = controller.Update(Select(FinalFrame(position: goal, opened: true)), 160);
        if (id == "landmark:20") Assert.Contains("The northern door is open.", finish.Speech);
    }

    [Fact]
    public void AStuckNativeOpeningTimesOutAndCanBeCancelledWithoutMovement()
    {
        var contact = new NavigationPoint(3072, 1728, 1);
        var closed = FinalFrame(position: contact);
        var controller = new NavigationController();
        while (controller.CurrentCategoryLabel != "Interactable Objects") controller.Handle(NavigationCommand.NextCategory, closed, 0);
        controller.Handle(NavigationCommand.ToggleWalk, closed, 16);
        var opening = FinalFrame(position: contact, priority: 2, scriptAddress: 0x558);
        Assert.True(controller.Update(opening, 32).AutoWalking);
        var timeout = controller.Update(opening, 3040);
        Assert.False(timeout.Guiding);
        Assert.Equal(NavigationDirection.None, timeout.Direction);
        Assert.Contains(timeout.Speech, s => s.Contains("passage did not open"));
        controller.Handle(NavigationCommand.ToggleWalk, opening, 3056);
        var cancelled = controller.Cancel("manual input");
        Assert.False(cancelled.Guiding);
        Assert.Equal(NavigationDirection.None, cancelled.Direction);
    }

    [Theory]
    [InlineData(0x550)]
    [InlineData(0x563)]
    public void BusyDoorOutsideItsAuditedOpeningScriptRemainsRestricted(int scriptAddress)
    {
        var door = FinalFrame(priority: 2, scriptAddress: scriptAddress).Targets.Single(t => t.Id == "landmark:20");
        Assert.Contains("unavailable", door.GuidanceRestriction);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("misplaced")]
    [InlineData("removed")]
    [InlineData("paused")]
    public void UnverifiedDoorStateDoesNotCreateAThroughRoute(string variant)
    {
        var goals = FinalFrame().Targets.Single(t => t.Id == "exit:1").ApproachPoints;
        var frame = FinalFrame(variant: variant);
        Assert.Null(NavigationPathfinder.Search(frame.Graph, frame.Player, goals).Route);
        if (variant == "unknown")
            Assert.Equal("The door state is unavailable.", frame.Targets.Single(t => t.Id == "landmark:20").GuidanceRestriction);
    }

    [Theory]
    [InlineData(3441, 2546)]
    [InlineData(4069, 2574)]
    [InlineData(4232, 2550)]
    public void TheReportedPocketPositionsCanReachTheDoor(int x, int y)
    {
        var frame = FinalFrame(position: new(x, y, 1));
        var door = frame.Targets.Single(t => t.Id == "landmark:20");
        Assert.NotNull(NavigationPathfinder.Search(frame.Graph, frame.Player, door.ApproachPoints).Route);
    }

    [Fact]
    public void OnlyAnArmedPadIsARouteTerminal()
    {
        var armed = Frame(321);
        var pad = armed.Targets.Single(t => t.Id == "script-region:0:320:23:25:24:26");
        Assert.All(pad.ApproachPoints, p => Assert.True(armed.Graph.IsTerminal(p)));
        var arrival = Frame(321, flags: 4 | 0x40);
        Assert.All(pad.ApproachPoints, p => Assert.False(arrival.Graph.IsTerminal(p)));
    }

    [Theory]
    [InlineData(97)]
    [InlineData(320)]
    public void ForwardPadInstructionsIdentifyTheTeleportPad(int scene) =>
        Assert.Contains("teleport pad", Frame(scene).Targets.Single(t => t.Id == "story:full:lavos").Instruction);

    internal static NavigationFrame FinalFrame(NavigationPoint? position = null, bool opened = false, bool doorCalls = true, int? priority = 7,
        int? scriptAddress = null, bool copied = false, string? variant = null)
    {
        using var stream = typeof(BlackOmenNavigationTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.black-omen324-reconstructed-0372.json")!;
        using var json = JsonDocument.Parse(stream);
        var root = json.RootElement;
        var actors = root.GetProperty("actors").Deserialize<FieldActorSnapshot[]>()!;
        var f = root.GetProperty("field");
        var leader = f.GetProperty("leader").GetInt32();
        actors = actors.Select(a => a.Index == 20 ? a with
        {
            ScriptCallsEnabled = doorCalls,
            ScriptPriority = priority,
            ScriptAddress = scriptAddress ?? a.ScriptAddress,
            FineX = variant == "misplaced" ? a.FineX + 256 : a.FineX,
            TileX = variant == "misplaced" ? a.TileX + 1 : a.TileX,
            ClassTag = variant == "removed" ? 0x87 : a.ClassTag,
            ScriptProcessingEnabled = variant != "paused"
        } :
            a.Index == leader && position is { } p ? a with
            {
                FineX = p.X,
                FineY = p.Y,
                TileX = p.X / 256,
                TileY = p.Y / 256,
                FractionX = p.X % 256,
                FractionY = p.Y % 256
            } : a).ToArray();
        var field = new FieldNavigationSnapshot(0x1000, 0x4000, 0x2000, 0x20000, actors.Length, 324, true, 1, 0, 0, leader,
            actors.Single(a => a.Index == leader), actors)
        { ActorCollisionRadius = f.GetProperty("collisionRadius").GetInt32(), LastPartySlotRaw = f.GetProperty("lastPartySlotRaw").GetInt32() };
        var s = root.GetProperty("story");
        var locals = s.GetProperty("locals").Deserialize<Dictionary<int, int>>()!;
        if (opened) locals[10] = 1;
        if (variant == "unknown") locals.Remove(10);
        var story = new FieldStoryState(s.GetProperty("point").GetInt32(), true)
        {
            Globals = s.GetProperty("globals").Deserialize<Dictionary<int, int>>()!,
            Locals = locals,
            Extended = s.GetProperty("extended").Deserialize<Dictionary<int, int>>()!
        };
        var map = root.GetProperty("map").Deserialize<FieldMapSnapshot>()!;
        if (opened || copied)
        {
            // Simulate the original E5 tile copy, not a product state write.
            byte[] Copy(byte[] original)
            {
                var changed = (byte[])original.Clone();
                for (var y = 0; y < 3; y++) Array.Copy(original, (45 + y) * 32 + 30, changed, (3 + y) * 32 + 11, 2);
                return changed;
            }
            map = map with
            {
                CollisionShapes = Copy(map.CollisionShapes),
                TerrainFlags = Copy(map.TerrainFlags),
                CollisionLayers = Copy(map.CollisionLayers)
            };
        }
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, map, new(2016, 8816, 6240, 12016), [], story);
    }

    internal static FieldMapSnapshot Map(int scene)
    {
        using var stream = typeof(BlackOmenNavigationTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.black-omen-initial-maps-0372.json")!;
        using var json = JsonDocument.Parse(stream);
        return json.RootElement.EnumerateArray().Single(r => r.GetProperty("Scene").GetInt32() == scene)
            .GetProperty("Map").Deserialize<FieldMapSnapshot>()!;
    }

    internal static NavigationFrame Frame(int scene, int? flags = 4, NavigationPoint? position = null,
        FieldMapSnapshot? map = null, IReadOnlyList<FieldActorSnapshot>? actors = null, FieldStoryState? state = null)
    {
        var player = position ?? (scene switch
        {
            97 => new(2048, 3327, 1),
            98 => new(6144, 5727, 1),
            320 => new(6144, 11647, 1),
            321 => new(6144, 6692, 1),
            _ => new NavigationPoint(3584, 12031, 1)
        });
        var leader = Actor(1, player.X, player.Y) with { ClassTag = 0, IsPartyMember = true };
        var loop = Actor(0, 0, 0) with { ClassTag = 7, DrawMode = 0 };
        var count = scene switch { 97 or 98 => 10, 320 or 321 => 9, _ => 21 };
        var field = new FieldNavigationSnapshot(0x1000, 0x4000, 0x2000, 0x20000, count, scene, true, 12, 0, 1, 1,
            leader, actors ?? [loop, leader]);
        var globals = new Dictionary<int, int> { [0x1F7] = 14, [0x1A8] = 1, [0x1A9] = 0, [0x71] = 16, [0x15A] = 0 };
        if (flags is { } known) globals[0x1A7] = known;
        state ??= new FieldStoryState(216, true) { Globals = globals, Locals = Enumerable.Range(0, 32).ToDictionary(i => i, _ => 0) };
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, map ?? Map(scene), new(0, 0, 64 * 256, 64 * 256), [], state);
    }

    internal static FieldActorSnapshot Actor(int slot, int x, int y) =>
        new(slot, x / 256, x, x % 256, y / 256, y, y % 256, 0, 1, 1, 14, 4, 0x2020, 1, 1, false, true, true, true);
    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
