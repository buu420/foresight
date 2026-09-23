using System.Buffers.Binary;
using System.Collections.ObjectModel;
using ChronoTriggerAccessibility.Native.Memory;

namespace ChronoTriggerAccessibility.Native.Capture;

/// <summary>
/// One live field actor, in the game's own units. No labels are inferred here:
/// every value is the raw native field.
/// </summary>
/// <param name="Index">Script actor index. The script encoding is index * 2.</param>
/// <param name="TileX">Tile column. Natively <c>FineX &gt;&gt; 8</c>.</param>
/// <param name="FineX">Tile column in 1/256 sub-tile fixed point.</param>
/// <param name="FractionX">Sub-tile column, <c>FineX &amp; 0xFF</c>.</param>
/// <param name="Facing">0..3, written by the four ActorFacingSet opcodes.</param>
/// <param name="DrawMode">0 hidden, 1 drawn, 0x80 removed.</param>
public sealed record FieldActorSnapshot(
    int Index,
    int TileX,
    int FineX,
    int FractionX,
    int TileY,
    int FineY,
    int FractionY,
    int Facing,
    int DrawMode,
    int LoadedFlag,
    int VisualIndex,
    int ClassTag,
    int RenderPriorityPacked,
    int ActivationEnabled,
    int ActivationBinding,
    bool IsPartyMember,
    bool CoordinatesCoherent,
    bool FacingValid,
    bool DrawModeKnown)
{
    /// <summary>Native contact center adjustment at +14C, in pixels (178980).</summary>
    public int CollisionOffsetX { get; init; }
    /// <summary>Persistent call gates at actor+E8 and actor+30 bit80, tested by
    /// native confirm/touch dispatch (17FA20 / 16EF30). Neither is camera culling.</summary>
    public bool ScriptCallsEnabled { get; init; } = true;
    /// <summary>
    /// True when <see cref="ClassTag"/> names a real class in CTViewer's
    /// <c>SceneActorClass</c> space (0..6). 7 is Undefined, i.e. a slot no load path
    /// ever touched.
    /// </summary>
    public bool ClassTagKnown =>
        (ClassTag & ~FieldNavigationCapture.ClassTagRemovedBit) is >= 0 and <= 6;

    /// <summary>
    /// PC1/PC2/PC3/PCOutOfParty. Party membership is decided by the slot table, not by
    /// this - an out-of-party PC is still a player class.
    /// </summary>
    public bool IsPlayerClass =>
        (ClassTag & ~FieldNavigationCapture.ClassTagRemovedBit) is >= 0 and <= 3;

    /// <summary>
    /// Reproduces the native interaction filter at 0x1760B0 exactly: the activation byte
    /// and binding must both be non-zero, the class tag must not carry the removed bit,
    /// and party members are never their own targets. This is a transient scan
    /// state, not a standing capability: 161560 owns +152, while 17A6C0/17A860
    /// clear +20 outside the camera. Guide eligibility must use script contracts.
    /// </summary>
    public bool IsActivationCandidate =>
        ActivationEnabled != 0 &&
        ActivationBinding != 0 &&
        (ClassTag & FieldNavigationCapture.ClassTagRemovedBit) == 0 &&
        !IsPartyMember;

    /// <summary>True only for the audited "dshow" state; hidden and removed are not visible.</summary>
    public bool IsDrawn => DrawModeKnown && DrawMode == FieldNavigationCapture.DrawModeDrawn;

    /// <summary>
    /// True when every audited invariant holds for this actor. Slots that a scene never
    /// loaded can legitimately be incoherent, so callers must filter on this rather than
    /// assuming the whole snapshot failed.
    /// </summary>
    public bool IsUsable => CoordinatesCoherent && FacingValid && DrawModeKnown;
}

/// <summary>
/// One coherent read of the field engine's navigation state.
/// </summary>
public sealed record FieldNavigationSnapshot(
    uint Engine,
    uint ActorBase,
    uint FieldState,
    uint ScriptData,
    int ActorCount,
    int SceneId,
    bool SceneIdCoherent,
    int ControlFlag,
    int InputMode,
    int ScriptActorIndex,
    int LeadPlayerActorIndex,
    FieldActorSnapshot? LeadPlayer,
    IReadOnlyList<FieldActorSnapshot> Actors,
    IReadOnlyList<int> PartySlotActorIndices = null!)
{
    public int LastPartySlotRaw { get; init; } = 0x80;
    public int ActorCollisionRadius { get; init; } = 160;
}

