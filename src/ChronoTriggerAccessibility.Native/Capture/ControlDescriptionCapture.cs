using System.Buffers.Binary;
using System.Collections.ObjectModel;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

public sealed record ControlDescriptionRecord(
    int MessageId,
    string Text,
    int X,
    int Y,
    int Anchor,
    IReadOnlyList<ControlDescriptionSprite> AssociatedSprites);

public sealed record ControlDescriptionSprite(string Name, int X, int Y);

public sealed record ControlDescriptionTextObservation(int MessageId, string Text);

public sealed record ControlDescriptionSnapshot(
    string Title,
    string NextLabel,
    nuint Manager,
    int ManagerFocusKey,
    IReadOnlyList<ControlDescriptionRecord> Records,
    IReadOnlyList<ControlDescriptionSprite> Sprites);

public static class ControlDescriptionCapture
{
    public const uint RecordTableRva = 0x39B408;
    public const uint NormalSpriteTableRva = 0x39B598;
    public const uint AlternateSpriteTableRva = 0x39B638;

    private static readonly (int MessageId, int X, int Y, int Anchor)[] NativeRecords =
    [
        (0x0E, 185, -53, 2), (0x02, 190, -53, 0),
        (0x0E, 185, -72, 2), (0x03, 190, -72, 0),
        (0x0E, 185, -91, 2), (0x04, 190, -91, 0),
        (0x0E, 185, -110, 2), (0x05, 190, -110, 0),
        (0x0E, 185, -129, 2), (0x06, 190, -129, 0), (0x0F, 124, -129, 1),
        (0x07, 190, -148, 0),
        (0x0E, 185, -167, 2), (0x08, 190, -167, 0), (0x10, 124, -167, 1),
        (0x0E, 185, -186, 2), (0x12, 190, -186, 0),
        (0x0E, 185, -205, 2), (0x09, 190, -205, 0),
        (0x0E, 185, -224, 2), (0x0A, 190, -224, 0),
        (0x0E, 185, -243, 2), (0x0B, 190, -243, 0),
        (0x0E, 185, -262, 2), (0x0C, 190, -262, 0),
    ];

    private static readonly (string InternalName, string AccessibleName, int X, int Y)[] NormalSprites =
    [
        ("BtnA", "A button", 100, -53), ("BtnB", "B button", 100, -72),
        ("BtnY", "Y button", 100, -91), ("BtnX", "X button", 100, -110),
        ("BtnL", "L button", 100, -129), ("BtnR", "R button", 132, -129),
        ("BtnL", "L button", 100, -167), ("BtnR", "R button", 132, -167),
        ("Start", "Start", 100, -186), ("Up", "Up", 100, -205),
        ("Left", "Left", 100, -224), ("Right", "Right", 100, -243),
        ("Down", "Down", 100, -262),
    ];

    private static readonly uint[] NormalSpriteNameRvas =
    [
        0x3B1E54, 0x3B1E60, 0x3B1E78, 0x3B1E6C,
        0x3B1E90, 0x3B1E84, 0x3B1E90, 0x3B1E84,
        0x3B1EA4, 0x3B1E9C, 0x3B1EBC, 0x3B1EB0, 0x3B1ED4,
    ];

    public static IReadOnlyList<int> RequiredMessageIds { get; } =
        new ReadOnlyCollection<int>(NativeRecords.Select(record => record.MessageId).ToArray());

