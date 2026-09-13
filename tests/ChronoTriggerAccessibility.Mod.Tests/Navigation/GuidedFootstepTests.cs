using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class GuidedFootstepTests
{
    [Theory]
    [InlineData(128)]
    [InlineData(256)]
    public void StartingGuidanceAndRepeatingBeginAFreshCount(int units)
    {
        var h = new Harness(units);
        h.Tick();
        h.Move(3 * units / 4, units);
        h.Key('I');
        h.Move(3 * units / 4 + units / 2, units);
        Assert.Equal(0, h.Beats);
        h.Key('K');
        h.Move(3 * units / 4 + units, units);
        Assert.Equal(0, h.Beats);
        h.Move(3 * units / 4 + units * 3 / 2, units);
        Assert.Equal(1, h.Beats);
    }

    [Theory]
    [InlineData(128)]
    [InlineData(256)]
    public void FiveThenOneInstructionsHaveFiveThenOneBeatsIncludingEarlyNativeArrival(int units)
    {
        var h = new Harness(units);
        h.Tick();
        h.Key('I');
        Assert.EndsWith("Right 5 steps.", h.Speech[^1]);
        // Approach the turn in actual substep increments, including its tolerance.
        h.Move(5 * units - units / 16, units);
        Assert.Equal("Up 1 step.", h.Speech[^1]);
        Assert.Equal(5, h.Beats);
        h.Move(5 * units - units / 16, units / 8);
        Assert.Contains("Arrived", h.Speech[^1]);
        Assert.Equal(6, h.Beats);
        h.Tick(); h.Tick();
        Assert.Equal(6, h.Beats);
    }

    [Theory]
    [InlineData(128)]
    [InlineData(256)]
    public void FractionalLegDoesNotInventAnExtraWholeStepAtTheTurn(int units)
    {
        var h = new Harness(units);
        h.Tick();
        h.Move(units / 4, units);
        h.Key('I');
        Assert.EndsWith("Right 4.75 steps.", h.Speech[^1]);
        h.Move(5 * units - units / 16, units);
        Assert.Equal("Up 1 step.", h.Speech[^1]);
        Assert.Equal(4, h.Beats);
    }

    private sealed class Harness
    {
        private readonly int units;
        private readonly HashSet<int> keys = [];
        private readonly FieldFootstepRuntime footsteps;
        private readonly FieldNavigationRuntime navigation;
        private NavigationPoint position;
        private long now;
        public int Beats;
        public List<string> Speech { get; } = [];

        public Harness(int units)
        {
            this.units = units;
            position = new(0, units, 1);
            var end = new NavigationPoint(5 * units, 0, 1);
            var graph = new Passage(units);
            footsteps = new(_ => new(1, 1, 1, position.X, position.Y, units), () => true,
                keys.Contains, () => now, () => Beats++, () => { }, Speech.Add, _ => { });
            navigation = new(_ => new("test", true, position,
                [new("person", "Person", NavigationCategory.People, end, [end], true, false)], graph, units),
                new(keys.Contains, () => true), () => true, () => now, Speech.Add, _ => { },
                synchronizeFootsteps: footsteps.SetGuidance);
            navigation.Enable(); footsteps.Enable();
        }

        public void Tick(uint pad = 0)
        {
            now += 16;
            footsteps.OnInput(1, navigation.OnInput(1, pad));
        }
        public void Key(int key) { keys.Add(key); Tick(); keys.Clear(); Tick(); }
        public void Move(int x, int y)
        {
            var pad = position.X != x ? (position.X < x ? 0x100u : 0x200u) :
                (position.Y < y ? 0x400u : 0x800u);
            Tick(pad);
            while (position.X != x || position.Y != y)
            {
                position = position with { X = position.X + Math.Clamp(x - position.X, -8, 8),
                    Y = position.Y + Math.Clamp(y - position.Y, -8, 8) };
                Tick(pad);
            }
            Tick();
        }
    }

    private sealed class Passage(int units) : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint p)
        {
            if (p.Y > 0 && p.X >= 5 * units - units / 16) yield return p with { Y = Math.Max(0, p.Y - units / 4) };
            else if (p.X < 5 * units) yield return p with { X = Math.Min(5 * units, p.X + units / 4) };
        }
    }
}
