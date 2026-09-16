using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Ocean Palace packs six disconnected rooms into scene406. Native
/// exit identities, arrival coordinates and map177's separated floor bands
/// distinguish them; matching the scene alone cannot choose a usable passage.</summary>
internal static class OceanPalaceRoutes
{
    public static FullStoryObjective ForRoom(FullStoryObjective objective, int scene, NavigationPoint player,
        FieldStoryState state)
    {
        var step = objective.Id switch
        {
            "full:palace-east-switch" => 0,
            "full:palace-west-switch" => 1,
            "full:palace-switch" => 2,
            "full:palace-lower-west" => 3,
            "full:palace-lower-east" => 4,
            "full:palace-bridge" => 5,
            "full:golem-twins" => 6,
            _ => -1,
        };
        if (step < 0) return objective;
        var lower = step >= 3;
        return scene switch
        {
            404 when state.Point < 195 => Actor(9) with { Label = "Listen to Mune at the Ocean Palace entrance" },
            405 => step switch
            {
                0 => Exit(1), // arrives at406 (11,16), upper-left component
                1 => Exit(7), // arrives at406 (44,56), lower-right component
                2 => Exit(3, 5),
                _ => Exit(4), // great stair, not a disconnected switch room
            },
            406 => SmallRoom(),
            407 => step == 2 ? objective : Exit(0, 1),
            408 => Exit(lower ? 2 : 0),
            409 when lower && state.Point < 198 => Actor(33) with { Label = "Speak to Masa on the great stair" },
            409 => Exit(lower ? 1 : 0),
            410 => Exit(lower ? 1 : 0),
            411 => lower ? Actor(10) : Exit(0),
            417 => Exit(0),
            418 => lower ? Exit(0) : Actor(9),
            412 => step switch
            {
                < 3 => Exit(0),
                3 => Exit(2), // arrives at406 (16,36), middle-left component
                4 => Exit(1), // arrives at406 (39,36), middle-right component
                _ => objective,
            },
            _ => objective,
        };

        FullStoryObjective SmallRoom()
        {
            var x = player.X / 256;
            var y = player.Y / 256;
            var left = x < 28;
            // Map177 has occupied bands9..18,29..38,50..59, separated
            // by solid rows. These select a native exit, never a walk coordinate.
            var row = y < 24 ? 0 : y < 44 ? 1 : 2;
            if (step == 0 && row == 0) return left ? Actor(10) : Exit(5);
            if (step == 1 && row == 2) return left ? Exit(6) : Actor(16);
            if (step == 3 && row == 1 && left) return Actor(22);
            if (step == 4 && row == 1 && !left) return Actor(26);
            return row switch
            {
                0 => Exit(left ? 0 : 1),
                1 => Exit(left ? 9 : 8),
                _ => Exit(left ? 2 : 3),
            };
        }

        FullStoryObjective Exit(params int[] ids) => objective with
        {
            Goals = [new(scene, [], ids.Select(id => $"exit:{id}").ToArray())],
        };
        FullStoryObjective Actor(int id) => objective with { Goals = [new(scene, [id], [])] };
    }
}
