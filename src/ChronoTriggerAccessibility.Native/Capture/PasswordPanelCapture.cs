using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

/// <summary>
/// <c>Absent</c>: a coherent field frame proves there is no live scene-4 letter panel owner
/// (another scene, or no current VirtualPad published). It never claims anything was shown.
/// <c>Hidden</c> / <c>Shown</c>: the scene-4 panel's own show flag, its grid layer's and pad
/// node's cocos2d visibility and its input manager's lock all agree.
/// </summary>
public enum PasswordPanelState { Absent, Hidden, Shown }

/// <summary>
/// One coherent read of Lucca's scene-4 password letter panel (2 rows x 5 cells).
/// <c>FocusKey</c> is the native nsInput manager key 0-9 (row-major) or null when the manager
/// has no focus. <c>Language</c> is the game language index (0 ja ... 8 ko) or -1 when Absent.
/// <c>ControlRestored</c> is FieldState+0x108C, which opcode E3 writes verbatim (handler 163367):
/// the console's activation runs E3 00, and only a rejected letter's return runs E3 01.
/// No expected letter, entry history or progress is read.
/// </summary>
public sealed record PasswordPanelFrame(PasswordPanelState State, uint Context, uint Field, int Scene,
    uint Pad, uint Manager, int? FocusKey, int Language, bool ControlRestored);

/// <summary>
/// Reads the scene-4 VirtualPad letter panel without calling game code. Poll it from the field
/// script dispatcher AFTER the original instruction returns (show 1929C0, hide 192B00, E3 and
/// the nsInput focus all change inside instructions or between dispatches).
/// Evidence: artifacts/research/vortex-desert-lavos-lara-20261026/claude-native-ui-audit.md
/// section 2 and the companion JSON; root's REA re-verified 1929C0 and 191F90.
/// </summary>
public static class PasswordPanelCapture
{
    public const int SceneId = 4;
    public const int ScriptId = 26;
    public const int KeyCount = 10;
    public const int Columns = 5;

    /// <summary>The installed Atel_0026 header: 13 actors and the first function pointers.</summary>
    public const string ScriptHeaderHex = "0DA00145025202520252025202520252";

    /// <summary>DAT_0081C3E0: VirtualPad+0x290 while the field layer is entered (onEnter 2BBAD0,
    /// onExit 2BBAB0 clears it). Read through 240830 by every pad opcode.</summary>
    public const uint CurrentStickRva = 0x41C3E0;
    /// <summary>DAT_0081B4C4, the same actor store as engine+0x40 (FieldNavigationCapture).</summary>
    public const uint GlobalActorsRva = 0x41B4C4;
    /// <summary>2BBBAB: the field layer creates the pad with [[0x81B4C4]+0x120C0] as its scene id.</summary>
    public const uint PadSceneOffset = 0x120C0;
    /// <summary>DAT_007FA168: 0 ja, 1 en, 2 de, 3 it, 4 es, 5 fr, 6 zh-Hans, 7 zh-Hant, 8 ko (table 79AFEC).</summary>
    public const uint LanguageRva = 0x3FA168;

    public const uint VirtualPadVtableRva = 0x3A15B0;
    public const uint VirtualPadStickVtableRva = 0x3A1594;
    public const uint StickInterfaceOffset = 0x290;
    /// <summary>590DE0 stores a typed pad's manager here; the scene-4 grid leaves it zero.</summary>
    public const uint TypedManagerOffset = 0x2C4;
    public const uint PadTypeOffset = 0x2C8;
    public const uint DeferredBuildOffset = 0x2CC;
    public const uint GridLayerOffset = 0x2D0;
    public const uint GridManagerOffset = 0x2D4;
    public const uint ArmedOffset = 0x2D8;
    public const uint ShownOffset = 0x2D9;
    public const uint MaskTableOffset = 0x2DC;
    /// <summary>590DE0 type 0 -> &amp;DAT_0081EDB8 + 0 * 0x30, the table scene 4 runs on.</summary>
    public const uint TypeZeroMaskTableRva = 0x41EDB8;
    public const uint ManagerVtableRva = 0x3A5D0C;
    public const uint ManagerFocusKeyOffset = 0x2C4;
    /// <summary>191F90 and 192B00 write 1, 1929C0 writes 0 (input locked while hidden).</summary>
    public const uint ManagerInputLockOffset = 0x290;
    /// <summary>cocos2d Node visibility byte (libcocos2d Node::isVisible reads +0x1AD).</summary>
    public const uint NodeVisibleOffset = 0x1AD;

    private const uint PadSize = 0x2E0, ScriptHeader = 0x12000, ScriptIdOffset = 0xBB4;
    private const uint NoFocus = 0x80000000;

