using System.Text.Json;
using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Installed PC map connections and audited script identities. These are
/// labels and connections only; the native frame owns existence and coordinates.</summary>
public static class GameNavigationCatalog
{
    public sealed record Exit(int Id, int Destination, int X, int Y);
    public sealed record Load(int Class, int Visual);
    public sealed record Requirement(string Source, int Index, int Operation, int Value, bool Expected)
    {
        public bool Allows(FieldStoryState? state, bool local = true)
        {
            // A route may lead to a room after changing the active party there.
            if (!local && Source is "Local" or "ActiveParty") return true;
            int? value = Source switch
            {
                "Global" => Index == 0 ? state?.Point : state?.Global(Index),
                "Local" => state?.Local(Index), "Extra" => state?.Extra(Index),
                "Item" => state?.ItemCount(Index), "Gold" => state?.Gold,
                "ActiveParty" => state?.PartyContains(Index, true), "Recruited" => state?.PartyContains(Index, false), _ => null,
            };
            if (value is not { } current) return false;
            var result = Operation switch
            {
                0 => current == Value, 1 => current != Value, 2 => current > Value,
                3 => current < Value, 4 => current >= Value, 5 => current <= Value,
                6 => (current & Value) != 0, 7 => (current | Value) != 0, _ => false,
            };
            return result == Expected;
        }
    }
    public sealed record TileCopy(int Left, int Top, int Right, int Bottom, int X, int Y, int Flags);
    public sealed record Action(string Kind, Requirement[] Guards, bool Touch, int Destination = -1,
        int ArrivalX = 0, int ArrivalY = 0, int Value = 0, TileCopy? Copy = null)
    {
        public bool Available(FieldStoryState? state, bool local = true) => Guards.All(g => g.Allows(state, local));
    }
    public sealed record Actor(int Id, Load[] Loads, bool Marker, bool Touch, string? Label,
        bool GivesItem, int[] Destinations, Action[] Actions)
    {
        public bool Matches(FieldActorSnapshot actor) => Id == actor.Index &&
            (Marker ? actor.ClassTag == 7 : Loads.Any(l => l.Class == actor.ClassTag && l.Visual == actor.VisualIndex));
    }
    public sealed record Region(int Actor, string Kind, int Destination, int Value, int Left, int Top, int Right, int Bottom, Requirement[] Guards,
        string? Source = null, int Index = 0, bool Set = true)
    {
        public string Id => Kind == "Warp" ? $"script-region:{Actor}:{Destination}:{Left}:{Top}:{Right}:{Bottom}" :
            Kind == "Encounter" ? $"script-encounter:{Actor}:{Value}:{Left}:{Top}:{Right}:{Bottom}" :
            Kind == "Terrain" ? $"script-terrain:{Actor}:{Value}:{Left}:{Top}:{Right}:{Bottom}" :
            Kind == "Switch" ? $"script-switch:{Actor}:{Source}:{Index}:{Value}:{Set}:{Left}:{Top}:{Right}:{Bottom}" :
            $"script-action:{Actor}:{Value}:{Left}:{Top}:{Right}:{Bottom}";
        public bool Available(FieldStoryState? state, bool local = true) => Guards.All(g => g.Allows(state, local));
    }
    public sealed record World(int WorldId, int Id, int Destination, int X, int Y, bool Enabled, int NameIndex);
    public sealed record Scene(int Id, string? Name, Exit[] Exits, Actor[] Actors, Region[] Regions);
    private sealed record Catalog(string ExecutableSha256, Scene[] Scenes, World[] Worlds);
    private static readonly Catalog catalog = Read();
    private static readonly IReadOnlyDictionary<int, Scene> scenes = catalog.Scenes.ToDictionary(s => s.Id);
    public static IEnumerable<Scene> Scenes => scenes.Values;
    public static IEnumerable<World> Worlds => catalog.Worlds;
    public static Scene? ForScene(int scene) => scenes.GetValueOrDefault(scene);
    public static bool IsFieldScene(int scene) => scene > 0 && scene is not (>= 496 and <= 511) && scenes.ContainsKey(scene);
    public static string? AreaName(int scene) => ForScene(scene)?.Name;
    public static Actor? ActorInfo(int scene, FieldActorSnapshot actor) => ForScene(scene)?.Actors.FirstOrDefault(a => a.Matches(actor));
    public static string? ExitLabel(int scene, int exit)
    {
        var entry = ForScene(scene)?.Exits.FirstOrDefault(e => e.Id == exit);
        if (entry is null) return null;
        if (entry.Destination is >= 496 and <= 511) return "Outside";
        return AreaName(entry.Destination) is { } name ? "To " + name : "Passage";
    }

    public static string DestinationLabel(int destination) => destination is >= 496 and <= 511 ? "Outside" :
        AreaName(destination) is { } name ? "To " + name : "Passage";

    private static Catalog Read()
    {
        using var stream = typeof(GameNavigationCatalog).Assembly.GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Navigation.Data.game-navigation.json")
            ?? throw new InvalidDataException("Navigation catalog resource is missing.");
        var catalog = JsonSerializer.Deserialize<Catalog>(stream) ?? throw new InvalidDataException("Navigation catalog is empty.");
        if (catalog.ExecutableSha256 != "8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7" ||
            catalog.Scenes.Length != 669 || catalog.Scenes.Any(s => s.Id is < 0 or > 1023 || s.Exits.Length > 128 || s.Actors.Length > 64))
            throw new InvalidDataException("Navigation catalog does not match the supported game.");
        return catalog;
    }
}
