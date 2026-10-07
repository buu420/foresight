using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;
using static ChronoTriggerAccessibility.Native.Capture.FieldNavigationCapture;

namespace ChronoTriggerAccessibility.Native.Capture;

public readonly record struct FieldFloorSnapshot(uint Engine, uint ActorBase, int Scene, int Actor,
    int ForceX, int ForceY, int? RunMode, int? RunToggle);

/// <summary>Read-only floor movement at the accepted field-input boundary.
/// Ghidra: 178FF0 writes force to engine+854 -> +88/+8C from +138;
/// 175C90 reads the native Dash mode and toggle at actorBase+13FD0/+13FD4.</summary>
public static class FieldFloorCapture
{
    public const uint EngineMotionPointerOffset = 0x854;
    public const uint ForceXOffset = 0x88;
    public const uint ForceYOffset = 0x8C;
    public const uint TerrainFlagsOffset = 0x138;
    public const uint RunModeOffset = 0x13FD0;
    public const uint RunToggleOffset = 0x13FD4;

    public static FieldFloorSnapshot? Capture(IReadableMemory memory, nuint engine)
    {
        try
        {
            if (FieldMotionCapture.Capture(memory, engine) is not { } player ||
                !Word(memory, engine + EngineMotionPointerOffset, out var motion) || motion == 0 ||
                !Word(memory, engine + EngineFieldStatePointerOffset, out var state) || state == 0 ||
                !Word(memory, engine + EngineRendererPointerOffset, out var renderer) || renderer == 0 ||
                !Word(memory, (nuint)motion + ForceXOffset, out var rawX) ||
                !Word(memory, (nuint)motion + ForceYOffset, out var rawY) ||
                !Word(memory, (nuint)motion + TerrainFlagsOffset, out var flags) || flags > byte.MaxValue) return null;
            var speed = (flags & 12) switch { 4 => 8, 8 => 16, 12 => 32, _ => 0 };
            var (x, y) = (flags & 3) switch
            {
                0 => (0, -speed), 1 => (0, speed), 2 => (-speed, 0), _ => (speed, 0),
            };
            // A rejected terrain probe can retain the previous force. Do not
            // report a newly sampled tile until the force agrees with it.
            if (unchecked((int)rawX) != x || unchecked((int)rawY) != y) return null;
            int? runMode = null, runToggle = null;
            if (Word(memory, (nuint)player.ActorBase + RunModeOffset, out var mode) && mode <= 2 &&
                Word(memory, (nuint)player.ActorBase + RunToggleOffset, out var toggle) && toggle <= 2)
            {
                runMode = (int)mode;
                runToggle = (int)toggle;
            }
            if (!Same(memory, engine + EngineActorBasePointerOffset, player.ActorBase) ||
                !Same(memory, engine + EngineMotionPointerOffset, motion) ||
                !Same(memory, engine + EngineFieldStatePointerOffset, state) ||
                !Same(memory, engine + EngineRendererPointerOffset, renderer) ||
                !Same(memory, (nuint)state + FieldStateSceneIdOffset, (uint)player.Scene) ||
                !Same(memory, (nuint)renderer + RendererSceneIdOffset, (uint)player.Scene) ||
                !Same(memory, (nuint)state + FieldStatePartySlotTableOffset, (uint)player.Actor * 2) ||
                !Word(memory, (nuint)state + FieldStateControlFlagOffset, out var control) || control == 0 ||
                !Same(memory, (nuint)state + FieldStateInputModeOffset, 0)) return null;
            return new(player.Engine, player.ActorBase, player.Scene, player.Actor, x, y, runMode, runToggle);
        }
        catch { return null; }
    }

    private static bool Same(IReadableMemory memory, nuint address, nuint expected) =>
        Word(memory, address, out var value) && value == expected;
    private static bool Word(IReadableMemory memory, nuint address, out uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        value = 0;
        if (address == 0 || (ulong)address + 3 > uint.MaxValue || !memory.TryRead(address, bytes)) return false;
        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        return true;
    }
}
