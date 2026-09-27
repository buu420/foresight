using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>
/// Interaction facts the navigation catalog does not record, generated from the installed
/// scene scripts by tools/research/future_story/build_field_content.py.
/// <para>Stand-ins: inn, shop, tavern and ferry keepers stand behind counters, outside the
/// native Confirm range (<see cref="FieldInteractionRange"/>) from every customer tile. The
/// game gives the counter its own actor whose Confirm handler is a forwarded call to the
/// keeper (02/03/04, sometimes behind a 12/75/77 re-entry lock), a byte-identical copy of the
/// keeper's handler, or (Forward) a handler that reaches the keeper on some paths. Truce Inn's
/// counter marker is <c>12 10 00 00 08, 75 10, 02 10 11, 77 10, 00</c>: a call to the
/// innkeeper's handler. Call and Twin stand-ins are the keeper's own destination.</para>
/// <para>Services: a Confirm handler, or a stand-in's forwarded one, that opens a shop
/// (C8 80-BF) or heals after taking gold (F8/F9/FA with CE) is labelled as a shop or inn, which
/// is what the counter and the keeper look like. Free healing (beds, machines) is not.</para>
/// <para>Marker groups: adjacent interaction markers with byte-identical Confirm handlers are
/// one feature drawn in the map, such as Guardia Forest's two-tile signposts.</para>
/// </summary>
public static class FieldContentFacts
{
    private sealed record StandInEntry(int Scene, int StandIn, int Target, string Kind, GameNavigationCatalog.Requirement[] Guards);
    private sealed record ServiceEntry(int Scene, int Actor, string Kind);
    private sealed record GroupEntry(int Scene, int[] Actors);
    private sealed record Data(string ExecutableSha256, StandInEntry[] StandIns, ServiceEntry[] Services,
        GroupEntry[] MarkerGroups);

    private static readonly Data data = Read();
    private static readonly ILookup<(int Scene, int Target), StandInEntry> standIns =
        data.StandIns.ToLookup(s => (s.Scene, s.Target));
    private static readonly IReadOnlyDictionary<(int Scene, int Actor), string> services =
        data.Services.ToDictionary(s => (s.Scene, s.Actor), s => s.Kind);
    private static readonly ILookup<int, int[]> groups = data.MarkerGroups.ToLookup(g => g.Scene, g => g.Actors);

    /// <summary>Actors that receive Confirm for <paramref name="target"/>. SameDestination is
    /// false for a Forward stand-in, whose own handler also does other things.</summary>
    public static IEnumerable<(int Actor, bool SameDestination)> StandInsFor(int scene, int target, FieldStoryState? story = null) =>
        standIns[(scene, target)].Where(s => s.Guards.All(g => g.Allows(story)))
            .Select(s => (s.StandIn, s.Kind is "Call" or "Twin"));

    /// <summary>"Shop" or "Inn" when this actor's Confirm handler provides it.</summary>
    public static string? Service(int scene, int actor) => services.GetValueOrDefault((scene, actor));

    public static IEnumerable<int[]> MarkerGroups(int scene) => groups[scene];

    /// <summary>What a sighted player sees, for sprites the visual table only calls an object
    /// or a person. Curated labels still take precedence.</summary>
    public static string? ActorLabel(int scene, FieldActorSnapshot actor, NavigationCategory category)
    {
        if (Service(scene, actor.Index) is { } service)
            return category == NavigationCategory.People ? service == "Shop" ? "Shopkeeper" : "Innkeeper" : service;
        if ((actor.ClassTag & ~FieldNavigationCapture.ClassTagRemovedBit) != 4) return null;
        return actor.VisualIndex switch
        {
            // All 17 placements are the black sealed chests, and each Confirm handler gives an
            // item (CA). Twelve test story point A5 (the charged pendant) themselves; the Forest
            // Ruins pair and the three in the Hero's Grave are opened by their own room flags.
            183 => "Sealed box",
            // All 22 placements are gate portals (Leene Square, Lucca's Gate, Bangor and Proto
            // Dome, the Lavos portals); their warps lead to other eras or the End of Time.
            151 => "Time Gate",
            _ => null,
        };
    }

    private static Data Read()
    {
        using var stream = typeof(FieldContentFacts).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Navigation.Data.field-content.json")
            ?? throw new InvalidDataException("Field content resource is missing.");
        var result = JsonSerializer.Deserialize<Data>(stream) ?? throw new InvalidDataException("Field content is empty.");
        if (result.ExecutableSha256 != "8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7" ||
            result.StandIns.Any(s => GameNavigationCatalog.ForScene(s.Scene) is null || s.Kind is not ("Call" or "Twin" or "Forward") ||
                s.Guards is null || s.Kind == "Forward" && s.Guards.Length == 0) ||
            result.Services.Any(s => GameNavigationCatalog.ForScene(s.Scene) is null || s.Kind is not ("Shop" or "Inn")) ||
            result.MarkerGroups.Any(g => GameNavigationCatalog.ForScene(g.Scene) is null || g.Actors.Length < 2))
            throw new InvalidDataException("Field content does not match the supported game.");
        return result;
    }
}
