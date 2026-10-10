using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

/// <summary>Reptite Lair holes load person sprite 0x85 (sheet c140) and set static frame 2
/// (op AC 02), an open pit with a rocky rim; Confirm drops the party through it. Its
/// creatures load enemies 0x46/0x26/0x7B/0x01/0x5D/0x79 (sheets c333/c301/c386/c264/c356/c384).
/// Evidence: artifacts/research/ioka-lair-0352/lair-native-audit.</summary>
public sealed class ReptiteLairNavigationTests
{
    [Fact]
    public void OpenHoleIsANeutrallyNamedConfirmPassageRatherThanASparkle()
    {
        var frame = Build(284, Hole(14, 33, 12), Hole(16, 20, 30));
        var holes = frame.Targets.Where(t => t.Id.StartsWith("actor:14:") || t.Id.StartsWith("actor:16:")).ToArray();
        Assert.Equal(2, holes.Length);
        Assert.All(holes, h =>
        {
            Assert.Equal(NavigationCategory.Exits, h.Category);
            Assert.StartsWith("Hole ", h.Label);
            Assert.NotNull(h.ConfirmAt(h.ApproachPoints[0]));
            Assert.DoesNotContain("Hole 1", h.Instruction ?? "");
        });
        Assert.Equal(2, holes.Select(h => h.Label).Distinct().Count());
        Assert.DoesNotContain(frame.Targets, t => t.Label.Contains("Sparkle", StringComparison.Ordinal));
    }

    [Fact]
    public void ParkedHoleAtTheNativeHoldingTileIsNotOffered()
    {
        // Atel_0373 parks holes 15-18 at tile (0,0) (8B 00 00) until a beetle burrows and
        // moves one onto its own tile (8C); that corner is solid rock in all lair hole maps.
        var frame = Build(284, Hole(14, 33, 12), Hole(15, 0, 0));
        Assert.Contains(frame.Targets, t => t.Id.StartsWith("actor:14:"));
        Assert.DoesNotContain(frame.Targets, t => t.Id.StartsWith("actor:15:"));
    }

    [Fact]
    public void AmbushHoleKeepsItsOutcomeAndDestinationHidden()
    {
        // Atel_0374 hole 20 first starts a battle while local 0x12 is clear, then drops to Hole 2.
        var frame = Build(285, Hole(20, 25, 41));
        var hole = Assert.Single(frame.Targets, t => t.Id.StartsWith("actor:20:"));
        Assert.Equal(NavigationCategory.Exits, hole.Category);
        var words = string.Join(" ", hole.Label, hole.Instruction, hole.ArrivalInstruction);
        foreach (var hidden in new[] { "battle", "Battle", "ambush", "enemy", "Hole 2", "Reptite", "trap" })
            Assert.DoesNotContain(hidden, words);
        Assert.DoesNotContain(frame.Targets, t => t.Category == NavigationCategory.Enemies && t.Position == hole.Position);
    }

    [Theory]
    [InlineData(NavigationCommand.Guide)]
    [InlineData(NavigationCommand.ToggleWalk)]
    public void VisibleHoleWithDisabledNativeCallsCannotStartGuidance(NavigationCommand command)
    {
        // The activation scan can remain set while E8/bit80 blocks native Confirm calls.
        var frame = Build(285, Hole(20, 25, 41) with { ScriptCallsEnabled = false });
        var hole = Assert.Single(frame.Targets, t => t.Id.StartsWith("actor:20:"));
        Assert.StartsWith("Hole ", hole.Label);
        Assert.False(hole.GuideAvailable);
        frame = frame with { Targets = [hole] };
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        var result = controller.Handle(command, frame, 1);
        Assert.False(result.Guiding);
        Assert.False(result.AutoWalking);
        Assert.Contains("Hole interaction is currently unavailable.", result.Speech);
    }

    [Theory]
    [InlineData(NavigationCommand.Guide)]
    [InlineData(NavigationCommand.ToggleWalk)]
    public void DisablingNativeCallsStopsAnActiveHoleRoute(NavigationCommand command)
    {
        var actor = Hole(20, 25, 41);
        var frame = Build(285, actor);
        var hole = Assert.Single(frame.Targets, t => t.Id.StartsWith("actor:20:"));
        frame = frame with { Targets = [hole] };
        var controller = new NavigationController();
        controller.Handle(NavigationCommand.NextCategory, frame, 0);
        Assert.True(controller.Handle(command, frame, 1).Guiding);
        var blocked = Build(285, actor with { ScriptCallsEnabled = false });
        blocked = blocked with { Targets = blocked.Targets.Where(t => t.Id == hole.Id).ToArray() };
        var result = controller.Update(blocked, 20);
        Assert.False(result.Guiding);
        Assert.False(result.AutoWalking);
        Assert.Equal(NavigationDirection.None, result.Direction);
        Assert.Contains("Hole interaction is currently unavailable.", result.Speech);
    }

    [Theory]
    [InlineData(284, 9, 70, "Blue beetle")]
    [InlineData(285, 9, 38, "Pink flower creature")]
    [InlineData(286, 8, 123, "Large green dinosaur")]
    [InlineData(287, 13, 1, "Small green lizard")]
    [InlineData(288, 9, 93, "Mushroom creature")]
    [InlineData(288, 12, 121, "Gold winged creature")]
    public void LairCreaturesKeepTheirEncounterUnderTheirAppearance(int scene, int index, int enemy, string label)
    {
        var creature = FullGameNavigationTests.Actor(index, 20 * 256 + 128, 20 * 256 + 128) with { ClassTag = 5, VisualIndex = enemy };
        var frame = Build(scene, creature);
        Assert.Contains(frame.Targets, t => t.Label == label);
        Assert.DoesNotContain(frame.Targets, t => t.Label is "Creature");
    }

    [Fact]
    public void OtherScenesKeepTheirOwnUseOfTheSameSprite()
    {
        // Guardia Forest animates c140 differently (91 / 87 01), so the lair label must not leak.
        var bush = FullGameNavigationTests.Actor(36, 40 * 256 + 128, 38 * 256 + 128) with { VisualIndex = 133 };
        var frame = Build(119, bush);
        Assert.DoesNotContain(frame.Targets, t => t.Label.StartsWith("Hole", StringComparison.Ordinal));
    }

    private static FieldActorSnapshot Hole(int index, int tileX, int tileY) =>
        FullGameNavigationTests.Actor(index, tileX * 256 + 128, tileY * 256 + 128) with { VisualIndex = 133 };

    private static NavigationFrame Build(int scene, params FieldActorSnapshot[] actors)
    {
        var player = FullGameNavigationTests.Actor(1, 24 * 256 + 128, 24 * 256 + 128, true) with { ClassTag = 0 };
        var field = FullGameNavigationTests.Field(scene) with { LeadPlayer = player, Actors = [player, .. actors], ActorCount = 40 };
        var map = new FieldMapSnapshot(64, 64, new byte[4096], new byte[4096], Enumerable.Repeat((byte)1, 4096).ToArray(),
            1, false, 64, 64, Enumerable.Repeat((byte)128, 4096).ToArray());
        var locals = Enumerable.Range(0, 64).ToDictionary(i => i, _ => 0);
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, map, new(0, 0, 64 * 256, 64 * 256), [],
            new FieldStoryState(123, true) { Locals = locals, Globals = new Dictionary<int, int> { [0x46] = 0 } });
    }

    private sealed class NoMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination) => false;
    }
}
