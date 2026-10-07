using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class FactoryHatchTests
{
    [Fact]
    public void HiddenHatchTerminalGuidesToTheActualLaboratoryFight()
    {
        // Atel0040 hides actor12 at038F/0391 until actor0's D8 at0230
        // completes and request0235 calls actor12 fn3 to show the terminal.
        var frame = Frame(terminalDrawn: false);
        var encounters = frame.Targets.Where(t => t.Category == NavigationCategory.Enemies).ToArray();
        Assert.NotEmpty(encounters);
        Assert.DoesNotContain(frame.Targets, t => t.Id == "actor:12:4:127");
        var objective = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.Equal("Clear the laboratory entrance", objective.Label);
        Assert.False(objective.IsStoryNote);
        Assert.NotEmpty(objective.ApproachPoints);
        Assert.All(objective.ApproachPoints, p => Assert.Contains(encounters, e => e.ApproachPoints.Contains(p)));
        Assert.Contains("terminal", objective.Instruction);
    }

    [Fact]
    public void RevealedTerminalRetainsItsNativeConfirmApproach()
    {
        var frame = Frame(terminalDrawn: true);
        var terminal = Assert.Single(frame.Targets, t => t.Id == "actor:12:4:127");
        var objective = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.Equal("Open the laboratory hatch", objective.Label);
        Assert.False(objective.IsStoryNote);
        Assert.Equal(terminal.ApproachPoints, objective.ApproachPoints);
        Assert.NotNull(objective.ApproachConfirms);
        Assert.DoesNotContain(frame.Targets, t => t.Category == NavigationCategory.Enemies);
    }

    [Fact]
    public void OpenedHatchUsesTheLiveLadderInsteadOfReplayingTheFight()
    {
        var frame = Frame(terminalDrawn: false, hatchOpen: true);
        var ladder = Assert.Single(frame.Targets, t => t.Id == "exit:0");
        var objective = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.StoryEvents);
        Assert.Equal("Climb down to the lower laboratory", objective.Label);
        Assert.Equal(ladder.ApproachPoints, objective.ApproachPoints);
        Assert.False(objective.IsStoryNote);
    }

    [Fact]
    public void MissingPrerequisiteDoesNotInventAnInteractableHiddenTerminal()
    {
        var objective = Assert.Single(FutureStoryTargets.Build(229, State(false), [], default));
        Assert.True(objective.IsStoryNote);
        Assert.Empty(objective.ApproachPoints);
        Assert.Contains("laboratory", objective.Instruction);
        Assert.Contains("encounter", objective.Instruction);
    }

    private static NavigationFrame Frame(bool terminalDrawn, bool hatchOpen = false)
    {
        var player = FullGameNavigationTests.Actor(1, 8192, 4736, true) with { ClassTag = 0 };
        var controller = FullGameNavigationTests.Actor(0, 0, 0) with { ClassTag = 7, DrawMode = 0 };
        var terminal = FullGameNavigationTests.Actor(12, 7296, 1280) with
        { VisualIndex = 127, DrawMode = terminalDrawn ? 1 : 0 };
        var monsters = Enumerable.Range(9, 3).Select(i => FullGameNavigationTests.Actor(i,
            (i == 9 ? 28 : i == 10 ? 33 : 30) * 256 + 128, (i == 11 ? 4 : 3) * 256 + 255) with
            { ClassTag = 5, VisualIndex = i == 9 ? 105 : 106, DrawMode = terminalDrawn || hatchOpen ? 0x80 : 1 });
        var field = new FieldNavigationSnapshot(0x1000, 0x4000, 0x2000, 0x20000, 14, 229, true,
            1, 0, 0, 1, player, [controller, player, terminal, .. monsters]);
        // Open test terrain isolates story selection. It is not a captured Factory map.
        var map = new FieldMapSnapshot(64, 64, new byte[4096], new byte[4096],
            Enumerable.Repeat((byte)1, 4096).ToArray(), 1, false, 64, 64,
            Enumerable.Repeat((byte)128, 4096).ToArray());
        map.ExitCells[10 * 64 + 34] = 0;
        return new FieldNavigationSource(new NoMemory(), _ => { }).Build(
            field, map, new(5632, 0, 9856, 3200), [], State(hatchOpen));
    }

    private static FieldStoryState State(bool open) => new(60, false)
    {
        Globals = new Dictionary<int, int> { [0x58] = 0x64, [0x5C] = open ? 0x20 : 0 },
        Locals = new Dictionary<int, int> { [8] = 0 },
    };
    private sealed class NoMemory : IReadableMemory
    { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
