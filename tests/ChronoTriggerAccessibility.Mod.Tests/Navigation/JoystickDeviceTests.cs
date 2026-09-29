using System.Buffers.Binary;
using System.Text;
using ChronoTriggerAccessibility.Mod.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class JoystickDeviceTests
{
    [Fact]
    public void WinmmCapabilityOffsetsIdentifyOnlyRecognizedRawSonyDevices()
    {
        var bytes = new byte[728];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, 0x054C);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 0x0CE6);
        Encoding.Unicode.GetBytes("Wireless Controller\0").CopyTo(bytes, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(92), 14);
        var device = JoystickDevice.FromCapabilities(bytes);
        Assert.Equal("Wireless Controller", device.Name);
        Assert.Equal(14u, device.Buttons);
        Assert.True(device.RawSony);
        Assert.False((device with { Manufacturer = 0x28DE, Product = 0x11FF }).RawSony);
        Assert.False((device with { Buttons = 10 }).RawSony);
        Assert.False((device with { Product = 0xFFFF }).RawSony);
        Assert.False((device with { Manufacturer = 0x045E }).RawSony);
    }
}
