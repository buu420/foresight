using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

public static class SettingsCapture
{
    public const uint ConfigVtableRva = 0x3A702C;
    public const uint PagerVtableRva = 0x3AC940;
    public const int MaximumPageCount = 32;
    public const int MaximumRowCount = 64;

    private const int ConfigSize = 0x2FC;
    private const int DescriptorVectorOffset = 0x2C8;
    private const int PagerPointerOffset = 0x2D4;
    private const int ConfigActivePageOffset = 0x2E8;
    private const int ContextOffset = 0x2F8;
    private const int PagerSize = 0x2E2;
    private const int PagerPageCountOffset = 0x2D0;
    private const int PagerActivePageOffset = 0x2D4;
    private const int PagerActiveRootOffset = 0x2D8;
    private const int PagerTransitionOffset = 0x2E1;
    private const int ManagerKeyOffset = 0x2C4;
    private const int ManagerRequiredSize = ManagerKeyOffset + sizeof(int);
    private const int DescriptorStride = 0x0C;
    private const int RowStride = 0x98;
    private const int RowUiTypeOffset = 0x00;
    private const int RowLabelOffset = 0x04;
    private const int RowHelpOffset = 0x1C;
    private const int RowValuesOffset = 0x34;
    private const int RowSelectedIndexOffset = 0x90;

