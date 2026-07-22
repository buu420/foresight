using System.Buffers.Binary;
using System.Collections.ObjectModel;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

public sealed record DialogueLineSnapshot(int Index, string Text, uint Flags);

public sealed record DialogueChoicesSnapshot(
    int FirstLineIndex,
    IReadOnlyList<string> Labels,
    IReadOnlyList<uint> Flags,
    int SelectedIndex);

public sealed record DialogueSnapshot(
    nuint Window,
    int Cursor,
    int PageBase,
    int Phase,
    DialogueLineSnapshot? Line,
    DialogueChoicesSnapshot? Choices);

public static class DialogueCapture
{
    public const uint VtableRva = 0x3A0624;
    public const int ObjectSize = 0x340;
    public const int ActiveOffset = 0x2BD;
    public const int CurrentLineOffset = 0x2C0;
    public const int PageBaseOffset = 0x2C4;
    public const int PhaseOffset = 0x2CC;
    public const int SelectedChoiceOffset = 0x2D0;
    public const int ChoiceCountOffset = 0x2D4;
    public const int ParsedStringsOffset = 0x308;
    public const int FlagsOffset = 0x314;
    public const int MaximumVisibleLines = 4;
    public const int MaximumChoiceCount = 4;
    public const int MaximumFlagVectorByteLength = 1_024;

    private const uint KnownFlagMask = 0x1F;
    private const uint ChoiceFlag = 0x10;

