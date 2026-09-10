using System.Buffers.Binary;
using System.Collections.ObjectModel;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

public readonly record struct LocalizedMessageKey(int Bank, int MessageId);

public sealed class ModeSelectTextContract : IEquatable<ModeSelectTextContract>
{
    public ModeSelectTextContract(
        LocalizedMessageKey label,
        IEnumerable<LocalizedMessageKey> values,
        IEnumerable<LocalizedMessageKey> help)
    {
        Label = label;
        Values = new ReadOnlyCollection<LocalizedMessageKey>(values.ToArray());
        Help = new ReadOnlyCollection<LocalizedMessageKey>(help.ToArray());
    }

    public LocalizedMessageKey Label { get; }
    public IReadOnlyList<LocalizedMessageKey> Values { get; }
    public IReadOnlyList<LocalizedMessageKey> Help { get; }

    public bool Equals(ModeSelectTextContract? other) =>
        other is not null &&
        Label == other.Label &&
        Values.SequenceEqual(other.Values) &&
        Help.SequenceEqual(other.Help);

    public override bool Equals(object? obj) => Equals(obj as ModeSelectTextContract);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Label);
        foreach (var item in Values)
        {
            hash.Add(item);
        }
        foreach (var item in Help)
        {
            hash.Add(item);
        }
        return hash.ToHashCode();
    }
}

public sealed record ModeSelectLocalizedRow(
    nuint RecordAddress,
    string Label,
    IReadOnlyList<string> Values,
    IReadOnlyList<string> Help,
    int CurrentValueIndex);

public sealed record ModeSelectRowSnapshot(
    int Index,
    nuint RecordAddress,
    string Label,
    IReadOnlyList<string> Values,
    IReadOnlyList<string> Help,
    int CurrentValueIndex)
{
    public string CurrentValue => Values[CurrentValueIndex];
    public string CurrentHelp => Help[CurrentValueIndex];
}

public enum ModeSelectSubfocus
{
    Left,
    Right,
    Start,
}

public sealed record ModeSelectFocus(
    int CompositeKey,
    int RowIndex,
    ModeSelectSubfocus Subfocus,
    bool IsStart);

public sealed record ModeSelectSnapshot(
    nuint RecordsBegin,
    nuint RecordsEnd,
    nuint RecordsCapacity,
    IReadOnlyList<ModeSelectRowSnapshot> Rows,
    string StartLabel,
    int CompositeFocus,
    ModeSelectFocus Focus);

public sealed record ModeSelectRuntimeRowLayout(
    int Index,
    nuint RecordAddress,
    nuint ValuesBegin,
    nuint ValuesEnd,
    nuint ValuesCapacity,
    nuint GetterTarget);

public sealed record ModeSelectRuntimeLayout(
    nuint RecordsBegin,
    nuint RecordsEnd,
    nuint RecordsCapacity,
    int CompositeFocus,
    ModeSelectFocus Focus,
    IReadOnlyList<ModeSelectRuntimeRowLayout> Rows);

public static class ModeSelectCapture
{
    public const uint RecordsBeginOffset = 0x290;
    public const uint RecordsEndOffset = 0x294;
    public const uint RecordsCapacityOffset = 0x298;
    public const uint CompositeFocusOffset = 0x29C;
    public const uint ValuesBeginOffset = 0x24;
    public const uint ValuesEndOffset = 0x28;
    public const uint ValuesCapacityOffset = 0x2C;
    public const uint CurrentValueGetterTargetOffset = 0x54;
    public const uint RecordStride = 0x88;
    public const int RowCount = 3;

    public static IReadOnlyList<ModeSelectTextContract> TextContracts { get; } =
        new ReadOnlyCollection<ModeSelectTextContract>(
        [
            // The displayed alternatives are in the record's +0x24 vector; +0x18 supplies help.
            new(new(0x23, 0x5A), [new(0x3F, 0x05), new(0x3F, 0x06)], [new(0x23, 0xC0), new(0x23, 0xC1)]),
            new(new(0x3F, 0x31), [new(0x3F, 0x32), new(0x3F, 0x33)], [new(0x23, 0xC7), new(0x23, 0xC6)]),
            new(new(0x42, 0x1A), [new(0x42, 0x1C), new(0x42, 0x1D)], [new(0x41, 0x55), new(0x41, 0x54)]),
        ]);

    public static LocalizedMessageKey StartTextKey { get; } = new(0x23, 0xD7);

