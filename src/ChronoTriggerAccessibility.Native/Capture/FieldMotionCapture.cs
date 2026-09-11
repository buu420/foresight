using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;
using static ChronoTriggerAccessibility.Native.Capture.FieldNavigationCapture;

namespace ChronoTriggerAccessibility.Native.Capture;

public readonly record struct FieldMotionSnapshot(uint Engine, uint ActorBase, int Scene, int Actor,
    int FineX, int FineY);

/// <summary>Reads only the controlled lead actor using FieldNavigationCapture's
/// audited layout. No map, path search, or other actors are read for footsteps.</summary>
public static class FieldMotionCapture
{
    public static FieldMotionSnapshot? Capture(IReadableMemory memory, nuint engine)
    {
        try
        {
            if (engine == 0 || !Word(memory, engine + EngineScriptDataPointerOffset, out var script) ||
                !Word(memory, engine + EngineActorBasePointerOffset, out var actors) ||
                !Word(memory, engine + EngineFieldStatePointerOffset, out var state) ||
                !Word(memory, engine + EngineRendererPointerOffset, out var renderer) ||
                script == 0 || actors == 0 || state == 0 || renderer == 0 ||
                !Word(memory, state + FieldStateControlFlagOffset, out var control) || control == 0 ||
                !Word(memory, state + FieldStateInputModeOffset, out var mode) || mode != 0 ||
                !Word(memory, state + FieldStateSceneIdOffset, out var scene) ||
                !Word(memory, renderer + RendererSceneIdOffset, out var renderedScene) || scene != renderedScene ||
                !Word(memory, state + FieldStatePartySlotTableOffset, out var slot) ||
                (slot & 0x81) != 0 || slot / 2 >= MaximumActors) return null;
            Span<byte> count = stackalloc byte[1];
            if (!Read(memory, script + ScriptObjectCountOffset, count) ||
                count[0] is 0 or > MaximumActors || slot / 2 >= count[0]) return null;
            var actor = (int)(slot / 2);
            Span<byte> body = stackalloc byte[(int)ActorStride];
            if (!Read(memory, actors + ActorArrayOffset + (nuint)(actor * ActorStride), body)) return null;
            var x = At(body, ActorFineXOffset);
            var y = At(body, ActorFineYOffset);
            if (At(body, ActorClassTagOffset) is < 0 or > 3 ||
                At(body, ActorDrawModeOffset) != DrawModeDrawn ||
                At(body, ActorFacingOffset) is < 0 or > 3 ||
                x is < 0 or > ushort.MaxValue || y is < 0 or > ushort.MaxValue ||
                At(body, ActorTileXOffset) != x >> 8 || At(body, ActorFractionXOffset) != (x & 255) ||
                At(body, ActorTileYOffset) != y >> 8 || At(body, ActorFractionYOffset) != (y & 255)) return null;
            // Reject a scene/control/leader change during the read.
            if (!Same(memory, engine + EngineActorBasePointerOffset, actors) ||
                !Same(memory, engine + EngineFieldStatePointerOffset, state) ||
                !Same(memory, engine + EngineRendererPointerOffset, renderer) ||
                !Same(memory, engine + EngineScriptDataPointerOffset, script) ||
                !Same(memory, state + FieldStateSceneIdOffset, scene) ||
                !Same(memory, renderer + RendererSceneIdOffset, scene) ||
                !Same(memory, state + FieldStatePartySlotTableOffset, slot) ||
                !Same(memory, state + FieldStateControlFlagOffset, control) ||
                !Same(memory, state + FieldStateInputModeOffset, mode)) return null;
            return new((uint)engine, (uint)actors, (int)scene, actor, x, y);
        }
        catch { return null; }
    }

    private static int At(ReadOnlySpan<byte> bytes, uint offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes[(int)offset..]);
    private static bool Same(IReadableMemory memory, nuint address, nuint expected) =>
        Word(memory, address, out var value) && value == expected;
    private static bool Word(IReadableMemory memory, nuint address, out nuint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        value = 0;
        if (!Read(memory, address, bytes)) return false;
        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        return true;
    }
    private static bool Read(IReadableMemory memory, nuint address, Span<byte> bytes) =>
        address != 0 && (ulong)address + (uint)bytes.Length - 1 <= uint.MaxValue && memory.TryRead(address, bytes);
}
