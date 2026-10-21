using System.Runtime.InteropServices;

namespace ChronoTriggerAccessibility.Mod.Diagnostics;

/// <summary>Process and system memory at a failed native texture allocation. Every value is in bytes.
/// Null means that value was not obtained; nothing is estimated. <see cref="Error"/> names each failed group.</summary>
/// <param name="WorkingSetBytes">The process working set (PROCESS_MEMORY_COUNTERS_EX.WorkingSetSize).</param>
/// <param name="PrivateBytes">The process commit charge (PROCESS_MEMORY_COUNTERS_EX.PrivateUsage).</param>
/// <param name="AvailablePhysicalBytes">Physical memory immediately reusable (MEMORYSTATUSEX.ullAvailPhys).</param>
/// <param name="AvailablePageFileBytes">The most this process can still commit (MEMORYSTATUSEX.ullAvailPageFile).</param>
/// <param name="LargestFreeRegionBytes">The largest MEM_FREE region inside this process's application address range.
/// Raw VirtualQuery sizes, not rounded to allocation granularity. Null unless the whole range was swept.</param>
/// <param name="TotalFreeRegionBytes">The sum of those MEM_FREE regions. Null unless the whole range was swept.</param>
public sealed record TextureMemorySnapshot(ulong? WorkingSetBytes, ulong? PrivateBytes, ulong? AvailablePhysicalBytes,
    ulong? AvailablePageFileBytes, ulong? LargestFreeRegionBytes, ulong? TotalFreeRegionBytes, string? Error);

/// <summary>One VirtualQuery result: the region's first address, its size in bytes and its MEM_* state.</summary>
public readonly record struct TextureMemoryRegion(ulong BaseAddress, ulong Size, uint State)
{
    /// <summary>MEM_FREE (winnt.h).</summary>
    public const uint Free = 0x10000;
}

/// <summary>The read-only queries behind <see cref="TextureMemoryCapture"/>. A failed call returns false and its
/// Win32 error code. <see cref="TextureMemoryCapture.Capture()"/> uses the native implementation.</summary>
public interface ITextureMemorySource
{
    bool TryProcessCounters(out ulong workingSetBytes, out ulong privateBytes, out int error);
    bool TrySystemCounters(out ulong availablePhysicalBytes, out ulong availablePageFileBytes, out int error);
    /// <summary>The lowest and highest (inclusive) addresses this process can query.</summary>
    bool TryApplicationRange(out ulong minimumAddress, out ulong maximumAddress, out int error);
    /// <summary>The region containing <paramref name="address"/>, as VirtualQuery reports it.</summary>
    bool TryQueryRegion(ulong address, out TextureMemoryRegion region, out int error);
}

/// <summary>Captures memory counters after RenderTexture::create returned null. It only reads: no allocation
/// through the game, no writes to process memory, no graphics API calls. It never throws.</summary>
public static class TextureMemoryCapture
{
    /// <summary>The sweep stops, without a free summary, after this many regions.</summary>
    public const int MaximumRegions = 262_144;

    private static readonly TextureMemorySnapshot SourceUnavailable = new(null, null, null, null, null, null, "memory source unavailable");
    private static readonly TextureMemorySnapshot CaptureFailed = new(null, null, null, null, null, null, "memory capture failed");

    public static TextureMemorySnapshot Capture() => Capture(NativeTextureMemorySource.Instance);

