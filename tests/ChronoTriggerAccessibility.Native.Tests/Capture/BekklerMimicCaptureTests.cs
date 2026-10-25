using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

public sealed class BekklerMimicCaptureTests
{
    [Theory]
    [InlineData(0x382, 0x4F, BekklerMimicAction.Window, null)]
    [InlineData(0x53F, 0xAA, BekklerMimicAction.RightArm, "B")]
    [InlineData(0x546, 0xAA, BekklerMimicAction.LeftArm, "A")]
    [InlineData(0x54D, 0xAA, BekklerMimicAction.Surprise, "X")]
    [InlineData(0x554, 0xAA, BekklerMimicAction.Laugh, "Y")]
    [InlineData(0x3C4, 0x77, BekklerMimicAction.Failed, null)]
    [InlineData(0x3AE, 0xEA, BekklerMimicAction.Won, null)]
    public void WaitsForTheNativeWriteAndUsesTheLiveButtonLabels(int pc, int opcode,
        BekklerMimicAction action, string? button)
    {
        var memory = new MimicMemory(pc);
        Assert.True(BekklerMimicCapture.TryCapture(memory, 0x1000, opcode, out var before));
        Assert.Equal(action, before.Action);
        Assert.Null(BekklerMimicCapture.Complete(memory, before, 0x400000));
        memory.Complete();
        var proof = BekklerMimicCapture.Complete(memory, before, 0x400000);
        Assert.NotNull(proof); Assert.Equal(button, proof.Button);
    }

    [Fact]
    public void NeverReadsTheRandomChoiceBeforeThePoseIsVisiblyApplied()
    {
        var memory = new MimicMemory(0x554);
        Assert.True(BekklerMimicCapture.TryCapture(memory, 0x1000, 0xAA, out var before));
        Assert.DoesNotContain((nuint)MimicMemory.Local(7), memory.Reads);
        Assert.DoesNotContain((nuint)MimicMemory.Local(0x10), memory.Reads);
        Assert.Null(BekklerMimicCapture.Complete(memory, before, 0x400000));
        Assert.DoesNotContain((nuint)MimicMemory.Local(0x10), memory.Reads);
        memory.Complete();
        Assert.NotNull(BekklerMimicCapture.Complete(memory, before, 0x400000));
        Assert.DoesNotContain((nuint)MimicMemory.Local(7), memory.Reads);
    }

    [Theory]
    [InlineData(0x6010, 433)] // Wrong scene.
    [InlineData(0x6180, 14)] // Player, not the demonstrated doppel.
    [InlineData(0x1024, 0x536)] // Neutral pose.
    [InlineData(0xB2000, 24)] // Different script header.
    [InlineData(0xB2555, 0xAA00)] // Different instruction.
    [InlineData(0xE18F0, 0)] // Round zero.
    [InlineData(0xE18F0, 23)] // Not a round.
    [InlineData(0xE1998, 0)] // Inactive.
    [InlineData(0xE1938, 0xFF)] // Idle input state.
    [InlineData(MimicMemory.Model + 0xD0, 0)]
    [InlineData(MimicMemory.Model + 0xD0, 0x80)]
    [InlineData(MimicMemory.Model + 0x40, 0x83)]
    [InlineData(MimicMemory.Model + 0x44, 7)]
    public void RejectsUnrelatedHiddenOrInactiveActions(int address, int value)
    {
        var memory = new MimicMemory(0x554); memory.Word((uint)address, value);
        Assert.False(BekklerMimicCapture.TryCapture(memory, 0x1000, 0xAA, out _));
    }

    [Theory]
    [InlineData(0x1000, 0xA1000)]
    [InlineData(0x1040, 0xD1000)]
    [InlineData(0x1850, 0x7000)]
    [InlineData(0x6010, 433)]
    [InlineData(0x6180, 14)]
    [InlineData(0x1024, 0x554)]
    [InlineData(0xE18F0, 2)]
    [InlineData(0xE1930, 0)] // Old pre-loop demonstration does not match the active round.
    [InlineData(0xE1938, 0xFF)]
    [InlineData(MimicMemory.Model + 0x68, 0)]
    [InlineData(MimicMemory.Model + 0x74, 0)]
    [InlineData(MimicMemory.Model + 0xD0, 0)]
    [InlineData(MimicMemory.Model + 0x44, 6)]
    public void RejectsAChangedOwnerWindowOrIncompleteVisualWrite(int address, int value)
    {
        var memory = new MimicMemory(0x554);
        Assert.True(BekklerMimicCapture.TryCapture(memory, 0x1000, 0xAA, out var before));
        memory.Complete(); memory.Word((uint)address, value);
        Assert.Null(BekklerMimicCapture.Complete(memory, before, 0x400000));
    }

