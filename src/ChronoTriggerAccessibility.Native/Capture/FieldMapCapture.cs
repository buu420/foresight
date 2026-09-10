using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

public sealed record FieldMapSnapshot(int Width, int Height, byte[] CollisionShapes,
    byte[] TerrainFlags, byte[] CollisionLayers, int PlayerLayer, bool TransitionPending,
    int ExitWidth, int ExitHeight, byte[] ExitCells);

/// <summary>Reads MapTable's three live byte planes (DB1F0), the active exit grid
/// (179F90/178FF0), and the physical player layer used by 178FF0. Never loads or names
/// a destination scene: the grid identifies only exit cells in the current area.</summary>
public static class FieldMapCapture
{
    public static bool TryCapture(IReadableMemory memory, FieldNavigationSnapshot field,
        out FieldMapSnapshot snapshot, out string error)
    {
        snapshot = null!;
        error = "The current field map is unavailable or changing.";
        try
        {
            if (!field.SceneIdCoherent || field.SceneId < 0 || field.SceneId > 1023 ||
                !Word(memory, field.FieldState + 0x1010u, out var scene) || scene != field.SceneId ||
                !Grid(memory, field.ActorBase + 0x1100Cu, out var shapes, out var width, out var height) ||
                !Grid(memory, field.ActorBase + 0x11020u, out var flags, out var width1, out var height1) ||
                !Grid(memory, field.ActorBase + 0x11034u, out var layers, out var width2, out var height2) ||
                width != width1 || width != width2 || height != height1 || height != height2 ||
                !Read(memory, field.ScriptData + 0x2E155u, 1, out var playerLayer) || playerLayer[0] is < 1 or > 3 ||
                !Word(memory, field.FieldState + 0x106Cu, out var transition) ||
                !Grid(memory, field.Engine + 0xE70u, out var exits, out var exitWidth, out var exitHeight)) return false;

            if (!Vector(memory, field.Engine + 0xE58u, 4, 1024, out var offsets, out var sceneCount) ||
                scene >= sceneCount || !Word(memory, offsets + (uint)scene * 4, out var firstExit) || firstExit < 0 ||
                !Vector(memory, field.Engine + 0xE64u, 28, 8192, out _, out var recordCount)) return false;
            var endExit = recordCount;
            if (scene + 1 < sceneCount && !Word(memory, offsets + (uint)(scene + 1) * 4, out endExit)) return false;
            if (endExit < firstExit || endExit > recordCount || endExit - firstExit > 128) return false;
            if (exits.Any(value => value < 128 && value >= endExit - firstExit)) return false;
            if (!Word(memory, field.FieldState + 0x1010u, out scene) || scene != field.SceneId) return false;
            snapshot = new(width, height, shapes, flags, layers, playerLayer[0], (transition & 0x90) != 0,
                exitWidth, exitHeight, exits);
            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            error = $"Field map capture failed safely: {exception.GetType().Name}.";
            return false;
        }
    }

    private static bool Grid(IReadableMemory memory, nuint descriptor, out byte[] bytes, out int width, out int height)
    {
        bytes = [];
        width = height = 0;
        if (!Word(memory, descriptor + 12, out width) || !Word(memory, descriptor + 16, out height) ||
            width is < 1 or > 256 || height is < 1 or > 256 ||
            !Vector(memory, descriptor, 1, 65536, out var begin, out var count) || count != width * height) return false;
        return Read(memory, begin, count, out bytes);
    }

    private static bool Vector(IReadableMemory memory, nuint descriptor, int stride, int maximum,
        out nuint begin, out int count)
    {
        begin = 0;
        count = 0;
        if (!Word(memory, descriptor, out var rawBegin) || !Word(memory, descriptor + 4, out var rawEnd)) return false;
        begin = (uint)rawBegin;
        var end = (uint)rawEnd;
        if (begin == 0 || end < begin || (ulong)end - begin > (ulong)(maximum * stride) || (end - begin) % (uint)stride != 0) return false;
        count = (int)((end - begin) / (uint)stride);
        return true;
    }

    private static bool Word(IReadableMemory memory, nuint address, out int value)
    {
        value = 0;
        Span<byte> bytes = stackalloc byte[4];
        if (address == 0 || (ulong)address + 3 > uint.MaxValue || !memory.TryRead(address, bytes)) return false;
        value = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        return true;
    }

    private static bool Read(IReadableMemory memory, nuint address, int count, out byte[] bytes)
    {
        bytes = [];
        if (count is < 1 or > 65536 || address == 0 || (ulong)address + (uint)count - 1 > uint.MaxValue) return false;
        bytes = new byte[count];
        return memory.TryRead(address, bytes);
    }
}