    public static TextureMemorySnapshot Capture(ITextureMemorySource source)
    {
        try
        {
            if (source is null) return SourceUnavailable;
            ulong? workingSet = null, privateBytes = null, physical = null, pageFile = null, largest = null, total = null;
            string? process = null, system = null, regions = null;
            try
            {
                if (source.TryProcessCounters(out var w, out var p, out var error)) (workingSet, privateBytes) = (w, p);
                else process = $"process counters: GetProcessMemoryInfo failed (error {error})";
            }
            catch (Exception exception) { process = "process counters: " + exception.GetType().Name; }
            try
            {
                if (source.TrySystemCounters(out var a, out var f, out var error)) (physical, pageFile) = (a, f);
                else system = $"system counters: GlobalMemoryStatusEx failed (error {error})";
            }
            catch (Exception exception) { system = "system counters: " + exception.GetType().Name; }
            try { regions = Sweep(source, out largest, out total); }
            catch (Exception exception)
            {
                (largest, total) = (null, null);
                regions = "free regions: " + exception.GetType().Name;
            }
            var failures = process is null && system is null && regions is null ? null :
                string.Join("; ", new[] { process, system, regions }.Where(part => part is not null));
            return new(workingSet, privateBytes, physical, pageFile, largest, total, failures);
        }
        catch (Exception) { return CaptureFailed; }
    }

    /// <summary>Walks the application range region by region. Returns null after reaching the maximum address;
    /// otherwise a short reason, with both outputs null so a partial sweep is never reported as complete.</summary>
    private static string? Sweep(ITextureMemorySource source, out ulong? largest, out ulong? total)
    {
        (largest, total) = (null, null);
        if (!source.TryApplicationRange(out var minimum, out var maximum, out var rangeError))
            return $"free regions: application range unavailable (error {rangeError})";
        if (maximum < minimum) return $"free regions: invalid application range 0x{minimum:X}-0x{maximum:X}";
        ulong address = minimum, largestFree = 0, totalFree = 0;
        for (var count = 0; count < MaximumRegions; count++)
        {
            if (!source.TryQueryRegion(address, out var region, out var error))
                return $"free regions: VirtualQuery failed at 0x{address:X} (error {error})";
            // The region must contain the address, so that the next address strictly advances.
            if (region.Size == 0 || region.BaseAddress > address || region.Size - 1 > ulong.MaxValue - region.BaseAddress)
                return $"free regions: invalid region at 0x{address:X}";
            var last = region.BaseAddress + (region.Size - 1);
            if (last < address) return $"free regions: invalid region at 0x{address:X}";
            if (region.State == TextureMemoryRegion.Free)
            {
                // Only the part inside [address, maximum]; at least one byte, at most the region size.
                var bytes = Math.Min(last, maximum) - address + 1;
                if (bytes > ulong.MaxValue - totalFree) return $"free regions: total overflow at 0x{address:X}";
                totalFree += bytes;
                if (bytes > largestFree) largestFree = bytes;
            }
            if (last >= maximum)
            {
                (largest, total) = (largestFree, totalFree);
                return null;
            }
            address = last + 1;
        }
        return $"free regions: scan stopped after {MaximumRegions} regions at 0x{address:X}";
    }
}

/// <summary>kernel32 queries of the calling process only.</summary>
internal sealed unsafe class NativeTextureMemorySource : ITextureMemorySource
{
    public static readonly NativeTextureMemorySource Instance = new();
    private const int InvalidParameter = 87, BadLength = 24;

    public bool TryProcessCounters(out ulong workingSetBytes, out ulong privateBytes, out int error)
    {
        (workingSetBytes, privateBytes) = (0, 0);
        var counters = new TextureMemoryNative.ProcessMemoryCountersEx { Cb = (uint)sizeof(TextureMemoryNative.ProcessMemoryCountersEx) };
        if (!TextureMemoryNative.GetProcessMemoryInfo(TextureMemoryNative.GetCurrentProcess(), &counters, counters.Cb))
        {
            error = Marshal.GetLastPInvokeError();
            return false;
        }
        (workingSetBytes, privateBytes, error) = (counters.WorkingSetSize, counters.PrivateUsage, 0);
        return true;
    }

