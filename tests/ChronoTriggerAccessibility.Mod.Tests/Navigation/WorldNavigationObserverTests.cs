using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class WorldNavigationObserverTests
{
    [Fact]
    public void AreaSpeechPrecedesTheQueuedClueAndTheLatchWaitsForPublication()
    {
        var spoken = new List<string>(); var surviving = new List<string>();
        var source = new WorldNavigationSource(new EmptyMemory(), _ => { }, text => { spoken.Add(text); surviving.Add(text); });
        var observer = new WorldNavigationObserver(source, new(text => { surviving.Clear(); surviving.Add(text); }, _ => { }));
        var frame = source.Build(Snapshot(), _ => "Mayor's Manor");
        Assert.Empty(spoken);
        observer.World(frame);
        Assert.Equal(["World map.", "Sparkles rise above Mayor's Manor."], surviving);
        observer.World(source.Build(Snapshot(), _ => "Mayor's Manor"));
        Assert.Single(spoken);
    }

    [Fact]
    public void ReturningFromAFieldRearmsTheSameNativeWorldOwner()
    {
        var spoken = new List<string>(); var source = Source(spoken); var observer = Observer(source);
        var first = observer.World(source.Build(Snapshot(), _ => "Mayor's Manor"));
        observer.Field(first! with { Scene = "field:50", AreaName = "Mayor's Manor" });
        observer.World(source.Build(Snapshot(), _ => "Mayor's Manor"));
        Assert.Equal(2, spoken.Count);
    }

    [Fact]
    public void BoardingAndLandingInTheSameWorldDoNotRepeatTheClue()
    {
        var spoken = new List<string>(); var source = Source(spoken); var observer = Observer(source);
        observer.World(source.Build(Snapshot(), _ => "Mayor's Manor"));
        observer.World(source.BuildFlight(Snapshot(true), _ => "Mayor's Manor"));
        observer.World(source.Build(Snapshot(), _ => "Mayor's Manor"));
        Assert.Single(spoken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AVisibleParticleDoesNotRequireAnOffscreenEntranceContactOrInventATarget(bool flight)
    {
        var spoken = new List<string>(); var source = Source(spoken); var observer = Observer(source);
        var world = Snapshot(flight) with { Viewport = new(300 * 16, 800 * 16, 343 * 16, 844 * 16) };
        var frame = observer.World(flight ? source.BuildFlight(world, _ => "Mayor's Manor") : source.Build(world, _ => "Mayor's Manor"));
        Assert.Equal(["Sparkles rise above Mayor's Manor."], spoken);
        Assert.Equal(flight ? 1 : 0, frame!.Targets.Count);
    }

    [Fact]
    public void ADiscardedFrameCannotPublishOrLatchTheCue()
    {
        var spoken = new List<string>(); var source = Source(spoken); var observer = Observer(source);
        source.Build(Snapshot(), _ => "Mayor's Manor");
        observer.World(null);
        Assert.Empty(spoken);
        observer.World(source.Build(Snapshot(), _ => "Mayor's Manor"));
        Assert.Single(spoken);
    }

    internal static WorldNavigationSnapshot Snapshot(bool flight = false) => new(new(1, 2, 3, 4, 0, 0x400, 336, 848),
        new byte[6144], new byte[512], [new(21, 21, 53, 1, 50, 0, 52, 28, true) { ContactPoints = [new(336, 848)] }],
        new(300 * 16, 800 * 16, 400 * 16, 900 * 16), null)
    {
        PorreSparkles = [new(336, 840)],
        Vehicle = flight ? new(1, 2, 3, 4, 0, VehicleKind.Epoch, 0xC70, 3, 336, 848, 0xE0) : null,
    };
    private static WorldNavigationSource Source(List<string> spoken) => new(new EmptyMemory(), _ => { }, spoken.Add);
    private static WorldNavigationObserver Observer(WorldNavigationSource source) => new(source, new(_ => { }, _ => { }));
    private sealed class EmptyMemory : IReadableMemory
    { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
