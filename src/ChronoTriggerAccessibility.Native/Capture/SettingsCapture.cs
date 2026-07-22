using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

internal readonly record struct SettingsVector(uint Begin, int Count, int ByteLength);

internal sealed record SettingsMemoryGuard(nuint Address, byte[] Expected, string Name);

internal static class SettingsCaptureMemory
{
    internal const int VectorHeaderSize = 0x0C;

    internal static bool FitsX86Address(nuint address) =>
        address != 0 && address <= uint.MaxValue;

    internal static bool FitsX86Range(nuint address, int byteLength) =>
        address != 0 && byteLength > 0 && address <= uint.MaxValue &&
        (ulong)address + (uint)byteLength - 1 <= uint.MaxValue;

    internal static bool TryAddX86(nuint address, uint offset, out nuint result)
    {
        var sum = (ulong)address + offset;
        if (!FitsX86Address(address) || sum > uint.MaxValue)
        {
            result = 0;
            return false;
        }

        result = (nuint)sum;
        return true;
    }

    internal static bool TryElementAddress(
        nuint begin,
        int index,
        int stride,
        out nuint address)
    {
        if (index < 0 || stride <= 0)
        {
            address = 0;
            return false;
        }

        var offset = (ulong)(uint)index * (uint)stride;
        var sum = (ulong)begin + offset;
        if (!FitsX86Address(begin) || sum > uint.MaxValue)
        {
            address = 0;
            return false;
        }

        address = (nuint)sum;
        return true;
    }

    internal static bool TryResolveVtable(
        nuint imageBase,
        uint rva,
        out uint expected,
        out string diagnostic)
    {
        var sum = (ulong)imageBase + rva;
        if (!FitsX86Address(imageBase) || sum > uint.MaxValue)
        {
            expected = 0;
            diagnostic = "Settings image base or expected vtable crosses the x86 address space.";
            return false;
        }

        expected = (uint)sum;
        diagnostic = string.Empty;
        return true;
    }

    internal static bool TryReadBytes(
        IReadableMemory memory,
        nuint address,
        int length,
        string name,
        out byte[] bytes,
        out string diagnostic)
    {
        bytes = [];
        if (!FitsX86Range(address, length))
        {
            diagnostic = $"{name} range crosses the x86 address space.";
            return false;
        }

        bytes = new byte[length];
        if (!memory.TryRead(address, bytes))
        {
            bytes = [];
            diagnostic = $"{name} at 0x{address:X8} is unreadable.";
            return false;
        }

        diagnostic = string.Empty;
        return true;
    }

