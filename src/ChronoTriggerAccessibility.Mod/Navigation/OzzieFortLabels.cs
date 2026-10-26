namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Distinct neutral room labels. The native debug labels disclose encounters
/// and outcomes, so they must not name a passage before it is entered.</summary>
public static class OzzieFortLabels
{
    public static string? AreaName(int scene) => scene switch
    {
        177 => "Ozzie's Fort, Entrance",
        183 => "Ozzie's Fort, Room A",
        178 => "Ozzie's Fort, Room B",
        184 => "Ozzie's Fort, Room C",
        179 => "Ozzie's Fort, Room D",
        180 => "Ozzie's Fort, Room E",
        181 => "Ozzie's Fort, Room F",
        _ => null,
    };
}
