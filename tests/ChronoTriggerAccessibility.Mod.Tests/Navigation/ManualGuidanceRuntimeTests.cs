using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class ManualGuidanceRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatReadsTheRemainingLegAndCoalescesWithANewTurn(bool world)
    {
        var h = new Harness(world);
        h.Press('I');
        h.Move(2, 1, 0x100);
        h.Speech.Clear();
        Assert.Equal(0u, h.Press('K'));
        Assert.Equal(["Person, 1 of 1. Right 3 steps."], h.Speech);

        h.Tick(); // Release K before pressing it again on the turn frame.
        h.Position = h.Point(5, 1);
        h.Speech.Clear();
        Assert.Equal(0x100u, h.Press('K', 0x100));
        Assert.Equal(["Up 1 step."], h.Speech);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatDuringRecoveryPreservesTheStopAndUpdatedRouteWithoutDuplicates(bool world)
    {
        var h = new Harness(world);
        h.Press('I');
        h.Move(2, 1, 0x100);
        h.Position = h.Point(2, 2);
        h.Speech.Clear();
        Assert.Equal(0x400u, h.Press('K', 0x400));
        Assert.Equal(["Off route. Stop moving for new directions."], h.Speech);

        h.Tick(); // Release the keys; the native position has stopped too.
        h.Now += 200;
        h.Speech.Clear();
        Assert.Equal(0u, h.Press('K'));
        Assert.Equal(["Route updated. Right 3 steps."], h.Speech);
    }

    private sealed class Harness
    {
        private readonly bool world;
        private readonly int units;
        private readonly HashSet<int> keys = [];
        private readonly FieldNavigationRuntime runtime;
        public List<string> Speech { get; } = [];
        public NavigationPoint Position;
        public long Now;

        public Harness(bool world)
        {
            this.world = world;
            units = world ? 128 : 256;
            Position = Point(0, 1);
            var graph = new Corridor(units);
            NavigationFrame Capture(nint _) => new(world ? "world:1:0" : "room", true, Position,
                [new("person", "Person", NavigationCategory.People, Point(7, 0),
                    [Point(7, 0)], true, false)], graph, units);
            runtime = new(Capture, new(keys.Contains, () => true), () => true, () => Now,
                Speech.Add, _ => { }, worldCapture: Capture);
            runtime.Enable();
            Tick();
        }

        public NavigationPoint Point(int x, int y) => new(x * units, y * units, 1);
        public uint Tick(uint input = 0)
        {
            Now += 16;
            return world ? runtime.OnWorldInput(1, input) : runtime.OnInput(1, input);
        }
        public uint Press(int key, uint input = 0)
        {
            keys.Add(key);
            var result = Tick(input);
            keys.Clear();
            return result;
        }
        public void Move(int x, int y, uint input) { Position = Point(x, y); Tick(input); }
    }

    private sealed class Corridor(int units) : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint point)
        {
            foreach (var next in new[] { point with { X = point.X + units }, point with { Y = point.Y - units },
                point with { X = point.X - units }, point with { Y = point.Y + units } })
                if ((next.Y == units && next.X >= 0 && next.X <= 5 * units) ||
                    (next.Y == 0 && next.X >= 5 * units && next.X <= 7 * units) ||
                    (next.Y == 2 * units && next.X >= 2 * units && next.X <= 5 * units)) yield return next;
        }
    }
}