    public bool TrySystemCounters(out ulong availablePhysicalBytes, out ulong availablePageFileBytes, out int error)
    {
        (availablePhysicalBytes, availablePageFileBytes) = (0, 0);
        var status = new TextureMemoryNative.MemoryStatusEx { Length = (uint)sizeof(TextureMemoryNative.MemoryStatusEx) };
        if (!TextureMemoryNative.GlobalMemoryStatusEx(&status))
        {
            error = Marshal.GetLastPInvokeError();
            return false;
        }
        (availablePhysicalBytes, availablePageFileBytes, error) = (status.AvailPhys, status.AvailPageFile, 0);
        return true;
    }

    // GetSystemInfo, not GetNativeSystemInfo: VirtualQuery can only address this process's own range, which
    // under WOW64 is the 32-bit range reported here. GetSystemInfo cannot fail.
    public bool TryApplicationRange(out ulong minimumAddress, out ulong maximumAddress, out int error)
    {
        TextureMemoryNative.SystemInfo info;
        TextureMemoryNative.GetSystemInfo(&info);
        (minimumAddress, maximumAddress, error) = ((nuint)info.MinimumApplicationAddress, (nuint)info.MaximumApplicationAddress, 0);
        return true;
    }

    public bool TryQueryRegion(ulong address, out TextureMemoryRegion region, out int error)
    {
        region = default;
        if (address > nuint.MaxValue)
        {
            error = InvalidParameter;
            return false;
        }
        TextureMemoryNative.MemoryBasicInformation info;
        var size = (nuint)sizeof(TextureMemoryNative.MemoryBasicInformation);
        var written = TextureMemoryNative.VirtualQuery((void*)(nuint)address, &info, size);
        if (written == 0)
        {
            error = Marshal.GetLastPInvokeError();
            return false;
        }
        if (written < size)
        {
            error = BadLength;
            return false;
        }
        (region, error) = (new((nuint)info.BaseAddress, info.RegionSize, info.State), 0);
        return true;
    }
}

/// <summary>Layouts from the Windows SDK headers (winnt.h, psapi.h, sysinfoapi.h). SIZE_T, PVOID and DWORD_PTR
/// follow the process pointer size, so each layout is correct for both x86 and x64.</summary>
internal static unsafe partial class TextureMemoryNative
{
    /// <summary>MEMORY_BASIC_INFORMATION: 28 bytes on x86, 48 on x64. The x64-only WORD PartitionId
    /// (#if defined(_WIN64)) lies in the alignment padding before RegionSize, so it is not declared.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryBasicInformation
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public uint AllocationProtect;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    /// <summary>PROCESS_MEMORY_COUNTERS_EX: two DWORDs, then nine SIZE_T members. 44 bytes on x86, 80 on x64.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessMemoryCountersEx
    {
        public uint Cb;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivateUsage;
    }

    /// <summary>MEMORYSTATUSEX: 64 bytes on both. dwLength must be set before the call.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    /// <summary>SYSTEM_INFO: 36 bytes on x86, 48 on x64. The leading DWORD union is read as its WORD pair.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemInfo
    {
        public ushort ProcessorArchitecture;
        public ushort Reserved;
        public uint PageSize;
        public nint MinimumApplicationAddress;
        public nint MaximumApplicationAddress;
        public nint ActiveProcessorMask;
        public uint NumberOfProcessors;
        public uint ProcessorType;
        public uint AllocationGranularity;
        public ushort ProcessorLevel;
        public ushort ProcessorRevision;
    }

    [LibraryImport("kernel32.dll")]
    internal static partial nint GetCurrentProcess();

    // psapi.h maps GetProcessMemoryInfo to K32GetProcessMemoryInfo in kernel32.dll (PSAPI_VERSION 2).
    [LibraryImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetProcessMemoryInfo(nint process, ProcessMemoryCountersEx* counters, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GlobalMemoryStatusEx(MemoryStatusEx* buffer);

    [LibraryImport("kernel32.dll")]
    internal static partial void GetSystemInfo(SystemInfo* info);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nuint VirtualQuery(void* address, MemoryBasicInformation* buffer, nuint length);
}
