using System.Reflection;
using System.Text.Json;
using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class PrisonSavePointCaptureTests
{
    [Fact]
    public void CapturedSavePointIsRoutableAndDoesNotDuplicateItsActors()
    {
        // Read-only capture, 2026-09-26 19:01:33, including the actual map planes,
        // actor processing flags, and live story state. No invented room geometry.
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "ChronoTriggerAccessibility.Mod.Tests.Navigation.prison-save-point-live-0334.json")!;
        var f = JsonSerializer.Deserialize<PrisonSkywalkTests.Capture>(stream)!;
        var frame = new FieldNavigationSource(new NoMemory(), _ => { }).Build(f.Field, f.Map,
            new(0, 0, 4096, 8192), [], f.Story);
        var save = Assert.Single(frame.Targets, t => t.Label == "Save point");
        Assert.Equal("save:4:13:10", save.Id);
        var route = NavigationPathfinder.Find(frame.Graph, frame.Player, save.ApproachPoints);
        Assert.NotNull(route);
        Assert.Equal((13, 10), (route[^1].X / 256, route[^1].Y / 256));
        Assert.DoesNotContain(frame.Targets, t => t.Id is "actor:3:4:121" or "landmark:4");
        Assert.Contains("Confirm", save.ArrivalInstruction);
    }
    private sealed class NoMemory : IReadableMemory { public bool TryRead(nuint address, Span<byte> destination) => false; }
}
