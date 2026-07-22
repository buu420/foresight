using System.Runtime.InteropServices;

namespace ChronoTriggerAccessibility.Mod.Diagnostics;

public interface IAccessibleFatalError
{
    void Show(string message);
}

public sealed partial class AccessibleFatalError : IAccessibleFatalError
{
    private const uint Ok = 0x00000000;
    private const uint IconError = 0x00000010;
    private const uint SetForeground = 0x00010000;
    private const uint TopMost = 0x00040000;

    public void Show(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _ = MessageBoxW(
            IntPtr.Zero,
            message,
            "Chrono Trigger Accessibility",
            Ok | IconError | SetForeground | TopMost);
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(IntPtr window, string text, string caption, uint type);
}
