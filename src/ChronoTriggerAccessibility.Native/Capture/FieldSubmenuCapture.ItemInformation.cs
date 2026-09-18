namespace ChronoTriggerAccessibility.Native.Capture;

public sealed partial class FieldSubmenuCapture
{
    private sealed record ItemInformationFrame(nuint Panel, string Name, int Quantity, int Encoded, string Signature);

    private FieldSubmenuSnapshot? CaptureInventoryInformation(nuint image, nuint node)
    {
        // 1C6740 builds static usable-by labels and item attributes; +304 owns a
        // single nsStateMachine::State. The retained main-list focus is disabled.
        if (!TryReadItemInformationFrame(image, node, out var frame) ||
            rendered.Read(frame.Panel) is not { Count: > 0 and <= MaximumPanelLines } lines)
            return null;
        var heading = text.Get(image, MenuCaptionBank, 0x4F);
        var caption = text.Get(image, MenuCaptionBank, InventoryCaptionMessageId);
        if (string.IsNullOrWhiteSpace(caption) || string.IsNullOrWhiteSpace(heading) || lines[0] != heading)
            return null;
        var body = $"Item information. {frame.Name}, quantity {frame.Quantity}. " +
            JoinRenderedLines(lines).Replace(":, ", ": ", StringComparison.Ordinal).TrimEnd('.') + ".";
        if (TryReadStatusBarDescription(image, node, 0x2CC, out var description) && description.Length > 0)
            body += $" {description}.";
        // 1C8D60 handles both confirm and cancel by calling the same close routine.
        body += " Confirm or Cancel to close.";

        if (!TryReadItemInformationFrame(image, node, out var again) || again != frame ||
            rendered.Read(frame.Panel) is not { } repeated || !lines.SequenceEqual(repeated)) return null;
        return new(InventoryKind, caption, $"inventory:information:{frame.Signature}", body);
    }

    private bool TryReadItemInformationFrame(nuint image, nuint node, out ItemInformationFrame frame)
    {
        frame = null!;
        if (!Pointer(node, out var vt) || vt != image + ClassicItemNodeVtableRva ||
            !Pointer(node + 0x300, out var panel) || !HasAncestor(panel, node) ||
            !Pointer(node + 0x304, out var manager) ||
            !TryReadActiveState(image, node, out var active, out var key, out var state) || active != manager || key != 0 ||
            !Pointer(state, out var stateVt) || stateVt != image + 0x3ABC2C ||
            !Pointer(manager + FocusableMapOffset, out var map) || !UInt32(map + 8, out var count) || count != 1 ||
            !Pointer(node + ItemRowsBeginOffset, out var begin) || !Pointer(node + ItemRowsEndOffset, out var end) ||
            end <= begin || (end - begin) % ItemRowStride != 0 || (end - begin) / ItemRowStride > MaximumRows ||
            !Int32(node + ItemCursorOffset, out var cursor) || cursor < 0 || (nuint)cursor >= (end - begin) / ItemRowStride ||
            !Int32(node + ItemHeldRowOffset, out var held) || held != cursor ||
            !TryReadRow(image, begin, cursor, out var name, out var quantity, out var encoded) ||
            encoded >> 12 >= 4 || quantity > 99) return false;
        frame = new(panel, name, quantity, encoded,
            $"{manager:X}:{state:X}:{panel:X}:{begin:X}:{end:X}:{cursor}:{encoded}:{quantity}");
        return true;
    }
}
