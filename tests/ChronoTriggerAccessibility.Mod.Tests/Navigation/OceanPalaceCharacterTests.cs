using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Zeal and Ocean Palace characters by the names the game itself shows, only in the
/// installed script slots that load that exact sprite and speak under that name, and only once
/// the game has shown the name. Enemy-table sprites (load_enemy) can load as class 5 or 6;
/// NPC sprites (load_person) are class 4. Evidence:
/// artifacts/research/ocean-palace-0367/claude-entities/findings.md.</summary>
public sealed class OceanPalaceCharacterTests
{
    /// <summary>Scene, slot, class, visual, the first story point at which every path has shown
    /// the name, and the name.</summary>
    public static TheoryData<int, int, int, int, int, string> ProvenSlots => new()
    {
        // load_enemy A1 = monster.txt "Dalton". Atel_0204 actor 11 shows MSG03_094
        // "Dalton: Yes, Your Majesty!" before actor 10 sets 0xA8; no other script sets it.
        { 334, 10, 6, 0xA1, 0xA8, "Dalton" },   // Atel_0206 03A2, MSG03_118-121
        { 414, 14, 6, 0xA1, 0xA8, "Dalton" },   // Atel_0222 03C1, MSG04_070-075
        { 424, 17, 5, 0xA1, 0xA8, "Dalton" },   // Atel_0402, CMES3_117-133
        // load_person 43 speaking "Elder:". Algetty's cutscene runs KMES1_094 through this slot
        // and ends with 5A BA; the Beds cutscene runs CMES3_017 before 5A CD.
        { 382, 8, 4, 0x43, 0xBA, "Elder" },
        { 424, 8, 4, 0x43, 0xCD, "Elder" },
        { 425, 9, 4, 0x43, 0xCD, "Elder" },
        // load_person 00 speaking "Melchior:". Only his cabin (Atel_0421) sets 0x6C, in the
        // handler that continues straight into KMES0_115 "Melchior: Oh, you're back again!".
        { 382, 10, 4, 0x00, 0x6C, "Melchior" },
        // load_enemy 98/97 = "Mune"/"Masa". Denadoro (Atel_0188) shows both names before 5A 60;
        // Tata's 0x66 write is reachable only after 0x60.
        { 404, 9, 5, 0x98, 0x60, "Mune" },      // Atel_0220 037E, MSG03_122
        { 409, 33, 5, 0x97, 0x60, "Masa" },     // Atel_0397 0ACA, CMES3_015/016
        // Atel_0204 shows "Queen:" (MSG03_090) and "Prophet:" (MSG03_091) before 5A A8.
        { 415, 13, 4, 0x4A, 0xA8, "Queen" },    // Atel_0223 0A63
        { 415, 14, 4, 0x3C, 0xA8, "Prophet" },  // Atel_0223 0B7A
        // Atel_0204 never shows "Schala:". Every path does in Atel_0205 (MSG03_099), entered
        // straight after 0xA8, before Atel_0058 sets 0xAB.
        { 415, 12, 4, 0x09, 0xAB, "Schala" },   // Atel_0223 091D
        // This room follows the Ocean Palace Lavos battle; 414 sets 0xC9 on the way.
        { 423, 20, 4, 0x4A, 0xC9, "Queen" },    // Atel_0230 0F32, MSG03_194/195
        { 423, 22, 6, 0xCA, 0xC9, "Lavos" },    // Atel_0230 1036, monster.txt "Lavos"
    };

    [Theory, MemberData(nameof(ProvenSlots))]
    public void ProvenSlotIsNamedExactlyFromItsIntroductionPoint(int scene, int slot, int type, int visual,
        int introduced, string name)
    {
        Assert.Null(Label(scene, slot, type, visual, introduced - 1));
        Assert.Equal(name, Label(scene, slot, type, visual, introduced));
    }

