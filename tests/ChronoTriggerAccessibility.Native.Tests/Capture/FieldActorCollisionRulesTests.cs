using System.Reflection;
using System.Text.Json;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class FieldActorCollisionRulesTests
{
    [Fact]
    public void ActorContactsMatchTheOriginalExecutableIncludingBoundariesAndSlotPrecedence()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "ChronoTriggerAccessibility.Native.Tests.Capture.actor-collision-native-0330.json")!;
        using var doc = JsonDocument.Parse(stream);
        foreach (var g in doc.RootElement.GetProperty("Groups").EnumerateArray())
        {
            var scene = g.GetProperty("Scene").GetInt32();
            var last = g.GetProperty("Last").GetInt32();
            var actors = g.GetProperty("Actors").EnumerateArray().Select(a =>
            {
                int Read(string key) => a.GetProperty(key).GetInt32();
                var x = Read("X"); var y = Read("Y"); var i = Read("Index");
                return new FieldActorSnapshot(i, x / 256, x, x % 256, y / 256, y, y % 256,
                    0, 1, Read("Loaded"), 80, Read("Class"), 0, Read("Activation"), Read("Cache"),
                    i * 2 == last || i == 1, true, true, true) { CollisionOffsetX = Read("Offset") };
            }).ToArray();
            var field = new FieldNavigationSnapshot(0, 0, 0, 0, 64, scene, true, 1, 0, 1, 1, null, actors)
            {
                LastPartySlotRaw = last,
                ActorCollisionRadius = FieldActorCollisionRules.Radius(scene, g.GetProperty("Fair").GetInt32()),
            };
            var rules = new FieldActorCollisionRules(field);
            foreach (var p in g.GetProperty("Probes").EnumerateArray())
            {
                Assert.Equal(p[2].GetBoolean(), rules.BlocksProbe(p[0].GetInt32(), p[1].GetInt32()));
                Assert.Equal(p[2].GetBoolean(), rules.ContactProbe(p[0].GetInt32(), p[1].GetInt32()) is not null);
            }
        }
    }

    [Fact]
    public void TheContactSlotIsTheFirstMatchOnlyWhenThatMatchBlocks()
    {
        static FieldActorSnapshot Actor(int index, int loaded) =>
            new(index, 20, 20 * 256, 0, 20, 20 * 256, 0, 0, 1, loaded, 80, 5, 0, 0, 128, false, true, true, true);
        FieldActorCollisionRules Rules(params FieldActorSnapshot[] actors) =>
            new(new FieldNavigationSnapshot(0, 0, 0, 0, 64, 1, true, 1, 0, 1, 1, null, actors) { ActorCollisionRadius = 160 });

        // Slot 12 is tested before slot 7 and wins.
        Assert.Equal(12, Rules(Actor(7, 1), Actor(12, 1)).ContactProbe(20 * 256, 20 * 256));
        // An unloaded higher slot wins without blocking; the lower loaded slot is never reached.
        Assert.Null(Rules(Actor(7, 1), Actor(12, 0)).ContactProbe(20 * 256, 20 * 256));
        // A selected touch destination does not block, so it is not the blocker either.
        Assert.Null(Rules(Actor(12, 1)).ContactProbe(20 * 256, 20 * 256, new HashSet<int> { 12 }));
        Assert.Equal(12, Rules(Actor(12, 1)).ContactActor(20 * 256, 21 * 256, 20 * 256, 21 * 256 - 16));
        Assert.Null(Rules(Actor(12, 1)).ContactProbe(30 * 256, 30 * 256));
    }
}
