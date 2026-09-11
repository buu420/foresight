using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

public sealed record WorldMotionSnapshot(uint Context, uint ScriptData, uint ActorBase, uint Renderer,
    int World, int Actor, int PixelX, int PixelY);

public readonly record struct WorldPixelPoint(int X, int Y);
public sealed record WorldEntrance(int Index, int TileX, int TileY, int NameIndex, int Destination,
    int Facing, int DestinationX, int DestinationY, bool Available)
{
    public IReadOnlyList<WorldPixelPoint> ContactPoints { get; init; } = [];
}

public sealed record WorldNavigationSnapshot(WorldMotionSnapshot Motion, byte[] Map, byte[] Properties,
    IReadOnlyList<WorldEntrance> Entrances, FieldViewport Viewport, int? StoryPoint)
{
    public int EraMessageIndex { get; init; }
    public bool IsVisible(int fineX, int fineY)
    {
        for (var x = -1; x <= 1; x++)
            for (var y = -1; y <= 1; y++)
                if (Viewport.Contains(fineX + x * 1536 * 16, fineY + y * 1024 * 16)) return true;
        return false;
    }
}

public static class WorldNavigationCapture
{
    public static WorldNavigationSnapshot? Capture(IReadableMemory memory, nuint imageBase, nuint context, out string stage)
    {
        stage = "world identity";
        try
        {
            var motion = Header(memory, imageBase, context, out stage);
            if (motion is null) return null;
            nuint data = motion.ScriptData, actors = motion.ActorBase;
            stage = "world viewport";
            var viewport = WorldViewportCapture.Capture(memory, imageBase, motion.Renderer);
            if (viewport is null) return null;
            stage = "world collision map";
            var map = new byte[6144]; var properties = new byte[512];
            if (!memory.TryRead(data + 0x23800, map) || !memory.TryRead(data + 0x25000, properties)) return null;
            stage = "world entrances";
            if (!Value(memory, data + 0x2FB38, 1, out var count)) return null;
            var records = new byte[count * 8];
            if (!memory.TryRead(data + 0x25E00, records)) return null;
            var entrances = new List<WorldEntrance>((int)count);
            for (var i = 0; i < count; i++)
            {
                var record = records.AsSpan(i * 8, 8);
                var x = record[0] & 0x7F; var y = record[1] & 0x3F;
                entrances.Add(new(i, x, y, record[2], BinaryPrimitives.ReadUInt16LittleEndian(record[3..]),
                    record[5], record[6], record[7], (record[0] & 0x80) != 0)
                {
                    ContactPoints = [new(x * 16, y * 16), new(x * 16 + 8, y * 16),
                        new(x * 16, y * 16 + 8), new(x * 16 + 8, y * 16 + 8)]
                });
            }
            stage = "world story state";
            if (!Value(memory, actors + 0x110B0, 4, out var progress) || progress > 255 ||
                !Value(memory, data + 0x2FBAD, 1, out var eraKnown)) return null;
            var era = motion.World switch
            {
                0 => 106, 1 => (eraKnown & 1) != 0 ? 107 : 111,
                2 => progress >= 54 ? 108 : 111, 3 => 109, >= 4 and <= 6 => 110, _ => 0
            };
            var recheck = new byte[records.Length];
            if (Header(memory, imageBase, context, out _) != motion ||
                !Value(memory, actors + 0x110B0, 4, out var secondProgress) || secondProgress != progress ||
                !Value(memory, data + 0x2FBAD, 1, out var secondEraKnown) || secondEraKnown != eraKnown ||
                !Value(memory, data + 0x2FB38, 1, out var secondCount) || secondCount != count ||
                !memory.TryRead(data + 0x25E00, recheck) || !records.AsSpan().SequenceEqual(recheck) ||
                WorldViewportCapture.Capture(memory, imageBase, motion.Renderer) != viewport)
            { stage = "world changed during capture"; return null; }
            stage = "ready";
            return new(motion, map, properties, entrances.AsReadOnly(), viewport.Value, (int)progress) { EraMessageIndex = era };
        }
        catch { stage = "world read failed: " + stage; return null; }
    }

    public static WorldMotionSnapshot? Motion(IReadableMemory memory, nuint imageBase, nuint context, out string stage)
    {
        stage = "world identity";
        try
        {
            var first = Header(memory, imageBase, context, out stage);
            if (first is null) return null;
            var second = Header(memory, imageBase, context, out stage);
            if (first != second) { stage = "world changed during capture"; return null; }
            stage = "ready"; return first;
        }
        catch { stage = "world read failed"; return null; }
    }

    // Only call at the positively identified world walking-task boundary. These
    // persistent script bytes do not independently identify which mode is active.
    private static WorldMotionSnapshot? Header(IReadableMemory memory, nuint imageBase, nuint context, out string stage)
    {
        stage = "world identity";
        if (imageBase == 0 || context == 0 || (ulong)context > uint.MaxValue ||
            !Value(memory, imageBase + 0x41B4BCu, 4, out var data) || data == 0 ||
            !Value(memory, imageBase + 0x41B4C4u, 4, out var actors) || actors == 0 ||
            !Value(memory, context, 4, out var contextData) || contextData != data ||
            !Value(memory, context + 0x40u, 4, out var contextActors) || contextActors != actors ||
            !Value(memory, context + 0x1E68u, 4, out var renderer) || renderer == 0 ||
            !Value(memory, context + 0x3324u, 4, out var world) || world > 7 ||
            !Value(memory, (nuint)renderer + 0x290u, 4, out var renderedWorld) || world != renderedWorld ||
            !Value(memory, (nuint)data + 0x2E100u, 2, out var destination) || destination != 496 + world) return null;
        stage = "world player control";
        if (!Value(memory, (nuint)data + 0x20980u, 1, out var control) || (control & 0x80) != 0 ||
            !Value(memory, (nuint)data + 0x2E27Cu, 1, out var mode) || mode != 1 ||
            !Value(memory, (nuint)data + 0x2E27Eu, 1, out var transport) || transport != 0 ||
            !Value(memory, (nuint)data + 0x2E280u, 1, out var scripted) || scripted != 0) return null;
        stage = "world position";
        // These globals are updated after actual walking and feed the native
        // entrance test. D+2E04E is transient script-actor scratch outside that
        // call, so it must not identify the player for movement sampling.
        if (!Value(memory, (nuint)data + 0x2E283u, 2, out var x) || x >= 1536 ||
            !Value(memory, (nuint)data + 0x2E285u, 2, out var y) || y >= 1024) return null;
        return new((uint)context, data, actors, renderer, (int)world, (int)control, (int)x, (int)y);
    }

    internal static bool Value(IReadableMemory memory, nuint address, int size, out uint value)
    {
        value = 0; Span<byte> bytes = stackalloc byte[4];
        if (address == 0 || (ulong)address + (uint)size - 1 > uint.MaxValue || !memory.TryRead(address, bytes[..size])) return false;
        value = size switch { 1 => bytes[0], 2 => BinaryPrimitives.ReadUInt16LittleEndian(bytes), _ => BinaryPrimitives.ReadUInt32LittleEndian(bytes) };
        return true;
    }
}
