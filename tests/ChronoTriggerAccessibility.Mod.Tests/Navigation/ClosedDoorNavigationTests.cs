using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class ClosedDoorNavigationTests
{
    [Theory]
    [InlineData(1930, 3571, 0)]
    [InlineData(1930, 3571, 1)]
    [InlineData(1930, 3571, 2)]
    [InlineData(1930, 3571, 3)]
    [InlineData(1536, 3583, 0)]
    [InlineData(1536, 3583, 1)]
    [InlineData(1536, 3583, 2)]
    [InlineData(1536, 3583, 3)]
    public void SpekkioReturnRouteStagesAnAvailableDoorFromReportedPositions(int x, int y, int facing)
    {
        var frame = Room(new(x, y, 1), OpenableState(facing));
        var exit = Assert.Single(frame.Targets, t => t.Id == "exit:0");
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, exit.ApproachPoints);
        Assert.NotNull(route.Route);
        Assert.NotNull(route.IntermediateContact);
        Assert.Contains(route.IntermediateId, new[] { "landmark:15", "landmark:16" });
        var live = new FieldNavigationGraph(Map());
        Assert.All(route.Route, p => Assert.True(live.TryPosition(p.X, p.Y, p.Layer, out var at) && at == p));
    }

    [Fact]
    public void ClosedSpekkioDoorIsReadableSceneryWithARealApproach()
    {
        var frame = Room(new(1930, 3571, 1), OpenableState());
        var door = Assert.Single(frame.Targets, t => t.Id == "landmark:15");
        Assert.Equal("Door to the main room", door.Label);
        Assert.Equal(NavigationCategory.Objects, door.Category);
        Assert.NotEmpty(door.ApproachPoints);
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, door.ApproachPoints);
        Assert.NotNull(route.Route);
        Assert.Equal("landmark:16", route.IntermediateId);
        Assert.Equal("Continue down through the doorway.", door.ArrivalInstruction);
        Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Objects);
    }

    [Theory]
    [InlineData(NavigationCommand.Guide)]
    [InlineData(NavigationCommand.ToggleWalk)]
    public void ReturnGuidanceKeepsMovingSouthAtTheNativeOpeningContact(NavigationCommand command)
    {
        var first = Room(new(1930, 3571, 1), OpenableState(2));
        var exit = Assert.Single(first.Targets, t => t.Id == "exit:0");
        var route = NavigationPathfinder.Search(first.Graph, first.Player, exit.ApproachPoints).Route!;
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, first, 0);
        var started = controller.Handle(command, first, 16);
        Assert.True(started.Guiding);
        var endpoint = Room(route[^1], OpenableState(3));
        var result = controller.Update(endpoint, 32);
        Assert.True(result.Guiding, string.Join(" ", result.Speech));
        Assert.Equal(command == NavigationCommand.ToggleWalk, result.AutoWalking);
        Assert.Contains("Continue down until the passage opens.", result.Speech);
        Assert.Equal(command == NavigationCommand.ToggleWalk ? NavigationDirection.South : NavigationDirection.None, result.Direction);
        if (command == NavigationCommand.ToggleWalk)
        {
            var stopped = controller.Update(endpoint, 3100);
            Assert.False(stopped.AutoWalking);
            Assert.Equal(NavigationDirection.None, stopped.Direction);
            Assert.Contains(stopped.Speech, s => s.Contains("passage did not open"));
        }
    }

    [Fact]
    public void OpenedFloorIsRecapturedBeforeTheReturnRouteContinuesAndReentryNeedsOpeningAgain()
    {
        var state = OpenableState();
        var first = Room(new(1930, 3571, 1), state);
        var exit = Assert.Single(first.Targets, t => t.Id == "exit:0");
        var stage = NavigationPathfinder.Search(first.Graph, first.Player, exit.ApproachPoints);
        var opened = OpenedMap();
        var locals = state.Locals.ToDictionary(p => p.Key, p => p.Value); locals[7] = 1;
        var next = Room(stage.Route![^1], state with { Locals = locals }, opened);
        exit = Assert.Single(next.Targets, t => t.Id == "exit:0");
        var continuation = NavigationPathfinder.Search(next.Graph, next.Player, exit.ApproachPoints);
        Assert.NotNull(continuation.Route);
        Assert.Null(continuation.IntermediateId);
        Assert.All(continuation.Route, p => Assert.True(new FieldNavigationGraph(opened).TryPosition(p.X, p.Y, p.Layer, out _)));
        var reentry = Room(new(1536, 3583, 1), OpenableState(0));
        exit = Assert.Single(reentry.Targets, t => t.Id == "exit:0");
        Assert.NotNull(NavigationPathfinder.Search(reentry.Graph, reentry.Player, exit.ApproachPoints).IntermediateId);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void HandledOrDisabledOpeningCannotAuthorizeClosedFloor(bool handled, bool disabled)
    {
        var state = OpenableState();
        var locals = state.Locals.ToDictionary(p => p.Key, p => p.Value); locals[7] = handled ? 1 : 0;
        var frame = Room(new(1930, 3571, 1), state with { Locals = locals }, disabled: disabled);
        var exit = Assert.Single(frame.Targets, t => t.Id == "exit:0");
        Assert.Null(NavigationPathfinder.Search(frame.Graph, frame.Player, exit.ApproachPoints).Route);
    }

    [Fact]
    public void UnauditedTouchWithAdditionalEffectsKeepsItsExistingWaitInsteadOfDrivingIntoIt()
    {
        // A native terrain action is not sufficient to classify an automatic
        // doorway: some handlers also change story switches or start cutscenes.
        var map = CastleNavigationTests.ClosedStair();
        var copy = new GameNavigationCatalog.TileCopy(3, 0, 5, 2, 30, 38, 59);
        var scene = new GameNavigationCatalog.Scene(268, null, [],
            [new(9, [], true, true, null, false, [],
                [new("Terrain", [], true, Copy: copy), new("Switch", [], true)])], []);
        var graph = new FieldTerrainGraph(map, [Marker(9, 8064, 10495)], OpenableState(), scene, []);
        var route = NavigationPathfinder.Search(graph, new(8064, 13823, 1), [new(9856, 2176, 1)]);
        Assert.NotNull(route.Route);
        Assert.Equal("landmark:9", route.IntermediateId);
        Assert.Null(route.IntermediateContact);
    }

    [Theory]
    [InlineData(120, 15, 11136, 3, 9, NavigationDirection.North)]
    [InlineData(120, 15, 8576, 0, 10, NavigationDirection.South)]
    [InlineData(21, 78, 11136, 3, 33, NavigationDirection.North)]
    [InlineData(21, 50, 8576, 0, 34, NavigationDirection.South)]
    public void OtherVerifiedCastleDoorsUseTheirNativeContactAndFreshOpenedFloor(
        int scene, int point, int y, int exitId, int actor, NavigationDirection direction)
    {
        var map = CastleMap(scene);
        var state = new FieldStoryState(point, false)
            { Locals = Enumerable.Range(0, 256).ToDictionary(i => i, _ => 0) };
        var markers = scene == 120 ? new[] { Marker(9, 8064, 10495), Marker(10, 8064, 10239) }
            : new[] { Marker(33, 8064, 10495), Marker(34, 8064, 10239) };
        var player = Marker(1, 8064, y) with { IsPartyMember = true, ClassTag = 0, LoadedFlag = 1 };
        var field = Field(scene, player, markers);
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        var frame = source.Build(field, map, new(0, 0, 16384, 16384), [], state);
        var exit = Assert.Single(frame.Targets, t => t.Id == $"exit:{exitId}");
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, exit.ApproachPoints);
        Assert.NotNull(route.Route);
        Assert.Equal($"landmark:{actor}", route.IntermediateId);
        Assert.NotNull(route.IntermediateContact);
        Assert.All(route.Route, p => Assert.True(new FieldNavigationGraph(map).TryPosition(p.X, p.Y, p.Layer, out _)));
        var controller = new NavigationController();
        frame = frame with { Targets = [exit] };
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        Assert.True(controller.Handle(NavigationCommand.ToggleWalk, frame, 16).AutoWalking);
        var atContact = frame with { Player = route.Route[^1] };
        var waiting = controller.Update(atContact, 32);
        Assert.True(waiting.AutoWalking);
        Assert.Equal(direction, waiting.Direction);

        var copy = GameNavigationCatalog.ForScene(scene)!.Actors.Single(a => a.Id == actor).Actions
            .First(a => a.Kind == "Terrain" && a.Touch).Copy!;
        var opened = CopyMap(map, copy);
        var locals = state.Locals.ToDictionary(p => p.Key, p => p.Value); locals[scene == 120 ? 6 : 7] = 1;
        var next = source.Build(field with { LeadPlayer = player with { FineY = route.Route[^1].Y } }, opened,
            new(0, 0, 16384, 16384), [], state with { Locals = locals });
        exit = Assert.Single(next.Targets, t => t.Id == $"exit:{exitId}");
        var continuation = NavigationPathfinder.Search(next.Graph, next.Player, exit.ApproachPoints);
        Assert.NotNull(continuation.Route);
        Assert.Null(continuation.IntermediateId);
        Assert.All(continuation.Route, p => Assert.True(new FieldNavigationGraph(opened).TryPosition(p.X, p.Y, p.Layer, out _)));
    }

    [Fact]
    public void SouthernCastleDoorKeepsTheNativeStoryRestriction()
    {
        var player = Marker(1, 8064, 11136) with { IsPartyMember = true, ClassTag = 0, LoadedFlag = 1 };
        var state = new FieldStoryState(77, false)
            { Locals = Enumerable.Range(0, 256).ToDictionary(i => i, _ => 0) };
        var frame = new FieldNavigationSource(new NoMemory(), _ => { }).Build(
            Field(21, player, [Marker(33, 8064, 10495), Marker(34, 8064, 10239)]), CastleMap(21),
            new(0, 0, 16384, 16384), [], state);
        var exit = Assert.Single(frame.Targets, t => t.Id == "exit:3");
        Assert.Null(NavigationPathfinder.Search(frame.Graph, frame.Player, exit.ApproachPoints).Route);
    }

    [Fact]
    public void MainRoomNameAndDestinationDoNotExposeTheInternalCharacterName()
    {
        Assert.Equal("End of Time, Main Room", GameNavigationCatalog.AreaName(464));
        Assert.Equal("To End of Time, Main Room", GameNavigationCatalog.DestinationLabel(464));
        var map = new FieldMapSnapshot(8, 8, new byte[64], new byte[64], Enumerable.Repeat((byte)1, 64).ToArray(),
            1, false, 8, 8, Enumerable.Repeat((byte)128, 64).ToArray());
        var player = Marker(1, 512, 512) with { ClassTag = 0, IsPartyMember = true, LoadedFlag = 1 };
        var field = Field(464, player, []);
        var frame = new FieldNavigationSource(new NoMemory(), _ => { }, _ => "End of Time, Gaspar's Room")
            .Build(field, map, new(0, 0, 2048, 2048), [], new(77, false));
        Assert.Equal("End of Time, Main Room", frame.AreaName);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(-1)]
    public void LockedOrUnreadableSpekkioOpeningGuardDoesNotAuthorizeTheExit(int enabled)
    {
        var state = OpenableState();
        var locals = state.Locals.ToDictionary(p => p.Key, p => p.Value);
        if (enabled == -1) locals.Remove(38); else locals[38] = enabled;
        var frame = Room(new(1930, 3571, 1), state with { Locals = locals });
        var exit = Assert.Single(frame.Targets, t => t.Id == "exit:0");
        Assert.Null(NavigationPathfinder.Search(frame.Graph, frame.Player, exit.ApproachPoints).Route);
    }

    private static FieldStoryState OpenableState(int facing = 2) => new(77, false)
    {
        Locals = Enumerable.Range(0, 256).ToDictionary(i => i, i => i == 38 ? facing : 0),
    };

    private static NavigationFrame Room(NavigationPoint at, FieldStoryState story, FieldMapSnapshot? map = null, bool disabled = false)
    {
        var lead = Marker(1, at.X, at.Y) with { ClassTag = 0, IsPartyMember = true, LoadedFlag = 1 };
        var field = Field(465, lead, [Marker(15, 1408, 3839) with { ScriptCallsEnabled = !disabled },
            Marker(16, 1408, 3583) with { ScriptCallsEnabled = !disabled }]);
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, map ?? Map(), new(0, 0, 4224, 8192), [], story);
    }

    private static FieldNavigationSnapshot Field(int scene, FieldActorSnapshot lead, FieldActorSnapshot[] actors) =>
        new(0x1000, 0x4000, 0x2000, 0x20000, actors.Length + 1, scene, true, 1, 0, 0, 0, lead, [lead, .. actors])
            { ActorCollisionRadius = FieldActorCollisionRules.Radius(scene) };

    private static FieldActorSnapshot Marker(int index, int x, int y) =>
        new(index, x >> 8, x, x & 255, y >> 8, y, y & 255, 0, 1, 0, 0, 7, 0, 0, 128, false, true, true, true);

    private static FieldMapSnapshot Map()
    {
        using var stream = typeof(ClosedDoorNavigationTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.spekkio-initial-map-0332.json")!;
        return JsonSerializer.Deserialize<FieldMapSnapshot>(stream)!;
    }

    private static FieldMapSnapshot OpenedMap()
    {
        // Atel0284 E5 copies the three installed collision planes. Simulate the
        // next native capture, not movement authority based on a preview.
        var map = Map();
        var copy = GameNavigationCatalog.ForScene(465)!.Actors.Single(a => a.Id == 16).Actions
            .Single(a => a.Kind == "Terrain").Copy!;
        return CopyMap(map, copy);
    }

    private static FieldMapSnapshot CopyMap(FieldMapSnapshot map, GameNavigationCatalog.TileCopy copy)
    {
        var result = map with { CollisionShapes = (byte[])map.CollisionShapes.Clone(),
            TerrainFlags = (byte[])map.TerrainFlags.Clone(), CollisionLayers = (byte[])map.CollisionLayers.Clone() };
        foreach (var (original, updated) in new[] { (map.CollisionShapes, result.CollisionShapes),
                     (map.TerrainFlags, result.TerrainFlags), (map.CollisionLayers, result.CollisionLayers) })
        for (var y = 0; y <= copy.Bottom - copy.Top; y++)
        for (var x = 0; x <= copy.Right - copy.Left; x++)
            updated[(copy.Y + y) * map.Width + copy.X + x] = original[(copy.Top + y) * map.Width + copy.Left + x];
        return result;
    }

    private static FieldMapSnapshot CastleMap(int scene)
    {
        using var stream = typeof(ClosedDoorNavigationTests).Assembly.GetManifestResourceStream(
            $"ChronoTriggerAccessibility.Mod.Tests.Navigation.castle-hall-map-{scene}-0347.json")!;
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        var width = root.GetProperty("Width").GetInt32(); var height = root.GetProperty("Height").GetInt32();
        byte[] Plane(string name) => root.GetProperty(name).GetBytesFromBase64();
        return new(width, height, Plane("CollisionShapes"), Plane("TerrainFlags"), Plane("CollisionLayers"),
            1, false, width, height, Plane("ExitCells"));
    }

    private sealed class NoMemory : IReadableMemory
    { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