/// <summary>
/// Reads live field navigation state out of the 0xE88 field engine at
/// <c>FieldScene + 0x290</c>. Capture only: nothing here hooks, writes or moves anything.
///
/// Every constant below is fixed by two script coordinate setters that agree on the
/// whole addressing chain:
/// <list type="bullet">
/// <item>opcode 0x8B "xy" (<c>ActorCoordinatesSet</c>) at RVA 0x16C040</item>
/// <item>opcode 0x8D "dotxy" (<c>ActorCoordinatesSetPrecise</c>) at RVA 0x16C2C0</item>
/// </list>
/// Both begin:
/// <code>
/// MOV  EAX,[EDI+0x850]          ; field state
/// MOV  EBX,[EDI+0x40]           ; actor base pointer
/// ADD  EBX,0x6940               ; actor array
/// MOV  EAX,[EAX+0x1180]         ; acting actor, index * 2
/// SAR  EAX,1
/// IMUL EDX,EAX,0x154            ; actor stride
/// ADD  EBX,EDX
/// </code>
/// and then write the coordinate triples quoted on each constant.
/// </summary>
public static class FieldNavigationCapture
{
    /// <summary>Engine word 0 is the script/WRAM base; opcodes fetch at base + 0x12001 + pc.</summary>
    public const uint EngineScriptDataPointerOffset = 0x00;

    /// <summary>0x16C050: MOV EBX,[EDI+0x40].</summary>
    public const uint EngineActorBasePointerOffset = 0x40;

    /// <summary>0x16C04A: MOV EAX,[EDI+0x850].</summary>
    public const uint EngineFieldStatePointerOffset = 0x850;

    /// <summary>0x16C053: ADD EBX,0x6940.</summary>
    public const uint ActorArrayOffset = 0x6940;

    /// <summary>0x16C061: IMUL EDX,EAX,0x154.</summary>
    public const uint ActorStride = 0x154;

    /// <summary>0x16C0A4 (xy sets 0x80) and 0x16C321-0x16C328 (dotxy copies FineX &amp; 0xFF).</summary>
    public const uint ActorFractionXOffset = 0x7C;

    /// <summary>0x16C084 (xy stores the tile byte) and 0x16C318-0x16C31B (dotxy: FineX &gt;&gt; 8).</summary>
    public const uint ActorTileXOffset = 0x80;

    /// <summary>0x16C09C-0x16C0AB (xy: (tile &lt;&lt; 8) | 0x80) and 0x16C312 (dotxy: raw u16).</summary>
    public const uint ActorFineXOffset = 0x84;

    /// <summary>0x16C0E9 (xy sets 0xFF) and 0x16C35C-0x16C363 (dotxy copies FineY &amp; 0xFF).</summary>
    public const uint ActorFractionYOffset = 0x88;

    /// <summary>0x16C0C2 (xy stores the tile byte) and 0x16C353-0x16C356 (dotxy: FineY &gt;&gt; 8).</summary>
    public const uint ActorTileYOffset = 0x8C;

    /// <summary>0x16C0E1-0x16C0F3 (xy: (tile &lt;&lt; 8) | 0xFF) and 0x16C34D (dotxy: raw u16).</summary>
    public const uint ActorFineYOffset = 0x90;

    /// <summary>
    /// 0x161D1E / 0x161E5F / 0x161F5D / 0x161FFC write literal 0/1/2/3 from the four
    /// ActorFacingSet opcodes 0x0F / 0x17 / 0x1B / 0x1D.
    /// </summary>
    public const uint ActorFacingOffset = 0x60;

    /// <summary>
    /// 0x1629B3 / 0x1629F1 / 0x162A2A write literal 1/0/0x80 from the ActorSetDrawMode
    /// opcodes 0x7C "dshow" / 0x7D "dhide" / 0x7E "battlehide". This field is the draw
    /// mode, not an actor class.
    /// </summary>
    public const uint ActorDrawModeOffset = 0xD0;

    public const int DrawModeHidden = 0;
    public const int DrawModeDrawn = 1;
    public const int DrawModeRemoved = 0x80;