    internal static bool TryReadUInt32(
        IReadableMemory memory,
        nuint address,
        string name,
        out uint value,
        out string diagnostic)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        if (!FitsX86Range(address, bytes.Length) || !memory.TryRead(address, bytes))
        {
            value = 0;
            diagnostic = $"{name} at 0x{address:X8} is unreadable or crosses x86 memory.";
            return false;
        }

        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        diagnostic = string.Empty;
        return true;
    }

    internal static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);

    internal static int ReadInt32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);

    internal static bool TryReadVector(
        ReadOnlySpan<byte> header,
        int stride,
        int maximumCount,
        bool allowEmpty,
        string name,
        out SettingsVector vector,
        out string diagnostic)
    {
        vector = default;
        if (header.Length < VectorHeaderSize || stride <= 0 || maximumCount < 0)
        {
            diagnostic = $"{name} vector header or capture limits are invalid.";
            return false;
        }

        var begin = BinaryPrimitives.ReadUInt32LittleEndian(header);
        var end = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        var capacity = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        if (begin == 0)
        {
            if (end != 0 || capacity != 0)
            {
                diagnostic = $"{name} vector has a null begin with non-null end or capacity.";
                return false;
            }
            if (!allowEmpty)
            {
                diagnostic = $"{name} vector is empty.";
                return false;
            }

            diagnostic = string.Empty;
            return true;
        }
        if ((begin & 3) != 0 || (end & 3) != 0 || (capacity & 3) != 0)
        {
            diagnostic = $"{name} vector pointers are not naturally aligned x86 addresses.";
            return false;
        }
        if (end < begin || capacity < end)
        {
            diagnostic = $"{name} vector pointers are reversed or exceed capacity.";
            return false;
        }

        var span = end - begin;
        var capacitySpan = capacity - begin;
        if (span % (uint)stride != 0 || capacitySpan % (uint)stride != 0)
        {
            diagnostic = $"{name} vector bounds are not aligned to the exact 0x{stride:X}-byte stride.";
            return false;
        }

        var count = span / (uint)stride;
        var capacityCount = capacitySpan / (uint)stride;
        if (count > maximumCount || capacityCount > maximumCount)
        {
            diagnostic = $"{name} vector count or capacity exceeds the defensive {maximumCount}-element limit.";
            return false;
        }
        if (count == 0 && !allowEmpty)
        {
            diagnostic = $"{name} vector is empty.";
            return false;
        }
        if (span > int.MaxValue || (span != 0 && !FitsX86Range(begin, checked((int)span))))
        {
            diagnostic = $"{name} vector range crosses the x86 address space.";
            return false;
        }

        vector = new SettingsVector(begin, checked((int)count), checked((int)span));
        diagnostic = string.Empty;
        return true;
    }

    internal static bool TryReadString(
        IReadableMemory memory,
        nuint address,
        ReadOnlySpan<byte> capturedLayout,
        string name,
        out string value,
        out string diagnostic)
    {
        value = string.Empty;
        if (capturedLayout.Length < MsvcStringReader.LayoutSize || !FitsX86Range(address, MsvcStringReader.LayoutSize))
        {
            diagnostic = $"{name} string layout crosses x86 memory.";
            return false;
        }

        var captured = capturedLayout[..MsvcStringReader.LayoutSize].ToArray();
        var capturedMemory = new CapturedRangeMemory(memory, address, captured);
        if (!new MsvcStringReader(capturedMemory).TryRead(address, out value, out var error))
        {
            diagnostic = $"{name} is invalid: {error}";
            return false;
        }
        if (string.IsNullOrWhiteSpace(value))
        {
            diagnostic = $"{name} is blank.";
            return false;
        }

        value = new string(value.AsSpan());
        diagnostic = string.Empty;
        return true;
    }

    internal static bool TryReadStringVector(
        IReadableMemory memory,
        nuint headerAddress,
        ReadOnlySpan<byte> capturedHeader,
        bool allowEmpty,
        string name,
        ICollection<SettingsMemoryGuard> guards,
        out IReadOnlyList<string> values,
        out SettingsVector vector,
        out string diagnostic)
    {
        values = Array.Empty<string>();
        if (!TryReadVector(
                capturedHeader,
                MsvcStringReader.LayoutSize,
                checked((int)MsvcStringVectorReader.MaximumElementCount),
                allowEmpty,
                name,
                out vector,
                out diagnostic))
        {
            return false;
        }

        var headerCopy = capturedHeader[..VectorHeaderSize].ToArray();
        guards.Add(new SettingsMemoryGuard(headerAddress, headerCopy, $"{name} header"));
        if (vector.Count == 0)
        {
            diagnostic = string.Empty;
            return true;
        }
        if (!TryReadBytes(memory, vector.Begin, vector.ByteLength, $"{name} string layouts", out var layouts, out diagnostic))
        {
            return false;
        }

        var capturedMemory = new CapturedRangeMemory(
            new CapturedRangeMemory(memory, headerAddress, headerCopy),
            vector.Begin,
            layouts);
        if (!new MsvcStringVectorReader(capturedMemory).TryRead(headerAddress, out values, out var error))
        {
            diagnostic = $"{name} is invalid: {error}";
            return false;
        }
        if (values.Any(string.IsNullOrWhiteSpace))
        {
            diagnostic = $"{name} contains blank localized text.";
            return false;
        }

        values = values.Select(value => new string(value.AsSpan())).ToArray();
        guards.Add(new SettingsMemoryGuard(vector.Begin, layouts, $"{name} string layouts"));
        diagnostic = string.Empty;
        return true;
    }

    internal static bool TryRevalidate(
        IReadableMemory memory,
        IEnumerable<SettingsMemoryGuard> guards,
        out string diagnostic)
    {
        foreach (var guard in guards)
        {
            if (!TryReadBytes(memory, guard.Address, guard.Expected.Length, guard.Name, out var observed, out diagnostic))
            {
                return false;
            }
            if (!observed.AsSpan().SequenceEqual(guard.Expected))
            {
                diagnostic = $"{guard.Name} changed while dependent Settings state was captured.";
                return false;
            }
        }

        diagnostic = string.Empty;
        return true;
    }

    private sealed class CapturedRangeMemory(
        IReadableMemory inner,
        nuint capturedAddress,
        byte[] capturedBytes) : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination)
        {
            if (address >= capturedAddress)
            {
                var offset = address - capturedAddress;
                if (offset <= int.MaxValue &&
                    (ulong)offset + (ulong)destination.Length <= (ulong)capturedBytes.Length)
                {
                    capturedBytes.AsSpan((int)offset, destination.Length).CopyTo(destination);
                    return true;
                }
            }

            return inner.TryRead(address, destination);
        }
    }
}
