using System.Buffers.Binary;
using System.Text;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Tests.Support;

internal sealed class NavigationMemory : IReadableMemory
{
    private readonly Dictionary<nuint, byte> bytes = [];
    private nuint heap = 0x900000;
    public Action<nuint>? BeforeRead { get; set; }
    public NavigationMemory Add(nuint address, ReadOnlySpan<byte> data)
    {
        for (var i = 0; i < data.Length; i++) bytes[address + (nuint)i] = data[i];
        return this;
    }
    public NavigationMemory Byte(nuint address, byte value) => Add(address, [value]);
    public NavigationMemory Word(nuint address, uint value)
    {
        Span<byte> data = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(data, value);
        return Add(address, data);
    }
    public NavigationMemory Short(nuint address, ushort value)
    {
        Span<byte> data = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(data, value);
        return Add(address, data);
    }
    public NavigationMemory String(nuint address, string value)
    {
        var data = Encoding.UTF8.GetBytes(value); var layout = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(layout.AsSpan(16), data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(layout.AsSpan(20), Math.Max(15, data.Length));
        if (data.Length <= 15) data.CopyTo(layout, 0);
        else { BinaryPrimitives.WriteUInt32LittleEndian(layout, (uint)heap); Add(heap, data); heap += 8192; }
        return Add(address, layout);
    }
    public bool TryRead(nuint address, Span<byte> destination)
    {
        BeforeRead?.Invoke(address);
        for (var i = 0; i < destination.Length; i++)
        {
            if (!bytes.TryGetValue(address + (nuint)i, out var value)) return false;
            destination[i] = value;
        }
        return true;
    }
}
