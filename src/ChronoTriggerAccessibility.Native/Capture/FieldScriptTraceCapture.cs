using System.Buffers.Binary;
using System.Security.Cryptography;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

public sealed record FieldScriptTraceSnapshot(uint Context, uint Data, uint FieldState,
    uint Address, uint Actor, uint ControlBefore, string ScriptPrefix, string Bytes);

public static class FieldScriptTraceCapture
{
    public static bool TryCapture(IReadableMemory memory, nuint context,
        out FieldScriptTraceSnapshot snapshot)
    {
        snapshot = null!;
        try
        {
            if (!Fits(context, 0xBB8) ||
                !Word(memory, context, out var data) || !Fits(data, 0x22010) ||
                !Word(memory, context + 0x24, out var address) || address > 0xFFFF ||
                !Word(memory, context + 0x850, out var field) || !Fits(field, 0x1090) ||
                !Word(memory, context + 0xBB4, out var actor) ||
                !Word(memory, field + 0x108Cu, out var control)) return false;
            Span<byte> prefix = stackalloc byte[8];
            Span<byte> bytes = stackalloc byte[8];
            if (!memory.TryRead(data + 0x12000u, prefix) ||
                !memory.TryRead(data + 0x12001u + address, bytes)) return false;
            snapshot = new((uint)context, data, field, address, actor, control,
                Convert.ToHexString(prefix), Convert.ToHexString(bytes));
            return true;
        }
        catch (Exception) { return false; }
    }

    public static bool TryReadControlAfter(IReadableMemory memory, FieldScriptTraceSnapshot before,
        out uint control)
    {
        control = 0;
        try
        {
            return Word(memory, before.Context + 0x850u, out var field) && field == before.FieldState &&
                Word(memory, field + 0x108Cu, out control);
        }
        catch (Exception) { return false; }
    }

    public static string? TryHashAtel0000Candidate(IReadableMemory memory, FieldScriptTraceSnapshot before)
    {
        // Only this known header is a candidate; a prefix alone is not an identity claim.
        if (before.ScriptPrefix != "236004A204D504D6") return null;
        try
        {
            var bytes = new byte[0x15D2];
            return memory.TryRead(before.Data + 0x12000u, bytes)
                ? Convert.ToHexString(SHA256.HashData(bytes)) : null;
        }
        catch (Exception) { return null; }
    }

    private static bool Fits(nuint address, uint count) => address != 0 &&
        (ulong)address + count - 1 <= uint.MaxValue;

    private static bool Word(IReadableMemory memory, nuint address, out uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        value = 0;
        if (!Fits(address, 4) || !memory.TryRead(address, bytes)) return false;
        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        return true;
    }
}