    public static bool TryCreateSnapshot(
        IReadableMemory? memory,
        nuint imageBase,
        nuint config,
        nuint manager,
        out SettingsSnapshot snapshot,
        out string diagnostic)
    {
        try
        {
            return TryCreateSnapshotCore(memory, imageBase, config, manager, out snapshot, out diagnostic);
        }
        catch (Exception exception)
        {
            snapshot = null!;
            diagnostic = $"Settings memory capture failed safely: {exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    private static bool TryCreateSnapshotCore(
        IReadableMemory? memory,
        nuint imageBase,
        nuint config,
        nuint manager,
        out SettingsSnapshot snapshot,
        out string diagnostic)
    {
        snapshot = null!;
        if (memory is null || !FitsX86Address(imageBase) ||
            !FitsX86Range(config, ConfigSize) || !FitsX86Range(manager, ManagerRequiredSize))
        {
            diagnostic = "Settings memory, image base, config, or input manager is unavailable or outside x86 memory.";
            return false;
        }
        if (!TryAddX86(imageBase, ConfigVtableRva, out var expectedConfigVtable) ||
            !TryAddX86(imageBase, PagerVtableRva, out var expectedPagerVtable))
        {
            diagnostic = "Settings vtable resolution crosses the x86 address space.";
            return false;
        }
        if (!TryReadUInt32(memory, config, out var observedConfigVtable) || observedConfigVtable != expectedConfigVtable)
        {
            diagnostic = "Settings config vtable is unreadable or does not match MenuNodeConfigSteam.";
            return false;
        }

        var configBytes = new byte[ConfigSize];
        if (!memory.TryRead(config, configBytes))
        {
            diagnostic = $"Settings config at 0x{config:X8} is unreadable.";
            return false;
        }
        if (ReadUInt32(configBytes, 0) != expectedConfigVtable)
        {
            diagnostic = "Settings config vtable changed while its header was being captured.";
            return false;
        }

        var contextValue = ReadInt32(configBytes, ContextOffset);
        if (contextValue is not 0 and not 1)
        {
            diagnostic = $"Settings context {contextValue} is outside the audited in-game/title values 0 and 1.";
            return false;
        }
        var context = (SettingsContext)contextValue;

        var pager = ReadUInt32(configBytes, PagerPointerOffset);
        if (!FitsX86Range(pager, PagerSize))
        {
            diagnostic = "Settings ConfigPager pointer is null or crosses the x86 address space.";
            return false;
        }
        var pagerBytes = new byte[PagerSize];
        if (!memory.TryRead(pager, pagerBytes))
        {
            diagnostic = $"Settings ConfigPager at 0x{pager:X8} is unreadable.";
            return false;
        }
        if (ReadUInt32(pagerBytes, 0) != expectedPagerVtable)
        {
            diagnostic = "Settings ConfigPager vtable does not match the audited pager type.";
            return false;
        }

        var pageCount = ReadInt32(pagerBytes, PagerPageCountOffset);
        var pagerActivePage = ReadInt32(pagerBytes, PagerActivePageOffset);
        var configActivePage = ReadInt32(configBytes, ConfigActivePageOffset);
        var activeRoot = ReadUInt32(pagerBytes, PagerActiveRootOffset);
        var transition = pagerBytes[PagerTransitionOffset];
        if (pageCount <= 0 || pageCount > MaximumPageCount)
        {
            diagnostic = $"Settings page count {pageCount} is outside the defensive 1..{MaximumPageCount} range.";
            return false;
        }
        if (pagerActivePage < 0 || pagerActivePage >= pageCount || configActivePage != pagerActivePage)
        {
            diagnostic = "Settings config and pager do not identify one valid matching active page.";
            return false;
        }
        if (!FitsX86Address(activeRoot))
        {
            diagnostic = "Settings active page root is null or outside x86 memory.";
            return false;
        }
        if (transition != 0)
        {
            diagnostic = $"Settings pager transition byte {transition} is nonzero.";
            return false;
        }

        if (!TryReadVectorBounds(
                configBytes.AsSpan(DescriptorVectorOffset, MsvcStringVectorReader.HeaderSize),
                DescriptorStride,
                MaximumPageCount,
                "Settings descriptor",
                out var descriptorBegin,
                out var descriptorCount,
                out diagnostic))
        {
            return false;
        }
        if (descriptorCount != pageCount)
        {
            diagnostic = $"Settings descriptor count {descriptorCount} does not match pager page count {pageCount}.";
            return false;
        }
        if (!TryAddX86(descriptorBegin, checked((uint)(pagerActivePage * DescriptorStride)), out var activeDescriptor) ||
            !FitsX86Range(activeDescriptor, DescriptorStride))
        {
            diagnostic = "Settings active descriptor address crosses the x86 address space.";
            return false;
        }

        Span<byte> descriptorBytes = stackalloc byte[DescriptorStride];
        if (!memory.TryRead(activeDescriptor, descriptorBytes))
        {
            diagnostic = $"Settings active descriptor at 0x{activeDescriptor:X8} is unreadable.";
            return false;
        }
        if (!TryReadVectorBounds(
                descriptorBytes,
                RowStride,
                MaximumRowCount,
                "Settings row",
                out var rowBegin,
                out var rowCount,
                out diagnostic))
        {
            return false;
        }
        if (rowCount == 0)
        {
            diagnostic = "Settings active descriptor contains no rows.";
            return false;
        }

        if (!TryAddX86(manager, ManagerKeyOffset, out var managerKeyAddress))
        {
            diagnostic = "Settings input-manager key address crosses x86 memory.";
            return false;
        }
        Span<byte> managerKeyBytes = stackalloc byte[sizeof(int)];
        if (!memory.TryRead(managerKeyAddress, managerKeyBytes))
        {
            diagnostic = $"Settings input-manager key at 0x{managerKeyAddress:X8} is unreadable.";
            return false;
        }
        var nativeKey = BinaryPrimitives.ReadInt32LittleEndian(managerKeyBytes);
        if (nativeKey < 0 || nativeKey >= 1000)
        {
            diagnostic = $"Settings input-manager key {nativeKey} is negative, sentinel, or reserved.";
            return false;
        }
        var rowIndex = nativeKey / 4;
        var subcontrol = nativeKey % 4;
        if (rowIndex < 0 || rowIndex >= rowCount)
        {
            diagnostic = $"Settings key {nativeKey} decodes to row {rowIndex}, outside {rowCount} rows.";
            return false;
        }
        if (!TryAddX86(rowBegin, checked((uint)(rowIndex * RowStride)), out var rowAddress) ||
            !FitsX86Range(rowAddress, RowStride))
        {
            diagnostic = "Settings selected row address crosses the x86 address space.";
            return false;
        }

        var rowBytes = new byte[RowStride];
        if (!memory.TryRead(rowAddress, rowBytes))
        {
            diagnostic = $"Settings selected row at 0x{rowAddress:X8} is unreadable.";
            return false;
        }
        var uiType = ReadInt32(rowBytes, RowUiTypeOffset);
        if (uiType is < 0 or > 2)
        {
            diagnostic = $"Settings row UI type {uiType} is outside the audited 0..2 range.";
            return false;
        }

        var capturedRowMemory = new CapturedRangeMemory(memory, rowAddress, rowBytes);
        var stringReader = new MsvcStringReader(capturedRowMemory);
        if (!TryAddX86(rowAddress, RowLabelOffset, out var labelAddress))
        {
            diagnostic = "Settings row label address crosses x86 memory.";
            return false;
        }
        if (!stringReader.TryRead(labelAddress, out var label, out var labelError))
        {
            diagnostic = $"Settings row label is invalid: {labelError}";
            return false;
        }
        if (!TryAddX86(rowAddress, RowHelpOffset, out var helpAddress))
        {
            diagnostic = "Settings row help address crosses x86 memory.";
            return false;
        }
        if (!stringReader.TryRead(helpAddress, out var help, out var helpError))
        {
            diagnostic = $"Settings row help is invalid: {helpError}";
            return false;
        }
        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(help))
        {
            diagnostic = "Settings row requires nonblank localized label and help text.";
            return false;
        }

        if (!TryAddX86(rowAddress, RowValuesOffset, out var valuesAddress))
        {
            diagnostic = "Settings row value-vector address crosses x86 memory.";
            return false;
        }
        if (!new MsvcStringVectorReader(capturedRowMemory).TryRead(valuesAddress, out var values, out var valuesError))
        {
            diagnostic = $"Settings row value vector is invalid: {valuesError}";
            return false;
        }
        if (values.Count == 0 || !values.Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            diagnostic = "Settings row requires at least one nonblank localized displayed value.";
            return false;
        }

        var selectedIndex = ReadInt32(rowBytes, RowSelectedIndexOffset);
        if (selectedIndex < 0 || selectedIndex >= values.Count)
        {
            diagnostic = $"Settings selected value index {selectedIndex} is outside {values.Count} values.";
            return false;
        }
        var expectedPrimaryKey = checked(rowIndex * 4 + 1);
        if (uiType == 0 && (values.Count != 2 || nativeKey != checked(expectedPrimaryKey + selectedIndex)))
        {
            diagnostic = "Settings type-0 row requires exactly two values and a key matching its displayed selection.";
            return false;
        }
        if (uiType is 1 or 2 && (subcontrol != 1 || nativeKey != expectedPrimaryKey))
        {
            diagnostic = "Settings type-1/type-2 row requires the primary row subcontrol key.";
            return false;
        }
        if (uiType == 2 && values.Count != 2)
        {
            diagnostic = "Settings type-2 row requires exactly two displayed values.";
            return false;
        }

        if (!TryReadUInt32(memory, config, out var finalConfigVtable) || finalConfigVtable != expectedConfigVtable)
        {
            diagnostic = "Settings config vtable changed while dependent state was being captured.";
            return false;
        }

        var control = new MenuControlSnapshot(
            new string(label.AsSpan()),
            new string(values[selectedIndex].AsSpan()),
            new string(help.AsSpan()),
            nativeKey,
            rowIndex + 1,
            rowCount,
            Enabled: true,
            Visible: true);
        snapshot = new SettingsSnapshot(
            context,
            pagerActivePage,
            pageCount,
            uiType,
            nativeKey,
            selectedIndex,
            control);
        diagnostic = string.Empty;
        return true;
    }

