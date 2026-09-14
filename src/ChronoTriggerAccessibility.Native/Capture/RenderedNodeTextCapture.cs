using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

/// <summary>Reads visible Label text within one independently proven selected control or detail panel.
/// Retail Node children/parent offsets are audited in WorldViewportCapture; the LabelProtocol
/// getter is audited in BattleFeedbackCapture. This never searches the scene for a selection.</summary>
public sealed class RenderedNodeTextCapture(IReadableMemory memory)
{
    public IReadOnlyList<string>? Read(nuint root)
    {
        var seen = new HashSet<nuint>();
        var result = new List<string>();
        return Visit(root, null, 0, seen, result) ? result.AsReadOnly() : null;
    }

    private bool Visit(nuint node, nuint? parent, int depth, HashSet<nuint> seen, List<string> result)
    {
        if (depth > 16 || seen.Count >= 512 || !seen.Add(node) || !Fits(node, 0x1AE) ||
            !Byte(node + 0x1AD, out var visible) ||
            (parent.HasValue && (!Word(node + 0x16C, out var p) || p != parent.Value))) return false;
        if (visible == 0) return true;

        // Non-Label nodes have different secondary interfaces; do not cast them to Label.
        if (Word(node + 0x278, out var protocol) && Word(protocol + 8, out var getter))
        {
            Span<byte> code = stackalloc byte[4];
            if (Fits(getter, 4) && memory.TryRead(getter, code) &&
                code.SequenceEqual((ReadOnlySpan<byte>)[0x8D, 0x41, 0x28, 0xC3]))
            {
                if (!new MsvcStringReader(memory).TryRead(node + 0x2A0, out var text, out _)) return false;
                if (!string.IsNullOrWhiteSpace(text)) result.Add(text.Trim());
            }
        }
        if (!Word(node + 0x160, out var begin) || !Word(node + 0x164, out var end) ||
            end < begin || (end - begin) % 4 != 0 || end - begin > 512 * 4) return false;
        var children = new byte[end - begin];
        if (children.Length > 0 && (!Fits(begin, (uint)children.Length) || !memory.TryRead(begin, children))) return false;
        for (var i = 0; i < children.Length; i += 4)
        {
            if (!Visit(BinaryPrimitives.ReadUInt32LittleEndian(children.AsSpan(i, 4)), node,
                depth + 1, seen, result)) return false;
        }
        if (!Word(node + 0x160, out var b2) || b2 != begin || !Word(node + 0x164, out var e2) || e2 != end ||
            !Byte(node + 0x1AD, out var v2) || visible != v2 ||
            (parent.HasValue && (!Word(node + 0x16C, out var p2) || p2 != parent.Value))) return false;
        var again = new byte[children.Length];
        return children.Length == 0 || (memory.TryRead(begin, again) && children.AsSpan().SequenceEqual(again));
    }

    private bool Word(nuint address, out uint value)
    {
        value = 0; Span<byte> data = stackalloc byte[4];
        if (!Fits(address, 4) || !memory.TryRead(address, data)) return false;
        value = BinaryPrimitives.ReadUInt32LittleEndian(data); return true;
    }
    private bool Byte(nuint address, out byte value)
    {
        value = 0; Span<byte> data = stackalloc byte[1];
        if (!Fits(address, 1) || !memory.TryRead(address, data)) return false;
        value = data[0]; return true;
    }
    private static bool Fits(nuint address, uint length) => address != 0 && (ulong)address + length <= 0x100000000UL;
}
