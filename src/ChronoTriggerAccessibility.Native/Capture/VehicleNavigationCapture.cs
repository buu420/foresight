using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

/// <summary>Values equal the native transport byte D+2E27E while attached.</summary>
public enum VehicleKind { Epoch = 2, Dactyl = 3 }

/// <summary>A vehicle under player control at its own task boundary (Epoch task 28E1D0
/// actor state 3 hovering or 4 moving; Dactyl task 28A1E0 states 3 or 4).</summary>
public sealed record VehicleMotionSnapshot(uint Context, uint ScriptData, uint ActorBase, uint Renderer,
    int World, VehicleKind Kind, int ActorOffset, int ActorState, int PixelX, int PixelY, int Flags)
{
    /// <summary>D+2E294 bit 0x20: the Epoch can fly over the map. Always false for the Dactyls.</summary>
    public bool Wings => Kind == VehicleKind.Epoch && (Flags & 0x20) != 0;
    /// <summary>Actor +2A/+2C: the already committed endpoint while actor state is 4.</summary>
    public WorldPixelPoint? SegmentEnd { get; init; }
}

/// <summary>Native overlap record: left, right, top and bottom extents, in pixels.
/// 264530/264690 subtract the opposing extents and require a strictly negative result.</summary>
public sealed record VehicleContactShape(int Left, int Right, int Top, int Bottom);

public sealed record VehicleBlackOmen(int X, int Y, int ActorOffset, VehicleContactShape Shape, bool ContactPrompt);

/// <summary>Parked vehicles and native prompt flags. World layer2951B0 draws the
/// exclamation icon from boarding A+109B5 and landing A+109B6.</summary>
public sealed record WorldVehicleState(bool EpochPresent, int EpochX, int EpochY, bool EpochWings,
    bool DactylsPresent, int DactylX, int DactylY, int Transport, int MasterAction,
    bool BoardingPrompt, bool LandingPrompt)
{
    public VehicleContactShape? EpochShape { get; init; }
    public VehicleContactShape? DactylShape { get; init; }
    public VehicleContactShape? PartyBoardingShape { get; init; }
    public VehicleContactShape? EpochBoardingShape { get; init; }
    public VehicleContactShape? DactylBoardingShape { get; init; }
    public VehicleBlackOmen? BlackOmen { get; init; }
}

public static class VehicleNavigationCapture
{
    public const int EpochTask = 0x42DD;
    public const int DactylTask = 0x4CC5;
    public const int BlackOmenTask = 0x49CF;
    private const int FirstActor = 0xB30, ActorStride = 0x40, ActorCount = 64;

    public static VehicleMotionSnapshot? Motion(IReadableMemory memory, nuint imageBase, nuint context,
        VehicleKind kind, out string stage)
    {
        stage = "vehicle identity";
        try
        {
            var first = Header(memory, imageBase, context, kind, out stage);
            if (first is null) return null;
            var second = Header(memory, imageBase, context, kind, out stage);
            if (first != second) { stage = "vehicle changed during capture"; return null; }
            stage = "ready"; return first;
        }
        catch { stage = "vehicle read failed"; return null; }
    }

    /// <summary>Only meaningful at the vehicle task's own tick or pad boundary, where
    /// D+2E04E names that vehicle actor. A parked or foreign vehicle never qualifies.</summary>
    private static VehicleMotionSnapshot? Header(IReadableMemory memory, nuint imageBase, nuint context,
        VehicleKind kind, out string stage)
    {
        stage = "vehicle identity";
        if (!Enum.IsDefined(kind) ||
            !WorldNavigationCapture.Identity(memory, imageBase, context, out var data, out var actors, out var renderer, out var world)) return null;
        stage = "vehicle player control";
        if (!Value(memory, data + 0x20980u, 1, out var control) || (control & 0x80) != 0 ||
            !Value(memory, data + 0x2E27Cu, 1, out var mode) || mode != 1 ||
            !Value(memory, data + 0x2E27Eu, 1, out var transport) || transport != (uint)kind ||
            !Value(memory, data + 0x2E280u, 1, out var scripted) || scripted != 0) return null;
        stage = "vehicle flight state";
        var flagAddress = kind == VehicleKind.Epoch ? 0x2E294u : 0x2E29Eu;
        if (!Value(memory, data + flagAddress, 1, out var flags) || (flags & 0x40) == 0) return null;
        if (kind == VehicleKind.Epoch && (flags & 0x20) == 0) return null;
        if (kind == VehicleKind.Dactyl && (flags & 0x20) != 0) return null;
        stage = "vehicle actor";
        if (!Value(memory, data + 0x2E04Eu, 2, out var actor) || actor < FirstActor ||
            (actor - FirstActor) % ActorStride != 0 || (actor - FirstActor) / ActorStride >= ActorCount ||
            !Value(memory, data + 0x2E000u + actor, 2, out var task) ||
            task != (kind == VehicleKind.Epoch ? EpochTask : DactylTask) ||
            !Value(memory, data + 0x2E002u + actor, 1, out var state) || (state & 0x7F) is not (3 or 4)) return null;
        stage = "vehicle position";
        var position = kind == VehicleKind.Epoch ? 0x2E290u : 0x2E29Au;
        if (!Value(memory, data + position, 2, out var x) || x >= 1536 ||
            !Value(memory, data + position + 2, 2, out var y) || y >= 1024) return null;
        WorldPixelPoint? segmentEnd = null;
        if ((state & 0x7F) == 4)
        {
            stage = "vehicle segment endpoint";
            if (!Value(memory, data + 0x2E02Au + actor, 2, out var targetX) || targetX >= 1536 ||
                !Value(memory, data + 0x2E02Cu + actor, 2, out var targetY) || targetY >= 1024) return null;
            segmentEnd = new((int)targetX, (int)targetY);
        }
        return new((uint)context, data, actors, renderer, (int)world, kind, (int)actor, (int)(state & 0x7F), (int)x, (int)y, (int)flags)
        { SegmentEnd = segmentEnd };
    }