    public static bool TryValidateRuntimeLayout(
        IReadableMemory? memory,
        nuint scene,
        out ModeSelectRuntimeLayout layout,
        out string error)
    {
        try
        {
            return TryValidateRuntimeLayoutCore(memory, scene, out layout, out error);
        }
        catch (Exception exception)
        {
            layout = null!;
            error = $"Mode Select runtime-layout preflight failed safely: {exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    private static bool TryValidateRuntimeLayoutCore(
        IReadableMemory? memory,
        nuint scene,
        out ModeSelectRuntimeLayout layout,
        out string error)
    {
        layout = null!;
        if (memory is null || scene == 0)
        {
            error = "Mode Select scene memory is unavailable.";
            return false;
        }

        if (!TryReadPointer(memory, scene + RecordsBeginOffset, out var begin) ||
            !TryReadPointer(memory, scene + RecordsEndOffset, out var end) ||
            !TryReadPointer(memory, scene + RecordsCapacityOffset, out var capacity))
        {
            error = "Mode Select vector begin/end/capacity is unreadable.";
            return false;
        }

        var expectedSpan = checked((nuint)(RowCount * RecordStride));
        if (begin == 0 || end < begin || end - begin != expectedSpan)
        {
            error = $"Mode Select vector must contain exactly three 0x88-byte records (0x{expectedSpan:X} bytes).";
            return false;
        }
        if (capacity < end || (capacity - begin) % RecordStride != 0)
        {
            error = "Mode Select vector capacity is before the end pointer or not aligned to the 0x88-byte record stride.";
            return false;
        }
        if (!TryReadInt32(memory, scene + CompositeFocusOffset, out var compositeFocus) ||
            !TryDecodeFocus(compositeFocus, out var focus))
        {
            error = "Mode Select composite focus is unreadable or outside 0, 1, 10, 11, 20, 21, and 30.";
            return false;
        }

        var rows = new ModeSelectRuntimeRowLayout[RowCount];
        for (var index = 0; index < rows.Length; index++)
        {
            var record = begin + checked((nuint)(index * RecordStride));
            if (!TryReadPointer(memory, record + ValuesBeginOffset, out var valuesBegin) ||
                !TryReadPointer(memory, record + ValuesEndOffset, out var valuesEnd) ||
                !TryReadPointer(memory, record + ValuesCapacityOffset, out var valuesCapacity) ||
                valuesBegin == 0 ||
                valuesEnd < valuesBegin ||
                valuesEnd - valuesBegin != 2 * MsvcStringReader.LayoutSize ||
                valuesCapacity < valuesEnd ||
                (valuesCapacity - valuesBegin) % MsvcStringReader.LayoutSize != 0)
            {
                error = $"Mode Select row {index} value vector bounds are invalid or capacity is not aligned to the 0x18-byte string stride.";
                return false;
            }
            if (!TryReadPointer(memory, record + CurrentValueGetterTargetOffset, out var getterTarget) ||
                getterTarget == 0)
            {
                error = $"Mode Select row {index} current-value getter target is null or unreadable.";
                return false;
            }
            rows[index] = new ModeSelectRuntimeRowLayout(
                index, record, valuesBegin, valuesEnd, valuesCapacity, getterTarget);
        }

        layout = new ModeSelectRuntimeLayout(
            begin,
            end,
            capacity,
            compositeFocus,
            focus,
            new ReadOnlyCollection<ModeSelectRuntimeRowLayout>(rows));
        error = string.Empty;
        return true;
    }

    public static bool TryCreateSnapshot(
        IReadableMemory? memory,
        nuint scene,
        IReadOnlyList<ModeSelectLocalizedRow>? localizedRows,
        string startLabel,
        out ModeSelectSnapshot snapshot,
        out string error)
    {
        try
        {
            return TryCreateSnapshotCore(
                memory, scene, localizedRows, startLabel,
                out snapshot, out error);
        }
        catch (Exception exception)
        {
            snapshot = null!;
            error = $"Mode Select memory capture failed safely: {exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    private static bool TryCreateSnapshotCore(
        IReadableMemory? memory,
        nuint scene,
        IReadOnlyList<ModeSelectLocalizedRow>? localizedRows,
        string startLabel,
        out ModeSelectSnapshot snapshot,
        out string error)
    {
        snapshot = null!;
        if (memory is null || scene == 0)
        {
            error = "Mode Select scene memory is unavailable.";
            return false;
        }

        if (!TryReadPointer(memory, scene + RecordsBeginOffset, out var begin) ||
            !TryReadPointer(memory, scene + RecordsEndOffset, out var end) ||
            !TryReadPointer(memory, scene + RecordsCapacityOffset, out var capacity))
        {
            error = "Mode Select vector begin/end/capacity is unreadable.";
            return false;
        }

        var expectedSpan = checked((nuint)(RowCount * RecordStride));
        if (begin == 0 || end < begin || end - begin != expectedSpan)
        {
            error = $"Mode Select vector must contain exactly three 0x88-byte records (0x{expectedSpan:X} bytes).";
            return false;
        }

        if (capacity < end || (capacity - begin) % RecordStride != 0)
        {
            error = "Mode Select vector capacity is before the end pointer or not aligned to the 0x88-byte record stride.";
            return false;
        }

        if (localizedRows is null || localizedRows.Count != RowCount)
        {
            error = "Mode Select localized row capture must contain exactly three records.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(startLabel))
        {
            error = "Mode Select localized Start label is blank.";
            return false;
        }

        var captured = new ModeSelectRowSnapshot[RowCount];
        for (var index = 0; index < RowCount; index++)
        {
            var recordAddress = begin + checked((nuint)(index * RecordStride));
            var observed = localizedRows[index];
            if (observed is null)
            {
                error = $"Mode Select localized row {index} is null.";
                return false;
            }
            if (observed.RecordAddress != recordAddress)
            {
                error = $"Mode Select localized record ordering is invalid at row {index}.";
                return false;
            }

            if (observed.Values is null || observed.Help is null)
            {
                error = $"Mode Select localized row {index} contains a null value/help list.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(observed.Label) ||
                observed.Values.Count != 2 ||
                observed.Help.Count != 2 ||
                observed.Values.Any(string.IsNullOrWhiteSpace) ||
                observed.Help.Any(string.IsNullOrWhiteSpace))
            {
                error = $"Mode Select row {index} has a blank label/value/help or does not contain both audited alternatives.";
                return false;
            }

            if ((uint)observed.CurrentValueIndex >= observed.Values.Count)
            {
                error = $"Mode Select row {index} current value index {observed.CurrentValueIndex} is outside its value vector.";
                return false;
            }

            if (!TryReadPointer(memory, recordAddress + ValuesBeginOffset, out var valuesBegin) ||
                !TryReadPointer(memory, recordAddress + ValuesEndOffset, out var valuesEnd) ||
                !TryReadPointer(memory, recordAddress + ValuesCapacityOffset, out var valuesCapacity) ||
                valuesBegin == 0 ||
                valuesEnd < valuesBegin ||
                valuesEnd - valuesBegin != (nuint)(observed.Values.Count * MsvcStringReader.LayoutSize) ||
                valuesCapacity < valuesEnd ||
                (valuesCapacity - valuesBegin) % MsvcStringReader.LayoutSize != 0)
            {
                error = $"Mode Select row {index} value vector bounds are invalid or capacity is not aligned to the 0x18-byte string stride.";
                return false;
            }

            if (!TryReadPointer(memory, recordAddress + CurrentValueGetterTargetOffset, out var getter) ||
                getter == 0)
            {
                error = $"Mode Select row {index} current-value getter target is null or unreadable.";
                return false;
            }

            captured[index] = new ModeSelectRowSnapshot(
                index,
                recordAddress,
                new string(observed.Label.AsSpan()),
                new ReadOnlyCollection<string>(observed.Values.Select(value => new string(value.AsSpan())).ToArray()),
                new ReadOnlyCollection<string>(observed.Help.Select(value => new string(value.AsSpan())).ToArray()),
                observed.CurrentValueIndex);
        }

        if (!TryReadInt32(memory, scene + CompositeFocusOffset, out var compositeFocus) ||
            !TryDecodeFocus(compositeFocus, out var focus))
        {
            error = "Mode Select composite focus is unreadable or outside 0, 1, 10, 11, 20, 21, and 30.";
            return false;
        }

        snapshot = new ModeSelectSnapshot(
            begin,
            end,
            capacity,
            new ReadOnlyCollection<ModeSelectRowSnapshot>(captured),
            new string(startLabel.AsSpan()),
            compositeFocus,
            focus);
        error = string.Empty;
        return true;
    }

    private static bool TryDecodeFocus(int key, out ModeSelectFocus focus)
    {
        if (key == RowCount * 10)
        {
            focus = new ModeSelectFocus(key, RowCount, ModeSelectSubfocus.Start, IsStart: true);
            return true;
        }

        var row = key / 10;
        var remainder = key % 10;
        if (key < 0 || row >= RowCount || remainder is not 0 and not 1)
        {
            focus = null!;
            return false;
        }

        focus = new ModeSelectFocus(
            key,
            row,
            remainder == 0 ? ModeSelectSubfocus.Left : ModeSelectSubfocus.Right,
            IsStart: false);
        return true;
    }

    private static bool TryReadPointer(IReadableMemory memory, nuint address, out nuint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        if (!memory.TryRead(address, bytes))
        {
            value = 0;
            return false;
        }
        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        return true;
    }

    private static bool TryReadInt32(IReadableMemory memory, nuint address, out int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        if (!memory.TryRead(address, bytes))
        {
            value = 0;
            return false;
        }
        value = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        return true;
    }
}
