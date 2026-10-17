using ChronoTriggerAccessibility.Mod.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class OceanPalaceVisibilityTests
{
    [Theory]
    [InlineData("062759-1580", 15)]
    [InlineData("062759-1580", 30)]
    [InlineData("062759-1580", 31)]
    [InlineData("062759-1932", 32)]
    [InlineData("062759-1932", 33)]
    [InlineData("062759-1078", 16)]
    public void AuditedSpritesBelowTheOpaqueBackgroundDoNotBecomePeopleOrEnemies(string id, int slot)
    {
        var capture = OceanPalaceFixtures.Read(id);
        var actor = Assert.Single(capture.Field.Actors, a => a.Index == slot);
        Assert.Equal(0, actor.RenderPriorityPacked);
        var frame = OceanPalaceFixtures.Frame(capture);
        Assert.DoesNotContain(frame.Targets, t => t.Id.StartsWith($"actor:{slot}:", StringComparison.Ordinal) ||
            t.Id == $"actor-encounter:{slot}");
        // Captured actors remain in the collision model; only the public row is omitted.
        Assert.Contains(capture.Field.Actors, a => a == actor);
    }

    [Theory]
    [InlineData("062759-1580", "landmark:22")]
    [InlineData("062759-1932", "landmark:26")]
    public void BlockedWallSwitchDoesNotDescribeTheConcealedBlockers(string id, string target)
    {
        var wall = Assert.Single(OceanPalaceFixtures.Frame(id).Targets, t => t.Id == target);
        Assert.Contains("blocked", wall.GuidanceRestriction!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("people", wall.GuidanceRestriction!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("people", wall.Instruction!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheSameSpriteAtNormalDrawPriorityRemainsAVisiblePerson()
    {
        var capture = OceanPalaceFixtures.Read("062759-1580");
        capture = capture with { Field = capture.Field with { Actors = capture.Field.Actors.Select(a =>
            a.Index == 30 ? a with { RenderPriorityPacked = 0x2020 } : a).ToArray() } };
        Assert.Contains(OceanPalaceFixtures.Frame(capture).Targets, t => t.Id == "actor:30:4:48");
    }
}
