using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class RatChaseNavigationTests
{
    [Fact]
    public void ReachingTheRatKeepsPursuitActiveAndFollowsItsNextPosition()
    {
        var controller = Start(Frame(0, 1024));
        var close = controller.Update(Frame(768, 1024), 100);
        Assert.True(close.AutoWalking);
        Assert.Contains(close.Speech, s => s.Contains("Press Confirm"));
        Assert.DoesNotContain(close.Speech, s => s.Contains("Arrived"));
        Assert.Empty(controller.Update(Frame(768, 1024), 116).Speech);
        var moved = controller.Update(Frame(768, 1536), 132);
        Assert.True(moved.AutoWalking);
        Assert.Equal(NavigationDirection.East, moved.Direction);
    }

    [Fact]
    public void WaitingWithinCatchRangeDoesNotBecomeBlockedOrRepeatTheCueEveryFrame()
    {
        var controller = Start(Frame(768, 1024));
        for (var now = 16; now < 4000; now += 16)
        {
            var result = controller.Update(Frame(768, 1024), now);
            Assert.True(result.AutoWalking);
            Assert.Equal(NavigationDirection.None, result.Direction);
            Assert.Empty(result.Speech);
        }
    }

    [Fact]
    public void ManualGuidanceAlsoKeepsFollowingButNeverSuppliesMovement()
    {
        var controller = Start(Frame(768, 1024), NavigationCommand.Guide);
        var close = controller.Update(Frame(768, 1024), 100);
        Assert.True(close.Guiding);
        Assert.False(close.AutoWalking);
        var moved = controller.Update(Frame(768, 1536), 400);
        Assert.True(moved.Guiding);
        Assert.Equal(NavigationDirection.None, moved.Direction);
        Assert.NotNull(moved.ManualLeg);
    }

    [Fact]
    public void ConfirmIsPassedThroughWhilePursuitContinues()
    {
        var h = new Harness();
        Assert.Equal(0x100u, h.Start());
        for (var i = 0; i < 30; i++)
            Assert.Equal(0x180u, h.Tick(0x80));
        Assert.DoesNotContain(h.Speech, s => s.Contains("manual control"));
        Assert.Equal(0x100u, h.Tick());
    }

    [Theory]
    [InlineData(0x800u)] // movement
    [InlineData(0x88u)] // Confirm plus Cancel
    [InlineData(0x1u)] // game menu
    public void OtherPhysicalActionsStillCancelPursuit(uint pad)
    {
        var h = new Harness(); h.Start();
        Assert.Equal(pad, h.Tick(pad));
        Assert.Equal(0u, h.Tick());
        Assert.Contains(h.Speech, s => s.Contains("manual control"));
    }

    [Theory]
    [InlineData("focus")]
    [InlineData("gap")]
    [InlineData("dialogue")]
    [InlineData("caught")]
    [InlineData("escaped")]
    public void ChaseAuthorityEndsWithoutResumingItself(string cause)
    {
        var h = new Harness(); h.Start();
        switch (cause)
        {
            case "focus": h.Foreground = false; break;
            case "gap": h.Now += 1000; break;
            case "dialogue": h.Current = h.Current with { CanNavigate = false }; break;
            case "caught": h.Current = Frame(0, 1024, flags: 0x40); break;
            case "escaped": h.Current = Frame(0, 1024, absent: true); break;
        }
        Assert.Equal(0x80u, h.Tick(0x80));
        h.Foreground = true; h.Current = Frame(0, 1024);
        Assert.Equal(0u, h.Tick());
    }

    [Fact]
    public void OrdinaryNpcArrivalAndConfirmCancellationStayUnchanged()
    {
        var frame = Frame(768, 1024);
        // Use the unannotated native target: the story binding must opt in only to this chase.
        var ordinary = Rat(1024) with { Id = "npc", Category = NavigationCategory.People };
        frame = frame with { Targets = [ordinary] };
        var controller = new NavigationController();
        Assert.False(controller.Handle(NavigationCommand.ToggleWalk, frame, 0).AutoWalking);
        var h = new Harness { Current = frame with { Player = new(0, 0, 1) } };
        h.Start(NavigationCategory.People);
        Assert.Equal(0x80u, h.Tick(0x80));
        Assert.Equal(0u, h.Tick());
    }

    [Theory]
    [InlineData(12)]
    [InlineData(13)]
    public void NativeRatEntryKeepsFollowingOutsideTheStoryCategory(int slot)
    {
        var player = FullGameNavigationTests.Actor(1, 768, 768, true) with { ClassTag = 0, Facing = 3 };
        var rat = FullGameNavigationTests.Actor(slot, 1024, 768) with { ClassTag = 5, VisualIndex = 134 };
        var field = FullGameNavigationTests.Field(221) with
            { LeadPlayer = player, LeadPlayerActorIndex = 1, Actors = [player, rat] };
        var frame = new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, FullGameNavigationTests.Map(),
            new(0, 0, 2048, 2048), [], new(53, false) { Globals = new Dictionary<int, int> { [0xEC] = 0x10 } });
        var target = Assert.Single(frame.Targets, t => t.Id == $"actor:{slot}:5:134");
        Assert.Contains(NavigationDirection.East, target.AnyConfirmFacings(playerPoint()));
        var goal = NavigationPathfinder.Find(frame.Graph, frame.Player, target.ApproachPoints)![^1];
        var at = frame with { Player = goal, PlayerFacing = target.AnyConfirmFacings(goal)[0], Targets = [target] };
        var controller = new NavigationController();
        for (var i = 0; i < (int)target.Category; i++) controller.Handle(NavigationCommand.NextCategory, at, 0);
        Assert.True(controller.Handle(NavigationCommand.ToggleWalk, at, 0).AutoWalking);
        Assert.True(controller.AllowsConfirmWhileFollowing(at));
        NavigationPoint playerPoint() => new(player.FineX, player.FineY, 1);
    }

    [Fact]
    public void CatchCueRequiresTheActualFacingAndRepeatsOnlyWhenReachChanges()
    {
        var controller = Start(Frame(0, 1024));
        var wrong = controller.Update(Frame(768, 1024) with { PlayerFacing = NavigationDirection.West }, 100);
        Assert.Equal(NavigationDirection.East, wrong.Direction);
        Assert.DoesNotContain(wrong.Speech, s => s.Contains("In reach"));
        Assert.Contains(controller.Update(Frame(768, 1024), 116).Speech, s => s.Contains("In reach"));
        controller.Update(Frame(768, 1536), 1200);
        Assert.Contains(controller.Update(Frame(1280, 1536), 1300).Speech, s => s.Contains("In reach"));
    }

    [Theory]
    [InlineData("busy")]
    [InlineData("other-contact")]
    [InlineData("unknown-contact")]
    public void NativeReadinessMustAllowConfirmBeforeTheCatchCue(string reason)
    {
        NavigationFrame Capture(bool available)
        {
            var player = FullGameNavigationTests.Actor(1, 768, 768, true) with { ClassTag = 0, Facing = 3 };
            var rat = FullGameNavigationTests.Actor(13, 1024, 768) with
                { ClassTag = 5, VisualIndex = 134, ScriptPriority = !available && reason == "busy" ? 1 : 3 };
            var field = FullGameNavigationTests.Field(221) with
            {
                LeadPlayer = player, LeadPlayerActorIndex = 1, Actors = [player, rat],
                ContactActorRaw = available ? 0x80 : reason == "unknown-contact" ? null : reason == "other-contact" ? 28 : 0x80,
                ConfirmActorRaw = !available && reason == "other-contact" ? 28 : 0x80,
            };
            return new FieldNavigationSource(new NoMemory(), _ => { }).Build(field, FullGameNavigationTests.Map(),
                new(0, 0, 2048, 2048), [], new(53, false) { Globals = new Dictionary<int, int> { [0xEC] = 0x10 } });
        }
        var blocked = Capture(false);
        var controller = Start(blocked);
        var waiting = controller.Update(blocked, 100);
        Assert.True(waiting.AutoWalking);
        Assert.DoesNotContain(waiting.Speech, s => s.Contains("In reach") || s.Contains("Arrived"));
        Assert.Contains(controller.Update(Capture(true), 116).Speech, s => s.Contains("In reach"));
    }

    [Theory]
    [InlineData(1152, 3967, 1152, 3712, 1152, 3456, NavigationDirection.North)]
    [InlineData(4992, 767, 4992, 1024, 4992, 1280, NavigationDirection.South)]
    [InlineData(2208, 5311, 2464, 5312, 2720, 5312, NavigationDirection.East)]
    [InlineData(6080, 5311, 5824, 5312, 5568, 5312, NavigationDirection.West)]
    public void PursuitReplansOnTheInstalledRafterTerrain(int px, int py, int rx, int ry, int nextX, int nextY,
        NavigationDirection facing)
    {
        // Installed map119, scene221. Player coordinates are sampled from the reporter's log;
        // nearby rat positions are constructed along those rafters, not a full live snapshot.
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.arris-rat-map-0342.json")!;
        using var document = JsonDocument.Parse(stream);
        var map = document.RootElement.GetProperty("Map").Deserialize<FieldMapSnapshot>()!;
        var source = new FieldNavigationSource(new NoMemory(), _ => { });
        NavigationFrame Capture(int x, int y)
        {
            var player = FullGameNavigationTests.Actor(1, px, py, true) with { ClassTag = 0,
                Facing = facing switch { NavigationDirection.North => 0, NavigationDirection.South => 1,
                    NavigationDirection.West => 2, _ => 3 } };
            var rat = FullGameNavigationTests.Actor(13, x, y) with { ClassTag = 5, VisualIndex = 134 };
            var field = FullGameNavigationTests.Field(221) with
                { LeadPlayer = player, LeadPlayerActorIndex = 1, Actors = [player, rat] };
            return source.Build(field, map, new(0, 0, map.Width * 256, map.Height * 256), [],
                new(53, false) { Globals = new Dictionary<int, int> { [0xEC] = 0x10 } });
        }
        var initial = Capture(rx, ry);
        var controller = Start(initial);
        var close = controller.Update(initial, 16);
        Assert.True(close.AutoWalking);
        var next = controller.Update(Capture(nextX, nextY), 32);
        Assert.True(next.AutoWalking);
        Assert.Equal(facing, next.Direction);
        Assert.DoesNotContain(next.Speech, s => s.Contains("Arrived"));
    }

    private sealed class NoMemory : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination) => false;
    }

    private static NavigationController Start(NavigationFrame frame, NavigationCommand command = NavigationCommand.ToggleWalk)
    {
        var controller = new NavigationController();
        for (var i = 0; i < 3; i++) controller.Handle(NavigationCommand.NextCategory, frame, 0);
        controller.Handle(command, frame, 0);
        return controller;
    }

    private static NavigationTarget Rat(int x) => new("actor:13:5:134", "Rat", NavigationCategory.Objects,
        new(x, 0, 1), [new(x - 256, 0, 1)], true, true)
    {
        ConfirmFacings = p => FieldInteractionRange.Facings(p.X, p.Y, x, 0),
    };

    private static NavigationFrame Frame(int player, int rat, int flags = 0x10, bool absent = false) =>
        new("rat-room:221", true, new(player, 0, 1),
            FutureStoryTargets.Build(221, new(53, false) { Globals = new Dictionary<int, int> { [0xEC] = flags } },
                absent ? [] : [Rat(rat)], new(player, 0, 1)), new Aisle(), 256)
        { PlayerFacing = NavigationDirection.East };

    private sealed class Harness
    {
        public NavigationFrame Current = Frame(0, 1024);
        public bool Foreground = true;
        public long Now;
        public readonly List<string> Speech = [];
        private readonly HashSet<int> keys = [];
        private readonly FieldNavigationRuntime runtime;
        public Harness() => runtime = new(_ => Current, new(keys.Contains, () => Foreground),
            () => Foreground, () => Now, Speech.Add, _ => { });
        public uint Start(NavigationCategory category = NavigationCategory.StoryEvents)
        {
            runtime.Enable(); Tick();
            for (var i = 0; i < (int)category; i++) Press('O');
            return Press('P');
        }
        public uint Tick(uint pad = 0) { Now += 16; return runtime.OnInput(1, pad); }
        private uint Press(int key) { keys.Clear(); Tick(); keys.Add(key); var pad = Tick(); keys.Clear(); return pad; }
    }

    private sealed class Aisle : INavigationGraph
    {
        public IEnumerable<NavigationPoint> Neighbours(NavigationPoint p)
        {
            if (p.X < 2048) yield return p with { X = p.X + 64 };
            if (p.X > 0) yield return p with { X = p.X - 64 };
        }
    }
}
