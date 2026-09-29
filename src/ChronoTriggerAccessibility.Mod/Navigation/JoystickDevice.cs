using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace ChronoTriggerAccessibility.Mod.Navigation;

public sealed record JoystickDevice(string Name, ushort Manufacturer, ushort Product, uint Buttons)
{
    // SDL's Windows DirectInput maps for DualShock 4 v1/v2 and DualSense. Steam
    // virtual Xbox devices retain the game's own layout. Do not identify by name.
    public bool RawSony => Manufacturer == 0x054C && Product is 0x05C4 or 0x09CC or 0x0CE6 && Buttons >= 12;

    public static unsafe JoystickDevice? ReadWindows(uint id)
    {
        Span<byte> caps = stackalloc byte[728]; // JOYCAPSW, including both fixed UTF-16 arrays
        caps.Clear();
        fixed (byte* pointer = caps)
            if (joyGetDevCapsW(id, (nint)pointer, (uint)caps.Length) != 0) return null;
        return FromCapabilities(caps);
    }

    public static JoystickDevice FromCapabilities(ReadOnlySpan<byte> caps)
    {
        if (caps.Length != 728) throw new ArgumentException("Expected JOYCAPSW.", nameof(caps));
        return new(Encoding.Unicode.GetString(caps.Slice(4, 64)).Split('\0')[0],
            BinaryPrimitives.ReadUInt16LittleEndian(caps), BinaryPrimitives.ReadUInt16LittleEndian(caps[2..]),
            BinaryPrimitives.ReadUInt32LittleEndian(caps[92..]));
    }

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint joyGetDevCapsW(nuint id, nint caps, uint size);
}
