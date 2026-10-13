using System.Runtime.CompilerServices;
using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class TyrannoLaterRoomGuideTests
{
    [Theory]
    [InlineData(303, false, false, NavigationCommand.Guide)]
    [InlineData(303, false, false, NavigationCommand.ToggleWalk)]
    [InlineData(303, false, true, NavigationCommand.Guide)]
    [InlineData(303, false, true, NavigationCommand.ToggleWalk)]
    [InlineData(303, true, false, NavigationCommand.Guide)]
    [InlineData(303, true, false, NavigationCommand.ToggleWalk)]
    [InlineData(303, true, true, NavigationCommand.Guide)]
    [InlineData(303, true, true, NavigationCommand.ToggleWalk)]
    [InlineData(304, false, false, NavigationCommand.Guide)]
    [InlineData(304, false, false, NavigationCommand.ToggleWalk)]
    [InlineData(304, false, true, NavigationCommand.Guide)]
    [InlineData(304, false, true, NavigationCommand.ToggleWalk)]
    [InlineData(300, false, false, NavigationCommand.Guide)]
    [InlineData(300, false, false, NavigationCommand.ToggleWalk)]
    [InlineData(300, false, true, NavigationCommand.Guide)]
    [InlineData(300, false, true, NavigationCommand.ToggleWalk)]
    public void NativeRoomFloorsSupplyAUsableNextStoryLeg(int scene, bool east, bool completed,
        NavigationCommand command)
    {
        // Installed floors and script-proven copies/marker positions. These are
        // offline synthetic actor states, not live captures of these later rooms.
        var frame = Frame(scene, east, completed);
        var story = Assert.Single(frame.Targets, t => t.Id == "story:full:black-tyranno");
        Assert.False(story.IsStoryNote);
        Assert.NotEmpty(story.ApproachPoints);
        Assert.NotNull(NavigationPathfinder.Find(frame.Graph, frame.Player, story.ApproachPoints));
        if (scene == 303)
        {
            var ids = completed ? new[] { east ? 18 : 17 } : east ? new[] { 13, 16 } : new[] { 10 };
            Assert.All(story.ApproachPoints, p => Assert.Contains(frame.Targets,
                t => ids.Any(i => t.Id == $"landmark:{i}") && t.ApproachPoints.Contains(p)));
            if (completed) Assert.True(story.ConfirmFacings is not null || story.ApproachConfirms is { Count: > 0 });
            Assert.DoesNotContain(frame.Targets, t => t.Id.StartsWith("actor-encounter:", StringComparison.Ordinal));
        }
        var controller = new NavigationController();
        for (var i = 0; i < 3; i++) controller.Handle(NavigationCommand.NextCategory, frame, 0);
        var result = controller.Handle(command, frame, 1);
        Assert.True(result.Guiding);
        Assert.Equal(command == NavigationCommand.ToggleWalk, result.AutoWalking);
        Assert.DoesNotContain(result.Speech, s => s.StartsWith("No route"));
    }

    private static NavigationFrame Frame(int scene, bool east, bool completed, [CallerFilePath] string file = "")
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(file)!, "tyranno-installed-maps-0358.json")));
        var entry = doc.RootElement.EnumerateArray().Single(e => e.GetProperty("Scene").GetInt32() == scene);
        var map = JsonSerializer.Deserialize<FieldMapSnapshot>(entry.GetProperty("Map"))!;
        var x = scene == 303 ? east ? 29 : 11 : scene == 304 ? 7 : 49;
        var y = scene == 303 ? east ? 25 : 27 : scene == 304 ? 25 : 26;
        var player = FullGameNavigationTests.Actor(1, x * 256 + 128, y * 256 + 255, true) with { ClassTag = 0 };
        var actors = new List<FieldActorSnapshot> { player };
        FieldActorSnapshot Marker(int index, int tx, int ty) => FullGameNavigationTests.Actor(index, tx * 256 + 128, ty * 256 + 255) with
        {
            ClassTag = 7, LoadedFlag = 0, ActivationEnabled = 0, ActivationBinding = 128,
            ScriptCallsEnabled = true, ScriptProcessingEnabled = true,
        };
        var state = new FieldStoryState(0x98, true)
        {
            Globals = new Dictionary<int, int> { [0xED] = scene == 304 ? 0x80 : 0, [0xEE] = completed ? scene == 304 ? 1 : scene == 300 ? 8 : 0 : 0 },
            Locals = new Dictionary<int, int> { [8] = !east && completed ? 1 : 0, [9] = 0, [10] = 0, [11] = 0,
                [0x0C] = east && completed ? 1 : 0, [0x0D] = 0 },
        };
        if (scene == 303)
        {
            actors.AddRange(east ? new[] { Marker(13, 28, 23), Marker(16, 30, 23), Marker(18, 29, 5) }
                : new[] { Marker(9, 11, 22), Marker(10, 11, 18), Marker(11, 10, 26), Marker(12, 12, 26), Marker(17, 11, 5) });
            if (completed)
            {
                var copy = GameNavigationCatalog.ForScene(303)!.Actors.Single(a => a.Id == (east ? 13 : 10)).Actions.First(a => a.Kind == "Terrain").Copy!;
                map = Copy(map, copy);
            }
        }
        if (scene == 304) actors.AddRange(new[] { Marker(9, 7, 7), Marker(10, 6, 7) });
        if (scene == 300)
        {
            actors.Add(Marker(0, 255, 255));
            if (completed) map = Copy(map, new(61, 9, 63, 11, 48, 3, 59)); // Atel_0165 opening after face-off
        }
        var field = new FieldNavigationSnapshot(0x10000, 0x20000, 0x30000, 0x40000,
            actors.Max(a => a.Index) + 1, scene, true, 1, 0, 0, 1, player, actors.ToArray(), [1])
        { ActorCollisionRadius = 160, LastPartySlotRaw = 2, ContactActorRaw = 128, ConfirmActorRaw = 128 };
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, map,
            new(0, 0, 16384, 16384), [], state);
    }

    private static FieldMapSnapshot Copy(FieldMapSnapshot map, GameNavigationCatalog.TileCopy copy)
    {
        var shapes = map.CollisionShapes.ToArray(); var terrain = map.TerrainFlags.ToArray(); var layers = map.CollisionLayers.ToArray();
        for (var y = 0; y <= copy.Bottom - copy.Top; y++)
        for (var x = 0; x <= copy.Right - copy.Left; x++)
        {
            var source = (copy.Top + y) * map.Width + copy.Left + x;
            var target = (copy.Y + y) * map.Width + copy.X + x;
            if ((copy.Flags & 8) != 0) shapes[target] = map.CollisionShapes[source];
            if ((copy.Flags & 16) != 0) terrain[target] = map.TerrainFlags[source];
            if ((copy.Flags & 32) != 0) layers[target] = map.CollisionLayers[source];
        }
        return map with { CollisionShapes = shapes, TerrainFlags = terrain, CollisionLayers = layers };
    }
    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
