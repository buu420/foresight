using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class IokaContestCaptureTests
{
    // Installed Atel_0371 original file offsets, minus its actor-count byte.
    [Theory]
    [InlineData(0x7BD, 0x75, 6, 0, IokaContestAction.Started)]
    [InlineData(0x3E8, 0xAA, 2, 1, IokaContestAction.PlayerDrinks)]
    [InlineData(0x7CF, 0xAA, 6, 1, IokaContestAction.AylaDrinks)]
    [InlineData(0x7EA, 0xAA, 6, 1, IokaContestAction.AylaDrinks)]
    [InlineData(0x7F5, 0x77, 6, 1, IokaContestAction.Finished)]
    public void IdentifiesOnlyVerifiedContestActions(int pc, int opcode, int actor, int active,
        IokaContestAction action)
    {
        var memory = new ContestMemory(pc, actor, active);
        Assert.True(IokaContestCapture.TryCapture(memory, 0x1000, opcode, out var before));
        Assert.Equal(action, before.Action);
        Assert.False(IokaContestCapture.TryComplete(memory, before));

        memory.Complete(opcode);

        Assert.True(IokaContestCapture.TryComplete(memory, before));
    }

    [Theory]
    [InlineData(279, 0x7BD, 0x75, 6, 0)]
    [InlineData(280, 0x7BD, 0x77, 6, 0)]
    [InlineData(280, 0x7BD, 0x75, 2, 0)]
    [InlineData(280, 0x7BD, 0x75, 6, 1)]
    [InlineData(280, 0x3E8, 0xAA, 2, 0)]
    [InlineData(280, 0x3E8, 0xAA, 2, 7)]
    [InlineData(280, 0x384, 0xAA, 2, 1)] // Drinking with Lucca outside the contest.
    [InlineData(280, 0x8C3, 0x71, 7, 1)] // An internal counter is not a visible action.
    public void RejectsOtherScenesInstructionsActorsAndInactiveDrinks(int scene, int pc, int opcode,
        int actor, int active)
    {
        var memory = new ContestMemory(pc, actor, active);
        memory.Word(0x6010, scene);
        Assert.False(IokaContestCapture.TryCapture(memory, 0x1000, opcode, out _));
    }

    [Fact]
    public void RejectsWrongScriptHeaderOrInstructionAndUnreadableState()
    {
        var memory = new ContestMemory(0x7BD, 6, 0);
        memory.Bytes[0xB2000] = 22;
        Assert.False(IokaContestCapture.TryCapture(memory, 0x1000, 0x75, out _));
        memory.Bytes[0xB2000] = 23;
        memory.Bytes[0xB27BF] = 9;
        Assert.False(IokaContestCapture.TryCapture(memory, 0x1000, 0x75, out _));
        memory.Bytes[0xB27BF] = 0x0E;
        memory.Bytes.Remove(0x6180);
        Assert.False(IokaContestCapture.TryCapture(memory, 0x1000, 0x75, out _));
    }

    [Theory]
    [InlineData(0xD0, 0)]
    [InlineData(0xD0, 0x80)]
    [InlineData(0x40, 4)]
    [InlineData(0x44, 1)]
    public void RejectsHiddenRemovedAndWrongCharacterDrinkingAnimations(int offset, int value)
    {
        var memory = new ContestMemory(0x3E8, 2, 1);
        memory.Word(memory.ActorAddress + (uint)offset, value);
        Assert.False(IokaContestCapture.TryCapture(memory, 0x1000, 0xAA, out _));
    }

    [Theory]
    [InlineData(0x1000, 0xA1000)]
    [InlineData(0x1040, 0xD1000)]
    [InlineData(0x1850, 0x7000)]
    [InlineData(0x6010, 279)]
    [InlineData(0x6180, 4)]
    [InlineData(0x1024, 0x7BD)]
    public void RejectsAnOwnerSceneActorOrExecutionChangeAfterTheGameCall(int address, int value)
    {
        var memory = new ContestMemory(0x7BD, 6, 0);
        Assert.True(IokaContestCapture.TryCapture(memory, 0x1000, 0x75, out var before));
        memory.Complete(0x75);
        memory.Word((uint)address, value);
        Assert.False(IokaContestCapture.TryComplete(memory, before));
    }

    [Fact]
    public void RequiresTheNativeAnimationWriteAndActiveContestAfterDispatch()
    {
        var memory = new ContestMemory(0x3E8, 2, 1);
        Assert.True(IokaContestCapture.TryCapture(memory, 0x1000, 0xAA, out var before));
        memory.Word(0x1024, 0x3E9);
        Assert.False(IokaContestCapture.TryComplete(memory, before));
        memory.Complete(0xAA);
        memory.Word(0xE1920, 0);
        Assert.False(IokaContestCapture.TryComplete(memory, before));
    }
}

internal sealed class ContestMemory : IReadableMemory
{
    public Dictionary<nuint, byte> Bytes { get; } = [];
    private readonly int pc, actor;
    public uint ActorAddress => 0xD0000u + 0x6940u + (uint)actor * 0x154u;
    public int Reads { get; private set; }
    public ContestMemory(int pc = 0x7BD, int actor = 6, int active = 0)
    {
        this.pc = pc; this.actor = actor;
        Word(0x1000, 0xA0000); Word(0x1040, 0xD0000); Word(0x1024, pc);
        Word(0x1850, 0x5000); Word(0x6010, 280); Word(0x6180, actor * 2);
        Word(0xE1920, active);
        Add(0xB2000, Convert.FromHexString("17E002F3022403250325032503250325"));
        Add(0xB27BE, Convert.FromHexString("750E771B771C0404"));
        Add(0xB23E9, Convert.FromHexString("AA3B00032832AA3A"));
        Add(0xB27D0, Convert.FromHexString("AA50121BAF031B71"));
        Add(0xB27EB, Convert.FromHexString("AA50771C111E032A"));
        Add(0xB27F6, Convert.FromHexString("770E120F00000311"));
        Word(ActorAddress + 0x40, 3); Word(ActorAddress + 0x44, actor == 2 ? 0 : 5);
        Word(ActorAddress + 0xD0, 1); Word(ActorAddress + 0x68, 0); Word(ActorAddress + 0x74, 0);
    }
    public void Complete(int opcode)
    {
        Word(0x1024, pc + (opcode == 0xAA ? 1 : 2));
        if (opcode is 0x75 or 0x77) Word(0xE1920, opcode == 0x75 ? 1 : 0);
        if (opcode == 0xAA)
        { Word(ActorAddress + 0x68, actor == 2 ? 0x3B : 0x50); Word(ActorAddress + 0x74, 1); }
    }
    public void Word(nuint address, int value)
    {
        Span<byte> data = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(data, value); Add(address, data);
    }
    public void Add(nuint address, ReadOnlySpan<byte> data)
    { for (var i = 0; i < data.Length; i++) Bytes[address + (nuint)i] = data[i]; }
    public bool TryRead(nuint address, Span<byte> destination)
    {
        Reads++;
        for (var i = 0; i < destination.Length; i++)
            if (!Bytes.TryGetValue(address + (nuint)i, out destination[i])) return false;
        return true;
    }
}
