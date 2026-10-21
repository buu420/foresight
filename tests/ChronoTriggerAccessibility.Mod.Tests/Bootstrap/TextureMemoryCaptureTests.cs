using System.Runtime.InteropServices;
using ChronoTriggerAccessibility.Mod.Diagnostics;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Bootstrap;

public sealed class TextureMemoryCaptureTests
{
    private const uint Commit = 0x1000, Reserve = 0x2000, Free = 0x10000;

    [Fact]
    public void ACompleteSweepReportsTheLargestAndTotalFreeBytesOfTheApplicationRange()
    {
        var source = new SequenceSource(0x10000, 0x8FFFF,
            new(0x10000, 0x10000, Free), new(0x20000, 0x20000, Commit), new(0x40000, 0x30000, Free), new(0x70000, 0x20000, Reserve));

        var snapshot = TextureMemoryCapture.Capture(source);

        Assert.Equal(new TextureMemorySnapshot(1000, 2000, 3000, 4000, 0x30000, 0x40000, null), snapshot);
        Assert.Equal([0x10000ul, 0x20000, 0x40000, 0x70000], source.Queried);
    }

    [Fact]
    public void FreeBytesOutsideTheApplicationRangeAreNotCounted()
    {
        // The first region begins below the minimum address; the last free region runs past the maximum.
        var source = new SequenceSource(0x10000, 0x8FFFF,
            new(0x0, 0x20000, Free), new(0x20000, 0x60000, Commit), new(0x80000, 0x100000, Free));

        var snapshot = TextureMemoryCapture.Capture(source);

        Assert.Equal(0x10000ul, snapshot.LargestFreeRegionBytes);
        Assert.Equal(0x20000ul, snapshot.TotalFreeRegionBytes);
        Assert.Null(snapshot.Error);
    }

    [Fact]
    public void AFailedRegionQueryWithholdsTheFreeSummaryAndKeepsEveryOtherCounter()
    {
        var source = new SequenceSource(0x10000, 0x8FFFF, new(0x10000, 0x10000, Free), new(0x20000, 0x20000, Commit))
            { RegionError = 5 };

        var snapshot = TextureMemoryCapture.Capture(source);

        Assert.Equal(new TextureMemorySnapshot(1000, 2000, 3000, 4000, null, null,
            "free regions: VirtualQuery failed at 0x40000 (error 5)"), snapshot);
    }

    [Theory]
    [InlineData(0x40000ul, 0ul)]                 // empty region: the sweep cannot advance
    [InlineData(0x50000ul, 0x10000ul)]           // starts after the queried address
    [InlineData(0x20000ul, 0x10000ul)]           // ends before the queried address: the sweep would go backwards
    [InlineData(0x40000ul, ulong.MaxValue)]      // end of region overflows the address type
    public void ARegionThatCannotAdvanceTheSweepWithholdsTheFreeSummary(ulong regionBase, ulong regionSize)
    {
        var source = new SequenceSource(0x10000, 0x8FFFF, new(0x10000, 0x30000, Free), new(regionBase, regionSize, Free));

        var snapshot = TextureMemoryCapture.Capture(source);

        Assert.Equal(new TextureMemorySnapshot(1000, 2000, 3000, 4000, null, null, "free regions: invalid region at 0x40000"),
            snapshot);
    }

    [Fact]
    public void ASweepBeyondTheRegionBoundIsReportedAsStoppedNotComplete()
    {
        var source = new EndlessSource();

        var snapshot = TextureMemoryCapture.Capture(source);

        Assert.Null(snapshot.LargestFreeRegionBytes);
        Assert.Null(snapshot.TotalFreeRegionBytes);
        var stoppedAt = 0x10000ul + (ulong)TextureMemoryCapture.MaximumRegions * 0x1000;
        Assert.Equal($"free regions: scan stopped after {TextureMemoryCapture.MaximumRegions} regions at 0x{stoppedAt:X}",
            snapshot.Error);
        Assert.Equal(TextureMemoryCapture.MaximumRegions, source.Queries);
    }

