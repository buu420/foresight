using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Names for optional guide content that the field source already surfaces as
/// ordinary People and Objects. This supplies labels only: it never builds targets, never
/// chooses a category, and never supplies coordinates, so optional content keeps the
/// category the appearance gives it instead of being promoted into Story Events.
/// <para>Every tuple was read from the installed scene scripts: the actor's own load opcode
/// gives the class and visual, and its first dialogue or shop opcode gives the identity.
/// Evidence and per-slot dialogue: artifacts/research/story-discovery-0312.</para>
/// <para>The field source applies this after the existing label providers.</para>
/// </summary>
public static class OptionalGuideTargets
{
    /// <summary>Returns a name for an optional actor, or null to keep its appearance label.
    /// The tuple is the same identity the field source builds its actor id from.</summary>
    public static string? ActorLabel(int scene, FieldActorSnapshot actor) =>
        (scene, actor.Index, actor.ClassTag, actor.VisualIndex) switch
        {
            // Leene Square, Central Plaza. Slot 17 is already named by OpeningStoryTargets.
            (5, 12, 4, 79) => "Race bookmaker",
            (5, 13, 4, 82) => "Race tipster",
            (5, 14, 4, 0) => "Melchior the swordsmith",
            (5, 16, 4, 22) => "Item seller",
            (5, 18, 4, 83) => "Tent of Horrors barker",
            (5, 24, 4, 22) => "Armour seller",
            (5, 25, 4, 22) => "Strength game attendant",
            (5, 26, 4, 56) => "Strength game scorer",

            // Leene Square, Plaza Rear. Slot 15 is already named by OpeningStoryTargets.
            (439, 10, 4, 79) => "Fairgoer pointing to Gato",
            (439, 11, 4, 82) => "Old man with the lunch",
            (439, 12, 4, 85) => "Girl who lost her kitten",
            (439, 14, 4, 112) => "Candy stall",
            (439, 27, 4, 39) => "Drinking contest host",
            (439, 28, 4, 101) => "Wrapped lunch",
            (439, 30, 4, 89) => "Silver Point exchange",
            (439, 31, 4, 83) => "Fairgoer by the stairs",

            (6, 8, 5, 146) => "Gato",

            // Prehistoric Dancers.
            (7, 13, 4, 191) => "Dance caller",
            (7, 14, 4, 192) => "Fairgoer spending the mayor's money",

            // Truce, 1000 AD.
            (12, 8, 4, 22) => "Innkeeper",
            (12, 9, 4, 15) => "Thirsty musician",
            (12, 17, 4, 183) => "Sealed box",
            (13, 8, 4, 21) => "Mayor's greeter",
            (13, 10, 4, 14) => "Save point guide",
            (13, 12, 4, 15) => "Equipment tutor",
            (13, 15, 4, 18) => "Shelter guide",
            (13, 16, 4, 19) => "Running guide",
            (14, 8, 4, 17) => "Mayor of Truce",
            (14, 9, 4, 15) => "Status ailment tutor",
            (14, 11, 4, 43) => "Tech tutor",
            (14, 12, 4, 51) => "Battle command tutor",
            (17, 8, 4, 22) => "Shopkeeper",
            (17, 9, 4, 41) => "Fritz",
            (17, 10, 4, 80) => "Elaine",
            (17, 11, 4, 100) => "Market goods",
            (18, 8, 4, 22) => "Ferry ticket seller",
            (18, 11, 4, 80) => "Elaine",

            // Porre's early optional stops. The native treasure grid supplies
            // ordinary chests and pickups independently of these actor names.
            (50, 8, 4, 25) => "Mayor of Porre",
            (51, 9, 4, 183) => "Sealed box 1",
            (51, 10, 4, 183) => "Sealed box 2",
            (53, 13, 4, 77) => "Piano player",
            (54, 8, 4, 22) => "Shopkeeper",
            (55, 8, 4, 17) => "Innkeeper",
            (56, 8, 4, 22) => "Ferry ticket seller",

            // 600 AD. The field source excludes actors retired by their scripts.
            (115, 9, 4, 52) => "Banta the blacksmith",
            (116, 10, 4, 48) => "Innkeeper",
            (116, 19, 4, 5) => "Toma the explorer",
            (117, 12, 4, 183) => "Sealed box",
            (118, 8, 4, 49) => "Shopkeeper",
            (123, 10, 4, 70) => "Master of Kitchens",
            (129, 9, 4, 50) => "Nun 1",
            (129, 10, 4, 50) => "Nun 2",
            (129, 11, 4, 50) => "Nun 3",
            (129, 12, 4, 50) => "Nun 4",
            _ => null,
        };
}