    public static bool TryCreateSnapshot(
        IReadableMemory? memory,
        nuint imageBase,
        nuint window,
        out DialogueSnapshot snapshot,
        out string error)
    {
        try
        {
            return TryCreateSnapshotCore(memory, imageBase, window, out snapshot, out error);
        }
        catch (Exception exception)
        {
            snapshot = null!;
            error = $"Dialogue memory capture failed safely: {exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    private static bool TryCreateSnapshotCore(
        IReadableMemory? memory,
        nuint imageBase,
        nuint window,
        out DialogueSnapshot snapshot,
        out string error)
    {
        snapshot = null!;
        if (memory is null || imageBase == 0 || window == 0)
        {
            error = "Dialogue window memory, image base, or object address is unavailable.";
            return false;
        }
        if (!TryAddX86(imageBase, VtableRva, out var expectedVtable) ||
            !FitsX86Range(window, ObjectSize))
        {
            error = "Dialogue image base or MsgWindow object crosses the x86 address space.";
            return false;
        }

        Span<byte> vtableBytes = stackalloc byte[sizeof(uint)];
        if (!memory.TryRead(window, vtableBytes))
        {
            error = $"Dialogue MsgWindow vtable at 0x{window:X8} is unreadable.";
            return false;
        }
        var observedVtable = BinaryPrimitives.ReadUInt32LittleEndian(vtableBytes);
        if (observedVtable != expectedVtable)
        {
            error = $"Dialogue object vtable 0x{observedVtable:X8} does not match the audited MsgWindow vtable 0x{expectedVtable:X8}.";
            return false;
        }

        var objectBytes = new byte[ObjectSize];
        if (!memory.TryRead(window, objectBytes))
        {
            error = $"Dialogue MsgWindow object at 0x{window:X8} is unreadable.";
            return false;
        }
        if (BinaryPrimitives.ReadUInt32LittleEndian(objectBytes) != expectedVtable)
        {
            error = "Dialogue MsgWindow vtable changed while its object was being snapshotted.";
            return false;
        }

        var active = objectBytes[ActiveOffset];
        if (active != 1)
        {
            error = $"Dialogue MsgWindow active byte {active} is not the required active value 1.";
            return false;
        }

        var cursor = ReadInt32(objectBytes, CurrentLineOffset);
        var pageBase = ReadInt32(objectBytes, PageBaseOffset);
        var phase = ReadInt32(objectBytes, PhaseOffset);
        var selectedChoice = ReadInt32(objectBytes, SelectedChoiceOffset);
        var choiceCount = ReadInt32(objectBytes, ChoiceCountOffset);
        if (phase is < 0 or > 4)
        {
            error = $"Dialogue phase {phase} is outside the audited 0..4 range.";
            return false;
        }

        if (!TryAddX86(window, ParsedStringsOffset, out var stringsHeaderAddress))
        {
            error = "Dialogue parsed-string vector header address overflows x86 memory.";
            return false;
        }
        var capturedHeader = objectBytes.AsSpan(ParsedStringsOffset, MsvcStringVectorReader.HeaderSize).ToArray();
        var snapshotMemory = new CapturedHeaderMemory(memory, stringsHeaderAddress, capturedHeader);
        if (!new MsvcStringVectorReader(snapshotMemory).TryRead(stringsHeaderAddress, out var strings, out var stringsError))
        {
            error = $"Dialogue parsed-string vector is invalid: {stringsError}";
            return false;
        }
        if (!TryReadFlags(memory, objectBytes, strings.Count, out var flags, out error))
        {
            return false;
        }
        if (strings.Count == 0)
        {
            error = "Active Dialogue MsgWindow parsed-string vector is empty.";
            return false;
        }

        if (cursor < 0 || cursor > strings.Count)
        {
            error = $"Dialogue current line {cursor} is outside the parsed-string range 0..{strings.Count}.";
            return false;
        }
        if (pageBase < 0 || pageBase > cursor)
        {
            error = $"Dialogue page base {pageBase} is outside the committed current-line range 0..{cursor}.";
            return false;
        }
        if (cursor - pageBase > MaximumVisibleLines)
        {
            error = $"Dialogue current line is more than four visible rows beyond its page base.";
            return false;
        }

        DialogueLineSnapshot? line = null;
        DialogueChoicesSnapshot? choices = null;
        if (phase == 4)
        {
            if (choiceCount <= 0)
            {
                error = $"Dialogue phase 4 choice count {choiceCount} is not positive.";
                return false;
            }
            if (choiceCount > MaximumChoiceCount)
            {
                error = $"Dialogue choice count {choiceCount} exceeds the four visible-row safety limit.";
                return false;
            }
            if (choiceCount > cursor)
            {
                error = $"Dialogue choice range current-choiceCount ({cursor}-{choiceCount}) is negative.";
                return false;
            }
            if (selectedChoice < -1 || selectedChoice >= choiceCount)
            {
                error = $"Dialogue selected choice {selectedChoice} is outside -1..{choiceCount - 1}.";
                return false;
            }

            var firstChoice = cursor - choiceCount;
            if (firstChoice < pageBase)
            {
                error = $"Dialogue choice range begins at line {firstChoice}, before visible page base {pageBase}.";
                return false;
            }
            var capturedLabels = new string[choiceCount];
            var capturedFlags = new uint[choiceCount];
            for (var index = 0; index < choiceCount; index++)
            {
                var lineIndex = firstChoice + index;
                if ((flags[lineIndex] & ChoiceFlag) == 0)
                {
                    error = $"Dialogue line {lineIndex} in the visible choice range is missing the 0x10 choice flag.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(strings[lineIndex]))
                {
                    error = $"Dialogue choice line {lineIndex} is blank.";
                    return false;
                }
                capturedLabels[index] = new string(strings[lineIndex].AsSpan());
                capturedFlags[index] = flags[lineIndex];
            }
            choices = new DialogueChoicesSnapshot(
                firstChoice,
                new ReadOnlyCollection<string>(capturedLabels),
                new ReadOnlyCollection<uint>(capturedFlags),
                selectedChoice);
        }
        else if (phase is 0 or 2 && cursor < strings.Count)
        {
            if ((flags[cursor] & ChoiceFlag) != 0)
            {
                error = $"Dialogue ordinary phase {phase} current line {cursor} unexpectedly has the 0x10 choice flag.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(strings[cursor]))
            {
                error = $"Dialogue current line {cursor} is blank.";
                return false;
            }
            line = new DialogueLineSnapshot(cursor, new string(strings[cursor].AsSpan()), flags[cursor]);
        }

        snapshot = new DialogueSnapshot(window, cursor, pageBase, phase, line, choices);
        error = string.Empty;
        return true;
    }

    private static bool TryReadFlags(
        IReadableMemory memory,
        byte[] objectBytes,
        int stringCount,
        out IReadOnlyList<uint> flags,
        out string error)
    {
        flags = Array.Empty<uint>();
        var header = objectBytes.AsSpan(FlagsOffset, MsvcStringVectorReader.HeaderSize);
        var begin = BinaryPrimitives.ReadUInt32LittleEndian(header);
        var end = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        var capacity = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        if (end < begin)
        {
            error = "Dialogue flags vector end pointer is reversed before its begin pointer.";
            return false;
        }
        if (capacity < end)
        {
            error = "Dialogue flags vector capacity pointer is before its end pointer.";
            return false;
        }
        var span = end - begin;
        var capacitySpan = capacity - begin;
        if (span % sizeof(uint) != 0 || capacitySpan % sizeof(uint) != 0)
        {
            error = "Dialogue flags vector bounds are not aligned to the exact 4-byte stride.";
            return false;
        }
        if (span > MaximumFlagVectorByteLength || capacitySpan > MaximumFlagVectorByteLength)
        {
            error = $"Dialogue flags vector span or capacity exceeds the {MaximumFlagVectorByteLength}-byte safety limit.";
            return false;
        }
        var count = span / sizeof(uint);
        if (count != stringCount)
        {
            error = $"Dialogue parsed-string and flag vectors must have equal counts; observed {stringCount} and {count}.";
            return false;
        }
        if (span == 0)
        {
            error = string.Empty;
            return true;
        }
        if (begin == 0)
        {
            error = "Non-empty Dialogue flags vector has a null begin pointer.";
            return false;
        }

        var bytes = new byte[checked((int)span)];
        if (!memory.TryRead(begin, bytes))
        {
            error = $"Dialogue flags vector elements at 0x{begin:X8} are unreadable.";
            return false;
        }
        var captured = new uint[checked((int)count)];
        for (var index = 0; index < captured.Length; index++)
        {
            var value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index * sizeof(uint)));
            if ((value & ~KnownFlagMask) != 0)
            {
                error = $"Dialogue flag 0x{value:X8} at line {index} contains unaudited bits outside 0x1F.";
                return false;
            }
            captured[index] = value;
        }
        flags = new ReadOnlyCollection<uint>(captured);
        error = string.Empty;
        return true;
    }

    private static int ReadInt32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));

    private static bool FitsX86Range(nuint address, int byteLength) =>
        byteLength > 0 && address <= uint.MaxValue &&
        (ulong)address + (uint)byteLength - 1 <= uint.MaxValue;

    private static bool TryAddX86(nuint address, uint offset, out uint result)
    {
        var sum = (ulong)address + offset;
        if (address > uint.MaxValue || sum > uint.MaxValue)
        {
            result = 0;
            return false;
        }
        result = (uint)sum;
        return true;
    }

    private sealed class CapturedHeaderMemory(
        IReadableMemory inner,
        nuint headerAddress,
        byte[] header) : IReadableMemory
    {
        public bool TryRead(nuint address, Span<byte> destination)
        {
            if (address == headerAddress && destination.Length == header.Length)
            {
                header.CopyTo(destination);
                return true;
            }
            return inner.TryRead(address, destination);
        }
    }
}
