using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

/// <summary>The live rising-particle effect above Porre's manor, not its saved quest flag.</summary>
public static class WorldSparkleCapture
{
    // Event_0000.dat file 0F41..0F8B, loaded at bank 1 PC 1341. This guards
    // the spawner and both child draw commands, not a quest-state inference.
    private static readonly byte[] Script = Convert.FromHexString(
        "010602B00A100A1130892C50015003097A137F38142C54015403097A137F38142C4C014C03097A137F38142C4C015403097A137F38141A4A130D2B24390C2E000000002F0080FFFF313052");

    public static IReadOnlyList<WorldPixelPoint> Capture(IReadableMemory memory, WorldMotionSnapshot motion) => Capture(memory, motion, out _);

    public static IReadOnlyList<WorldPixelPoint> Capture(IReadableMemory memory, WorldMotionSnapshot motion, out string stage)
    {
        stage = "not world0";
        if (motion.World != 0 || motion.ScriptData == 0) return [];
        try
        {
            nuint data = motion.ScriptData;
            Span<byte> script = stackalloc byte[75];
            Span<byte> actors = stackalloc byte[0x1000];
            Span<byte> second = stackalloc byte[0x1000];
            stage = "script guard unreadable";
            if (!memory.TryRead(data + 0x11341, script)) return [];
            stage = "script guard mismatch";
            if (!script.SequenceEqual(Script)) return [];
            stage = "actor table unreadable";
            if (!memory.TryRead(data + 0x2EB30, actors) || !memory.TryRead(data + 0x2EB30, second)) return [];
            stage = "actor table changed";
            if (!actors.SequenceEqual(second)) return [];
            stage = "script guard changed";
            if (!memory.TryRead(data + 0x11341, script) || !script.SequenceEqual(Script)) return [];
            var spawner = false;
            var children = new List<WorldPixelPoint>(4);
            // Native newevent allocates slots 4..63. A newly created child at 137A
            // has not drawn yet; anmwait/move resume at 137D/1389 after drawing.
            for (var i = 4; i < 64; i++)
            {
                var actor = actors.Slice(i * 0x40, 0x40);
                if (BinaryPrimitives.ReadUInt16LittleEndian(actor) != 0x0F63 || actor[9] != 1 ||
                    (actor[0xF] & 0xBE) != 0xB6) continue;
                var pc = BinaryPrimitives.ReadUInt16LittleEndian(actor[7..]);
                if (pc is 0x1341 or 0x1354 or 0x135F or 0x136A or 0x1375) spawner = true;
                if (pc is not (0x137D or 0x1389)) continue;
                var x = BinaryPrimitives.ReadUInt16LittleEndian(actor[0x14..]);
                var y = BinaryPrimitives.ReadUInt16LittleEndian(actor[0x18..]);
                // Four native origins, followed by at most 24 pixels of rise.
                if (x is 332 or 336 or 340 && y is >= 820 and <= 852) children.Add(new(x, y));
            }
            stage = !spawner ? "no live spawner" : children.Count == 0 ? "no drawn child" : "drawn children";
            return spawner ? children.AsReadOnly() : [];
        }
        catch { stage = "effect read failed"; return []; }
    }
}