    private static bool TryReadVectorBounds(
        ReadOnlySpan<byte> header,
        int stride,
        int maximumCount,
        string name,
        out uint begin,
        out int count,
        out string diagnostic)
    {
        begin = BinaryPrimitives.ReadUInt32LittleEndian(header);
        var end = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        var capacity = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        count = 0;
        if (begin == 0 || end < begin || capacity < end)
        {
            diagnostic = $"{name} vector pointers are null, reversed, or exceed capacity.";
            return false;
        }
        if ((begin & 3) != 0 || (end & 3) != 0 || (capacity & 3) != 0)
        {
            diagnostic = $"{name} vector pointers are not naturally aligned x86 addresses.";
            return false;
        }
        var span = end - begin;
        var capacitySpan = capacity - begin;
        if (span % (uint)stride != 0 || capacitySpan % (uint)stride != 0)
        {
            diagnostic = $"{name} vector bounds are not aligned to the exact 0x{stride:X}-byte stride.";
            return false;
        }
        var elementCount = span / (uint)stride;
        var capacityCount = capacitySpan / (uint)stride;
        if (elementCount > maximumCount || capacityCount > maximumCount)
        {
            diagnostic = $"{name} vector count or capacity exceeds the defensive {maximumCount}-element limit.";
            return false;
        }
        if (!FitsX86Range(begin, checked((int)span)))
        {
            diagnostic = $"{name} vector range crosses x86 memory.";
            return false;
        }
        count = checked((int)elementCount);
        diagnostic = string.Empty;
        return true;
    }

    private static bool TryReadUInt32(IReadableMemory memory, nuint address, out uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        if (!memory.TryRead(address, bytes))
        {
            value = 0;
            return false;
        }
        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        return true;
    }

    private static int ReadInt32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));

    private static uint ReadUInt32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    private static bool FitsX86Address(nuint address) => address != 0 && address <= uint.MaxValue;

    private static bool FitsX86Range(nuint address, int byteLength) =>
        address != 0 && byteLength > 0 && address <= uint.MaxValue &&
        (ulong)address + (uint)byteLength - 1 <= uint.MaxValue;

    private static bool TryAddX86(nuint address, uint offset, out uint result)
    {
        var sum = (ulong)address + offset;
        if (!FitsX86Address(address) || sum > uint.MaxValue)
        {
            result = 0;
            return false;
        }
        result = (uint)sum;
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
