using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Mod.Menus;

public sealed class FieldSubmenuSource(IReadableMemory memory)
{
    private readonly FieldSubmenuCapture fields = new(memory);
    private readonly SaveSlotCapture saves = new(memory);
    private readonly FieldTechDetailsCapture tech = new(memory);
    private readonly LoadedGameTextCapture messages = new(memory);
    private nuint image;
    public void BindImageBase(nuint value) => image = value;
    public FieldSubmenuSnapshot? Capture(nuint node)
    {
        if (image == 0 || !Word(node, out var vt)) return null;
        if (vt == image + SaveSlotCapture.NodeVtableRva) return saves.Capture(image, node);
        if (vt == image + FieldSubmenuCapture.ClassicTechNodeVtableRva) return tech.Capture(image, node);
        return fields.Capture(image, node);
    }
    public string? Title(nuint node)
    {
        if (image == 0 || !Word(node, out var vt) || !Byte(node + 0x1AD, out var visible) || visible == 0) return null;
        if (vt == image + SaveSlotCapture.NodeVtableRva)
        {
            if (!Word(node + 0x2CC, out var mode) || mode is not (0 or 1 or 4 or 5)) return null;
            return mode == 1 ? messages.Get(image, 0x41, 0x0A) ?? "Load" : messages.Get(image, 0x23, 0x26) ?? "Save";
        }
        if (vt < image || !FieldSubmenuCapture.SupportedNodeVtableRvas.Contains((uint)(vt - image))) return null;
        var (caption, fallback) = (uint)(vt - image) switch
        {
            FieldSubmenuCapture.ClassicItemNodeVtableRva => (0x21, "Inventory"),
            FieldSubmenuCapture.ClassicTechNodeVtableRva => (0x22, "Techs"),
            FieldSubmenuCapture.ClassicFormationNodeVtableRva => (0x25, "Party"),
            _ => (0x20, "Equipment"),
        };
        return messages.Get(image, 0x23, caption) ?? fallback;
    }
    public bool ConfirmationActive(nuint node) => Word(node, out var vt) &&
        vt == image + SaveSlotCapture.NodeVtableRva && saves.ConfirmationActive(node);
    public nuint StandaloneFormation(nuint scene) =>
        image != 0 && fields.TryFindStandaloneFormation(image, scene, out var node) ? node : 0;
    public bool IsAttached(nuint scene, nuint node, bool touch) =>
        Word(scene + (touch ? 0x294u : 0x290u), out var selected) && selected == node &&
        Word(node + 0x16C, out var parent) && parent == scene;
    public bool IsSelectedSaveRecord(nuint node, nuint record) =>
        Word(node + 0x304, out var manager) && Word(manager + 0x2C4, out var key) && key < 20 &&
        Word(node + 0x2D0, out var records) && (ulong)records + key * 0x940UL == (ulong)record;
    public bool OwnsManager(nuint node, nuint manager)
    {
        if (node == 0 || !Word(manager, out var mt) || mt != image + 0x3A5D0C ||
            !Word(node, out var vt)) return false;
        if (vt == image + SaveSlotCapture.NodeVtableRva)
            return Word(node + 0x304, out var own) && own == manager;
        if (vt == image + FieldSubmenuCapture.ClassicTechNodeVtableRva)
            return (Word(node + 0x308, out var techManager) && techManager == manager) ||
                (Word(node + 0x2E8, out var characterManager) && characterManager == manager);
        return fields.OwnsManager(image, node, manager);
    }
    private bool Word(nuint p, out uint value)
    {
        value = 0; Span<byte> data = stackalloc byte[4];
        if (p == 0 || (ulong)p + 4 > 0x100000000UL || !memory.TryRead(p, data)) return false;
        value = BinaryPrimitives.ReadUInt32LittleEndian(data); return true;
    }
    private bool Byte(nuint p, out byte value)
    {
        value = 0; Span<byte> data = stackalloc byte[1];
        if (p == 0 || (ulong)p + 1 > 0x100000000UL || !memory.TryRead(p, data)) return false;
        value = data[0]; return true;
    }
}
