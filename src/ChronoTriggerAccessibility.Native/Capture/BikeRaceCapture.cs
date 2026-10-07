using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

public enum BikeRacePhase { Preparing, Racing, Finished }
public enum BikeRaceResult { Unknown, Won, Lost }
public enum BikeRaceLane { Above, Aligned, Below }
public sealed record BikeRaceSnapshot(BikeRacePhase Phase, BikeRaceResult Result,
    int Distance, int Score, bool BoostsEnabled, int Boosts, bool BoostReady,
    int Lead, BikeRaceLane JohnnyLane, bool Paused = false);

public sealed class BikeRaceCapture(IReadableMemory memory)
{
    // Ghidra 2EC620/2ED720/2EDE60, supported executable only. C04 is the
    // special-event phase; 2D8DB0 clears the VM state before C04 becomes 1.
    public const uint VtableRva = 0x3B49C8;
    public BikeRaceSnapshot? Capture(nuint image, nuint scene)
    {
        try
        {
            if (image == 0 || scene == 0 || !Word(scene, out var vtable) || vtable != image + VtableRva ||
                !Word(scene + 0xC04, out var phase) || phase != 1 ||
                !Word(image + 0x41B4BC, out var data) || data == 0 ||
                !Word(scene + 4, out var sceneData) || data != sceneData) return null;
            Span<byte> state = stackalloc byte[0x80];
            Span<byte> hud = stackalloc byte[12];
            Span<byte> resultBytes = stackalloc byte[2];
            Span<byte> mode = stackalloc byte[1];
            Span<byte> pause = stackalloc byte[1];
            if (!Read(data + 0x2E040u, state) || !Read(scene + 0x4EFC, hud) ||
                !Read(data + 0x2E380u, resultBytes) || !Read(scene + 0xC10, mode) ||
                !Read(data + 0x2E392u, pause) || !Word(scene + 0x4EB8, out var score)) return null;
            var boosts = BinaryPrimitives.ReadInt32LittleEndian(hud);
            var cooldown = BinaryPrimitives.ReadInt32LittleEndian(hud[4..]);
            var distance = BinaryPrimitives.ReadInt32LittleEndian(hud[8..]);
            var enabled = mode[0] == 0;
            var playerLane = BinaryPrimitives.ReadUInt16LittleEndian(state[0xE..]);
            var johnnyLane = BinaryPrimitives.ReadUInt16LittleEndian(state[0x33..]);
            var player = BinaryPrimitives.ReadUInt32LittleEndian(state[0x11..]);
            var johnny = BinaryPrimitives.ReadUInt32LittleEndian(state[0x36..]);
            // 1AE7F0/1AEE10 latch the result before the finish animation. High
            // byte contains award flags and is not part of the winner value.
            var result = resultBytes[0] switch { 1 => BikeRaceResult.Won, 2 => BikeRaceResult.Lost, _ => BikeRaceResult.Unknown };
            var running = BinaryPrimitives.ReadUInt16LittleEndian(state[0x7C..]) != 0;
            if (mode[0] > 1 || resultBytes[0] > 2 || distance is < 0 or > 1100 || score > 65535 ||
                playerLane > 0x7000 || johnnyLane > 0x7000 || player > 0x200000 || johnny > 0x200000 ||
                (enabled && (boosts is < 0 or > 3 || cooldown is < 0 or > 128)) ||
                !Word(image + 0x41B4BC, out var dataAgain) || dataAgain != data ||
                !Word(scene, out var typeAgain) || typeAgain != vtable ||
                !Word(scene + 0xC04, out var phaseAgain) || phaseAgain != phase) return null;
            // Native collision overlap is abs(lateral delta) < 0x1000. The
            // overhead track display increases Y with lane (1B47C0).
            var delta = johnnyLane - playerLane;
            var lane = Math.Abs(delta) < 0x1000 ? BikeRaceLane.Aligned : delta < 0 ? BikeRaceLane.Above : BikeRaceLane.Below;
            return new(result != BikeRaceResult.Unknown ? BikeRacePhase.Finished : running ? BikeRacePhase.Racing : BikeRacePhase.Preparing,
                result, distance, (int)score, enabled, enabled ? boosts : 0, enabled && boosts > 0 && cooldown == 0,
                Math.Sign((long)player - johnny), lane, pause[0] != 0);
        }
        catch { return null; }
    }
    private bool Read(nuint address, Span<byte> bytes) => address != 0 &&
        (ulong)address + (uint)bytes.Length - 1 <= uint.MaxValue && memory.TryRead(address, bytes);
    private bool Word(nuint address, out uint value)
    {
        Span<byte> bytes = stackalloc byte[4]; value = 0;
        if (!Read(address, bytes)) return false;
        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes); return true;
    }
}
