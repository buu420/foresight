using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class ControlDescriptionCaptureTests
{
    private const nuint ImageBase = 0x400000;
    private static readonly (int Id, int X, int Y, int Anchor)[] ExpectedRecords =
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

    [Fact]
    public void CapturesAllTwentyFiveAuditedLocalizedRecordsInNativeReadingOrder()
    {
        var localized = ControlDescriptionCapture.RequiredMessageIds
            .Select(messageId => new ControlDescriptionTextObservation(
                messageId,
                $"localized-{messageId:X}"))
            .ToList();

        Assert.True(
            ControlDescriptionCapture.TryCreateSnapshot(
                "Localized controls title",
                "Localized next",
                localized,
                artworkSelector: 0,
                manager: 0xA000,
                managerFocusKey: 0,
                out var snapshot,
                out var error),
            error);

        Assert.Equal(25, snapshot.Records.Count);
        Assert.Equal(ControlDescriptionCapture.RequiredMessageIds, snapshot.Records.Select(record => record.MessageId));
        Assert.All(snapshot.Records, record =>
            Assert.Equal(localized.First(item => item.MessageId == record.MessageId).Text, record.Text));
    }

    [Fact]
    public void PreservesEveryAuditedRecordPositionAndAnchor()
    {
        var localized = CompleteLocalizedRecords();

        Assert.True(
            ControlDescriptionCapture.TryCreateSnapshot(
                "Title", "Next", localized, 0, 0xA000, 0,
                out var snapshot, out var error),
            error);

        Assert.Equal(
            ExpectedRecords,
            snapshot.Records.Select(record =>
                (record.MessageId, record.X, record.Y, record.Anchor)));
    }

    [Fact]
    public void SelectorOneOnlySwapsAAndBArtworkWithoutChangingTheControlLayout()
    {
        var localized = CompleteLocalizedRecords();
        Assert.True(ControlDescriptionCapture.TryCreateSnapshot(
            "Title", "Next", localized, 0, 0xA000, 0,
            out var normal, out var normalError), normalError);
        Assert.True(ControlDescriptionCapture.TryCreateSnapshot(
            "Title", "Next", localized, 1, 0xA000, 0,
            out var alternate, out var alternateError), alternateError);

        var expectedNormal = new (string Name, int X, int Y)[]
        {
            ("A button", 100, -53), ("B button", 100, -72), ("Y button", 100, -91),
            ("X button", 100, -110), ("L button", 100, -129), ("R button", 132, -129),
            ("L button", 100, -167), ("R button", 132, -167), ("Start", 100, -186),
            ("Up", 100, -205), ("Left", 100, -224), ("Right", 100, -243),
            ("Down", 100, -262),
        };
        Assert.Equal(expectedNormal, normal.Sprites.Select(sprite =>
            (sprite.Name, sprite.X, sprite.Y)));

        Assert.Equal("B button", alternate.Sprites[0].Name);
        Assert.Equal("A button", alternate.Sprites[1].Name);
        Assert.DoesNotContain(normal.Sprites, sprite =>
            sprite.Name.StartsWith("Btn", StringComparison.Ordinal));
        Assert.Equal(
            normal.Sprites.Skip(2).Select(sprite => (sprite.Name, sprite.X, sprite.Y)),
            alternate.Sprites.Skip(2).Select(sprite => (sprite.Name, sprite.X, sprite.Y)));
        Assert.Equal(
            normal.Sprites.Select(sprite => (sprite.X, sprite.Y)),
            alternate.Sprites.Select(sprite => (sprite.X, sprite.Y)));

        Assert.All(normal.Records, record => Assert.Equal(
            normal.Sprites.Where(sprite => sprite.Y == record.Y),
            record.AssociatedSprites));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ValidatesExactRuntimeRecordAndSelectedSpriteTables(int selector)
    {
        var memory = CreateRuntimeTableMemory();
        Assert.True(ControlDescriptionCapture.TryValidateRuntimeTables(
            memory, ImageBase, selector, out var error), error);

        memory.AddInt32(
            ImageBase + ControlDescriptionCapture.RecordTableRva + 4,
            999);
        Assert.False(ControlDescriptionCapture.TryValidateRuntimeTables(
            memory, ImageBase, selector, out var recordError));
        Assert.Contains("record", recordError, StringComparison.OrdinalIgnoreCase);

        memory = CreateRuntimeTableMemory();
        var selectedTable = selector == 0
            ? ControlDescriptionCapture.NormalSpriteTableRva
            : ControlDescriptionCapture.AlternateSpriteTableRva;
        memory.AddPointer(ImageBase + selectedTable, ImageBase + 0x1234);
        Assert.False(ControlDescriptionCapture.TryValidateRuntimeTables(
            memory, ImageBase, selector, out var spriteError));
        Assert.Contains("sprite", spriteError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsMissingBlankOrDuplicateRequiredLocalizedObservationsWithoutThrowing()
    {
        var missing = CompleteLocalizedRecords();
        missing.RemoveAll(item => item.MessageId == 0x12);
        AssertRejected(missing, "missing");

        var blank = CompleteLocalizedRecords();
        var blankIndex = blank.FindIndex(item => item.MessageId == 0x12);
        blank[blankIndex] = blank[blankIndex] with { Text = "   " };
        AssertRejected(blank, "blank");

        var duplicate = CompleteLocalizedRecords();
        duplicate.Add(duplicate[0]);
        AssertRejected(duplicate, "more than once");

        var nullElement = CompleteLocalizedRecords();
        nullElement[0] = null!;
        AssertRejected(nullElement, "null");
    }

    [Theory]
    [InlineData("", "Next", 0, 0xA000u, 0, "title")]
    [InlineData("Title", "", 0, 0xA000u, 0, "Next")]
    [InlineData("Title", "Next", -1, 0xA000u, 0, "selector")]
    [InlineData("Title", "Next", 2, 0xA000u, 0, "selector")]
    [InlineData("Title", "Next", 0, 0u, 0, "manager")]
    [InlineData("Title", "Next", 0, 0xA000u, 1, "focus")]
    public void RejectsInvalidTitleNextSelectorManagerOrInitialFocus(
        string title,
        string next,
        int selector,
        uint manager,
        int focus,
        string expectedDiagnostic)
    {
        var exception = Record.Exception(() =>
        {
            Assert.False(ControlDescriptionCapture.TryCreateSnapshot(
                title, next, CompleteLocalizedRecords(), selector, manager, focus,
                out var snapshot, out var error));
            Assert.Null(snapshot);
            Assert.Contains(expectedDiagnostic, error, StringComparison.OrdinalIgnoreCase);
        });

        Assert.Null(exception);
    }

    private static void AssertRejected(
        IReadOnlyList<ControlDescriptionTextObservation> localized,
        string expectedDiagnostic)
    {
        var exception = Record.Exception(() =>
        {
            Assert.False(ControlDescriptionCapture.TryCreateSnapshot(
                "Title", "Next", localized, 0, 0xA000, 0,
                out var snapshot, out var error));
            Assert.Null(snapshot);
            Assert.Contains(expectedDiagnostic, error, StringComparison.OrdinalIgnoreCase);
        });

        Assert.Null(exception);
    }

    private static List<ControlDescriptionTextObservation> CompleteLocalizedRecords() =>
        ExpectedRecords.Select(record => record.Id)
            .Select(messageId => new ControlDescriptionTextObservation(
                messageId,
                $"localized-{messageId:X}"))
            .ToList();

    private static TestMemory CreateRuntimeTableMemory()
    {
        var memory = new TestMemory();
        for (var index = 0; index < ExpectedRecords.Length; index++)
        {
            var record = ExpectedRecords[index];
            var address = ImageBase + ControlDescriptionCapture.RecordTableRva + (nuint)(index * 16);
            memory
                .AddInt32(address, record.Id)
                .AddInt32(address + 4, record.X)
                .AddInt32(address + 8, record.Y)
                .AddInt32(address + 12, record.Anchor);
        }

        uint[] nameRvas =
        [
            0x3B1E54, 0x3B1E60, 0x3B1E78, 0x3B1E6C,
            0x3B1E90, 0x3B1E84, 0x3B1E90, 0x3B1E84,
            0x3B1EA4, 0x3B1E9C, 0x3B1EBC, 0x3B1EB0, 0x3B1ED4,
        ];
        var normal = new (int X, int Y)[]
        {
            (100, -53), (100, -72), (100, -91), (100, -110),
            (100, -129), (132, -129), (100, -167), (132, -167),
            (100, -186), (100, -205), (100, -224), (100, -243), (100, -262),
        };
        foreach (var tableRva in new[]
            {
                ControlDescriptionCapture.NormalSpriteTableRva,
                ControlDescriptionCapture.AlternateSpriteTableRva,
            })
        {
            for (var index = 0; index < normal.Length; index++)
            {
                var nameIndex = tableRva == ControlDescriptionCapture.AlternateSpriteTableRva && index < 2
                    ? 1 - index
                    : index;
                var address = ImageBase + tableRva + (nuint)(index * 12);
                memory
                    .AddPointer(address, ImageBase + nameRvas[nameIndex])
                    .AddInt32(address + 4, normal[index].X)
                    .AddInt32(address + 8, normal[index].Y);
            }
        }
        return memory;
    }

    private sealed class TestMemory : IReadableMemory
    {
        private readonly Dictionary<nuint, byte[]> segments = [];
        public TestMemory AddPointer(nuint address, nuint value) => AddInt32(address, checked((int)value));
        public TestMemory AddInt32(nuint address, int value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            segments[address] = bytes;
            return this;
        }
        public bool TryRead(nuint address, Span<byte> destination)
        {
            if (!segments.TryGetValue(address, out var bytes) || bytes.Length < destination.Length)
            {
                return false;
            }
            bytes.AsSpan(0, destination.Length).CopyTo(destination);
            return true;
        }
    }
}
