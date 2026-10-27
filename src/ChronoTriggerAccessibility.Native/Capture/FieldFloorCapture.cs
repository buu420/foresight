using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;
using static ChronoTriggerAccessibility.Native.Capture.FieldNavigationCapture;

namespace ChronoTriggerAccessibility.Native.Capture;

public readonly record struct FieldFloorSnapshot(uint Engine, uint ActorBase, int Scene, int Actor,
    int ForceX, int ForceY, int? RunMode, int? RunToggle)
{
    /// <summary>A refused terrain probe disagrees with the bounded committed
    /// force. The force/Run owner is usable; this does not prove the refused surface.</summary>
    public bool TerrainSampleRejected { get; init; }
    public bool? WindGusting { get; init; }
    /// <summary>Atel_0052 actor1 is between its calm wait and the gust flag, changing the wind's
    /// scroll and volume. Null when its current script position is unknown.</summary>
    public bool? WindBuilding { get; init; }
}

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
    // Atel_0052 actor1 brackets the visible gust with local0C = 1/0.
    // Ghidra 169280/1693A0 store native script locals at actorBase+118B0, stride8.
    public const uint DeathPeakWindOffset = 0x118B0 + 12 * 8;

    // Atel_0052 actor1's startup loop, installed file bytes 0x020A..0x0246 (SHA-256 9162AF03...).
    // Opcodes fetch at script+12001+PC, so its PCs are file offsets - 1. 16EB10 (AD) keeps a
    // waiting actor's saved PC (+48) on its AD until the count completes.
    private const int WindLoopStart = 0x0209;
    private static readonly byte[] WindLoop = Convert.FromHexString(
        "AD40E600F80400AD04E600FC0400AD04EB08FFE600F80400AD08E600E00400AD08E600A00400750CAD3C" +
        "E600C00400770CAD04E600E00400EB0820113C");
    // Instruction starts: the scroll/volume ramp before 75 0C (0x022F), the gust through 77 0C,
    // and the calm wait plus the easing back to the base scroll.
    private static readonly int[] WindRamp = [0x020B, 0x0210, 0x0212, 0x0217, 0x0219, 0x021C, 0x0221, 0x0223, 0x0228, 0x022A, 0x022F];
    private static readonly int[] WindGust = [0x0231, 0x0233, 0x0238];
    private static readonly int[] WindCalm = [0x0209, 0x023A, 0x023C, 0x0241, 0x0244];

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
            // 173750 leaves a refused probe in +138 while 1734D0 still applies
            // the committed +88/+8C next frame. In the audited Desert and Geno Dome, retain
            // that bounded movement fact without calling the probe a surface.
            var rejected = unchecked((int)rawX) != x || unchecked((int)rawY) != y;
            if (rejected)
            {
                x = unchecked((int)rawX); y = unchecked((int)rawY);
                if (player.Scene is not (159 or 160 or 161 or 256) ||
                    !(x == 0 && Math.Abs((long)y) is 0 or 8 or 16 or 32 ||
                      y == 0 && Math.Abs((long)x) is 0 or 8 or 16 or 32)) return null;
            }
            int? runMode = null, runToggle = null;
            bool? windGusting = null;
            if (player.Scene == 244 && Word(memory, (nuint)player.ActorBase + DeathPeakWindOffset, out var gust) && gust <= 1)
                windGusting = gust != 0;
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
                !Same(memory, (nuint)motion + ForceXOffset, rawX) ||
                !Same(memory, (nuint)motion + ForceYOffset, rawY) ||
                !Same(memory, (nuint)motion + TerrainFlagsOffset, flags) ||
                !Same(memory, (nuint)state + FieldStateSceneIdOffset, (uint)player.Scene) ||
                !Same(memory, (nuint)renderer + RendererSceneIdOffset, (uint)player.Scene) ||
                !Same(memory, (nuint)state + FieldStatePartySlotTableOffset, (uint)player.Actor * 2) ||
                !Word(memory, (nuint)state + FieldStateControlFlagOffset, out var control) || control == 0 ||
                !Same(memory, (nuint)state + FieldStateInputModeOffset, 0)) return null;
            if (windGusting is { } wind &&
                !Same(memory, (nuint)player.ActorBase + DeathPeakWindOffset, wind ? 1u : 0u)) windGusting = null;
            var windBuilding = windGusting is { } gusting ? WindBuilding(memory, engine, player.ActorBase, gusting) : null;
            return new(player.Engine, player.ActorBase, player.Scene, player.Actor, x, y, runMode, runToggle)
                { WindGusting = windGusting, WindBuilding = windBuilding, TerrainSampleRejected = rejected };
        }
        catch { return null; }
    }

    /// <summary>Actor1's current position in the installed wind loop, read twice. A position
    /// that disagrees with the gust flag, or any other script or actor state, is unknown.</summary>
    private static bool? WindBuilding(IReadableMemory memory, nuint engine, uint actorBase, bool gusting)
    {
        var actor = (nuint)actorBase + ActorArrayOffset + ActorStride;
        Span<byte> loop = stackalloc byte[WindLoop.Length];
        Span<byte> flags = stackalloc byte[1];
        if (!Word(memory, engine + EngineScriptDataPointerOffset, out var script) || script == 0 ||
            !memory.TryRead((nuint)script + ScriptObjectCountOffset + 1 + WindLoopStart, loop) ||
            !loop.SequenceEqual(WindLoop) ||
            !Same(memory, actor + ActorClassTagOffset, 7) ||
            !memory.TryRead(actor + 0x30, flags) || (flags[0] & 0x80) != 0 ||
            !Word(memory, actor + 0x48, out var pc)) return null;
        bool? building = WindRamp.Contains((int)pc) && !gusting ? true :
            WindGust.Contains((int)pc) && gusting || WindCalm.Contains((int)pc) && !gusting ? false : null;
        if (building is null || !Same(memory, actor + 0x48, pc) ||
            !Same(memory, (nuint)actorBase + DeathPeakWindOffset, gusting ? 1u : 0u) ||
            !Same(memory, engine + EngineScriptDataPointerOffset, script)) return null;
        return building;
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
