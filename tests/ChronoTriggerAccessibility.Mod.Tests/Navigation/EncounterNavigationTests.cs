using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Field battles in this game are scripted coordinate triggers, not random.
/// The compiler already records them as catalogue regions of kind Encounter (native
/// opcode D8 reached under a leader-coordinate guard), so they are named and located
/// by the game's own script rather than by appearance.</summary>
public sealed class EncounterNavigationTests
{
    // Scene 29 Prison Towers, Guardroom: actor 2 runs a battle across the row 23
    // line while global 0x198 bit 32 is clear. That bit is what the script sets
    // once the fight is over.
    private const int Scene = 29;
    private const int TriggerActor = 2;
    private const int ClearedFlag = 0x198;

    [Fact]
    public void ScriptedFieldBattleIsOfferedAsAnEnemy()
    {
        var frame = Build(flag: 0);
        var target = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Enemies);
        Assert.StartsWith("script-encounter:", target.Id);
        Assert.True(target.GuideAvailable);
        Assert.NotEmpty(target.ApproachPoints);
    }

    [Fact]
    public void EnemyLabelDescribesTheTriggerWithoutNamingTheMonsters()
    {
        var target = Assert.Single(Build(flag: 0).Targets, t => t.Category == NavigationCategory.Enemies);
        Assert.Equal("Encounter", target.Label);
    }

    [Fact]
    public void RoutingToAnEnemyReachesItsTriggerAndLeavesTheFightToThePlayer()
    {
        var frame = Build(flag: 0);
        var target = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Enemies);
        var route = NavigationPathfinder.Search(frame.Graph, frame.Player, target.ApproachPoints).Route;
        Assert.NotNull(route);
        Assert.Contains(route[^1], target.ApproachPoints);
        Assert.Equal(23, route[^1].Y / 256);
    }

    [Fact]
    public void AFinishedEncounterDisappears()
    {
        Assert.DoesNotContain(Build(flag: 32).Targets, t => t.Category == NavigationCategory.Enemies);
    }

    [Fact]
    public void AnEncounterWhoseTriggerActorIsGoneDisappears()
    {
        var player = Player();
        var field = FullGameNavigationTests.Field(Scene) with { LeadPlayer = player, Actors = [player] };
        var frame = new FieldNavigationSource(new NoMemory(), _ => { })
            .Build(field, Room(), new(0, 0, 8192, 8192), [], Story(0));
        Assert.DoesNotContain(frame.Targets, t => t.Category == NavigationCategory.Enemies);
    }

    [Fact]
    public void AppendingEnemiesLeavesTheExistingCategoryOrderIntact()
    {
        Assert.Equal(0, (int)NavigationCategory.People);
        Assert.Equal(1, (int)NavigationCategory.Exits);
        Assert.Equal(2, (int)NavigationCategory.Objects);
        Assert.Equal(3, (int)NavigationCategory.StoryEvents);
        Assert.Equal(4, (int)NavigationCategory.Enemies);
    }

    private static NavigationFrame Build(int flag)
    {
        var player = Player();
        var trigger = FullGameNavigationTests.Actor(TriggerActor, 1408, 1408);
        var field = FullGameNavigationTests.Field(Scene) with { LeadPlayer = player, Actors = [player, trigger] };
        return new FieldNavigationSource(new NoMemory(), _ => { })
            .Build(field, Room(), new(0, 0, 8192, 8192), [], Story(flag));
    }

    private static FieldActorSnapshot Player() =>
        FullGameNavigationTests.Actor(0, 384, 384) with { IsPartyMember = true, ClassTag = 0 };

    private static FieldStoryState Story(int flag) =>
        new(20, false) { Globals = new Dictionary<int, int> { [ClearedFlag] = flag } };

    /// <summary>Open 32x32 floor, wide enough to contain the scene's row 23 trigger.</summary>
    internal static FieldMapSnapshot Room() => new(32, 32, new byte[1024], new byte[1024],
        Enumerable.Repeat((byte)1, 1024).ToArray(), 1, false, 32, 32,
        Enumerable.Repeat((byte)128, 1024).ToArray());

    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
