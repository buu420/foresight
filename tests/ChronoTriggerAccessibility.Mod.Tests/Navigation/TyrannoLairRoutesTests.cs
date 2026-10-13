using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Story 0x98 inside the Tyranno Lair. Gates from the installed scripts
/// (artifacts/research/tyranno-0358/findings.md): 298 actor 0 fn1 opens the exit-5 gate on
/// 0xED bit 80 and the exit-8 gate on 0xED bit 40; 303's wall switches (actors 18 and 17)
/// toggle those bits; the skulls open on locals 0C/0D (east, actors 13/16) and 08 (west,
/// actor 10); 304's north markers 9/10 start the battle and set 0xEE bit 01; 300 actor 0
/// runs the face-off below row 20 and sets 0xEE bit 08; 299 opens its inner passage on
/// 0x5C bit 10. Room bands are the installed floors' connected sections.</summary>
public sealed class TyrannoLairRoutesTests
{
    private const string Continue = "Continue through the Tyranno Lair.";

    public static TheoryData<int, int, int, int, int, int, string> Rooms => new()
    {
        // scene, tile x, tile y, 0xED, 0xEE, 0x5C, expected goal ("exit:n", "actor:a,b" or "none")
        { 298, 15, 10, 0, 0, 0, "exit:0" },       // northwest terrace
        { 298, 40, 10, 0, 0, 0, "exit:2" },       // northeast terrace, via the top room to the northwest
        { 298, 20, 30, 0, 0, 0, "exit:6" },       // middle terrace, gate closed
        { 298, 20, 30, 0x80, 0, 0, "exit:5" },    // middle terrace, gate open
        { 298, 20, 30, 0x40, 0, 0, "exit:6" },
        { 298, 20, 55, 0x80, 1, 0, "exit:9" },    // southern terrace, gate closed
        { 298, 20, 55, 0xC0, 1, 0, "exit:8" },    // southern terrace, gate open
        { 298, 20, 20, 0, 0, 0, "none" },         // between the terraces: no native floor
        { 305, 25, 50, 0, 0, 0, "exit:0" },       // forward, never back to the northwest terrace
        { 302, 20, 12, 0, 0, 0, "exit:0" },       // top room
        { 302, 53, 42, 0, 0, 0, "exit:2" },       // small east room
        { 302, 13, 58, 0, 0, 0, "exit:5" },       // small west room
        { 302, 32, 58, 0x80, 1, 0, "exit:7" },    // small centre room
        { 302, 55, 15, 0, 0, 0x10, "exit:8" },    // prison section
        { 302, 40, 30, 0, 0, 0, "none" },
        { 430, 20, 38, 0, 0, 0x10, "exit:0" },
        { 299, 55, 45, 0, 0, 0x10, "exit:2" },    // entrance, inner passage open
        { 299, 55, 45, 0, 0, 0, "none" },         // entrance, inner passage closed
        { 299, 50, 35, 0, 0, 0x10, "exit:2" },
        { 304, 7, 20, 0x80, 0, 0, "actor:9,10" }, // north battle markers
        { 304, 7, 20, 0x80, 1, 0, "exit:0" },
        { 300, 49, 24, 0xC0, 1, 0, "actor:0" },   // face-off coordinate event
        { 300, 49, 24, 0xC0, 9, 0, "exit:1" },
    };

    [Theory]
    [MemberData(nameof(Rooms))]
    public void EachRoomContinuesThroughItsOwnNativePassage(int scene, int x, int y, int ed, int ee, int x5c, string expected)
    {
        var result = Route(scene, x, y, State(ed, ee, x5c));
        Assert.Equal(expected, Describe(result));
        Assert.Equal("full:black-tyranno", result.Id);
        if (expected != "none") Assert.Equal(Continue, result.Instruction);
    }

    public static TheoryData<int, int, int, int, int, int, string> SkullRooms => new()
    {
        // tile x, y, 0xED, local 08, local 0C, local 0D, expected
        { 29, 22, 0, 0, 0, 0, "actor:13,16" },  // east skull closed: both floor switches, no preference
        { 29, 22, 0, 0, 1, 0, "actor:18" },
        { 29, 22, 0, 0, 0, 1, "actor:18" },
        { 29, 8, 0, 0, 1, 0, "actor:18" },      // inside the opened skull
        { 29, 22, 0x80, 0, 1, 0, "exit:1" },    // gate already open: never press it again
        { 29, 8, 0x80, 0, 0, 0, "exit:1" },
        { 11, 22, 0x80, 0, 0, 0, "actor:10" },  // west skull closed
        { 11, 22, 0x80, 1, 0, 0, "actor:17" },
        { 11, 8, 0x80, 1, 0, 0, "actor:17" },
        { 11, 22, 0xC0, 1, 0, 0, "exit:0" },    // gate already open
        { 20, 22, 0, 0, 0, 0, "none" },         // solid rock between the two rooms
    };