    /// <summary>
    /// 0x184FE5-0x184FF7: the scene loader reads the current scene id from
    /// <c>fieldState+0x1010</c> and passes it to 0x1597E0, which stores that same value
    /// into <c>renderer+0x2A0</c> at 0x159824. Capture only accepts the id when both
    /// copies agree.
    /// </summary>
    public const uint FieldStateSceneIdOffset = 0x1010;

    /// <summary>0x184FEB: MOV ECX,[ESI+0xB9C] (renderer, assigned by FieldSceneInit 0x2BBB20).</summary>
    public const uint EngineRendererPointerOffset = 0xB9C;

    /// <summary>0x159824: MOV [EDI+0x2A0],EAX inside the scene loader 0x1597E0.</summary>
    public const uint RendererSceneIdOffset = 0x2A0;

    /// <summary>
    /// 0x16B172 and 0x16B272 both write literal 1 here, from the NPC load helper
    /// (opcode 0x82 "people" -&gt; 0x16B120) and the enemy load helper
    /// (opcode 0x83 "monster" -&gt; 0x16B220). It marks a loaded actor; it does NOT
    /// distinguish the classes, since both write the same value.
    /// </summary>
    public const uint ActorLoadedFlagOffset = 0xD8;

    /// <summary>
    /// The class-local visual index the load opcodes bind. Both loaders read it and add
    /// a class bias before handing it to the sprite binder 0x160200:
    /// <code>
    /// 0x16B151  MOV EAX,[ESI+0x44]   ; NPC helper 0x16B120, opcode 0x82 "people"
    /// 0x16B156  ADD EAX,0x7
    /// 0x16B15B  CALL 0x160200
    ///
    /// 0x16B252  MOV EAX,[EBX+0x44]   ; enemy helper 0x16B220, opcode 0x83 "monster"
    /// 0x16B257  ADD EAX,0x107
    /// 0x16B25E  CALL 0x160200
    /// </code>
    /// The bias is applied in a register and is NOT stored in the actor, so this field
    /// alone does not identify the class. See <see cref="NpcVisualIdBias"/>.
    /// </summary>
    public const uint ActorVisualIndexOffset = 0x44;

    /// <summary>
    /// The actor class tag, written as a literal by every audited load path:
    /// <code>
    /// 0x16A864  MOV [EDI+0x40],0x2   ; PC     - opcode 0x80 "autobind" and the seven
    ///                                ;          fixed autobinds, via worker 0x16A7D0
    /// 0x16B048  MOV [EDX+0x40],0x4   ; NPC    - opcode 0x82 "people",  helper 0x16B010
    /// 0x16B0BD  MOV [ESI+0x40],0x5   ; Enemy  - opcode 0x83 "monster", helper 0x16B090
    /// </code>
    /// CTViewer's primary-source note that "actors of type 7 (the default) are not
    /// drawn" matches an unloaded slot, so 7 is treated as the default/unused tag.
    /// Opcode 0x81 "char" (PCAsNPC) writes its tag inside 0x1604C0, which is NOT yet
    /// audited - see <see cref="ClassTagKnown"/>.
    /// </summary>
    public const uint ActorClassTagOffset = 0x40;

    /// <summary>
    /// CTViewer <c>SceneActorClass</c> (scene/actor.rs) enumerates the whole space:
    /// PC1 = 0, PC2 = 1, PC3 = 2, PCOutOfParty = 3, NPC = 4, Enemy = 5,
    /// EnemyPeaceful = 6, Undefined = 7.
    ///
    /// Natively proven in this build: 2 (0x16A864), 3 (0x162A80), 4 (0x16B048),
    /// 5 (0x16B0BD). Values 0, 1 and 6 come from CTViewer only - no literal write to
    /// <see cref="ActorClassTagOffset"/> for them was located here.
    /// </summary>
    public const int ClassTagPlayerFirst = 0;

    public const int ClassTagPlayerSecond = 1;

    /// <summary>PC3, not "the player". 0x16A864: MOV [EDI+0x40],0x2.</summary>
    public const int ClassTagPlayerThird = 2;

    /// <summary>PCOutOfParty. 0x162A80: MOV [ECX+0x6980],0x3 (opcode 0x81 "char").</summary>
    public const int ClassTagPlayerOutOfParty = 3;

    public const int ClassTagNpc = 4;
    public const int ClassTagEnemy = 5;

    /// <summary>CTViewer EnemyPeaceful; not observed as a literal in this build.</summary>
    public const int ClassTagEnemyPeaceful = 6;