    public static bool TryValidateRuntimeTables(
        IReadableMemory? memory,
        nuint imageBase,
        int artworkSelector,
        out string error)
    {
        try
        {
            if (memory is null || imageBase == 0)
            {
                error = "Control Descriptions runtime-table memory or image base is unavailable.";
                return false;
            }
            if (artworkSelector is not 0 and not 1)
            {
                error = $"Controller artwork selector {artworkSelector} is outside the audited 0/1 range.";
                return false;
            }
            for (var index = 0; index < NativeRecords.Length; index++)
            {
                var address = imageBase + RecordTableRva + checked((nuint)(index * 16));
                if (!TryReadInt32(memory, address, out var messageId) ||
                    !TryReadInt32(memory, address + 4, out var x) ||
                    !TryReadInt32(memory, address + 8, out var y) ||
                    !TryReadInt32(memory, address + 12, out var anchor))
                {
                    error = $"Control Descriptions runtime record {index} is unreadable.";
                    return false;
                }
                var expected = NativeRecords[index];
                if ((messageId, x, y, anchor) != expected)
                {
                    error = $"Control Descriptions runtime record {index} does not match the audited message/geometry/anchor table.";
                    return false;
                }
            }

            var tableRva = artworkSelector == 0 ? NormalSpriteTableRva : AlternateSpriteTableRva;
            for (var index = 0; index < NormalSprites.Length; index++)
            {
                var address = imageBase + tableRva + checked((nuint)(index * 12));
                if (!TryReadPointer(memory, address, out var spriteName) ||
                    !TryReadInt32(memory, address + 4, out var x) ||
                    !TryReadInt32(memory, address + 8, out var y))
                {
                    error = $"Control Descriptions selected sprite record {index} is unreadable.";
                    return false;
                }
                var nameIndex = artworkSelector == 1 && index < 2 ? 1 - index : index;
                var expectedName = imageBase + NormalSpriteNameRvas[nameIndex];
                var expected = NormalSprites[index];
                if (spriteName != expectedName || x != expected.X || y != expected.Y)
                {
                    error = $"Control Descriptions selected sprite record {index} does not match the audited type/position table.";
                    return false;
                }
            }
            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            error = $"Control Descriptions runtime-table validation failed safely: {exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    public static bool TryCreateSnapshot(
        string title,
        string nextLabel,
        IReadOnlyList<ControlDescriptionTextObservation>? localizedRecords,
        int artworkSelector,
        nuint manager,
        int managerFocusKey,
        out ControlDescriptionSnapshot snapshot,
        out string error)
    {
        snapshot = null!;
        if (string.IsNullOrWhiteSpace(title))
        {
            error = "The localized Control Descriptions title is blank.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(nextLabel))
        {
            error = "The localized Next control label is blank.";
            return false;
        }

        if (artworkSelector is not 0 and not 1)
        {
            error = $"Controller artwork selector {artworkSelector} is outside the audited 0/1 range.";
            return false;
        }

        if (manager == 0)
        {
            error = "The Control Descriptions input manager is null.";
            return false;
        }

        if (managerFocusKey != 0)
        {
            error = $"The Control Descriptions initial focus key is {managerFocusKey}; expected audited Next key 0.";
            return false;
        }

        if (localizedRecords is null || localizedRecords.Count < NativeRecords.Length)
        {
            error = $"Localized Control Descriptions records are missing; expected exactly {NativeRecords.Length}.";
            return false;
        }

        if (localizedRecords.Count > NativeRecords.Length)
        {
            error = $"A localized Control Descriptions record was observed more than once; expected exactly {NativeRecords.Length}.";
            return false;
        }

        for (var index = 0; index < NativeRecords.Length; index++)
        {
            var expected = NativeRecords[index];
            var observed = localizedRecords[index];
            if (observed is null)
            {
                error = $"Localized Control Descriptions record {index} is null.";
                return false;
            }
            if (observed.MessageId != expected.MessageId)
            {
                error = $"Localized Control Descriptions record {index} has message 0x{observed.MessageId:X}; expected 0x{expected.MessageId:X} in native table order.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(observed.Text))
            {
                error = $"Localized Control Descriptions record {index} (message 0x{observed.MessageId:X}) is blank.";
                return false;
            }
        }

        var sprites = NormalSprites
            .Select((sprite, index) => new ControlDescriptionSprite(
                artworkSelector == 1 && index == 0
                    ? "B button"
                    : artworkSelector == 1 && index == 1
                        ? "A button"
                        : sprite.AccessibleName,
                sprite.X,
                sprite.Y))
            .ToArray();
        var records = NativeRecords
            .Select((record, index) => new ControlDescriptionRecord(
                record.MessageId,
                new string(localizedRecords[index].Text.AsSpan()),
                record.X,
                record.Y,
                record.Anchor,
                new ReadOnlyCollection<ControlDescriptionSprite>(
                    sprites.Where(sprite => sprite.Y == record.Y).ToArray())))
            .ToArray();

        snapshot = new ControlDescriptionSnapshot(
            new string(title.AsSpan()),
            new string(nextLabel.AsSpan()),
            manager,
            managerFocusKey,
            new ReadOnlyCollection<ControlDescriptionRecord>(records),
            new ReadOnlyCollection<ControlDescriptionSprite>(sprites));
        error = string.Empty;
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
}