    [Fact]
    public void AnUnavailableOrInvertedApplicationRangeIsReportedInsteadOfGuessed()
    {
        var unavailable = TextureMemoryCapture.Capture(new SequenceSource(0x10000, 0x8FFFF) { RangeError = 87 });
        var inverted = TextureMemoryCapture.Capture(new SequenceSource(0x90000, 0x10000, new TextureMemoryRegion(0x10000, 0x10000, Free)));

        Assert.Equal(new TextureMemorySnapshot(1000, 2000, 3000, 4000, null, null,
            "free regions: application range unavailable (error 87)"), unavailable);
        Assert.Equal(new TextureMemorySnapshot(1000, 2000, 3000, 4000, null, null,
            "free regions: invalid application range 0x90000-0x10000"), inverted);
    }

    [Fact]
    public void EachCounterGroupFailsIndependentlyWithItsOwnShortError()
    {
        var regions = new TextureMemoryRegion[] { new(0x10000, 0x80000, Free) };
        var process = TextureMemoryCapture.Capture(new SequenceSource(0x10000, 0x8FFFF, regions) { ProcessError = 6 });
        var system = TextureMemoryCapture.Capture(new SequenceSource(0x10000, 0x8FFFF, regions) { SystemError = 8 });
        var all = TextureMemoryCapture.Capture(new SequenceSource(0x10000, 0x8FFFF) { ProcessError = 6, SystemError = 8, RegionError = 87 });

        Assert.Equal(new TextureMemorySnapshot(null, null, 3000, 4000, 0x80000, 0x80000,
            "process counters: GetProcessMemoryInfo failed (error 6)"), process);
        Assert.Equal(new TextureMemorySnapshot(1000, 2000, null, null, 0x80000, 0x80000,
            "system counters: GlobalMemoryStatusEx failed (error 8)"), system);
        Assert.Equal(new TextureMemorySnapshot(null, null, null, null, null, null,
            "process counters: GetProcessMemoryInfo failed (error 6); system counters: GlobalMemoryStatusEx failed (error 8); " +
            "free regions: VirtualQuery failed at 0x10000 (error 87)"), all);
    }

    [Fact]
    public void NoSourceFailureEscapesCapture()
    {
        var thrown = TextureMemoryCapture.Capture(new ThrowingSource());
        var missing = TextureMemoryCapture.Capture(null!);

        Assert.Equal(new TextureMemorySnapshot(null, null, null, null, null, null,
            "process counters: InvalidOperationException; system counters: InvalidOperationException; " +
            "free regions: InvalidOperationException"), thrown);
        Assert.Equal(new TextureMemorySnapshot(null, null, null, null, null, null, "memory source unavailable"), missing);
    }

    [Fact]
    public void NativeLayoutsMatchTheWindowsSdkForThisProcessBitness()
    {
        // winnt.h, psapi.h and sysinfoapi.h (SDK 10.0.26100.0): PartitionId exists only under _WIN64 and
        // sits in alignment padding there; SIZE_T, PVOID and DWORD_PTR follow the process pointer size.
        var x64 = IntPtr.Size == 8;
        AssertLayout("MemoryBasicInformation", x64 ? 48 : 28, ("BaseAddress", 0), ("RegionSize", x64 ? 24 : 12), ("State", x64 ? 32 : 16));
        AssertLayout("ProcessMemoryCountersEx", x64 ? 80 : 44, ("WorkingSetSize", x64 ? 16 : 12), ("PrivateUsage", x64 ? 72 : 40));
        AssertLayout("MemoryStatusEx", 64, ("AvailPhys", 16), ("AvailPageFile", 32));
        AssertLayout("SystemInfo", x64 ? 48 : 36, ("MinimumApplicationAddress", 8), ("MaximumApplicationAddress", x64 ? 16 : 12));
    }

    [Fact]
    public void TheNativeCaptureReadsThisProcessAndSystemWithoutErrors()
    {
        var snapshot = TextureMemoryCapture.Capture();

        Assert.Null(snapshot.Error);
        Assert.True(snapshot.WorkingSetBytes > 0);
        Assert.True(snapshot.PrivateBytes > 0);
        Assert.True(snapshot.AvailablePhysicalBytes > 0);
        Assert.NotNull(snapshot.AvailablePageFileBytes);
        Assert.True(snapshot.LargestFreeRegionBytes > 0);
        Assert.True(snapshot.TotalFreeRegionBytes >= snapshot.LargestFreeRegionBytes);
        // Free space cannot exceed this process's user address space (2 or 4 GB for x86, 128 TB for x64).
        Assert.True(snapshot.TotalFreeRegionBytes <= (IntPtr.Size == 8 ? 0x8000_0000_0000ul : 0x1_0000_0000ul));
    }

