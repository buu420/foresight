using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class NorthCapeTargetsTests
{
    [Theory]
    [InlineData("Magus")]
    [InlineData("Janus")]
    public void TheNativeEncounterPointUsesTheCharactersCurrentNameAndKeepsItsAppearance(string name)
    {
        var point = Assert.Single(Frame(name: name).Targets, t => t.Id == "actor:16:4:113");
        Assert.Equal(name + " encounter", point.Label);
        Assert.Equal(NavigationCategory.Objects, point.Category);
        Assert.Contains("sparkling light", point.Instruction);
        Assert.NotEmpty(point.ApproachPoints);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(128)]
    [InlineData(null)]
    public void AResolvedOrUnreadableEncounterDoesNotRenameThePickupBranch(int? state)
    {
        var point = Assert.Single(Frame(state: state).Targets, t => t.Id == "actor:16:4:113");
        Assert.Equal("Sparkle", point.Label);
    }

    [Fact]
    public void OtherSparkleActorsNeverInheritTheCapeEncounterName()
    {
        var point = Assert.Single(Frame(scene: 427).Targets, t => t.Id == "actor:16:4:113");
        Assert.Equal("Sparkle", point.Label);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0x84)]
    public void HiddenOrRemovedPointIsNotAdded(int variant) =>
        Assert.DoesNotContain(Frame(variant: variant).Targets, t => t.Id.StartsWith("actor:16:"));

    [Fact]
    public void TheRealCharacterIsNamedOnlyWhenDrawnAndTheDollIsNotConfusedWithHim()
    {
        Assert.DoesNotContain(Frame().Targets, t => t.Id.StartsWith("actor:7:") || t.Id.StartsWith("actor:18:"));
        var visible = Frame(magusDrawn: true);
        var magus = Assert.Single(visible.Targets, t => t.Id == "actor:7:3:6");
        Assert.Equal("Magus", magus.Label);
        Assert.Equal(NavigationCategory.People, magus.Category);
        Assert.DoesNotContain(visible.Targets, t => t.Id.StartsWith("actor:18:"));
    }

    private static NavigationFrame Frame(int scene = 428, string name = "Magus", int? state = 0, int? variant = null, bool magusDrawn = false)
    {
        // Observed October24 report, entry actors: lead6/Frog, point16/c120,
        // Magus7 hidden at(1920,3071), and gated doppel doll18 parked off-map.
        // Facing, priority and open test floor are unlogged test assumptions.
        var player = IokaPeopleTests.Actor(6, 2432, 7935) with { ClassTag = 0, VisualIndex = 5, IsPartyMember = true };
        var point = IokaPeopleTests.Actor(16, 1920, 1791) with
        {
            VisualIndex = 113, ClassTag = variant == 0x84 ? 0x84 : 4,
            DrawMode = variant == 0 ? 0 : 1, ScriptAddress = 0x998, ScriptPriority = 7,
            ActivationEnabled = scene == 428 ? 0 : 1
        };
        var magus = IokaPeopleTests.Actor(7, 1920, 3071) with { ClassTag = 3, VisualIndex = 6, DrawMode = magusDrawn ? 1 : 0 };
        var doll = IokaPeopleTests.Actor(18, 65280, 65280) with { ClassTag = 3, VisualIndex = 6, DrawMode = 0 };
        var field = new FieldNavigationSnapshot(0x1000, 0x4000, 0x2000, 0x20000, 19, scene, true,
            1, 0, 6, 6, player, [player, point, magus, doll]);
        var map = new FieldMapSnapshot(16, 32, new byte[512], new byte[512], Enumerable.Repeat((byte)1, 512).ToArray(),
            1, false, 16, 32, Enumerable.Repeat((byte)128, 512).ToArray());
        var globals = new Dictionary<int, int>();
        if (state is { } known) globals[0x138] = known;
        var story = new FieldStoryState(210, true) { Globals = globals };
        return new FieldNavigationSource(new IokaPeopleTests.NoMemory(), _ => { }, characterName: c => c == 6 ? name : null)
            .Build(field, map, new(0, 0, 4096, 8192), [], story);
    }
}
