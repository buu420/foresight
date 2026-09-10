using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

public readonly record struct FieldViewport(int Left, int Top, int Right, int Bottom)
{
    public bool Contains(int fineX, int fineY) => fineX >= Left && fineX < Right && fineY >= Top && fineY < Bottom;
}

public sealed record FieldTreasure(int Index, int FineX, int FineY);

public static class FieldEnvironmentCapture
{
    /// <summary>Native camera tile bounds: 173B40 updates left/right and top/bottom
    /// together; 173C90/173D00 advance their fractional phases every eight pixels.
    /// 15DFA0 tests the same four edges. Use this native window conservatively, without
    /// guessing additional widescreen margins from desktop resolution.</summary>
    public static bool TryViewport(IReadableMemory memory, FieldNavigationSnapshot field, out FieldViewport viewport)
    {
        viewport = default;
        if (!Word(memory, field.Engine + 0x854u, out var pointer) || (uint)pointer != (ulong)field.ActorBase + 0x1327C ||
            !Word(memory, (uint)pointer + 0x10u, out var left) || !Word(memory, (uint)pointer + 0x14u, out var right) ||
            !Word(memory, (uint)pointer + 0x18u, out var top) || !Word(memory, (uint)pointer + 0x1Cu, out var bottom) ||
            !Word(memory, (uint)pointer + 0x1A8u, out var phaseX) || !Word(memory, (uint)pointer + 0x1B4u, out var phaseY) ||
            left is < -256 or > 512 || top is < -256 or > 512 || right <= left || right - left > 256 ||
            bottom <= top || bottom - top > 256 || phaseX is < 0 or > 15 || phaseY is < 0 or > 15) return false;
        viewport = new((left * 8 + (phaseX & 7)) * 16, (top * 8 + (phaseY & 7)) * 16,
            ((right + 1) * 8 + (phaseX & 7)) * 16, ((bottom + 1) * 8 + (phaseY & 7)) * 16);
        return true;
    }

    /// <summary>179690 builds the active chest grid and resolves any shared-scene
    /// treasure table aliases into field+2190/2194. Its open bits match 17D0C0. Only
    /// rendered chest tiles (property0 bit 1) are exposed, never invisible pickups
    /// or reward contents. The graphics byte must also be one of the four native
    /// closed-chest tiles that 179690 replaces when opening a chest.</summary>
    public static bool TryTreasures(IReadableMemory memory, FieldNavigationSnapshot field, FieldMapSnapshot map,
        out IReadOnlyList<FieldTreasure> treasures)
    {
        treasures = [];
        if (!Word(memory, field.Engine + 0xE44u, out var begin) || !Word(memory, field.Engine + 0xE48u, out var end) ||
            !Word(memory, field.Engine + 0xE50u, out var width) || !Word(memory, field.Engine + 0xE54u, out var height) ||
            width is < 1 or > 256 || height is < 1 or > 256 || (long)(uint)end - (uint)begin != width * height) return false;
        var cells = new byte[width * height];
        if (!Read(memory, (uint)begin, cells)) return false;
        if (cells.All(value => value >= 128)) return true;
        if (!Word(memory, field.FieldState + 0x2190u, out var first) || !Word(memory, field.FieldState + 0x2194u, out var last) ||
            first < 0 || last < first || last - first > 128 || last > 8192 ||
            !Word(memory, field.ActorBase + 0x10FD0u, out var graphics) ||
            !Word(memory, field.ActorBase + 0x10FDCu, out var graphicsWidth) || graphicsWidth != width) return false;
        var result = new List<FieldTreasure>();
        Span<byte> tile = stackalloc byte[1];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var id = cells[y * width + x];
            if (id >= 128) continue;
            if (id >= last - first) return false;
            if (x >= map.Width || y >= map.Height || (map.CollisionShapes[y * map.Width + x] & 1) == 0) continue;
            var global = first + id;
            if (!Word(memory, field.ActorBase + 0x110B4u + (uint)((global >> 3) & 63) * 4, out var opened)) return false;
            if ((opened & (1 << (global & 7))) != 0) continue;
            if (!Read(memory, (uint)graphics + (uint)(y * width + x), tile)) return false;
            if (tile[0] is not (0xFE or 0xEE or 0xE0 or 0xF0)) continue;
            result.Add(new(global, x * 256 + 128, y * 256 + 128));
        }
        treasures = result.AsReadOnly();
        return true;
    }

    private static bool Word(IReadableMemory memory, nuint address, out int value)
    {
        value = 0;
        Span<byte> bytes = stackalloc byte[4];
        if (!Read(memory, address, bytes)) return false;
        value = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        return true;
    }

    private static bool Read(IReadableMemory memory, nuint address, Span<byte> bytes) =>
        address != 0 && (ulong)address + (uint)bytes.Length - 1 <= uint.MaxValue && memory.TryRead(address, bytes);
}