    [Theory]
    [MemberData(nameof(SkullRooms))]
    public void SkullRoomsFollowTheLoadedSkullAndGateState(int x, int y, int ed, int local8, int localC, int localD, string expected)
    {
        var state = State(ed, 0, 0) with
        {
            Locals = new Dictionary<int, int> { [0x08] = local8, [0x0C] = localC, [0x0D] = localD },
        };
        Assert.Equal(expected, Describe(Route(303, x, y, state)));
    }

    [Theory]
    [InlineData(298, 20, 30, "ed")]
    [InlineData(298, 20, 55, "ed")]
    [InlineData(303, 29, 22, "ed")]
    [InlineData(303, 29, 22, "local0C")]
    [InlineData(303, 11, 22, "local08")]
    [InlineData(304, 7, 20, "ee")]
    [InlineData(300, 49, 24, "ee")]
    [InlineData(299, 55, 45, "5c")]
    public void UnknownNativeStateNeverPromisesAPassage(int scene, int x, int y, string missing)
    {
        // The east case keeps its own gate closed so the skull locals are what decide.
        var state = State(missing == "local0C" ? 0x40 : 0x80, 0, 0x10) with
        {
            Locals = new Dictionary<int, int> { [0x08] = 0, [0x0C] = 0, [0x0D] = 0 },
        };
        state = missing switch
        {
            "ed" => state with { Globals = state.Globals.Where(g => g.Key != 0xED).ToDictionary() },
            "ee" => state with { Globals = state.Globals.Where(g => g.Key != 0xEE).ToDictionary() },
            "5c" => state with { Globals = state.Globals.Where(g => g.Key != 0x5C).ToDictionary() },
            "local0C" => state with { Locals = state.Locals.Where(l => l.Key != 0x0C).ToDictionary() },
            _ => state with { Locals = state.Locals.Where(l => l.Key != 0x08).ToDictionary() },
        };
        var result = Route(scene, x, y, state);
        Assert.Equal("none", Describe(result));
        Assert.DoesNotContain("Confirm", result.Instruction ?? "");
    }

    [Theory]
    [InlineData(0x97)]
    [InlineData(0x99)]
    public void OnlyTheExactNativeStoryPointIsRouted(int point)
    {
        var objective = Objective();
        var state = State(0, 0, 0) with { Point = point };
        Assert.Same(objective, TyrannoLairRoutes.ForRoom(objective, 298, Tile(20, 30), state));
    }

    [Fact]
    public void OtherObjectivesAndTheBattleRoomAreLeftAlone()
    {
        var other = Objective() with { Id = "full:fossil-head" };
        Assert.Same(other, TyrannoLairRoutes.ForRoom(other, 298, Tile(20, 30), State(0, 0, 0)));
        var objective = Objective();
        Assert.Same(objective, TyrannoLairRoutes.ForRoom(objective, 301, Tile(8, 20), State(0xC0, 9, 0)));
    }

    [Fact]
    public void TheCatalogObjectiveIsTheOneRouted()
    {
        var objective = Assert.Single(FullStoryObjectives.Build(298, State(0, 0, 0)), o => o.Id == "full:black-tyranno");
        Assert.Equal("exit:6", Describe(TyrannoLairRoutes.ForRoom(objective, 298, Tile(20, 30), State(0, 0, 0))));
    }

    private static readonly string[] Hidden = ["Nizbel", "Azala", "Tyranno Lair,", "Warp", "Switches", "Passages", "Black Tyranno", "Prisoners", "Traps"];

    [Fact]
    public void ExteriorDoorwaysAreNamedByTerraceAndOrder()
    {
        var labels = Enumerable.Range(0, 10).Select(e => TyrannoLairLabels.ExitLabel(298, e, State(0xC0, 0, 0))).ToArray();
        Assert.All(labels, l => Assert.NotNull(l));
        Assert.Equal(10, labels.Distinct().Count());
        Assert.All(labels, l => Assert.Contains("terrace", l!));
        Assert.All(labels, l => Assert.DoesNotContain(Hidden, h => l!.Contains(h, StringComparison.OrdinalIgnoreCase)));
        Assert.StartsWith("Northwest terrace", labels[0]);
        Assert.StartsWith("Northeast terrace", labels[2]);
        Assert.StartsWith("Middle terrace", labels[5]);
        Assert.StartsWith("Southern terrace", labels[8]);
        Assert.Null(TyrannoLairLabels.ExitLabel(298, 10, State(0, 0, 0)));
    }

