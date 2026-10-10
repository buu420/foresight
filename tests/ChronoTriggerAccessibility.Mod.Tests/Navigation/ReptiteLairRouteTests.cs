using System.Runtime.CompilerServices;
using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Hole contact on the installed lair maps (reptite-lair-maps-0352.json, checked
/// against resources.bin). Hole tiles come from Atel_0344/0373/0374/0375: fixed holes are
/// set by 8B, and dug or late holes are moved by 8C or 8B. The scene-change arrival is the
/// drop point; on the installed collision the party stands three or four tiles lower, in a
/// small chamber that reaches that chamber's own hole. Tests start on that nearest standable
/// tile. Movement uses the independent native frame oracle; a hole actor's contact uses the
/// captured collision rules. Confirm is never pressed by the controller.</summary>
public sealed class ReptiteLairRouteTests
{
    private static readonly Dictionary<int, FieldMapSnapshot> Installed = Load();

    public static TheoryData<int, int, int, int, int, int> Holes => new()
    {
        // scene, hole actor, hole tile X/Y, landing tile X/Y
        { 284, 14, 33, 12, 23, 10 },
        { 284, 15, 29, 24, 23, 10 },   // dug onto a beetle's route tile (Atel_0373 0x306)
        { 285, 20, 25, 41, 20, 38 },
        { 285, 21, 27, 59, 20, 55 },
        { 285, 22, 36, 40, 42, 38 },
        { 285, 23, 44, 43, 33, 44 },   // placed later by fn3 (Atel_0374 0x6CF)
        { 285, 24, 36, 57, 36, 54 },
        { 286, 21, 3, 10, 10, 8 },
        { 286, 22, 5, 27, 12, 22 },
        { 286, 23, 19, 19, 29, 14 },
        { 286, 24, 37, 30, 44, 21 },
        { 222, 9, 53, 42, 56, 39 },
    };

    public static TheoryData<int, int, int, int, int, int, int, bool> Walks
    {
        get
        {
            var data = new TheoryData<int, int, int, int, int, int, int, bool>();
            foreach (var row in Holes)
            foreach (var speed in new[] { 16, 32 })
            foreach (var auto in new[] { true, false })
                data.Add((int)row[0], (int)row[1], (int)row[2], (int)row[3], (int)row[4], (int)row[5], speed, auto);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Walks))]
    public void HoleIsReachedAndFacedForConfirmWithNativeWalkOrDash(int scene, int hole, int tileX, int tileY,
        int landX, int landY, int speed, bool auto)
    {
        var run = Walk(scene, HoleActor(hole, tileX, tileY), landX, landY, speed, auto);
        Assert.True(run.Speech.Any(s => s.Contains("Arrived at Hole", StringComparison.Ordinal)),
            $"scene {scene} hole {hole} speed {speed} auto {auto} ended at {run.Player}: {string.Join(" | ", run.Speech.TakeLast(8))}");
        Assert.DoesNotContain(run.Speech, s => s.Contains("blocked") || s.Contains("No route") || s.Contains("Off route"));
        Assert.True(Math.Abs(run.Player.X - (landX * 256 + 128)) + Math.Abs(run.Player.Y - (landY * 256 + 255)) >= 256,
            "The landing already stood at the hole.");
        // Arrival leaves the player facing the hole within the native Confirm reach.
        Assert.Contains(run.Facing, run.Target.AnyConfirmFacings(run.Player));
    }

    [Fact]
    public void ParkedHiddenAndMovedHolesRefreshFromTheLiveActor()
    {
        var parked = HoleActor(15, 0, 0);
        Assert.DoesNotContain(Frame(284, Player(23, 10), parked).Targets, t => t.Id.StartsWith("actor:15:"));
        // A beetle burrows and 8C moves the same actor to its tile: the hole appears there.
        var dug = HoleActor(15, 29, 24);
        var target = Assert.Single(Frame(284, Player(23, 10), dug).Targets, t => t.Id.StartsWith("actor:15:"));
        Assert.Equal(("Hole B", NavigationCategory.Exits), (target.Label, target.Category));
        Assert.Equal(29, target.Position.X / 256);
        // During a battle the holes are hidden (7D); they return when shown again (7C).
        var hidden = dug with { DrawMode = FieldNavigationCapture.DrawModeHidden };
        Assert.DoesNotContain(Frame(284, Player(23, 10), hidden).Targets, t => t.Id.StartsWith("actor:15:"));
        Assert.Contains(Frame(284, Player(23, 10), dug).Targets, t => t.Id.StartsWith("actor:15:"));
    }

    [Fact]
    public void ChambersDoNotInventRoutesToAnotherChambersHole()
    {
        // 222's second hole (52,56) is outside the arrival chamber on the installed map.
        var frame = Frame(222, Player(56, 39), HoleActor(10, 52, 56));
        var target = Assert.Single(frame.Targets, t => t.Id.StartsWith("actor:10:"));
        Assert.Null(NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route);
    }

    private sealed record Run(List<string> Speech, NavigationPoint Player, NavigationDirection Facing, NavigationTarget Target);

    private static Run Walk(int scene, FieldActorSnapshot hole, int landX, int landY, int speed, bool auto)
    {
        var map = Installed[scene];
        var player = Player(landX, landY);
        var point = new NavigationPoint(player.FineX, player.FineY, 1);
        Assert.True(new FieldNavigationGraph(map).TryPosition(point.X, point.Y, 1, out point));
        var facing = NavigationDirection.South;
        NavigationFrame Current()
        {
            var lead = Player(0, 0) with { FineX = point.X, TileX = point.X >> 8, FineY = point.Y, TileY = point.Y >> 8 };
            var frame = Frame(scene, lead, map with { PlayerLayer = point.Layer }, hole);
            return frame with { Targets = frame.Targets.Where(t => t.Id.StartsWith($"actor:{hole.Index}:")).ToArray(), PlayerFacing = facing };
        }
        var rules = new FieldActorCollisionRules(FullGameNavigationTests.Field(scene) with { Actors = [hole] });
        var controller = new NavigationController();
        var frame = Current();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        var result = controller.Handle(auto ? NavigationCommand.ToggleWalk : NavigationCommand.Guide, frame, 1);
        var speech = new List<string>(result.Speech);
        var turn = NavigationDirection.None;
        for (var tick = 1; tick <= 3000 && result.Guiding; tick++)
        {
            foreach (var line in result.Speech)
                foreach (var (word, direction) in new[] { ("up", NavigationDirection.North), ("down", NavigationDirection.South),
                             ("left", NavigationDirection.West), ("right", NavigationDirection.East) })
                    if (line.Contains($"Turn {word}", StringComparison.Ordinal)) turn = direction;
            var command = auto ? result.Direction : result.ManualLeg?.Direction ?? turn;
            if (command != NavigationDirection.None)
            {
                facing = command;
                var next = NativeWalkingReplay.Move(map, point, command, speed);
                // The hole actor is solid to the native contact scan; a press into it only turns.
                if (!rules.BlocksMove(point.X, point.Y, next.X, next.Y)) point = next;
            }
            frame = Current();
            result = controller.Update(frame, tick * 16, !auto && command != NavigationDirection.None);
            speech.AddRange(result.Speech);
        }
        return new(speech, point, facing, frame.Targets.Single());
    }

    internal static FieldActorSnapshot Player(int tileX, int tileY) =>
        FullGameNavigationTests.Actor(1, tileX * 256 + 128, tileY * 256 + 255, true) with { ClassTag = 0 };

    internal static FieldActorSnapshot HoleActor(int index, int tileX, int tileY) =>
        FullGameNavigationTests.Actor(index, tileX * 256 + 128, tileY * 256 + 255) with { VisualIndex = 133 };

    private static NavigationFrame Frame(int scene, FieldActorSnapshot player, params FieldActorSnapshot[] actors) =>
        Frame(scene, player, Installed[scene], actors);

    private static NavigationFrame Frame(int scene, FieldActorSnapshot player, FieldMapSnapshot map, params FieldActorSnapshot[] actors)
    {
        var field = FullGameNavigationTests.Field(scene) with { LeadPlayer = player, Actors = [player, .. actors], ActorCount = 40 };
        var locals = Enumerable.Range(0, 64).ToDictionary(i => i, _ => 0);
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, map,
            new(0, 0, map.Width * 256, map.Height * 256), [],
            new FieldStoryState(123, true) { Locals = locals, Globals = new Dictionary<int, int> { [0x46] = 0 } });
    }

    private static Dictionary<int, FieldMapSnapshot> Load([CallerFilePath] string source = "")
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(source)!, "reptite-lair-maps-0352.json")));
        return json.RootElement.GetProperty("Maps").EnumerateArray().ToDictionary(e => e.GetProperty("Scene").GetInt32(),
            e => JsonSerializer.Deserialize<FieldMapSnapshot>(e.GetProperty("Map").GetRawText())! with { PlayerLayer = 1 });
    }

    private sealed class NoMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination) => false;
    }
}
