using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

public sealed record FieldStoryState(int Point, bool MotherIntroducedFriend);

/// <summary>Optional story context. Opcode 18 reads A+110B0 at 161E80 and
/// opcode 5A writes it at 16257C. Global bytes are expanded to dwords:
/// gbiton reads/writes A+110B0+index*4 at 168BD8/168BF5. Atel_0323
/// sets global 0140 bit 0 after the initial conversation and name prompt.</summary>
public static class FieldStoryCapture
{
    public static FieldStoryState? Capture(IReadableMemory memory, FieldNavigationSnapshot field)
    {
        try
        {
            if (!field.SceneIdCoherent || !Matches() ||
                !Word((nuint)field.ActorBase + 0x110B0u, out var point) ||
                !Word((nuint)field.ActorBase + 0x115B0u, out var flags) || !Matches() ||
                !Word((nuint)field.ActorBase + 0x110B0u, out var repeated) || repeated != point) return null;
            return new(point & 255, (flags & 1) != 0);
        }
        catch { return null; }

        bool Matches() => Word((nuint)field.Engine + 0x40u, out var actors) && (uint)actors == field.ActorBase &&
            Word((nuint)field.FieldState + 0x1010u, out var scene) && scene == field.SceneId;
        bool Word(nuint address, out int value)
        {
            value = 0;
            Span<byte> bytes = stackalloc byte[4];
            if (address == 0 || (ulong)address + 3 > uint.MaxValue || !memory.TryRead(address, bytes)) return false;
            value = BinaryPrimitives.ReadInt32LittleEndian(bytes); return true;
        }
    }
}
