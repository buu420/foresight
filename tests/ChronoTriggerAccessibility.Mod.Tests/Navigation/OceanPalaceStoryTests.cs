using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class OceanPalaceStoryTests
{
    [Fact]
    public void MammonSequenceIsNotAdvertisedAsAnExitToALaterBattleRoom()
    {
        var frame = OceanPalaceFixtures.Frame("062759-7128");
        Assert.DoesNotContain(frame.Targets, t => t.Category == NavigationCategory.Exits &&
            t.Id == "script-region:11:467:0:0:255:22");
        var story = Assert.Single(frame.Targets, t => t.Id == "story:full:ocean-palace-lavos");
        Assert.DoesNotContain("Lavos", story.Label + " " + story.Instruction, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(story.ApproachPoints);
    }

    [Fact]
    public void VisibleLavosOwnsTheContinuationAndItsScriptWarpStaysOutOfExits()
    {
        var player = Actor(1, 0, 0, 8 * 256 + 128, 14 * 256 + 128, party: true);
        var queen = Actor(20, 4, 0x4A, 1536, 2048);
        var lavos = Actor(22, 6, 0xCA, 2032, 2240);
        var frame = Scene423(player, queen, lavos);
        Assert.DoesNotContain(frame.Targets, t => t.Category == NavigationCategory.Exits &&
            t.Id.StartsWith("script-region:20:415:", StringComparison.Ordinal));
        var person = Assert.Single(frame.Targets, t => t.Id == "actor:22:6:202");
        Assert.Equal("Lavos", person.Label);
        Assert.Equal(NavigationCategory.People, person.Category);
        Assert.NotEmpty(person.ApproachPoints);
        Assert.Null(person.ConfirmFacings);
        Assert.All(person.ApproachPoints, p => Assert.True(p.X / 256 is 7 or 8 && p.Y / 256 <= 10));
        var story = Assert.Single(frame.Targets, t => t.Id == "story:full:ocean-palace-lavos");
        Assert.Contains("Lavos", story.Label);
        Assert.Equal(person.ApproachPoints, story.ApproachPoints);
    }

    [Fact]
    public void AlgettyEntryObjectiveDoesNotTargetTheParkedDalton()
    {
        var story = Assert.Single(FullStoryObjectives.Build(380, new(0xB9, true)),
            objective => objective.Id == "full:algetty-elder");
        Assert.Contains(story.Goals, goal => goal.Scene == 382 && goal.Actors.Length == 0);
        Assert.DoesNotContain(story.Goals, goal => goal.Actors.Contains(13));
    }

    private static FieldActorSnapshot Actor(int index, int type, int visual, int x, int y, bool party = false) =>
        FullGameNavigationTests.Actor(index, x, y, party) with {
            ClassTag = type, VisualIndex = visual, RenderPriorityPacked = 0x2020,
            ActivationBinding = 128, ScriptPriority = 7,
        };

    private static NavigationFrame Scene423(FieldActorSnapshot player, params FieldActorSnapshot[] people)
    {
        var field = FullGameNavigationTests.Field(423) with {
            LeadPlayer = player, LeadPlayerActorIndex = player.Index, Actors = [player, .. people], ActorCount = 30,
        };
        var map = new FieldMapSnapshot(16, 48, new byte[768], new byte[768],
            Enumerable.Repeat((byte)1, 768).ToArray(), 1, false, 16, 48,
            Enumerable.Repeat((byte)128, 768).ToArray());
        var state = new FieldStoryState(0xC9, true) {
            // The Queen enables the approach after Magus's native sequence.
            Locals = Enumerable.Range(0, 128).ToDictionary(index => index, index => index == 11 ? 1 : 0),
            Globals = FieldStoryCapture.ObjectiveGlobalIndices.ToDictionary(index => index, _ => 0),
        };
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, map,
            new(0, 0, 4096, 12288), [], state);
    }

    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
