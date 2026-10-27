namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Neutral identifiers for debug titles that reveal an NPC or future fight.
/// Room letters are stable mod labels, not asserted native floors or event names.</summary>
public static class CharacterRoomLabels
{
    public static string? AreaName(int scene)=>scene switch
    {
        9=>"Lucca's House, Upstairs Room",
        151=>"Denadoro Mountains, Inner Chamber",
        169=>"Magus's Keep, Chamber A",
        173=>"Magus's Keep, Chamber B",
        174=>"Magus's Keep, Chamber C",
        268=>"Geno Dome, Upper Floor",
        269=>"Geno Dome, Side Rooms",
        470=>"Magus's Keep, Passage C",
        300=>"Tyranno Lair, Chamber",
        328=>"Zeal Palace, Side Chamber A",
        402=>"Zeal Palace, Passage A",
        403=>"Zeal Palace, Side Chamber B",
        465=>"End of Time, Back Room",
        _=>null,
    };
}
