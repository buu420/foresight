using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class TemporalLabPassageTests
{
    [Theory]
    [InlineData("capture-1-4289", false)] [InlineData("capture-1-4289", true)]
    [InlineData("capture-2-1818", false)] [InlineData("capture-2-1818", true)]
    public void ConveyorRoomExitAndStoryReachTheNativeNorthwestDoor(string capture, bool automatic)
    {
        var sample = Sample(capture);
        var frame = Build(sample.Field, sample.Map, sample.Story);
        Assert.Equal(0, sample.Story.Extra(0x2A));
        var door = Assert.Single(frame.Targets, t => t.Id == "landmark:12");
        Assert.Contains("door", door.Label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("scenery", door.Label, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(door.ContactPosition);
        Assert.NotNull(NavigationPathfinder.Search(frame.Graph, frame.Player, door.ApproachPoints).Route);
        foreach (var id in new[] { "exit:2", "story:full:lavos" })
        {
            var target = Assert.Single(frame.Targets, t => t.Id == id);
            var route = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints);
            Assert.NotNull(route.Route);
            Assert.Equal("landmark:12", route.IntermediateId);
            Assert.NotNull(route.IntermediateContact);
            var (_, result) = Start(frame, id, automatic);
            Assert.True(result.Guiding, string.Join(" | ", result.Speech));
            Assert.Equal(automatic, result.AutoWalking);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PatrolDetectionIsNotAnExitOrABarrierToTheReturnDoor(bool automatic)
    {
        var sample = Sample("capture-2-1872");
        var frame = Build(sample.Field, sample.Map, sample.Story);
        Assert.Equal(new[] { "exit:0", "exit:1", "exit:2" }, frame.Targets
            .Where(t => t.Category == NavigationCategory.Exits).Select(t => t.Id).Order().ToArray());
        Assert.DoesNotContain(frame.Targets, t => t.Id.StartsWith("script-region:14:", StringComparison.Ordinal));
        foreach (var id in new[] { "exit:0" })
        {
            var target = Assert.Single(frame.Targets, t => t.Id == id);
            var route = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints);
            Assert.NotNull(route.Route);
            var (_, result) = Start(frame, id, automatic);
            Assert.True(result.Guiding, $"{id}: {string.Join(" | ", result.Speech)}");
        }
        // The captured robots and their native collision authority remain intact.
        Assert.Equal(5, sample.Field.Actors.Count(a => a.ClassTag == 5 && a.VisualIndex == 284));
        Assert.Contains(frame.Targets, t => t.ActorSlot is >= 8 and <= 12);
        // The eastern native barrier still closes the optional side room at E2A0.
        var side = frame.Targets.Single(t => t.Id == "exit:1");
        Assert.Null(NavigationPathfinder.Search(frame.Graph, frame.Player, side.ApproachPoints).Route);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TheReleasedSideRoomReachesItsDoorAndKeepsTheNativeChestBranch(bool automatic)
    {
        var sample = Sample("capture-2-1872");
        var map = Released(sample.Map);
        var story = sample.Story with { Extended = new Dictionary<int, int>(sample.Story.Extended) { [0x2A] = 0x1A },
            Locals = new Dictionary<int, int>(sample.Story.Locals) { [121] = 1 } };
        var frame = Build(sample.Field, map, story);
        var door = Assert.Single(frame.Targets, t => t.Id == "landmark:16");
        Assert.Contains("door", door.Label, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(door.ContactPosition);
        Assert.NotNull(NavigationPathfinder.Search(frame.Graph, frame.Player, door.ApproachPoints).Route);
        var exit = Assert.Single(frame.Targets, t => t.Id == "exit:1");
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, exit.ApproachPoints);
        Assert.NotNull(route.Route);
        Assert.Equal("landmark:16", route.IntermediateId);
        Assert.True(Start(frame, exit.Id, automatic).Result.Guiding);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ArrivalAtTheSecurityRoomRequiresCrossingItsActualExitCell(bool automatic)
    {
        var sample = Sample("capture-2-1872");
        // Derived near-exit pose, corroborated by the log's repeated X256 arrivals.
        var field = At(sample.Field, 256, 1951);
        var frame = Build(field, sample.Map, sample.Story);
        var before = frame.Player; var inside = new NavigationPoint(240, 1951, 1);
        Assert.False(frame.Graph.IsSameTerminal(before, inside));
        var (controller, result) = Start(frame, "exit:2", automatic);
        Assert.True(result.Guiding, string.Join(" | ", result.Speech));
        Assert.DoesNotContain(result.Speech, s => s.StartsWith("Arrived", StringComparison.Ordinal));
        Assert.NotEqual(NavigationDirection.None, automatic ? result.Direction : result.ManualLeg?.Direction);
        var target = frame.Targets.Single(t => t.Id == "exit:2");
        var actualGoal = NavigationPathfinder.Search(frame.Graph, before, target.ApproachPoints).Route![^1];
        Assert.InRange(actualGoal.X, 0, 255);
        result = controller.Update(frame with { Player = actualGoal }, 32, !automatic);
        Assert.False(result.Guiding);
        Assert.Contains(result.Speech, s => s.StartsWith("Arrived", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("calls")] [InlineData("missing-local")] [InlineData("native-lock")]
    public void AnUnavailableDoorCannotInventAContinuation(string variant)
    {
        var sample = Sample("capture-2-1818");
        var open = Open(sample.Map, GameNavigationCatalog.ActorInfo(617, sample.Field.Actors.Single(a => a.Index == 12))!
            .Actions.First(a => a.Touch && a.Copy is not null).Copy!);
        var goals = Build(sample.Field, open, sample.Story).Targets.Single(t => t.Id == "exit:2").ApproachPoints;
        var field = variant == "calls" ? sample.Field with { Actors = sample.Field.Actors
            .Select(a => a.Index == 12 ? a with { ScriptCallsEnabled = false } : a).ToArray() } : sample.Field;
        var locals = new Dictionary<int, int>(sample.Story.Locals);
        var extra = new Dictionary<int, int>(sample.Story.Extended);
        if (variant == "missing-local") locals.Remove(100);
        if (variant == "native-lock") extra[0x2A] = 2;
        var frame = Build(field, sample.Map, sample.Story with { Locals = locals, Extended = extra });
        Assert.NotEmpty(goals);
        Assert.Null(NavigationPathfinder.Search(frame.Graph, frame.Player, goals).Route);
    }

    [Theory]
    [InlineData("capture-2-1818", 12, 2, 100, 2, false)]
    [InlineData("capture-2-1818", 12, 2, 100, 2, true)]
    [InlineData("capture-2-1872", 16, 1, 122, 1, false)]
    [InlineData("capture-2-1872", 16, 1, 122, 1, true)]
    public void FreshOpenedTerrainContinuesToTheRealExit(string capture, int actor, int exit, int local, int value, bool automatic)
    {
        var sample = Sample(capture);
        var marker = sample.Field.Actors.Single(a => a.Index == actor);
        var action = GameNavigationCatalog.ActorInfo(sample.Field.SceneId, marker)!.Actions.First(a => a.Touch && a.Copy is not null);
        // Derived replay of the installed E5 FB copy and its following native local write.
        var opened = Open(sample.Field.SceneId == 618 ? Released(sample.Map) : sample.Map, action.Copy!);
        var story = sample.Story with { Locals = new Dictionary<int, int>(sample.Story.Locals) { [local] = value },
            Extended = new Dictionary<int, int>(sample.Story.Extended) { [0x2A] = sample.Field.SceneId == 618 ? 0x1A : 0 } };
        var frame = Build(sample.Field, opened, story);
        var target = Assert.Single(frame.Targets, t => t.Id == $"exit:{exit}");
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints);
        Assert.NotNull(route.Route);
        Assert.Null(route.IntermediateId);
        Assert.True(Start(frame, target.Id, automatic).Result.Guiding);
    }

    internal static (FieldNavigationSnapshot Field, FieldMapSnapshot Map, FieldStoryState Story) Sample(string capture)
    {
        using var doc = JsonDocument.Parse(typeof(TemporalLabPassageTests).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.temporal-lab-captures-0385.json")!);
        var root = doc.RootElement.GetProperty("captures").EnumerateArray().Single(c => c.GetProperty("captureId").GetString() == capture);
        var actors = root.GetProperty("actors").Deserialize<FieldActorSnapshot[]>()!;
        var f = root.GetProperty("field"); var s = root.GetProperty("story");
        var lead = actors.Single(a => a.Index == f.GetProperty("leader").GetInt32());
        // Addresses, full-map viewport and party list below are derived; the native
        // map/actors/flags/radius/last-slot data retain their exact captured values.
        var field = new FieldNavigationSnapshot(0x1000, 0x4000, 0x2000, 0x20000, actors.Length,
            f.GetProperty("scene").GetInt32(), true, 1, 0, 0, lead.Index, lead, actors)
        {
            ActorCollisionRadius = f.GetProperty("collisionRadius").GetInt32(),
            LastPartySlotRaw = f.GetProperty("lastPartySlotRaw").GetInt32(),
            ContactActorRaw = f.GetProperty("contactActorRaw").GetInt32(), ConfirmActorRaw = f.GetProperty("confirmActorRaw").GetInt32(),
        };
        var story = new FieldStoryState(s.GetProperty("point").GetInt32(), false)
        {
            Globals = s.GetProperty("globals").Deserialize<Dictionary<int, int>>()!,
            Locals = s.GetProperty("locals").Deserialize<Dictionary<int, int>>()!,
            Extended = s.GetProperty("extended").Deserialize<Dictionary<int, int>>()!,
            Party = actors.Where(a => a.IsPartyMember && a.IsPlayerClass).OrderBy(a => a.Index).Select(a => a.VisualIndex).ToArray(),
        };
        return (field, root.GetProperty("map").Deserialize<FieldMapSnapshot>()!, story);
    }
    internal static FieldNavigationSnapshot At(FieldNavigationSnapshot field, int x, int y)
    {
        var lead = field.LeadPlayer! with { FineX = x, TileX = x / 256, FractionX = x % 256, FineY = y, TileY = y / 256, FractionY = y % 256 };
        return field with { LeadPlayer = lead, Actors = field.Actors.Select(a => a.Index == lead.Index ? lead : a).ToArray() };
    }
    internal static NavigationFrame Build(FieldNavigationSnapshot field, FieldMapSnapshot map, FieldStoryState story, FieldViewport? viewport = null) =>
        new FieldNavigationSource(new Empty(), _ => { }).Build(field, map,
            viewport ?? new(0, 0, map.Width * 256, map.Height * 256), [], story);
    internal static (NavigationController Controller, NavigationResult Result) Start(NavigationFrame frame, string id, bool automatic)
    {
        var target = frame.Targets.Single(t => t.Id == id); var controller = new NavigationController();
        for (var i = 0; i < (int)target.Category; i++) controller.Handle(NavigationCommand.NextCategory, frame, 0);
        for (var i = 0; i < frame.Targets.Count && controller.TargetId != id; i++) controller.Handle(NavigationCommand.NextTarget, frame, 0);
        Assert.Equal(id, controller.TargetId);
        return (controller, controller.Handle(automatic ? NavigationCommand.ToggleWalk : NavigationCommand.Guide, frame, 1));
    }
    private static FieldMapSnapshot Open(FieldMapSnapshot map, GameNavigationCatalog.TileCopy copy)
    {
        Assert.Equal(251, copy.Flags);
        var opened = map with { CollisionShapes = map.CollisionShapes.ToArray(), CollisionLayers = map.CollisionLayers.ToArray(), TerrainFlags = map.TerrainFlags.ToArray() };
        foreach (var (from, to) in new[] { (map.CollisionShapes, opened.CollisionShapes), (map.CollisionLayers, opened.CollisionLayers), (map.TerrainFlags, opened.TerrainFlags) })
            for (var y = copy.Top; y <= copy.Bottom; y++) Array.Copy(from, y * map.Width + copy.Left, to,
                (copy.Y + y - copy.Top) * map.Width + copy.X, copy.Right - copy.Left + 1);
        return opened;
    }
    internal static FieldMapSnapshot Released(FieldMapSnapshot map) =>
        // Atel0618 startup028E/0296 and background0A66/0A6E. Derived native-copy
        // replay only; the unopened captured map remains unchanged.
        Open(Open(map, new(12, 37, 12, 40, 6, 25, 251)), new(0, 37, 0, 41, 50, 22, 251));
    private sealed class Empty : IReadableMemory { public bool TryRead(nuint address, Span<byte> bytes) => false; }
}