    private static void AssertLayout(string name, int size, params (string Field, int Offset)[] fields)
    {
        var type = typeof(TextureMemoryCapture).Assembly.GetType("ChronoTriggerAccessibility.Mod.Diagnostics.TextureMemoryNative+" + name);
        Assert.True(type is not null, $"Missing native layout {name}.");
        Assert.Equal(size, Marshal.SizeOf(type));
        foreach (var (field, offset) in fields) Assert.Equal(offset, (int)Marshal.OffsetOf(type, field));
    }

    /// <summary>Returns one region per query, in order, as consecutive VirtualQuery calls would.</summary>
    private sealed class SequenceSource(ulong minimum, ulong maximum, params TextureMemoryRegion[] regions) : ITextureMemorySource
    {
        public int ProcessError { get; init; }
        public int SystemError { get; init; }
        public int RangeError { get; init; }
        public int RegionError { get; init; } = 87;
        public List<ulong> Queried { get; } = [];

        public bool TryProcessCounters(out ulong workingSetBytes, out ulong privateBytes, out int error)
        {
            (workingSetBytes, privateBytes, error) = (1000, 2000, ProcessError);
            return ProcessError == 0;
        }

        public bool TrySystemCounters(out ulong availablePhysicalBytes, out ulong availablePageFileBytes, out int error)
        {
            (availablePhysicalBytes, availablePageFileBytes, error) = (3000, 4000, SystemError);
            return SystemError == 0;
        }

        public bool TryApplicationRange(out ulong minimumAddress, out ulong maximumAddress, out int error)
        {
            (minimumAddress, maximumAddress, error) = (minimum, maximum, RangeError);
            return RangeError == 0;
        }

        public bool TryQueryRegion(ulong address, out TextureMemoryRegion region, out int error)
        {
            Queried.Add(address);
            var index = Queried.Count - 1;
            region = index < regions.Length ? regions[index] : default;
            error = index < regions.Length ? 0 : RegionError;
            return index < regions.Length;
        }
    }

    /// <summary>An address space of endless one-page committed regions.</summary>
    private sealed class EndlessSource : ITextureMemorySource
    {
        public int Queries { get; private set; }

        public bool TryProcessCounters(out ulong workingSetBytes, out ulong privateBytes, out int error)
        {
            (workingSetBytes, privateBytes, error) = (1, 1, 0);
            return true;
        }

        public bool TrySystemCounters(out ulong availablePhysicalBytes, out ulong availablePageFileBytes, out int error)
        {
            (availablePhysicalBytes, availablePageFileBytes, error) = (1, 1, 0);
            return true;
        }

        public bool TryApplicationRange(out ulong minimumAddress, out ulong maximumAddress, out int error)
        {
            (minimumAddress, maximumAddress, error) = (0x10000, ulong.MaxValue - 1, 0);
            return true;
        }

        public bool TryQueryRegion(ulong address, out TextureMemoryRegion region, out int error)
        {
            Queries++;
            (region, error) = (new(address, 0x1000, Commit), 0);
            return true;
        }
    }

    private sealed class ThrowingSource : ITextureMemorySource
    {
        public bool TryProcessCounters(out ulong workingSetBytes, out ulong privateBytes, out int error) =>
            throw new InvalidOperationException("process");

        public bool TrySystemCounters(out ulong availablePhysicalBytes, out ulong availablePageFileBytes, out int error) =>
            throw new InvalidOperationException("system");

        public bool TryApplicationRange(out ulong minimumAddress, out ulong maximumAddress, out int error) =>
            throw new InvalidOperationException("range");

        public bool TryQueryRegion(ulong address, out TextureMemoryRegion region, out int error) =>
            throw new InvalidOperationException("region");
    }
}
