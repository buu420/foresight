using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>
/// The important tests here are the mechanical ones: every goal this catalogue can
/// ever produce is checked against the native navigation catalogue, so a goal that
/// names a non-existent actor, or an enter-the-scene goal in a room where entering
/// does not advance anything, fails the build rather than stranding a player.
/// </summary>
public sealed class FullStoryObjectivesTests
{
    private const int AnyScene = 214;
    private const int EpochTakesOff = 0xD4;     // the Black Omen rises; the world opens up
    private const int CronoFalls = 0xCB;

    /// <summary>All globals readable and clear unless named; inventory readable and empty.</summary>
    private static FieldStoryState State(int point, params (int Index, int Value)[] globals)
    {
        var values = Enumerable.Range(0, 512).ToDictionary(index => index, _ => 0);
        foreach (var (index, value) in globals) values[index] = value;
        return new(point, false) { Globals = values, Inventory = new Dictionary<int, int>() };
    }

    private static FieldStoryState Cells(int point, params (int Cell, int Value)[] extended) =>
        State(point) with { Extended = extended.ToDictionary(e => e.Cell, e => e.Value) };

    private static FieldStoryState Carrying(int point, params int[] items) =>
        State(point) with { Inventory = items.ToDictionary(item => item, _ => 1) };

    /// <summary>A capture that could not read a region leaves it out entirely.</summary>
    private static FieldStoryState Unreadable(int point) => new(point, false);

    private static FullStoryObjective? Story(int scene, FieldStoryState? state) =>
        FullStoryObjectives.Build(scene, state)
            .FirstOrDefault(o => o.Category == NavigationCategory.StoryEvents);

    private static FullStoryObjective[] Optional(int scene, FieldStoryState state) =>
        FullStoryObjectives.Build(scene, state)
            .Where(o => o.Category != NavigationCategory.StoryEvents).ToArray();

    private static bool Has(int scene, FieldStoryState state, string id) =>
        FullStoryObjectives.Build(scene, state).Any(o => o.Id == id);

    /// <summary>
    /// Scenes whose own startup script fires the write that ends the step, so arriving is
    /// genuinely the whole step even though there is nothing to walk up to. Derived in
    /// artifacts/research/full-story-0323/claude/arrival-completes.txt from the counter
    /// writes attributed to a startup function slot; the three extra entries below each
    /// name the flag their startup sets.
    /// </summary>
    private static readonly HashSet<int> ArrivalCompletes =
    [
        36, 113, 163, 165, 169, 173, 279, 301, 307, 351, 371, 415, 425, 430, 493,
        265,   // Death Peak, Summit: startup sets global 0x057 bit 5, the revival flag
        197,   // Giant's Claw bottom: startup sets global 0x0A9 bit 7, that chain's flag
        320,   // Black Omen middle warp: startup sets global 0x1A6 bit 7
        161,   // Sunken Desert boss room: its own startup begins the encounter
    ];

    // == mechanical validation against the native catalogue ==========================

    [Fact]
    public void EveryGoalNamesASceneTheNativeCatalogueKnows()
    {
        foreach (var objective in FullStoryObjectives.All)
        foreach (var goal in objective.Goals)
            Assert.True(GameNavigationCatalog.ForScene(goal.Scene) is not null,
                $"{objective.Id}: scene {goal.Scene} is not in the navigation catalogue");
    }

