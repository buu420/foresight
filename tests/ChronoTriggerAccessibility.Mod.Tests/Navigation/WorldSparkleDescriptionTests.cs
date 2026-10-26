using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class WorldSparkleDescriptionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VisibleNativeParticlesDescribeTheManorWithoutChangingItsRouteOrConfirm(bool flight)
    {
        var spoken = new List<string>();
        var source = Source(spoken);
        var frame = Build(source, Snapshot(flight));
        var target = Assert.Single(frame.Targets, t => t.Category == NavigationCategory.Exits);
        Assert.Equal("Mayor's Manor", target.Label);
        Assert.Contains("Sparkles rise above Mayor's Manor.", target.Instruction);
        Assert.DoesNotContain("stone", target.Instruction!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["Sparkles rise above Mayor's Manor."], spoken);
        var plain = Build(Source([]), Snapshot(flight) with { PorreSparkles = [] });
        var original = Assert.Single(plain.Targets, t => t.Category == NavigationCategory.Exits);
        Assert.Equal(original.Position, target.Position);
        Assert.Equal(original.ApproachPoints, target.ApproachPoints);
        Assert.Equal(original.ArrivalInstruction, target.ArrivalInstruction);
        Assert.Equal(original.Id, target.Id);
    }

    [Fact]
    public void AutomaticCueIsOncePerVisitButTheCurrentTargetDescriptionRemainsAvailable()
    {
        var spoken = new List<string>(); var source = Source(spoken); var world = Snapshot();
        Build(source, world);
        Assert.Contains("Sparkles rise", Assert.Single(Build(source, world).Targets).Instruction);
        Build(source, world with { PorreSparkles = [] });
        Build(source, world);
        Assert.Single(spoken);
        source.Reset();
        Build(source, world);
        Assert.Equal(2, spoken.Count);
    }

    [Fact]
    public void CameraWrapUsesTheActualParticlePosition()
    {
        var spoken = new List<string>(); var world = Snapshot();
        world = world with { Viewport = new((300 + 1536) * 16, 800 * 16, (400 + 1536) * 16, 900 * 16) };
        Build(Source(spoken), world);
        Assert.Single(spoken);
    }

    [Fact]
    public void OffscreenOrEndedEffectDoesNotPersistInDiscoveredEntranceDescriptions()
    {
        var spoken = new List<string>(); var source = Source(spoken); var world = Snapshot();
        Build(source, world);
        var ended = Assert.Single(Build(source, world with { PorreSparkles = [] }).Targets);
        Assert.Equal("Press Confirm to enter.", ended.Instruction);
        var away = world with { Viewport = new(0, 0, 1000, 1000) };
        var retained = Assert.Single(Build(source, away).Targets);
        Assert.True(retained.Discovered);
        Assert.False(retained.Visible);
        Assert.Equal("Press Confirm to enter.", retained.Instruction);
        Assert.Single(spoken);
    }

    [Theory]
    [InlineData(1, true, true)]
    [InlineData(0, false, true)]
    [InlineData(0, true, false)]
    public void WrongEraHiddenEntranceOrMissingNativeLabelCannotExplainTheClue(int era, bool available, bool named)
    {
        var spoken = new List<string>(); var source = Source(spoken); var world = Snapshot();
        world = world with { Motion = world.Motion with { World = era },
            Entrances = [world.Entrances[0] with { Available = available }] };
        var frame = source.Build(world, _ => named ? "Mayor's Manor" : null);
        Assert.Empty(spoken);
        Assert.All(frame.Targets, t => Assert.DoesNotContain("Sparkles", t.Instruction ?? ""));
    }

    [Fact]
    public void MissingTextCanRetryAndOffscreenParticlesDoNotTriggerSpeech()
    {
        var spoken = new List<string>(); var source = Source(spoken); var world = Snapshot();
        source.Build(world, _ => null);
        Build(source, world with { PorreSparkles = [new(336, 820)], Viewport = new(300 * 16, 840 * 16, 400 * 16, 900 * 16) });
        Assert.Empty(spoken);
        Build(source, world);
        Assert.Single(spoken);
    }

    private static NavigationFrame Build(WorldNavigationSource source, WorldNavigationSnapshot world)
    {
        var frame = world.Vehicle is null ? source.Build(world, _ => "Mayor's Manor") : source.BuildFlight(world, _ => "Mayor's Manor");
        source.PublishDescriptions(frame);
        return frame;
    }
    private static WorldNavigationSource Source(List<string> spoken) => new(new EmptyMemory(), _ => { }, spoken.Add);
    private static WorldNavigationSnapshot Snapshot(bool flight = false) => new(new(1, 2, 3, 4, 0, 0x400, 336, 848),
        new byte[6144], new byte[512],
        [new(21, 21, 53, 1, 50, 0, 52, 28, true) { ContactPoints = [new(336, 848)] }],
        new(300 * 16, 800 * 16, 400 * 16, 900 * 16), null)
    {
        PorreSparkles = [new(336, 840)],
        Vehicle = flight ? new(1, 2, 3, 4, 0, VehicleKind.Epoch, 0xC70, 3, 336, 848, 0xE0) : null,
    };
    private sealed class EmptyMemory : IReadableMemory
    { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