    /// <summary>Reads the parked vehicles and prompt flags for an already identified world.
    /// Returns null when any byte is unreadable; the caller keeps its walking frame.</summary>
    public static WorldVehicleState? State(IReadableMemory memory, nuint data, nuint actors, int world)
    {
        try
        {
            if (!Value(memory, data + 0x2E294u, 1, out var epochFlags) || !Value(memory, data + 0x2E29Fu, 2, out var epochMap) ||
                !Value(memory, data + 0x2E290u, 2, out var epochX) || !Value(memory, data + 0x2E292u, 2, out var epochY) ||
                !Value(memory, data + 0x2E29Eu, 1, out var dactylFlags) ||
                !Value(memory, data + 0x2E29Au, 2, out var dactylX) || !Value(memory, data + 0x2E29Cu, 2, out var dactylY) ||
                !Value(memory, data + 0x2E27Eu, 1, out var transport) || !Value(memory, data + 0x2E27Cu, 1, out var action) ||
                !Value(memory, actors + 0x109B5u, 1, out var boarding) || !Value(memory, actors + 0x109B6u, 1, out var landing))
                return null;
            var epochPresent = (epochFlags & 0x80) != 0 && (epochMap & 0x1FF) == 496 + world && epochX < 1536 && epochY < 1024;
            var dactylsPresent = (dactylFlags & 0x80) != 0 && world == 3 && dactylX < 1536 && dactylY < 1024;
            return new(epochPresent, (int)epochX, (int)epochY, (epochFlags & 0x20) != 0,
                dactylsPresent, (int)dactylX, (int)dactylY, (int)transport, (int)action, boarding != 0, landing != 0)
            {
                EpochShape = Shape(memory, data + 0x34946u), DactylShape = Shape(memory, data + 0x3494Eu),
                PartyBoardingShape = Shape(memory, data + 0x3324Cu),
                EpochBoardingShape = Shape(memory, data + 0x33254u), DactylBoardingShape = Shape(memory, data + 0x3325Cu),
                BlackOmen = BlackOmen(memory, data, world, epochFlags, transport),
            };
        }
        catch { return null; }
    }

    private static VehicleContactShape? Shape(IReadableMemory memory, nuint address)
    {
        Span<byte> bytes = stackalloc byte[8]; Span<byte> second = stackalloc byte[8];
        if (!memory.TryRead(address, bytes) || !memory.TryRead(address, second) || !bytes.SequenceEqual(second)) return null;
        var left = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
        var right = BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..]);
        var top = BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]);
        var bottom = BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]);
        return left < 1536 && right < 1536 && top < 1024 && bottom < 1024 ? new(left, right, top, bottom) : null;
    }

    /// <summary>Event_0000/1/2/6 spawn task49CF only after counterD4 and while
    /// world persistence byte1BAE has that era's bit. The actor itself is required
    /// as well: saved coordinates alone survive era changes and are not presence.</summary>
    private static VehicleBlackOmen? BlackOmen(IReadableMemory memory, nuint data, int world, uint epochFlags, uint transport)
    {
        var mask = world switch { 0 => 8, 1 => 4, 2 => 16, 6 => 2, _ => 0 };
        if (mask == 0 || !Value(memory, data + 0x2FBA6u, 1, out var point) || point < 212 ||
            !Value(memory, data + 0x2FBAEu, 1, out var surviving) || (surviving & mask) == 0 ||
            !Value(memory, data + 0x2E2A2u, 2, out var x) || x >= 1536 ||
            !Value(memory, data + 0x2E2A4u, 2, out var y) || y >= 1024 || Shape(memory, data + 0x34956u) is not { } shape) return null;
        Span<byte> actors = stackalloc byte[ActorStride * ActorCount];
        Span<byte> second = stackalloc byte[ActorStride * ActorCount];
        if (!memory.TryRead(data + 0x2E000u + FirstActor, actors) ||
            !memory.TryRead(data + 0x2E000u + FirstActor, second) || !actors.SequenceEqual(second)) return null;
        for (var i = 0; i < ActorCount; i++)
            if (BinaryPrimitives.ReadUInt16LittleEndian(actors[(i * ActorStride)..]) == BlackOmenTask)
                return new((int)x, (int)y, FirstActor + i * ActorStride, shape,
                    transport == 2 && (epochFlags & 0x18) == 8 &&
                    Value(memory, data + 0x2FB58u, 1, out var name) && name == 76);
        return null;
    }

    /// <summary>Prompt flags and transport for a context whose world identity holds. Cheap
    /// enough for every tick; null when the context is not the live world.</summary>
    public static WorldVehicleState? State(IReadableMemory memory, nuint imageBase, nuint context)
    {
        try
        {
            return WorldNavigationCapture.Identity(memory, imageBase, context, out var data, out var actors, out _, out var world)
                ? State(memory, data, actors, (int)world) : null;
        }
        catch { return null; }
    }

    private static bool Value(IReadableMemory memory, nuint address, int size, out uint value) =>
        WorldNavigationCapture.Value(memory, address, size, out value);
}