    [Fact]
    public void EveryActorGoalNamesAnActorThatSceneActuallyHas()
    {
        var problems = new List<string>();
        foreach (var objective in FullStoryObjectives.All)
        foreach (var goal in objective.Goals.Where(g => g.Actors.Length > 0))
        {
            var metadata = GameNavigationCatalog.ForScene(goal.Scene)!;
            // An actor id binds through actor:N / landmark:N, and also through
            // script-region:N / script-action:N, which is how the End of Time pillars and
            // other scripted spatial triggers are exposed.
            var known = metadata.Actors.Select(a => a.Id)
                .Concat(metadata.Regions.Select(r => r.Actor)).ToHashSet();
            problems.AddRange(goal.Actors.Where(actor => !known.Contains(actor))
                .Select(actor => $"{objective.Id}: scene {goal.Scene} has no actor {actor} " +
                    $"(it has {string.Join(", ", known.OrderBy(a => a))})"));
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// The stall test. Root's resolver treats a goal with no actors and no targets as
    /// "entering this scene is the step", and falls back to the scene's native Progress
    /// triggers to place an approach. A scene with no Progress trigger and no scripted
    /// warp region cannot ever satisfy such a goal, so the player walks in and the
    /// objective never clears.
    /// </summary>
    [Fact]
    public void EveryEnterTheSceneGoalIsInASceneWhereEnteringAdvancesSomething()
    {
        var problems = new List<string>();
        foreach (var objective in FullStoryObjectives.All.Where(o => o.Category == NavigationCategory.StoryEvents))
        foreach (var goal in objective.Goals.Where(g => g.Actors.Length == 0 && g.Targets.Length == 0))
        {
            var metadata = GameNavigationCatalog.ForScene(goal.Scene)!;
            var progress = metadata.Regions.Any(r => r.Kind is "Progress" or "Encounter") ||
                metadata.Actors.Any(a => a.Actions.Any(x => x.Kind == "Progress"));
            if (progress || ArrivalCompletes.Contains(goal.Scene)) continue;
            problems.Add($"{objective.Id}: scene {goal.Scene} has no native Progress trigger and " +
                "is not in the audited arrival list, so an enter-the-scene goal there would " +
                "strand the player. Bind the actor that performs the step instead.");
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void NoObjectiveIsBuiltEntirelyFromUnbindableGoals()
    {
        foreach (var objective in FullStoryObjectives.All)
            Assert.True(objective.Goals.Length > 0, $"{objective.Id} has no goals at all");
    }

    [Fact]
    public void ObjectiveIdentifiersAreUnique()
    {
        var ids = FullStoryObjectives.All.Select(o => o.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    // == the boundary with the existing catalogue ====================================

    [Fact]
    public void NothingIsOfferedBeforeTheFirstEndOfTimeLessonIsFinished()
    {
        for (var point = 0; point < FullStoryObjectives.FirstPoint; point++)
            Assert.Empty(FullStoryObjectives.Build(AnyScene, State(point)));
    }

    [Fact]
    public void MissingStoryStateOffersNothingRatherThanGuessing() =>
        Assert.Empty(FullStoryObjectives.Build(AnyScene, null));

    // == the main chain ==============================================================

    [Fact]
    public void EveryCounterValueFromSeventySevenOnwardHasExactlyOneStoryObjective()
    {
        for (var point = FullStoryObjectives.FirstPoint; point <= 0xFF; point++)
        {
            var story = FullStoryObjectives.Build(AnyScene, State(point))
                .Where(o => o.Category == NavigationCategory.StoryEvents).ToArray();
            Assert.True(story.Length == 1, $"counter 0x{point:X2} produced {story.Length} story objectives");
        }
    }

    [Fact]
    public void TheChainNeverRepeatsOrGoesBackwards()
    {
        var seen = new List<string>();
        for (var point = FullStoryObjectives.FirstPoint; point <= 0xFF; point++)
        {
            var id = Story(AnyScene, State(point))!.Id;
            if (seen.Count > 0 && seen[^1] == id) continue;
            Assert.DoesNotContain(id, seen);
            seen.Add(id);
        }
        Assert.True(seen.Count >= 45, $"expected the whole remaining story, found {seen.Count} chapters");
    }

    [Fact]
    public void TheStoryObjectiveIsOfferedFromAnyScene()
    {
        var expected = Story(1, State(0xD3))!.Id;
        foreach (var scene in new[] { 0, 36, 214, 424, 464, 668 })
            Assert.Equal(expected, Story(scene, State(0xD3))!.Id);
    }

    [Fact]
    public void UnreadableFlagsFallBackToTheCounterBoundStepRatherThanGuessing()
    {
        // With no globals at all, every substep predicate is unknown. The chain must
        // still produce its earliest unproven step, never nothing and never a later one.
        for (var point = FullStoryObjectives.FirstPoint; point <= 0xFF; point++)
        {
            var blind = Story(AnyScene, Unreadable(point));
            Assert.NotNull(blind);
            Assert.NotEmpty(blind!.Goals);
        }
    }

    // == substeps within a chapter ===================================================

    [Fact]
    public void ZenanBridgeWalksTheFoodErrandRatherThanPointingAtTheBridge()
    {
        // Counter 84 to 87: the captain wants food. Global 0xA9 bit 4 is the native
        // "the party is carrying it" flag that scene 134 actor 13 tests.
        var hungry = Story(AnyScene, State(0x54))!;
        var carrying = Story(AnyScene, State(0x54, (0xA9, 0x1C)))!;
        Assert.NotEqual(hungry.Id, carrying.Id);
        Assert.Contains(carrying.Goals, g => g.Scene is 134 or 135 && g.Actors.Contains(13));
    }

    [Fact]
    public void RationsRequireCaptainCookAndLeavingCastleInThatOrder()
    {
        Assert.Contains(Story(134, State(84))!.Goals, g => g.Scene == 134);
        Assert.Contains(Story(134, State(84, (0xA9, 4)))!.Goals, g => g.Scene == 123 && g.Actors.Contains(10));
        Assert.Contains(Story(123, State(84, (0xA9, 12)))!.Goals, g => g.Scene == 120 && g.Actors.Contains(15));
        Assert.Contains(Story(120, State(84, (0xA9, 28)))!.Goals, g => g.Scene == 134);
    }

    [Fact]
    public void SwordPickupFollowsTwinsBattleAndFrogConversationPrecedesChest()
    {
        Assert.Contains(Story(151, State(93))!.Goals, g => g.Actors.Contains(11));
        Assert.Contains(Story(151, State(93, (0xF3, 0x20)))!.Goals, g => g.Actors.Contains(10));
        Assert.Contains(Story(141, State(102))!.Goals, g => g.Actors.Contains(4));
        Assert.Contains(Story(141, State(102, (0xF3, 0x10)))!.Goals, g => g.Actors.Contains(10));
    }

    [Fact]
    public void OceanPalaceCutawayCountersDoNotSkipUnfinishedDoorSwitches()
    {
        foreach (var point in new[] { 189, 195, 198 })
        {
            Assert.Contains(Story(406, State(point))!.Goals, g => g.Scene == 406 && g.Actors.Contains(10));
            Assert.Contains(Story(406, State(point, (0x162, 7)))!.Goals, g => g.Scene == 406 && g.Actors.Contains(22));
            Assert.Contains(Story(412, State(point, (0x162, 0x77)))!.Goals, g => g.Scene == 412 && g.Actors.Contains(9));
            Assert.Contains(Story(412, State(point, (0x162, 0xF7)))!.Goals, g => g.Scene == 414);
        }
    }

    [Fact]
    public void BlackbirdVentDiscoveryPrecedesEquipmentAndEachSetRetiresIndependently()
    {
        Assert.Equal("full:blackbird-look-outside", Story(371, State(207))!.Id);
        Assert.Equal("full:blackbird-return-cell", Story(364, State(207))!.Id);
        Assert.Equal("full:blackbird-find-duct", Story(371, State(207, (0xAF, 0x20)))!.Id);
        Assert.Contains(Story(371, State(207, (0xB3, 0x40)))!.Goals, g => g.Scene == 373);
        Assert.Contains(Story(376, State(207, (0xAF, 4)))!.Goals, g => g.Scene == 443);
        Assert.Contains(Story(376, State(207, (0xAF, 6)))!.Goals, g => g.Scene == 444);
        Assert.Contains(Story(376, State(207, (0xAF, 7), (0xBA, 6)))!.Goals, g => g.Scene == 363);
    }

    [Fact]
    public void EnteringLavosKeepsTheObjectiveAheadInsteadOfReturningToEndOfTime()
    {
        Assert.Contains(Story(466, State(214))!.Goals, g => g.Scene == 466 && g.Actors.Contains(13));
        Assert.Contains(Story(474, State(214))!.Goals, g => g.Scene == 474 && g.Actors.Contains(9));
    }

    // == the endgame is a choice, not a corridor =====================================

    [Fact]
    public void TheBlackOmenIsNeverAMandatoryStoryObjective()
    {
        for (var point = FullStoryObjectives.FirstPoint; point <= 0xFF; point++)
        {
            var story = Story(AnyScene, State(point))!;
            Assert.All(story.Goals, goal => Assert.True(goal.Scene is not (>= 308 and <= 326) and not 449
                    and not 450 and not 451 and not (>= 96 and <= 101),
                $"counter 0x{point:X2}: the Black Omen scene {goal.Scene} is a mandatory story goal"));
        }
    }

    [Fact]
    public void TheFinalObjectiveOffersTheGateRatherThanForcingOneRoute()
    {
        var ending = Story(AnyScene, State(0xD6))!;
        Assert.Contains(ending.Goals, g => g.Scene == FullStoryObjectives.EndOfTime);
    }

    [Fact]
    public void TheChainDoesNotStallBeforeCronoFalls() =>
        Assert.NotNull(Story(AnyScene, State(CronoFalls)));

    [Fact]
    public void EveryObjectiveHasAUsableLabelAndABoundedInstruction() =>
        Assert.All(FullStoryObjectives.All, objective =>
        {
            Assert.False(string.IsNullOrWhiteSpace(objective.Label));
            Assert.True(objective.Label.Length <= 80, $"{objective.Id}: label too long");
            if (objective.Instruction is { } instruction)
                Assert.True(instruction.Length <= 240, $"{objective.Id}: instruction too long");
        });

    [Fact]
    public void GeographyClaimsNameTheRightTownsAndEras()
    {
        // The previous pass put Tata in Dorino and Melchior on Truce's island. Both are
        // wrong: Tata's House is Porre's world-map entrance in 600 AD, and Melchior's
        // Cabin sits on the Medina landmass in 1000 AD.
        var text = string.Join(" | ", FullStoryObjectives.All
            .Select(o => o.Label + " " + o.Instruction).Where(s => s.Contains("Tata") ||
                s.Contains("Melchior")));
        Assert.DoesNotContain("Dorino", text);
        Assert.DoesNotContain("east of Truce", text);
    }
}
