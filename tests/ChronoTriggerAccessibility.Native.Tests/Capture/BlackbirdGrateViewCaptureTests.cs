using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

/// <summary>Dispatcher contexts shaped like StoryActionCapture's (ctx+0 data, +24 PC, +40
/// actors, +850 field, +BB4 Atel id; field+1010 scene, +1180 actor*2). Script bytes are the
/// installed Atel_0274/0267/0265 instructions at their packet PCs.</summary>
public sealed class BlackbirdGrateViewCaptureTests
{
    private const uint Context = 0x1000, Data = 0xA0000, Actors = 0x200000, Field = 0x5000;

    [Fact]
    public void TheShaftsViewEntryIsActor0Fn3sE3()
    {
        var memory = Script(376, 274, 0x4BD, "E30005", actor: 0);
        Assert.Equal(new BlackbirdGrateViewEvent(BlackbirdGrateViewKind.Opening, 376, Context),
            BlackbirdGrateViewCapture.Capture(memory, Context, 0xE3));
    }

    [Fact]
    public void TheViewLoopNeedsItsExactE3AndTheViewFlag()
    {
        var memory = Script(369, 267, 0x2E8, "E30031", actor: 0, viewFlags: 0x80);
        Assert.Equal(BlackbirdGrateViewKind.Viewing, BlackbirdGrateViewCapture.Capture(memory, Context, 0xE3)?.Kind);
        // A normal visit has no view flag; the same instruction is then no view.
        memory.Word(Actors + 0x110B0 + 0xAF * 4, 0x7F);
        Assert.Null(BlackbirdGrateViewCapture.Capture(memory, Context, 0xE3));
        // An unreadable flag proves nothing either way.
        Assert.Null(BlackbirdGrateViewCapture.Capture(Script(369, 267, 0x2E8, "E30031", actor: 0, viewFlags: null), Context, 0xE3));
    }

    [Theory]
    [InlineData(369, 267, 0x2E7u, "E30031", 0)] // another PC
    [InlineData(369, 266, 0x2E8u, "E30031", 0)] // another script
    [InlineData(369, 267, 0x2E8u, "E30131", 0)] // E3 01 restores control
    [InlineData(369, 267, 0x2E8u, "E30031", 3)] // not actor0
    [InlineData(373, 271, 0x2E8u, "E30031", 0)] // Room 4 has no grate view
    public void OtherE3sAreNotViews(int scene, int script, uint pc, string bytes, int actor) =>
        Assert.Null(BlackbirdGrateViewCapture.Capture(Script(scene, script, pc, bytes, actor, 0x80), Context, 0xE3));

    [Fact]
    public void TheReturnIsActor0sE0BackToTheShaft()
    {
        // Atel_0267 0x02F8: E0 78 01 00 03 08 -> scene 376 at (3,8).
        var memory = Script(369, 267, 0x2F7, "E07801", actor: 0);
        Assert.Equal(BlackbirdGrateViewKind.Returning, BlackbirdGrateViewCapture.Capture(memory, Context, 0xE0)?.Kind);
        // Atel_0267 0x05BB: E0 73 01 -> the cell after capture, not a return from a view.
        Assert.Null(BlackbirdGrateViewCapture.Capture(Script(369, 267, 0x5BA, "E07301", actor: 0), Context, 0xE0));
        Assert.Null(BlackbirdGrateViewCapture.Capture(Script(376, 274, 0x59F, "E07101", actor: 8), Context, 0xE0));
    }

    [Fact]
    public void ADispatchWhoseByteIsNotTheOpcodeOrAnOtherOpcodeIsIgnored()
    {
        var memory = Script(376, 274, 0x4BD, "E30005", actor: 0);
        Assert.Null(BlackbirdGrateViewCapture.Capture(memory, Context, 0xE0));
        Assert.Null(BlackbirdGrateViewCapture.Capture(memory, Context, 0x31));
        Assert.Null(BlackbirdGrateViewCapture.Capture(new Memory(), Context, 0xE3));
    }

    [Theory]
    [InlineData(0x4E0u, 1, true)]
    [InlineData(0x4EEu, 2, true)]
    [InlineData(0x500u, 3, true)]
    [InlineData(0x517u, 4, true)]
    [InlineData(0x52Eu, 5, true)]
    [InlineData(0x4EEu, 1, false)] // another actor at that PC
    [InlineData(0x4E2u, 1, false)]
    public void OnlyTheShaftsLookDownPoseIsTheLookPose(uint pc, int actor, bool expected) =>
        Assert.Equal(expected, BlackbirdGrateViewCapture.IsLookPose(new(Context, Data, Actors, Field, 0, 376, 274, actor, 37, pc, 0xAA)));

    [Fact]
    public void TheCellClimbsCrouchIsNotTheLookPose() =>
        // Atel_0269's crawl into and out of the duct keeps its narration.
        Assert.False(BlackbirdGrateViewCapture.IsLookPose(new(Context, Data, Actors, Field, 0, 371, 269, 5, 12, 0x4EE, 0xAA)));

    private static Memory Script(int scene, int script, uint pc, string bytes, int actor, int? viewFlags = 0)
    {
        var memory = new Memory();
        memory.Add(Context, new byte[0xBB8]);
        memory.Word(Context, Data);
        memory.Word(Context + 0x24, pc);
        memory.Word(Context + 0x40, Actors);
        memory.Word(Context + 0x850, Field);
        memory.Word(Context + 0xBB4, (uint)script);
        memory.Add(Field + 0x1000, new byte[0x200]);
        memory.Word(Field + 0x1010, (uint)scene);
        memory.Word(Field + 0x1180, (uint)actor * 2);
        memory.Add(Data + 0x12000, [37]);
        memory.Add(Data + 0x12001 + pc, Convert.FromHexString(bytes));
        if (viewFlags is { } flags) memory.Word(Actors + 0x110B0 + 0xAF * 4, (uint)flags);
        return memory;
    }

    private sealed class Memory : IReadableMemory
    {
        private readonly Dictionary<nuint, byte> bytes = [];
        public void Word(nuint address, uint value)
        {
            Span<byte> data = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(data, value);
            Add(address, data);
        }
        public void Add(nuint address, ReadOnlySpan<byte> data)
        {
            for (var index = 0; index < data.Length; index++) bytes[address + (nuint)index] = data[index];
        }
        public bool TryRead(nuint address, Span<byte> destination)
        {
            for (var index = 0; index < destination.Length; index++)
                if (!bytes.TryGetValue(address + (nuint)index, out destination[index])) return false;
            return true;
        }
    }
}
