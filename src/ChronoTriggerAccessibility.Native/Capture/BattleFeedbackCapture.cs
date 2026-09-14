using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

public sealed record BattlePopupSnapshot(int Slot, string Text, byte Red, byte Green, byte Blue);

public sealed class BattleFeedbackCapture(IReadableMemory memory)
{
    public int ReadEffectKind(nuint image, int slot)
    {
        // 36900 caches the bucket at battleData+4CFC before writing its popups.
        // Result cells are 16 bytes each in SceneBattle+4C (canvas+14064).
        if (slot is < 0 or > 10 || !Word(image + BattleCapture.CanvasGlobalRva, out var canvas) ||
            !Integer(canvas + 0x1A89C, out var bucket) || bucket > 5 ||
            !Integer(image + 0x398E68u + bucket * 4, out var row) || row != bucket * 44 ||
            !Integer(canvas + 0x140ACu + row * 4 + (nuint)(slot * 16), out var kind) || kind is < 1 or > 5 ||
            !Word(image + BattleCapture.CanvasGlobalRva, out var same) || canvas != same ||
            !Integer(canvas + 0x1A89C, out var b2) || b2 != bucket) return 0;
        return (int)kind;
    }

    public string? ReadMessage(nuint menu)
    {
        // The supported cocos LabelProtocol getter is "lea eax,[ecx+28h]; ret".
        // Validate the actual getter, then read the finished string the native
        // message presenter wrote, including its own name/number substitution.
        if (!Fits(menu, 0x238) || !Word(menu + 0x234, out var label) || !Fits(label, 0x2B8) ||
            !Byte(label + 0x1AD, out var visible) || visible == 0 ||
            !Word(label + 0x278, out var protocol) || !Fits(protocol, 12) ||
            !Word(protocol + 8, out var getter) || !Fits(getter, 4)) return null;
        Span<byte> code = stackalloc byte[4];
        if (!memory.TryRead(getter, code) || !code.SequenceEqual((ReadOnlySpan<byte>)[0x8D, 0x41, 0x28, 0xC3]) ||
            !new MsvcStringReader(memory).TryRead(label + 0x2A0, out var text, out _) ||
            string.IsNullOrWhiteSpace(text) ||
            !Word(menu + 0x234, out var again) || label != again ||
            !Byte(label + 0x1AD, out visible) || visible == 0) return null;
        return text.Trim();
    }

    public IReadOnlyList<BattlePopupSnapshot> ReadPopups(nuint menu)
    {
        if (!Fits(menu, 0x1C0)) return [];
        var result = new List<BattlePopupSnapshot>();
        var strings = new MsvcStringReader(memory);
        Span<byte> color = stackalloc byte[3];
        for (var slot = 0; slot < 11; slot++)
        {
            if (!Byte(menu + 0x1B5u + (nuint)slot, out var visible) || visible == 0 ||
                !strings.TryRead(menu + 0x34u + (nuint)(slot * 24), out var text, out _) ||
                string.IsNullOrWhiteSpace(text) ||
                !memory.TryRead(menu + 0x194u + (nuint)(slot * 3), color) ||
                !Byte(menu + 0x1B5u + (nuint)slot, out visible) || visible == 0) continue;
            result.Add(new(slot, text.Trim(), color[0], color[1], color[2]));
        }
        return result.AsReadOnly();
    }

    private bool Word(nuint address, out nuint value)
    {
        value = 0; Span<byte> bytes = stackalloc byte[4];
        if (!Fits(address, 4) || !memory.TryRead(address, bytes)) return false;
        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes); return value != 0;
    }
    private bool Integer(nuint address, out uint value)
    {
        value = 0; Span<byte> bytes = stackalloc byte[4];
        if (!Fits(address, 4) || !memory.TryRead(address, bytes)) return false;
        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes); return true;
    }
    private bool Byte(nuint address, out byte value)
    {
        value = 0; Span<byte> bytes = stackalloc byte[1];
        if (!Fits(address, 1) || !memory.TryRead(address, bytes)) return false;
        value = bytes[0]; return true;
    }
    private static bool Fits(nuint address, uint length) => address != 0 && (ulong)address + length <= 0x100000000UL;
}
