using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ChronoTriggerAccessibility.Mod.Runtime;

public sealed partial class GameWindowWaiter : IGameWindowWaiter
{
    private static readonly EnumWindowsCallback Callback = VisitWindow;
    private readonly TimeSpan timeout;
    private readonly TimeSpan pollInterval;

    public GameWindowWaiter(TimeSpan? timeout = null, TimeSpan? pollInterval = null)
    {
        this.timeout = timeout ?? TimeSpan.FromSeconds(30);
        this.pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(50);
        if (this.timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        if (this.pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        }
    }

    public nint WaitForSoleVisibleWindow(int processId, CancellationToken cancellationToken)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        var deadline = DateTime.UtcNow + timeout;
        var visibleCount = 0;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = Enumerate(processId);
            visibleCount = result.Count;
            if (visibleCount == 1)
            {
                return result.Window;
            }

            if (cancellationToken.WaitHandle.WaitOne(pollInterval))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        throw new TimeoutException(
            $"Timed out waiting for exactly one visible top-level window owned by process {processId}; found {visibleCount}.");
    }

    private static WindowEnumeration Enumerate(int processId)
    {
        var state = new WindowEnumeration(processId);
        var handle = GCHandle.Alloc(state);
        try
        {
            if (!EnumWindows(Callback, GCHandle.ToIntPtr(handle)))
            {
                if (state.Failure is not null)
                {
                    throw new InvalidOperationException("Window enumeration failed.", state.Failure);
                }

                var error = Marshal.GetLastPInvokeError();
                if (error != 0)
                {
                    throw new Win32Exception(error, "EnumWindows failed.");
                }
            }

            return state;
        }
        finally
        {
            handle.Free();
        }
    }

    private static bool VisitWindow(IntPtr window, IntPtr parameter)
    {
        try
        {
            var handle = GCHandle.FromIntPtr(parameter);
            var state = (WindowEnumeration)handle.Target!;
            _ = GetWindowThreadProcessId(window, out var ownerProcessId);
            if (ownerProcessId == state.ProcessId && IsWindowVisible(window))
            {
                state.Count++;
                state.Window = window;
            }

            return true;
        }
        catch (Exception exception)
        {
            var handle = GCHandle.FromIntPtr(parameter);
            if (handle.Target is WindowEnumeration state)
            {
                state.Failure = exception;
            }

            return false;
        }
    }

    private sealed class WindowEnumeration(int processId)
    {
        public int ProcessId { get; } = processId;
        public int Count { get; set; }
        public IntPtr Window { get; set; }
        public Exception? Failure { get; set; }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr window);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr window, out int processId);
}
