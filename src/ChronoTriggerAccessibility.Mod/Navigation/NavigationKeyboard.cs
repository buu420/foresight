using System.Runtime.InteropServices;
using ChronoTriggerAccessibility.Core.Navigation;

namespace ChronoTriggerAccessibility.Mod.Navigation;

public sealed class NavigationKeyboard(Func<int, bool> isDown, Func<bool> isForeground)
{
    private static readonly (int Key, NavigationCommand Command)[] Bindings =
    [
        ('U', NavigationCommand.PreviousCategory), ('O', NavigationCommand.NextCategory),
        ('J', NavigationCommand.PreviousTarget), ('L', NavigationCommand.NextTarget),
        ('K', NavigationCommand.Repeat), ('I', NavigationCommand.Guide), ('P', NavigationCommand.ToggleWalk),
    ];
    private int previous;
    private bool armed;

    public NavigationKeyboard() : this(key => (GetAsyncKeyState(key) & 0x8000) != 0, IsGameForeground) { }

    public IReadOnlyList<NavigationCommand> Poll()
    {
        var current = 0;
        for (var i = 0; i < Bindings.Length; i++) if (isDown(Bindings[i].Key)) current |= 1 << i;
        var pressed = current & ~previous;
        previous = current;
        var wasArmed = armed;
        armed = true;
        if (!wasArmed || !isForeground() || isDown(0x10) || isDown(0x11) || isDown(0x12) ||
            isDown(0x5B) || isDown(0x5C)) return [];
        var commands = new List<NavigationCommand>();
        for (var i = 0; i < Bindings.Length; i++) if ((pressed & (1 << i)) != 0) commands.Add(Bindings[i].Command);
        return commands.AsReadOnly();
    }

    public void Suspend() => armed = false;

    public static bool IsGameForeground()
    {
        var window = GetForegroundWindow();
        return window != 0 && GetWindowThreadProcessId(window, out var process) != 0 && process == Environment.ProcessId;
    }

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