    /// <summary>CTViewer Undefined: the default, and not drawn.</summary>
    public const int ClassTagDefaultUnused = 7;

    /// <summary>
    /// Packed draw priority, written as literal <c>0x2020</c> by all four load paths
    /// (0x16B062, 0x16B0E5, 0x16AF89). Root's 0x15E0B0 audit establishes this as the
    /// packed render-priority word. It is NOT a collision or contact extent and must
    /// not be used as one.
    /// </summary>
    public const uint ActorRenderPriorityPackedOffset = 0x08;

    /// <summary>
    /// The interaction-candidate scan 0x1760B0 rejects an actor when this byte is zero:
    /// <code>
    /// 0x176104  CMP byte [EAX+0x152],0
    /// 0x17610B  JZ   skip
    /// </code>
    /// </summary>
    public const uint ActorActivationEnabledOffset = 0x152;

    /// <summary>
    /// The same scan rejects an actor when this dword is zero (0x17610D/0x176111).
    /// </summary>
    public const uint ActorActivationBindingOffset = 0x20;

    /// <summary>
    /// 0x176113: <c>TEST byte [EAX+0x40],0x80</c> - a set high bit on the class tag
    /// removes the actor from interaction. This matches CTViewer's <c>type_status</c>
    /// "dead" bit and confirms <see cref="ActorClassTagOffset"/> is that field.
    /// </summary>
    public const int ClassTagRemovedBit = 0x80;

    /// <summary>
    /// 0x176119 / 0x176121 / 0x176129 compare the candidate against
    /// <c>fieldState+0x11EC</c>, <c>+0x11F0</c> and <c>+0x11F4</c>, so exactly three
    /// party slots exist and every one of them is excluded from interaction.
    /// </summary>
    public const int PartySlotCount = 3;

    /// <summary>
    /// Bias added for opcode 0x82 "people" (0x16B156) before the sprite binder call.
    /// CTViewer decodes the PC-mode enemy operand as <c>index + 7</c> too, so 0..6 is
    /// the player-character range and NPC visual ids begin at 7.
    /// </summary>
    public const int NpcVisualIdBias = 7;

    /// <summary>Bias added for opcode 0x83 "monster" (0x16B257).</summary>
    public const int EnemyVisualIdBias = 0x107;

    /// <summary>0x173550: the field tick calls the player-input routine only when this is non-zero.</summary>
    public const uint FieldStateControlFlagOffset = 0x108C;

    /// <summary>0x175A4F: the player-input routine at 0x175A40 requires this to be 0.</summary>
    public const uint FieldStateInputModeOffset = 0x10E0;

    /// <summary>0x16C059: the acting script actor, in index * 2 encoding.</summary>
    public const uint FieldStateScriptActorOffset = 0x1180;

    /// <summary>0x161ACA: MOV EAX,[EAX+ECX*4+0x11EC] maps a party slot to an actor id.</summary>
    public const uint FieldStatePartySlotTableOffset = 0x11EC;

    /// <summary>
    /// The Atel script packet's object-count byte. Opcodes fetch at
    /// <c>scriptData + 0x12001 + pc</c>, so the byte immediately before script address 0
    /// is the packet header count.
    /// </summary>
    public const uint ScriptObjectCountOffset = 0x12000;

    /// <summary>
    /// Defensive bound only. The true engine maximum is not established; the largest
    /// installed Atel packet header observed is 0x23. Callers must not read this as a
    /// native limit.
    /// </summary>
    public const int MaximumActors = 64;

    public static bool TryCapture(IReadableMemory memory, nuint engine, out FieldNavigationSnapshot snapshot) =>
        TryCapture(memory, engine, out snapshot, out _);