    [Theory]
    [InlineData(334, 10, 6, 0xA1, 0xBB, "Dalton")]    // 03.16.02 23:41:53 "People. Creature"
    [InlineData(382, 8, 4, 0x43, 0xBA, "Elder")]      // 23:32:41, after the Algetty cutscene
    [InlineData(382, 10, 4, 0x00, 0xBA, "Melchior")]
    [InlineData(404, 9, 5, 0x98, 0xBD, "Mune")]       // 23:51:17 "People" 1
    [InlineData(409, 33, 5, 0x97, 0xC3, "Masa")]      // 05.37.29, shown after the fourth stair battle
    [InlineData(415, 12, 4, 0x09, 0xC9, "Schala")]    // 06.27.59 02:26:54
    [InlineData(415, 13, 4, 0x4A, 0xC9, "Queen")]
    [InlineData(415, 14, 4, 0x3C, 0xC9, "Prophet")]
    [InlineData(423, 20, 4, 0x4A, 0xC9, "Queen")]     // 02:31:19
    [InlineData(423, 22, 6, 0xCA, 0xC9, "Lavos")]
    public void ReportedActorsAreNamedAtTheirLoggedStoryPoints(int scene, int slot, int type, int visual,
        int point, string name) =>
        Assert.Equal(name, Label(scene, slot, type, visual, point));

    [Theory]
    [InlineData(334, 10, 5, 0xA1, "Dalton")]
    [InlineData(414, 14, 5, 0xA1, "Dalton")]
    [InlineData(404, 9, 6, 0x98, "Mune")]
    [InlineData(409, 33, 6, 0x97, "Masa")]
    [InlineData(423, 22, 5, 0xCA, "Lavos")]
    public void EnemySpriteSlotsAreNamedInEitherLoadMode(int scene, int slot, int type, int visual, string name) =>
        Assert.Equal(name, Label(scene, slot, type, visual, 0xD0));

    [Theory]
    [InlineData(415, 14, 4, 0x0C)]   // Atel_0223 0B70: with local08 = 4 this slot loads Belthasar
    [InlineData(415, 13, 4, 0x09)]   // Schala's sprite in the Queen's slot
    [InlineData(415, 12, 4, 0x4A)]   // the Queen's sprite in Schala's slot
    [InlineData(423, 20, 4, 0xCC)]   // Atel_0230 0F2E: this slot's other load
    [InlineData(423, 22, 4, 0xCE)]   // Atel_0230 1032: this slot's NPC load, not the Lavos sprite
    [InlineData(382, 8, 4, 0x41)]    // the messenger's sprite in the Elder's slot
    [InlineData(382, 8, 5, 0x43)]    // enemy table 43 is not the Elder's NPC sprite
    [InlineData(382, 10, 4, 0x01)]
    [InlineData(334, 11, 6, 0xA1)]   // Dalton's sprite in another slot
    [InlineData(404, 9, 5, 0x97)]    // Masa's sprite in Mune's slot
    [InlineData(409, 32, 5, 0x97)]
    [InlineData(401, 16, 5, 0x97)]   // Zeal Palace children: Masa's sprite, no named line of their own
    [InlineData(401, 17, 5, 0x97)]
    // Atel_0499 055B: Algetty's Dalton exists only parked off the floor for the cutscene, which
    // names him itself; 0A 1A removes him once 0xBA is set. No navigation use for the name.
    [InlineData(382, 13, 5, 0xA1)]
    [InlineData(382, 13, 6, 0xA1)]
    public void OtherSlotsOrSpritesKeepTheirAppearance(int scene, int slot, int type, int visual) =>
        Assert.Null(Label(scene, slot, type, visual, 0xFF));

    [Fact]
    public void TheProphetsSlotNeverRevealsALaterIdentity()
    {
        for (var point = 0; point <= 0xFF; point++)
        {
            Assert.NotEqual("Magus", Label(415, 14, 4, 0x3C, point));
            Assert.Null(Label(415, 14, 4, 0x0C, point));
        }
    }

    [Fact]
    public void AnUnreadableStoryStateNamesNobody() =>
        Assert.Null(StoryCharacterLabels.ActorLabel(334, Actor(10, 6, 0xA1), null));

    private static string? Label(int scene, int slot, int type, int visual, int point) =>
        StoryCharacterLabels.ActorLabel(scene, Actor(slot, type, visual), new FieldStoryState(point, true));

    private static FieldActorSnapshot Actor(int index, int type, int visual) =>
        new(index, 20, 20 * 256 + 128, 128, 20, 20 * 256 + 128, 128, 0, 1, 1, visual, type, 0x2020, 0, 0,
            false, true, true, true);
}
