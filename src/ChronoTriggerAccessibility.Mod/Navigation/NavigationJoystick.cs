namespace ChronoTriggerAccessibility.Mod.Navigation;

public sealed class NavigationJoystick(Func<uint, NavigationPadButtons, bool, bool, bool> filter,
    Func<uint, JoystickDevice?>? queryDevice = null, Action<string>? diagnostic = null)
{
    private readonly Dictionary<uint, JoystickDevice?> devices = [];

    public void Filter(uint deviceId, Span<uint> snapshot, uint result)
    {
        if (deviceId > 15) return;
        if (result != 0)
        {
            devices.Remove(deviceId);
            filter(deviceId, NavigationPadButtons.None, true, false);
            return;
        }
        // JOYINFOEX (JOY_RETURNALL), verified at the game's WINMM call site.
        if (snapshot.Length != 13 || snapshot[0] != 52 || snapshot[1] != 255)
            throw new InvalidOperationException("The native controller snapshot layout changed.");
        if (!devices.TryGetValue(deviceId, out var device))
        {
            device = queryDevice?.Invoke(deviceId);
            devices.Add(deviceId, device);
            diagnostic?.Invoke($"Navigation controller {deviceId}: name={device?.Name ?? "unknown"}; " +
                $"manufacturer=0x{device?.Manufacturer ?? 0:X4}; product=0x{device?.Product ?? 0:X4}; " +
                $"buttons={device?.Buttons ?? 0}; layout={(device?.RawSony == true ? "raw Sony" : "native Xbox") }.");
        }
        var sony = device?.RawSony == true;
        var raw = snapshot[8];
        var buttons = NavigationPadButtons.None;
        if ((raw & (sony ? 0x800u : 0x200u)) != 0) buttons |= NavigationPadButtons.RightStick;
        if ((raw & 0x10) != 0) buttons |= NavigationPadButtons.LeftShoulder;
        if ((raw & 0x20) != 0) buttons |= NavigationPadButtons.RightShoulder;
        if ((raw & (sony ? 2u : 1u)) != 0) buttons |= NavigationPadButtons.South;
        if ((raw & (sony ? 4u : 2u)) != 0) buttons |= NavigationPadButtons.East;
        if ((raw & (sony ? 1u : 4u)) != 0) buttons |= NavigationPadButtons.West;
        var pov = snapshot[10];
        if (pov is 0 or 4500 or 31500) buttons |= NavigationPadButtons.Up;
        if (pov is 13500 or 18000 or 22500) buttons |= NavigationPadButtons.Down;
        // GameController scales each axis by 2^-15 - 1 and uses a radial 0.2
        // dead zone in 18F3F0. Only X/Y generate game movement; other axes are ignored.
        var x = snapshot[2] / 32768.0 - 1;
        var y = snapshot[3] / 32768.0 - 1;
        var povActive = pov <= 31500 && pov % 4500 == 0;
        var neutral = raw == 0 && !povActive && x * x + y * y <= 0.2 * 0.2;
        if (!filter(deviceId, buttons, neutral, true)) return;
        for (var axis = 2; axis <= 7; axis++) snapshot[axis] = 32768;
        snapshot[8] = snapshot[9] = 0;
        snapshot[10] = 65535;
    }
}