    public static bool TryCapture(
        IReadableMemory memory,
        nuint engine,
        out FieldNavigationSnapshot snapshot,
        out string error)
    {
        snapshot = null!;
        try
        {
            return TryCaptureCore(memory, engine, out snapshot, out error);
        }
        catch (Exception exception)
        {
            snapshot = null!;
            error = $"Field navigation capture failed safely: {exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    private static bool TryCaptureCore(
        IReadableMemory memory,
        nuint engine,
        out FieldNavigationSnapshot snapshot,
        out string error)
    {
        snapshot = null!;
        if (memory is null || engine == 0)
        {
            error = "Field navigation memory or engine pointer is unavailable.";
            return false;
        }

        if (!Pointer(memory, engine + EngineScriptDataPointerOffset, out var scriptData) ||
            !Pointer(memory, engine + EngineActorBasePointerOffset, out var actorBase) ||
            !Pointer(memory, engine + EngineFieldStatePointerOffset, out var fieldState))
        {
            error = "Field engine script, actor-array or field-state pointer is null or unreadable.";
            return false;
        }

        if (!Byte(memory, scriptData + ScriptObjectCountOffset, out var rawCount) ||
            rawCount == 0 || rawCount > MaximumActors)
        {
            error = $"Field script actor count {rawCount} is outside the audited 1..{MaximumActors} bounds.";
            return false;
        }

        if (!Int32(memory, fieldState + FieldStateControlFlagOffset, out var controlFlag) ||
            !Int32(memory, fieldState + FieldStateInputModeOffset, out var inputMode) ||
            !Int32(memory, fieldState + FieldStateScriptActorOffset, out var rawScriptActor) ||
            !Int32(memory, fieldState + FieldStatePartySlotTableOffset, out _))
        {
            error = "Field state control, input-mode, script-actor or party-slot word is unreadable.";
            return false;
        }

        // 0x176119/0x176121/0x176129 compare against all three slots, so read them all:
        // every party member must be excluded from the target list, not just the lead.
        var partySlots = new int[PartySlotCount];
        var lastPartySlotRaw = 0x80;
        for (var slot = 0; slot < PartySlotCount; slot++)
        {
            partySlots[slot] = -1;
            if (Int32(memory, fieldState + FieldStatePartySlotTableOffset + (nuint)(slot * 4),
                    out var entry))
            {
                if (slot == PartySlotCount - 1) lastPartySlotRaw = entry;
                if ((entry & 0x80) == 0) partySlots[slot] = entry >> 1;
            }
        }

        var leadEntryIndex = partySlots[0];

        var sceneId = -1;
        var sceneCoherent = false;
        if (Int32(memory, fieldState + FieldStateSceneIdOffset, out var fieldSceneId) &&
            Pointer(memory, engine + EngineRendererPointerOffset, out var renderer) &&
            Int32(memory, renderer + RendererSceneIdOffset, out var rendererSceneId) &&
            fieldSceneId == rendererSceneId)
        {
            sceneId = fieldSceneId;
            sceneCoherent = true;
        }

        var actors = new FieldActorSnapshot[rawCount];
        for (var index = 0; index < rawCount; index++)
        {
            var isPartyMember = Array.IndexOf(partySlots, index) >= 0;
            if (!TryReadActor(memory, actorBase, index, isPartyMember, out var actor, out error))
            {
                return false;
            }

            actors[index] = actor;
        }

        // 0x161AD4/0x161AD6: TEST AL,AL / JNS. A set sign bit on the low byte means the
        // party member is absent, which is a normal state and not a capture failure.
        var leadIndex = -1;
        FieldActorSnapshot? lead = null;
        if (leadEntryIndex >= 0)
        {
            leadIndex = leadEntryIndex;
            if (leadIndex >= rawCount)
            {
                error = $"Field party lead actor {leadIndex} is outside the {rawCount} loaded actors.";
                return false;
            }

            lead = actors[leadIndex];

            // The walking lead is always a real loaded actor, so incoherence here means
            // the pointer chain is wrong rather than that a slot is simply unused.
            if (!lead.IsUsable)
            {
                error = $"Field party lead actor {leadIndex} fails the audited actor invariants.";
                return false;
            }
        }

        // 187527 assigns DAT_0081B4C4 to engine+40. The fair's special contact
        // radius at 178980 reads that actor-base block's byte +68A0.
        var fairFlags = 0;
        if (sceneId == 5 && !Byte(memory, actorBase + 0x68A0u, out fairFlags))
        {
            error = "The fair's native actor collision state is unreadable.";
            return false;
        }
        snapshot = new FieldNavigationSnapshot(
            (uint)engine,
            (uint)actorBase,
            (uint)fieldState,
            (uint)scriptData,
            rawCount,
            sceneId,
            sceneCoherent,
            controlFlag,
            inputMode,
            rawScriptActor >> 1,
            leadIndex,
            lead,
            new ReadOnlyCollection<FieldActorSnapshot>(actors),
            new ReadOnlyCollection<int>(partySlots))
        {
            LastPartySlotRaw = lastPartySlotRaw,
            ActorCollisionRadius = FieldActorCollisionRules.Radius(sceneId, fairFlags),
        };
        error = string.Empty;
        return true;
    }

    private static bool TryReadActor(
        IReadableMemory memory,
        nuint actorBase,
        int index,
        bool isPartyMember,
        out FieldActorSnapshot actor,
        out string error)
    {
        actor = null!;
        var address = actorBase + ActorArrayOffset + (nuint)((ulong)(uint)index * ActorStride);
        if (!Int32(memory, address + ActorFractionXOffset, out var fractionX) ||
            !Int32(memory, address + ActorTileXOffset, out var tileX) ||
            !Int32(memory, address + ActorFineXOffset, out var fineX) ||
            !Int32(memory, address + ActorFractionYOffset, out var fractionY) ||
            !Int32(memory, address + ActorTileYOffset, out var tileY) ||
            !Int32(memory, address + ActorFineYOffset, out var fineY) ||
            !Int32(memory, address + ActorFacingOffset, out var facing) ||
            !Int32(memory, address + ActorDrawModeOffset, out var drawMode) ||
            !Int32(memory, address + ActorLoadedFlagOffset, out var loadedFlag) ||
            !Int32(memory, address + ActorVisualIndexOffset, out var visualIndex) ||
            !Int32(memory, address + ActorClassTagOffset, out var classTag) ||
            !Int32(memory, address + ActorRenderPriorityPackedOffset, out var renderPriority) ||
            !Int32(memory, address + ActorActivationBindingOffset, out var activationBinding) ||
            !Int32(memory, address + 0x14Cu, out var collisionOffsetX) ||
            !Byte(memory, address + ActorActivationEnabledOffset, out var activationEnabled) ||
            !Byte(memory, address + 0xE8u, out var scriptDisabled) ||
            !Byte(memory, address + 0x30u, out var callFlags))
        {
            // Readability is a pointer-chain property, so this stays fatal.
            error = $"Field actor {index} at 0x{address:X} is not fully readable.";
            return false;
        }

        // Both coordinate setters derive tile and fraction from the fine value. Only
        // those two writers are audited, so a slot the scene never loaded may hold
        // values that satisfy nothing. Record the verdict instead of failing: an inert
        // actor is a normal engine state, not a capture error.
        var coordinatesCoherent =
            fineX is >= 0 and <= ushort.MaxValue && fineY is >= 0 and <= ushort.MaxValue &&
            tileX == fineX >> 8 && fractionX == (fineX & 0xFF) &&
            tileY == fineY >> 8 && fractionY == (fineY & 0xFF);
        var facingValid = facing is >= 0 and <= 3;
        var drawModeKnown = drawMode is DrawModeHidden or DrawModeDrawn or DrawModeRemoved;

        actor = new FieldActorSnapshot(
            index, tileX, fineX, fractionX, tileY, fineY, fractionY,
            facing, drawMode, loadedFlag, visualIndex, classTag, renderPriority,
            activationEnabled, activationBinding, isPartyMember,
            coordinatesCoherent, facingValid, drawModeKnown)
        {
            ScriptCallsEnabled = scriptDisabled == 0 && (callFlags & 0x80) == 0,
            CollisionOffsetX = collisionOffsetX,
        };
        error = string.Empty;
        return true;
    }

    private static bool Fits(nuint address, uint count) =>
        address != 0 && (ulong)address + count - 1 <= uint.MaxValue;

    private static bool Pointer(IReadableMemory memory, nuint address, out nuint value)
    {
        value = 0;
        if (!Int32(memory, address, out var raw) || raw == 0)
        {
            return false;
        }

        value = (nuint)(uint)raw;
        return true;
    }

    private static bool Byte(IReadableMemory memory, nuint address, out int value)
    {
        Span<byte> bytes = stackalloc byte[1];
        value = 0;
        if (!Fits(address, 1) || !memory.TryRead(address, bytes))
        {
            return false;
        }

        value = bytes[0];
        return true;
    }

    private static bool Int32(IReadableMemory memory, nuint address, out int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        value = 0;
        if (!Fits(address, 4) || !memory.TryRead(address, bytes))
        {
            return false;
        }

        value = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        return true;
    }
}
