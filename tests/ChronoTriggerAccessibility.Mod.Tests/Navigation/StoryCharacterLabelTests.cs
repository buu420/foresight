using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Ozzie and Kino by the names the game itself shows, only where the installed
/// scripts prove the actor speaks under that name and only after it was first shown.
/// Evidence: artifacts/research/npc-castle-0356/findings.md.
/// Ozzie: load_enemy A6/A7/A8, whose native monster.txt entries are "Ozzie"/"Ozzie the
/// Great"; named first at Zenan Bridge, which ends by setting story point 0x5A.
/// Kino: load_person 06, speaking CMES2/KMES1/MESK1/MESI0 "Kino:" lines; named first in
/// the Forest Maze, whose talk handler sets 0x7B just before KMES1_002.</summary>
public sealed class StoryCharacterLabelTests
{
    [Theory]
    [InlineData(166, 11, 5, 0xA6)]   // Upon Entering (MEST1_005 via controller 10)
    [InlineData(167, 8, 5, 0xA6)]    // Trap 1 - Guillotine
    [InlineData(168, 9, 5, 0xA6)]    // Trap 2 - Pit
    [InlineData(170, 10, 5, 0xA6)]   // Central Chamber: the tester's "Creature" within reach
    [InlineData(174, 10, 6, 0xA7)]   // Ozzie Battle Room, live class 6
    [InlineData(175, 11, 5, 0xA6)]   // Trap 3 - Conveyor Belt
    [InlineData(177, 8, 5, 0xA8)]    // Ozzie's Fort
    [InlineData(181, 8, 5, 0xA7)]
    [InlineData(205, 9, 5, 0xA6)]    // Denadoro flashback
    public void OzzieIsNamedWhereHisScriptsSpeakAsOzzie(int scene, int index, int type, int visual)
    {
        var frame = Build(scene, 0x88, Actor(index, type, visual));
        var target = Assert.Single(frame.Targets, t => t.Id == $"actor:{index}:{type}:{visual}");
        Assert.Equal("Ozzie", target.Label);
        Assert.Equal(NavigationCategory.People, target.Category);
    }

    [Theory]
    [InlineData(33, 8, 0xA6)]   // Medina Mayor's House: the same sprite never speaks as Ozzie
    [InlineData(31, 8, 0xA6)]   // Medina Town Square after 0x8A
    [InlineData(63, 10, 0xA6)]  // West Cape
    [InlineData(397, 9, 0xA6)]  // Mountain of Woe
    public void TheSameSpriteElsewhereKeepsItsAppearance(int scene, int index, int visual)
    {
        var frame = Build(scene, 0xB0, Actor(index, 5, visual));
        Assert.DoesNotContain(frame.Targets, t => t.Label == "Ozzie");
    }

    [Fact]
    public void OzzieIsNotNamedBeforeZenanBridgeHasShownHisName()
    {
        var frame = Build(170, 0x59, Actor(10, 5, 0xA6));
        Assert.DoesNotContain(frame.Targets, t => t.Label == "Ozzie");
    }

    [Theory]
    [InlineData(275, 12)]   // Chief's Hut (CMES2_163)
    [InlineData(279, 8)]    // Village Center by day (CMES2_055)
    [InlineData(280, 15)]   // Village Center at night (CMES2_080)
    [InlineData(282, 19)]   // Forest Maze (KMES1_002)
    [InlineData(299, 8)]    // Tyranno Lair entrance (MESK1_100)
    [InlineData(301, 14)]   // Black Tyranno battle (MESI0_084)
    [InlineData(302, 9)]    // Tyranno Lair interior (MESK1_091)
    [InlineData(430, 17)]   // Shown only at 0x97, set solely after Kino's MESK1_099 "Follow Kino!" in 302
    public void KinoIsNamedInPrehistoryOnceTheForestMazeHasShownHisName(int scene, int index)
    {
        var named = Assert.Single(Build(scene, 0x7B, Actor(index, 4, 6)).Targets, t => t.Id == $"actor:{index}:4:6");
        Assert.Equal("Kino", named.Label);
        var earlier = Assert.Single(Build(scene, 0x7A, Actor(index, 4, 6)).Targets, t => t.Id == $"actor:{index}:4:6");
        Assert.Equal("Blond person in blue", earlier.Label);
    }

    [Theory]
    [InlineData(85, 9)]    // Leene Square telepod parade
    [InlineData(79, 9)]    // Guardia Castle, used for the ending
    [InlineData(279, 9)]   // another Ioka villager slot
    public void ThePersonSpriteOutsideKinosScenesOrSlotsKeepsItsAppearance(int scene, int index)
    {
        var target = Assert.Single(Build(scene, 0xB0, Actor(index, 4, 6)).Targets, t => t.Id == $"actor:{index}:4:6");
        Assert.Equal("Blond person in blue", target.Label);
    }

    private static FieldActorSnapshot Actor(int index, int type, int visual) =>
        FullGameNavigationTests.Actor(index, 20 * 256 + 128, 20 * 256 + 128) with { ClassTag = type, VisualIndex = visual };

    private static NavigationFrame Build(int scene, int story, params FieldActorSnapshot[] actors)
    {
        var player = FullGameNavigationTests.Actor(1, 24 * 256 + 128, 24 * 256 + 128, true) with { ClassTag = 0 };
        var field = FullGameNavigationTests.Field(scene) with { LeadPlayer = player, Actors = [player, .. actors], ActorCount = 40 };
        var map = new FieldMapSnapshot(64, 64, new byte[4096], new byte[4096], Enumerable.Repeat((byte)1, 4096).ToArray(),
            1, false, 64, 64, Enumerable.Repeat((byte)128, 4096).ToArray());
        var locals = Enumerable.Range(0, 64).ToDictionary(i => i, _ => 0);
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, map, new(0, 0, 64 * 256, 64 * 256), [],
            new FieldStoryState(story, true) { Locals = locals, Globals = new Dictionary<int, int>() });
    }

    private sealed class NoMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination) => false;
    }
}