    // Localize/<lang>/png/button.png, cells (130 + 21 col, 21 row, 21, 21) points (2x texture),
    // cut by 191F90. Read from the installed atlases: en/de/ko 7ADE2B9C..., es DF0FD125...,
    // fr 82BBD177..., it A70841FE... show F R O C Z / A W T L I; ja, zh-Hans and zh-Hant
    // 18134333... show Q where the others show R. DAT_007A1548 puts the R/A/L bits exactly on
    // keys 1/5/8, corroborating the reading. Only the visible glyphs are exposed here.
    private static readonly string[] LatinGlyphs = ["F", "R", "O", "C", "Z", "A", "W", "T", "L", "I"];
    private static readonly string[] EastAsianGlyphs = ["F", "Q", "O", "C", "Z", "A", "W", "T", "L", "I"];
    private static readonly byte[] Header = Convert.FromHexString(ScriptHeaderHex);

    private readonly record struct Owners(uint Data, uint Actors, uint Field, uint Renderer, uint Script,
        uint FieldScene, uint RenderedScene, uint GlobalActors, uint PadScene, uint Stick);

    private readonly record struct PanelIdentity(uint Pad, uint Layer, uint Manager);

    /// <summary>The visible glyph for a native key, or null outside the proven 0..8 x 0..9 grid.</summary>
    public static string? Glyph(int language, int key) =>
        language is < 0 or > 8 || key is < 0 or >= KeyCount ? null
            : (language is 0 or 6 or 7 ? EastAsianGlyphs : LatinGlyphs)[key];

    /// <summary>Null means unknown (unreadable, incoherent, foreign or mid-transition): never a close.</summary>
    public static PasswordPanelFrame? Capture(IReadableMemory memory, nuint imageBase, nuint context)
    {
        try
        {
            if (memory is null || imageBase == 0 || !Fits(imageBase, CurrentStickRva + 4) ||
                !Fits(context, ScriptIdOffset + 4) || !ReadScene(memory, context, out var field, out var scene)) return null;
            // A watched dispatch in another coherent scene costs eight reads including the coherence reread.
            if (scene != SceneId)
                return ReadScene(memory, context, out var fieldAgain, out var sceneAgain) && fieldAgain == field && sceneAgain == scene
                    ? new(PasswordPanelState.Absent, (uint)context, field, (int)scene, 0, 0, null, -1, false)
                    : null;
            if (!ReadOwners(memory, imageBase, context, out var owners) ||
                owners.Field != field || owners.FieldScene != scene || owners.RenderedScene != scene ||
                owners.GlobalActors != owners.Actors) return null;
            if (owners.PadScene != SceneId || owners.Script != ScriptId || !HeaderMatches(memory, owners.Data)) return null;
            if (owners.Stick == 0)
                return StillSame(memory, imageBase, context, owners)
                    ? new(PasswordPanelState.Absent, (uint)context, owners.Field, (int)owners.FieldScene, 0, 0, null, -1, false)
                    : null;
            if (!ReadPanel(memory, imageBase, owners.Stick, out var panel) ||
                !Byte(memory, panel.Pad + ShownOffset, out var shownFlag) ||
                !Byte(memory, panel.Pad + NodeVisibleOffset, out var padVisible) ||
                !Byte(memory, panel.Layer + NodeVisibleOffset, out var layerVisible) ||
                !Byte(memory, panel.Manager + ManagerInputLockOffset, out var locked) ||
                !Word(memory, panel.Manager + ManagerFocusKeyOffset, out var focusRaw) ||
                !Word(memory, imageBase + LanguageRva, out var languageRaw) || languageRaw > 8 ||
                !Word(memory, owners.Field + FieldNavigationCapture.FieldStateControlFlagOffset, out var control)) return null;
            if (shownFlag > 1 || padVisible > 1 || layerVisible > 1 || locked > 1) return null;
            PasswordPanelState state;
            if (shownFlag == 1 && layerVisible == 1 && padVisible == 1 && locked == 0) state = PasswordPanelState.Shown;
            else if (shownFlag == 0 && layerVisible == 0 && locked == 1) state = PasswordPanelState.Hidden;
            else return null;
            int? focus = focusRaw < KeyCount ? (int)focusRaw : null;
            if (state == PasswordPanelState.Shown && focus is null && focusRaw != NoFocus) return null;
            // Identities are reread around the state: a changed owner is never reported.
            if (!StillSame(memory, imageBase, context, owners) ||
                !ReadPanel(memory, imageBase, owners.Stick, out var again) || again != panel) return null;
            return new(state, (uint)context, owners.Field, (int)owners.FieldScene, panel.Pad, panel.Manager, focus,
                (int)languageRaw, control != 0);
        }
        catch (Exception) { return null; }
    }

