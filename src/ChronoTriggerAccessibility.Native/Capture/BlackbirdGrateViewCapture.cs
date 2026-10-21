using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

public enum BlackbirdGrateViewKind { Opening, Viewing, Returning }

/// <summary>One proven step of a Blackbird grate view, seen at a field-script dispatch.</summary>
public readonly record struct BlackbirdGrateViewEvent(BlackbirdGrateViewKind Kind, int Scene, uint Context);

/// <summary>The Blackbird ventilation shaft's grate views, at the field-script dispatcher (1619E0).
/// The field input routine 175A40 (the navigation pad hook) runs only while FieldState+108C is set
/// (1734D0), and every view clears it with E3 00, so these dispatches are the only live evidence.
/// Script PCs below are packet offsets (file offset - 1; the packet starts at ScriptData+12001).</summary>
public static class BlackbirdGrateViewCapture
{
    public const int Shaft = 376;
    private const int ShaftScript = 274;
    // Atel_0274 actor0 fn3, file 0x04BE: 75 06, E3 00, party fn3, E8 82, 07 00 44, 65 07 AF.
    private const uint ShaftOpening = 0x4BD;
    private const uint Globals = 0x110B0, ViewGlobal = 0xAF;

    // Each viewed room's actor0 loop: 16 AF 80 (view flag), E3 00 at this PC, 31 (Confirm),
    // then clears AF80 and E0 back to the grate in 376.
    private static readonly Dictionary<int, (int Script, uint Loop)> Rooms = new()
    {
        [367] = (265, 0x18C), // Atel_0265 0x018D, the view loop also handles Dalton's first-view dialogue
        [368] = (266, 0x2C8), // Atel_0266 0x02C9
        [369] = (267, 0x2E8), // Atel_0267 0x02E9
        [372] = (270, 0x188), // Atel_0270 0x0189
        [374] = (272, 0x148), // Atel_0272 0x0149
        [375] = (273, 0x108), // Atel_0273 0x0109
        [444] = (279, 0x108), // Atel_0279 0x0109
        [445] = (280, 0x0C8), // Atel_0280 0x00C9
    };

    // Atel_0274 AA 1D at file 0x04E1/0x04EF/0x0501/0x0518/0x052F: party actors 1..5's fn3/fn4,
    // requested only by actor0 fn3 above (05 xx 53, 07 00 44). It is the look-down pose.
    private static readonly Dictionary<uint, int> LookPoses = new()
    {
        [0x4E0] = 1, [0x4EE] = 2, [0x500] = 3, [0x517] = 4, [0x52E] = 5,
    };

    public static bool IsViewRoom(int scene) => Rooms.ContainsKey(scene);

    /// <summary>Opening: the shaft's actor0 is about to leave for a view. Viewing: a room's
    /// actor0 is in its view loop with AF bit 80 set. Returning: that actor0 is about to E0
    /// back to 376. Anything unproven or unreadable is null, never a state.</summary>
    public static BlackbirdGrateViewEvent? Capture(IReadableMemory memory, nuint context, int opcode)
    {
        if (opcode is not (0xE3 or 0xE0)) return null;
        try
        {
            if (!StoryActionCapture.TryLocate(memory, context, opcode, out var at) || at.Actor != 0) return null;
            Span<byte> bytes = stackalloc byte[3];
            if (!memory.TryRead(at.Data + 0x12001 + at.Address, bytes) || bytes[0] != opcode) return null;
            if (opcode == 0xE3)
            {
                if (bytes[1] != 0) return null;
                if (at.Scene == Shaft && at.ScriptId == ShaftScript && at.Address == ShaftOpening)
                    return new(BlackbirdGrateViewKind.Opening, at.Scene, at.Context);
                return Rooms.TryGetValue(at.Scene, out var room) && room.Script == at.ScriptId && room.Loop == at.Address &&
                    ViewFlag(memory, at.Actors) == true ? new(BlackbirdGrateViewKind.Viewing, at.Scene, at.Context) : null;
            }
            var destination = BinaryPrimitives.ReadUInt16LittleEndian(bytes[1..]) & 0x3FF;
            return Rooms.TryGetValue(at.Scene, out var source) && source.Script == at.ScriptId && destination == Shaft
                ? new(BlackbirdGrateViewKind.Returning, at.Scene, at.Context) : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>The party's look-down pose in the shaft, so a generic gesture is not narrated.</summary>
    public static bool IsLookPose(StoryScriptLocation at) => at.Scene == Shaft && at.ScriptId == ShaftScript &&
        at.Opcode == 0xAA && LookPoses.TryGetValue(at.Address, out var actor) && actor == at.Actor;

    // Global bytes are expanded to dwords at A+110B0 (FieldStoryCapture).
    private static bool? ViewFlag(IReadableMemory memory, uint actors)
    {
        Span<byte> word = stackalloc byte[4];
        return actors != 0 && memory.TryRead(actors + Globals + ViewGlobal * 4, word)
            ? (BinaryPrimitives.ReadUInt32LittleEndian(word) & 0x80) != 0 : null;
    }
}
