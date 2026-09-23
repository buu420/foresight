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
                Assert.Equal(p[2].GetBoolean(), rules.BlocksProbe(p[0].GetInt32(), p[1].GetInt32()));
        }
    }
}
