using System.Collections;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class VehicleStoryRoutingWorkTests
{
    [Fact]
    public void OneReroutePreparesEachObjectiveOnceInsteadOfReenumeratingStateForEveryEntrance()
    {
        var globals = new CountedWords(Enumerable.Range(0, 512).ToDictionary(i => i, i => (i * 37) & 255));
        globals.ValuesByIndex[0x1FB] = 0x45;
        var state = new FieldStoryState(214, true)
        {
            Globals = globals,
            Extended = Enumerable.Range(0, 80).ToDictionary(i => i, i => (i * 11) & 255),
            Inventory = Enumerable.Range(1, 120).ToDictionary(i => i, i => 1 + i % 9),
            Party = [0, 1, 2, 3, 4, 5, 6, 128, 128], Gold = 472302,
        };
        var objectives = FullStoryObjectives.Build(502, state);
        var notes = objectives.Select(o => StoryTarget.Note("story:" + o.Id, o.Label, "pending")).ToArray();
        var vehicle = new NavigationTarget("vehicle:epoch", "Epoch", NavigationCategory.Objects,
            new(17920, 6528, 1), [new(17920, 6528, 1)], true, true) { GuideAvailable = true };
        var destinations = GameNavigationCatalog.Worlds.Where(w => w.WorldId == 6 && GameNavigationCatalog.IsFieldScene(w.Destination))
            .ToDictionary(w => "entrance:" + w.Id, w => w.Destination);
        globals.Enumerations = 0;
        var result = new VehicleStoryRouting().Reroute(notes, 6, state, [vehicle], true,
            destinations, new(18432, 6528, 1), n => "Era " + n);
        Assert.Contains(result, t => !t.IsStoryNote && t.ApproachPoints.Count != 0);
        Assert.True(globals.Enumerations > 0);
        Assert.True(globals.Enumerations <= objectives.Count,
            $"{globals.Enumerations} state enumerations for {objectives.Count} objectives.");
    }

    [Fact]
    public void AReusedStateDictionaryStillInvalidatesNativeRouteRestrictionsWhenItsValueChanges()
    {
        var globals = new Dictionary<int, int> { [0x57] = 0x10 };
        var state = new FieldStoryState(214, true) { Globals = globals };
        var router = new SceneConnectionRouter();
        Assert.Equal(2, router.DistanceWithinFields(307, state, 464));
        globals[0x57] = 0;
        Assert.Null(router.DistanceWithinFields(307, state, 464));
    }

    private sealed class CountedWords(Dictionary<int, int> values) : IReadOnlyDictionary<int, int>
    {
        public readonly Dictionary<int, int> ValuesByIndex = values;
        public int Enumerations;
        public int this[int key] => ValuesByIndex[key];
        public IEnumerable<int> Keys => ValuesByIndex.Keys;
        public IEnumerable<int> Values => ValuesByIndex.Values;
        public int Count => ValuesByIndex.Count;
        public bool ContainsKey(int key) => ValuesByIndex.ContainsKey(key);
        public bool TryGetValue(int key, out int value) => ValuesByIndex.TryGetValue(key, out value);
        public IEnumerator<KeyValuePair<int, int>> GetEnumerator() { Enumerations++; return ValuesByIndex.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