    /// <summary>FieldNavigationCapture's coherence rule: fieldState+0x1010 must equal the renderer's +0x2A0.</summary>
    private static bool ReadScene(IReadableMemory memory, nuint context, out uint field, out uint scene)
    {
        scene = 0;
        return Word(memory, context + FieldNavigationCapture.EngineFieldStatePointerOffset, out field) &&
            Fits(field, FieldNavigationCapture.FieldStateControlFlagOffset + 4) &&
            Word(memory, field + FieldNavigationCapture.FieldStateSceneIdOffset, out scene) && scene <= 0xFFFF &&
            Word(memory, context + FieldNavigationCapture.EngineRendererPointerOffset, out var renderer) &&
            Fits(renderer, FieldNavigationCapture.RendererSceneIdOffset + 4) &&
            Word(memory, renderer + FieldNavigationCapture.RendererSceneIdOffset, out var rendered) && rendered == scene;
    }

    private static bool ReadOwners(IReadableMemory memory, nuint imageBase, nuint context, out Owners owners)
    {
        owners = default;
        if (!Word(memory, context + FieldNavigationCapture.EngineScriptDataPointerOffset, out var data) ||
            !Word(memory, context + FieldNavigationCapture.EngineActorBasePointerOffset, out var actors) ||
            !Word(memory, context + FieldNavigationCapture.EngineFieldStatePointerOffset, out var field) ||
            !Word(memory, context + FieldNavigationCapture.EngineRendererPointerOffset, out var renderer) ||
            !Word(memory, context + ScriptIdOffset, out var script) ||
            !Fits(data, ScriptHeader + 16) || !Fits(actors, PadSceneOffset + 4) ||
            !Fits(field, FieldNavigationCapture.FieldStateControlFlagOffset + 4) ||
            !Fits(renderer, FieldNavigationCapture.RendererSceneIdOffset + 4) ||
            !Word(memory, field + FieldNavigationCapture.FieldStateSceneIdOffset, out var fieldScene) || fieldScene > 0xFFFF ||
            !Word(memory, renderer + FieldNavigationCapture.RendererSceneIdOffset, out var renderedScene) ||
            !Word(memory, imageBase + GlobalActorsRva, out var globalActors) ||
            !Word(memory, actors + PadSceneOffset, out var padScene) ||
            !Word(memory, imageBase + CurrentStickRva, out var stick)) return false;
        owners = new(data, actors, field, renderer, script, fieldScene, renderedScene, globalActors, padScene, stick);
        return true;
    }

    private static bool StillSame(IReadableMemory memory, nuint imageBase, nuint context, Owners owners) =>
        ReadOwners(memory, imageBase, context, out var again) && again == owners;

    private static bool ReadPanel(IReadableMemory memory, nuint imageBase, uint stick, out PanelIdentity panel)
    {
        panel = default;
        if (stick < StickInterfaceOffset) return false;
        var pad = stick - StickInterfaceOffset;
        if (!Fits(pad, PadSize) ||
            !Word(memory, pad, out var padVtable) || padVtable != imageBase + VirtualPadVtableRva ||
            !Word(memory, pad + StickInterfaceOffset, out var stickVtable) || stickVtable != imageBase + VirtualPadStickVtableRva ||
            !Word(memory, pad + TypedManagerOffset, out var typedManager) || typedManager != 0 ||
            !Word(memory, pad + PadTypeOffset, out var type) || type != 0 ||
            !Byte(memory, pad + DeferredBuildOffset, out var deferred) || deferred != 0 ||
            !Byte(memory, pad + ArmedOffset, out var armed) || armed != 1 ||
            !Word(memory, pad + MaskTableOffset, out var masks) || masks != imageBase + TypeZeroMaskTableRva ||
            !Word(memory, pad + GridLayerOffset, out var layer) || !Fits(layer, NodeVisibleOffset + 1) ||
            !Word(memory, pad + GridManagerOffset, out var manager) || !Fits(manager, ManagerFocusKeyOffset + 4) ||
            !Word(memory, manager, out var managerVtable) || managerVtable != imageBase + ManagerVtableRva) return false;
        panel = new(pad, layer, manager);
        return true;
    }

    private static bool HeaderMatches(IReadableMemory memory, uint data)
    {
        Span<byte> header = stackalloc byte[16];
        return memory.TryRead(data + ScriptHeader, header) && header.SequenceEqual(Header);
    }

    private static bool Fits(nuint address, uint length) => address != 0 && (ulong)address + length - 1 <= uint.MaxValue;

    private static bool Byte(IReadableMemory memory, nuint address, out byte value)
    {
        value = 0;
        Span<byte> bytes = stackalloc byte[1];
        if (!Fits(address, 1) || !memory.TryRead(address, bytes)) return false;
        value = bytes[0];
        return true;
    }

    private static bool Word(IReadableMemory memory, nuint address, out uint value)
    {
        value = 0;
        Span<byte> bytes = stackalloc byte[4];
        if (!Fits(address, 4) || !memory.TryRead(address, bytes)) return false;
        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        return true;
    }
}
