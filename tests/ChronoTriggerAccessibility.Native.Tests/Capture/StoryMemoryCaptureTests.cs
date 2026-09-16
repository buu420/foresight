using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class StoryMemoryCaptureTests
{
    [Fact]
    public void InventoryDistinguishesMissingItemFromUnreadableInventory()
    {
        var memory = new InventoryMemory();
        memory.Item(12, 0x123, 2);
        var inventory = StoryMemoryCapture.Inventory(memory, 0x4000);
        Assert.NotNull(inventory);
        var state = new FieldStoryState(165, false) { Inventory = inventory };
        Assert.Equal(2, state.ItemCount(0x123));
        Assert.Equal(0, state.ItemCount(0x124));
        Assert.Null(new FieldStoryState(165, false).ItemCount(0x123));
    }

    [Fact]
    public void InventoryMatchesNativeFirstPositiveRecordAndSkipsEmptySlots()
    {
        var memory = new InventoryMemory();
        memory.Item(1, 0x123, 0);
        memory.Item(2, 0x123, 3);
        memory.Item(3, 0x123, 4);
        Assert.Equal(3, StoryMemoryCapture.Inventory(memory, 0x4000)![0x123]);
    }

    [Fact]
    public void RejectsAnInventoryThatChangesBetweenTheTwoReads()
    {
        var memory = new InventoryMemory { Mutate = true };
        memory.Item(1, 0x123, 1);
        Assert.Null(StoryMemoryCapture.Inventory(memory, 0x4000));
    }

    [Fact]
    public void StableBulkReadOmitsOnlyTheChangedCell()
    {
        var memory = new BulkMemory();
        var result = StoryMemoryCapture.StableWords(memory, 0x4000, 3, 4, true);
        Assert.Equal(7, result[0]);
        Assert.False(result.ContainsKey(1));
        Assert.Equal(9, result[2]);
    }

    [Fact]
    public void PartyDistinguishesActiveReserveAndMissingCharacters()
    {
        var memory = new RosterMemory();
        var state = new FieldStoryState(213, false)
        { Party = StoryMemoryCapture.Party(memory, 0x4000), Gold = StoryMemoryCapture.Gold(memory, 0x4000) };
        Assert.Equal(1, state.PartyContains(5, true));
        Assert.Equal(0, state.PartyContains(4, true));
        Assert.Equal(1, state.PartyContains(4, false));
        Assert.Equal(0, state.PartyContains(6, false));
        Assert.Equal(501, state.Gold);
        Assert.Null(new FieldStoryState(213, false).PartyContains(5, false));
    }

    [Fact]
    public void InvalidRosterDoesNotPretendACharacterIsAbsent()
    {
        var memory = new RosterMemory { Invalid = true };
        Assert.Null(StoryMemoryCapture.Party(memory, 0x4000));
    }

    private sealed class RosterMemory : IReadableMemory
    {
        public bool Invalid { get; init; }
        public bool TryRead(nuint address, Span<byte> destination)
        {
            if (address == 0x1724c && destination.Length == 36)
            {
                int[] roster = [0, 1, 5, 4, 3, Invalid ? 99 : 255, 255, 255, 255];
                for (var i = 0; i < roster.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(destination[(i * 4)..], roster[i]);
                return true;
            }
            if (address != 0x5a24 || destination.Length != 4) return false;
            BinaryPrimitives.WriteInt32LittleEndian(destination, 501);
            return true;
        }
    }

    private sealed class InventoryMemory : IReadableMemory
    {
        private readonly byte[] bytes = new byte[0x15B * 12];
        public bool Mutate { get; init; }
        public void Item(int slot, int id, int count)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(slot * 12), id);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(slot * 12 + 4), count);
        }
        public bool TryRead(nuint address, Span<byte> destination)
        {
            if (address != 0x4810 || destination.Length != bytes.Length) return false;
            bytes.CopyTo(destination);
            if (Mutate) bytes[16] ^= 1;
            return true;
        }
    }
    private sealed class BulkMemory : IReadableMemory
    {
        private int reads;
        public bool TryRead(nuint address, Span<byte> destination)
        {
            if (address != 0x4000 || destination.Length != 12) return false;
            BinaryPrimitives.WriteInt32LittleEndian(destination, 0xAB07);
            BinaryPrimitives.WriteInt32LittleEndian(destination[4..], reads++);
            BinaryPrimitives.WriteInt32LittleEndian(destination[8..], 9);
            return true;
        }
    }
}