    [Theory]
    [InlineData(5, 0x00, true)]
    [InlineData(5, 0x80, false)]
    [InlineData(8, 0x80, true)]
    [InlineData(8, 0x40, false)]
    [InlineData(4, 0x00, false)]
    public void ClosedGatesAreDescribedAsGated(int exit, int ed, bool gated)
    {
        Assert.Equal(gated, TyrannoLairLabels.ExitLabel(298, exit, State(ed, 0, 0))!.Contains("gated", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownGateStateIsNotDescribed()
    {
        var unknown = State(0, 0, 0) with { Globals = new Dictionary<int, int>() };
        Assert.DoesNotContain("gated", TyrannoLairLabels.ExitLabel(298, 5, unknown)!);
        Assert.DoesNotContain("gated", TyrannoLairLabels.ExitLabel(298, 8, null)!);
    }

    [Theory]
    [InlineData(302, 9)]
    [InlineData(303, 2)]
    [InlineData(304, 2)]
    [InlineData(305, 2)]
    [InlineData(300, 2)]
    public void InteriorPassagesAreNamedWithoutHiddenRooms(int scene, int count)
    {
        var labels = Enumerable.Range(0, count).Select(e => TyrannoLairLabels.ExitLabel(scene, e, State(0, 0, 0))).ToArray();
        if (scene == 302) labels = labels.Where((_, i) => i is not (3 or 8)).ToArray(); // entrance and villagers keep theirs
        Assert.All(labels, l => Assert.NotNull(l));
        Assert.All(labels, l => Assert.DoesNotContain(Hidden, h => l!.Contains(h, StringComparison.OrdinalIgnoreCase)));
        Assert.Null(TyrannoLairLabels.ExitLabel(scene, count, State(0, 0, 0)));
    }

    [Fact]
    public void ThePassagesBackToTheTerracesNameWhereTheyArrive()
    {
        // 302 exits 0/1/2/5/7 arrive at 298 (21,9), (35,9), (45,9), (10,28), (10,55).
        Assert.Contains("northwest terrace", TyrannoLairLabels.ExitLabel(302, 0, null)!);
        Assert.Contains("northeast terrace", TyrannoLairLabels.ExitLabel(302, 1, null)!);
        Assert.Contains("northeast terrace", TyrannoLairLabels.ExitLabel(302, 2, null)!);
        Assert.Contains("middle terrace", TyrannoLairLabels.ExitLabel(302, 5, null)!);
        Assert.Contains("southern terrace", TyrannoLairLabels.ExitLabel(302, 7, null)!);
        Assert.NotEqual(TyrannoLairLabels.ExitLabel(302, 1, null), TyrannoLairLabels.ExitLabel(302, 2, null));
        Assert.Null(TyrannoLairLabels.ExitLabel(302, 3, null));
        Assert.Null(TyrannoLairLabels.ExitLabel(302, 8, null));
        Assert.Contains("southern terrace", TyrannoLairLabels.ExitLabel(303, 0, null)!);
        Assert.Contains("middle terrace", TyrannoLairLabels.ExitLabel(303, 1, null)!);
        Assert.Contains("middle terrace", TyrannoLairLabels.ExitLabel(304, 1, null)!);
        Assert.Contains("northwest terrace", TyrannoLairLabels.ExitLabel(305, 1, null)!);
        Assert.Contains("southern terrace", TyrannoLairLabels.ExitLabel(300, 0, null)!);
    }

    [Theory]
    [InlineData(9, "Floor switch")]
    [InlineData(11, "Floor switch")]
    [InlineData(12, "Floor switch")]
    [InlineData(13, "Floor switch")]
    [InlineData(16, "Floor switch")]
    [InlineData(10, "Skull")]
    [InlineData(17, "Wall switch")]
    [InlineData(18, "Wall switch")]
    public void SkullRoomFixturesAreNamedByWhatIsDrawn(int actor, string label)
    {
        Assert.Equal(label, TyrannoLairLabels.ActorLabel(303, actor));
    }

    [Theory]
    [InlineData(303, 8)]
    [InlineData(303, 14)]
    [InlineData(303, 19)]
    [InlineData(298, 10)]
    [InlineData(304, 9)]
    public void OtherActorsKeepTheirOwnLabels(int scene, int actor) => Assert.Null(TyrannoLairLabels.ActorLabel(scene, actor));

    private static FullStoryObjective Route(int scene, int x, int y, FieldStoryState state) =>
        TyrannoLairRoutes.ForRoom(Objective(), scene, Tile(x, y), state);

    private static FullStoryObjective Objective() => new("full:black-tyranno", "Face the Black Tyranno at the top of the lair",
        [new(301, [15, 16, 17, 18, 19, 20], [])], "Use Confirm at the approach to begin the encounter.");

    private static NavigationPoint Tile(int x, int y) => new(x * 256 + 128, y * 256 + 128, 1);

    private static FieldStoryState State(int ed, int ee, int x5c) => new(0x98, true)
    {
        Globals = new Dictionary<int, int> { [0] = 0x98, [0xED] = ed, [0xEE] = ee, [0x5C] = x5c },
        Locals = new Dictionary<int, int> { [0x08] = 0, [0x0C] = 0, [0x0D] = 0 },
    };

    private static string Describe(FullStoryObjective objective)
    {
        var goals = objective.Goals;
        if (goals.Length == 0) return "none";
        var goal = Assert.Single(goals);
        if (goal.Scene == 301 && goal.Targets.Length == 0) return "battle-room";
        return goal.Actors.Length != 0 ? $"actor:{string.Join(",", goal.Actors.Order())}" : string.Join(",", goal.Targets);
    }
}
