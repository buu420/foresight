namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Optional guide areas. Exit identities and destinations are from the
/// installed PC MapJump tables; current native grids still supply all route points.</summary>
public static class OptionalGuideAreas
{
    public static string? ExitLabel(int scene, int exit) => (scene, exit) switch
    {
        (3, 0) => "To Lucca's House, 1F Entrance",
        (3, 1) => "To Lucca's House, 2F Lucca's Room",
        (3, 2) => "To Lucca's House, 2F Lara's Room",
        (3, 3) => "To Lucca's House, 1F Hall / 1F Room",
        (3, 4) => "To Lucca's House, 1F Hall / 1F Room",
        (4, 0) => "Outside",
        (4, 1) => "To Lucca's House, 1F Hall / 1F Room",
        (4, 2) => "To Lucca's House, 2F Lara's Room",
        (4, 3) => "To Lucca's House, 2F Lucca's Room",
        (5, 0) => "Outside",
        (5, 1) => "To Leene Square, Plaza Rear",
        (5, 2) => "To Leene Square, Bekkler's Lab",
        (6, 0) => "To Leene Square, Plaza Rear",
        (7, 0) => "To Leene Square, Plaza Rear",
        (9, 0) => "To Lucca's House, 1F Entrance",
        (9, 1) => "To Lucca's House, 1F Hall / 1F Room",
        (10, 0) => "To Lucca's House, 1F Hall / 1F Room",
        (10, 1) => "To Lucca's House, 1F Entrance",
        (12, 0) => "Outside",
        (13, 0) => "Outside",
        (13, 1) => "To Truce, Mayor's House 2F",
        (14, 0) => "To Truce, Mayor's House 1F",
        (15, 0) => "Outside",
        (16, 0) => "Outside",
        (17, 0) => "Outside",
        (18, 0) => "Outside",
        (50, 0) => "Outside",
        (50, 1) => "To Porre, Mayor's Manor 2F",
        (51, 0) => "To Porre, Mayor's Manor 1F",
        (52, 0) => "Outside",
        (53, 0) => "Outside",
        (54, 0) => "Outside",
        (55, 0) => "Outside",
        (56, 0) => "Outside",
        (114, 0) => "Outside",
        (115, 0) => "Outside",
        (116, 0) => "Outside",
        (116, 1) => "To Truce, Inn 2F",
        (117, 0) => "To Truce, Inn 1F",
        (118, 0) => "Outside",
        (123, 0) => "To Guardia Castle, Main Hall",
        (124, 0) => "To Guardia Castle, Main Hall",
        (210, 0) => "Outside",
        (210, 1) => "To Trann Dome, Sealed Treasure Room",
        _ => null,
    };

    public static bool WorldDestination(int world, int destination, int? progress) =>
        progress is >= 3 and <= 77 && (world, destination) switch
        {
            (0, 4 or 5 or 12 or 13 or 15 or 16 or 17 or 18 or 50 or 52 or 53 or 54 or 55 or 56) => true,
            (1, 114 or 115 or 116 or 118) => true,
            (2, 210) => true,
            _ => false,
        };
}