    [Fact]
    public void ARemappingChangesTheCueAndAnUnknownBindingDoesNotGuess()
    {
        var memory = new MimicMemory(0x554);
        Assert.True(BekklerMimicCapture.TryCapture(memory, 0x1000, 0xAA, out var before));
        memory.Complete();
        memory.Word(0x2068F4, 1); memory.Word(0x2068FC, 0x40);
        Assert.Equal("A", BekklerMimicCapture.Complete(memory, before, 0x400000)!.Button);
        memory.Word(0x2068F4, 0x100);
        Assert.Null(BekklerMimicCapture.Complete(memory, before, 0x400000)!.Button);
        memory.Bytes.Remove(0x81B4C4);
        Assert.Null(BekklerMimicCapture.Complete(memory, before, 0x400000)!.Button);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void AnInputAcceptedBeforeTheDemonstrationDoesNotHideTheVisiblePose(int input)
    {
        // Native actor10 can accept a held press after the window opens but before
        // actor9 draws its pose. Sighted players still see the demonstration.
        var memory = new MimicMemory(0x554); memory.Word(MimicMemory.Local(0x11), input);
        Assert.True(BekklerMimicCapture.TryCapture(memory, 0x1000, 0xAA, out var before));
        Assert.Null(BekklerMimicCapture.Complete(memory, before, 0x400000));
        memory.Complete();
        var proof = BekklerMimicCapture.Complete(memory, before, 0x400000);
        Assert.NotNull(proof); Assert.Equal(BekklerMimicAction.Laugh, proof.Frame.Action);
        Assert.Equal("Y", proof.Button);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void RecognizesTheNativeDoppelForEveryPartyModel(int visual)
    {
        var memory = new MimicMemory(0x53F); memory.Word(MimicMemory.Model + 0x44, visual);
        Assert.True(BekklerMimicCapture.TryCapture(memory, 0x1000, 0xAA, out var before));
        memory.Complete(); Assert.NotNull(BekklerMimicCapture.Complete(memory, before, 0x400000));
    }

    [Fact]
    public void MissingMemoryAndIrrelevantOpcodesAreSilent()
    {
        var memory = new MimicMemory(0x554);
        Assert.False(BekklerMimicCapture.TryCapture(memory, 0x1000, 0x7F, out _));
        Assert.Empty(memory.Reads);
        memory.Bytes.Remove(0x6180);
        Assert.False(BekklerMimicCapture.TryCapture(memory, 0x1000, 0xAA, out _));
        Assert.False(BekklerMimicCapture.TryCapture(memory, unchecked((nuint)uint.MaxValue), 0xAA, out _));
    }

    [Fact]
    public void OnlyTheFourAnchoredDemonstrationsSupersedeGenericStoryGestures()
    {
        var location = new StoryScriptLocation(0x1000, 0xA0000, 0xD0000, 0x5000, 0,
            434, 71, 9, 25, 0x554, 0xAA);
        Assert.True(BekklerMimicCapture.IsDemonstration(location));
        Assert.False(BekklerMimicCapture.IsDemonstration(location with { Scene = 433 }));
        Assert.False(BekklerMimicCapture.IsDemonstration(location with { ScriptId = 434 }));
        Assert.False(BekklerMimicCapture.IsDemonstration(location with { Actor = 1 }));
        Assert.False(BekklerMimicCapture.IsDemonstration(location with { Address = 0x536 }));
    }
}

internal sealed class MimicMemory : IReadableMemory
{
    public const int Model = 0xD0000 + 0x6940 + 9 * 0x154;
    public Dictionary<nuint, byte> Bytes { get; } = [];
    public List<nuint> Reads { get; } = [];
    private readonly int pc;
    private static readonly Dictionary<int, string> Instructions = new()
    {
        [0x382] = "4F8011751DE80202", [0x53F] = "AA1E10161A0105AA",
        [0x546] = "AA1F100F1A0205AA", [0x54D] = "AA0910081A0305AA",
        [0x554] = "AA1A100100001211", [0x3C4] = "771DEA25AD10121C",
        [0x3AE] = "EA1CBB4E00041437",
    };
    public static uint Local(int index) => 0xD0000u + 0x118B0u + (uint)index * 8;
    public MimicMemory(int pc)
    {
        this.pc = pc;
        Word(0x1000, 0xA0000); Word(0x1040, 0xD0000); Word(0x1024, pc);
        Word(0x1018, pc); Word(0x1850, 0x5000); Word(0x6010, 434);
        Word(0x6180, pc is >= 0x53F and <= 0x554 ? 18 : 0);
        Word(Local(8), pc == 0x3AE ? 22 : 1);
        Word(Local(0x1D), pc == 0x382 ? 0 : 1);
        Word(Local(0x11), pc == 0x382 ? 0xFF : 0x80);
        Word(Local(0x10), pc switch { 0x546 => 1, 0x54D => 2, 0x554 => 3, _ => 0 });
        Add(0xB2000, Convert.FromHexString("19200344034403450345034503450345"));
        foreach (var pair in Instructions) Add(0xB2001u + (uint)pair.Key, Convert.FromHexString(pair.Value));
        Word(Model + 0x40, 3); Word(Model + 0x44, 0); Word(Model + 0xD0, 1);
        Word(Model + 0x68, 0); Word(Model + 0x74, 0);
        Word(0x81B4C4, 0x200000);
        Word(0x2068F4, 0x40); Word(0x2068F8, 0x80); Word(0x2068FC, 1); Word(0x206900, 8);
    }
    public void Complete()
    {
        Word(0x1024, pc + (pc == 0x382 ? 0 : pc == 0x3C4 ? 2 : 1));
        Word(0x1018, pc + (pc == 0x382 ? 3 : 2));
        if (pc == 0x382) Word(Local(0x11), 0x80);
        if (pc == 0x3C4) Word(Local(0x1D), 0);
        if (pc is >= 0x53F and <= 0x554)
        { Word(Model + 0x68, Convert.ToInt32(Instructions[pc].Substring(2, 2), 16)); Word(Model + 0x74, 1); }
    }
    public void Word(nuint address, int value)
    { Span<byte> data = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(data, value); Add(address, data); }
    public void Add(nuint address, ReadOnlySpan<byte> data)
    { for (var i = 0; i < data.Length; i++) Bytes[address + (nuint)i] = data[i]; }
    public bool TryRead(nuint address, Span<byte> destination)
    {
        Reads.Add(address);
        for (var i = 0; i < destination.Length; i++)
            if (!Bytes.TryGetValue(address + (nuint)i, out destination[i])) return false;
        return true;
    }
}
