using System.Buffers.Binary;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

/// <summary>
/// Pins the field navigation capture against the audited native layout.
///
/// Native evidence (see docs in the research note
/// <c>navigation-native-audit-claude.md</c> and <c>nav-units-claude.txt</c>):
/// the script coordinate setters <c>0x16C040</c> ("xy", opcode 0x8B) and
/// <c>0x16C2C0</c> ("dotxy", opcode 0x8D) both resolve the acting actor as
/// <c>*(engine+0x40) + 0x6940 + (*(fieldState+0x1180) &gt;&gt; 1) * 0x154</c> and then
/// write the coordinate triples, which fixes every constant asserted here.
/// </summary>
public sealed class FieldNavigationCaptureTests
{
    private const nuint Engine = 0x20000;
    private const nuint ActorBase = 0x30000;
    private const nuint FieldState = 0x40000;
    private const nuint ScriptData = 0x50000;
    private const nuint Renderer = 0x60000;
    private const int SceneId = 0x2A;
    private const nuint Actors = ActorBase + FieldNavigationCapture.ActorArrayOffset;

    [Fact]
    public void ConstantsMatchTheAuditedNativeLayout()
    {
        // 0x16C050-0x16C061: MOV EBX,[EDI+0x40]; ADD EBX,0x6940; IMUL EDX,EAX,0x154.
        Assert.Equal(0x40u, FieldNavigationCapture.EngineActorBasePointerOffset);
        Assert.Equal(0x6940u, FieldNavigationCapture.ActorArrayOffset);
        Assert.Equal(0x154u, FieldNavigationCapture.ActorStride);

        // 0x16C04A: MOV EAX,[EDI+0x850] (field state).
        Assert.Equal(0x850u, FieldNavigationCapture.EngineFieldStatePointerOffset);

        // 0x16C084/0x16C0A4/0x16C0AB: tile X, sub-tile X, fine X.
        Assert.Equal(0x7Cu, FieldNavigationCapture.ActorFractionXOffset);
        Assert.Equal(0x80u, FieldNavigationCapture.ActorTileXOffset);
        Assert.Equal(0x84u, FieldNavigationCapture.ActorFineXOffset);

        // 0x16C0E9/0x16C0C2/0x16C0F3: sub-tile Y, tile Y, fine Y.
        Assert.Equal(0x88u, FieldNavigationCapture.ActorFractionYOffset);
        Assert.Equal(0x8Cu, FieldNavigationCapture.ActorTileYOffset);
        Assert.Equal(0x90u, FieldNavigationCapture.ActorFineYOffset);

        // 0x161D1E/0x161E5F/0x161F5D/0x161FFC write 0/1/2/3 to actor+0x60
        // from the four ActorFacingSet opcodes 0x0F/0x17/0x1B/0x1D.
        Assert.Equal(0x60u, FieldNavigationCapture.ActorFacingOffset);

        // 0x1629B3/0x1629F1/0x162A2A write 1/0/0x80 to actor+0xD0 from the
        // ActorSetDrawMode opcodes 0x7C "dshow" / 0x7D "dhide" / 0x7E "battlehide".
        Assert.Equal(0xD0u, FieldNavigationCapture.ActorDrawModeOffset);
        Assert.Equal(0, FieldNavigationCapture.DrawModeHidden);
        Assert.Equal(1, FieldNavigationCapture.DrawModeDrawn);
        Assert.Equal(0x80, FieldNavigationCapture.DrawModeRemoved);

        // 0x173550 / 0x175A4F / 0x16C059 / 0x161ACA.
        Assert.Equal(0x108Cu, FieldNavigationCapture.FieldStateControlFlagOffset);
        Assert.Equal(0x10E0u, FieldNavigationCapture.FieldStateInputModeOffset);
        Assert.Equal(0x1180u, FieldNavigationCapture.FieldStateScriptActorOffset);
        Assert.Equal(0x11ECu, FieldNavigationCapture.FieldStatePartySlotTableOffset);

        // Atel packet object count sits one byte before script address 0, which the
        // opcode fetch reads at *(engine) + 0x12001 + pc.
        Assert.Equal(0x12000u, FieldNavigationCapture.ScriptObjectCountOffset);

        // 0x184FE5/0x184FEB/0x184FF1 read fieldState+0x1010 and engine+0xB9C, and the
        // loader 0x1597E0 mirrors the id to renderer+0x2A0 at 0x159824.
        Assert.Equal(0x1010u, FieldNavigationCapture.FieldStateSceneIdOffset);
        Assert.Equal(0xB9Cu, FieldNavigationCapture.EngineRendererPointerOffset);
        Assert.Equal(0x2A0u, FieldNavigationCapture.RendererSceneIdOffset);

        // 0x16B172 (NPC load 0x16B120, opcode 0x82 "people") and 0x16B272 (enemy load
        // 0x16B220, opcode 0x83 "monster") both write literal 1 here.
        Assert.Equal(0xD8u, FieldNavigationCapture.ActorLoadedFlagOffset);

        // 0x16B151/0x16B156 (NPC, opcode 0x82) and 0x16B252/0x16B257 (enemy, opcode
        // 0x83) both read actor+0x44 and add a class bias before calling the sprite
        // binder 0x160200. actor+0x4C is written by the enemy path (0x16B28A) and by
        // the PCAsNPC path (0x16AFB3), so it is not enemy-specific.
        Assert.Equal(0x44u, FieldNavigationCapture.ActorVisualIndexOffset);

        // 0x16A864 MOV [EDI+0x40],0x2 (PC worker 0x16A7D0), 0x16B048 MOV [EDX+0x40],0x4
        // (NPC helper 0x16B010), 0x16B0BD MOV [ESI+0x40],0x5 (enemy helper 0x16B090).
        Assert.Equal(0x40u, FieldNavigationCapture.ActorClassTagOffset);
        // CTViewer SceneActorClass: PC1 0, PC2 1, PC3 2, PCOutOfParty 3, NPC 4,
        // Enemy 5, EnemyPeaceful 6, Undefined 7. Natively proven here: 2, 3, 4, 5.
        // 0x162A80 MOV [ECX+0x6980],0x3 is opcode 0x81 "char" inline at 0x162A61;
        // 0x6980 - 0x6940 = 0x40, so it targets the same class-tag field.
        Assert.Equal(0, FieldNavigationCapture.ClassTagPlayerFirst);
        Assert.Equal(1, FieldNavigationCapture.ClassTagPlayerSecond);
        Assert.Equal(2, FieldNavigationCapture.ClassTagPlayerThird);
        Assert.Equal(3, FieldNavigationCapture.ClassTagPlayerOutOfParty);
        Assert.Equal(4, FieldNavigationCapture.ClassTagNpc);
        Assert.Equal(5, FieldNavigationCapture.ClassTagEnemy);
        Assert.Equal(6, FieldNavigationCapture.ClassTagEnemyPeaceful);
        Assert.Equal(7, FieldNavigationCapture.ClassTagDefaultUnused);

        // 0x16B062 / 0x16B0E5 / 0x16AF89 all write literal 0x2020. Root's 0x15E0B0
        // audit establishes this as packed draw priority, not a contact extent.
        Assert.Equal(0x08u, FieldNavigationCapture.ActorRenderPriorityPackedOffset);

        // The interaction-candidate scan 0x1760B0: CMP byte [EAX+0x152],0 (0x176104),
        // CMP dword [EAX+0x20],0 (0x17610D), TEST byte [EAX+0x40],0x80 (0x176113), and
        // three party-slot comparisons at 0x176119/0x176121/0x176129.
        Assert.Equal(0x152u, FieldNavigationCapture.ActorActivationEnabledOffset);
        Assert.Equal(0x20u, FieldNavigationCapture.ActorActivationBindingOffset);
        Assert.Equal(0x80, FieldNavigationCapture.ClassTagRemovedBit);
        Assert.Equal(3, FieldNavigationCapture.PartySlotCount);
        Assert.Equal(7, FieldNavigationCapture.NpcVisualIdBias);
        Assert.Equal(0x107, FieldNavigationCapture.EnemyVisualIdBias);
    }

    [Fact]
    public void CapturesTheAuditedEngineActorsAndLeadPlayer()
    {
        var memory = CreateValidMemory();

        Assert.True(FieldNavigationCapture.TryCapture(memory, Engine, out var snapshot, out var error), error);

        Assert.Equal((uint)Engine, snapshot.Engine);
        Assert.Equal((uint)ActorBase, snapshot.ActorBase);
        Assert.Equal((uint)FieldState, snapshot.FieldState);
        Assert.Equal((uint)ScriptData, snapshot.ScriptData);
        Assert.Equal(3, snapshot.ActorCount);
        Assert.Equal(1, snapshot.ControlFlag);
        Assert.Equal(0, snapshot.InputMode);
        Assert.Equal(2, snapshot.ScriptActorIndex);
        Assert.Equal(3, snapshot.Actors.Count);
        Assert.Equal(SceneId, snapshot.SceneId);
        Assert.True(snapshot.SceneIdCoherent);
        Assert.All(snapshot.Actors, actor => Assert.True(actor.IsUsable));
        Assert.Equal(1, snapshot.Actors[0].LoadedFlag);
        Assert.Equal(0x0C, snapshot.Actors[0].VisualIndex);
        Assert.Equal(FieldNavigationCapture.ClassTagNpc, snapshot.Actors[0].ClassTag);
        Assert.True(snapshot.Actors[0].ClassTagKnown);
        Assert.Equal(0x2020, snapshot.Actors[0].RenderPriorityPacked);

        // Slot 0 is actor 1, slot 1 is actor 2, slot 2 is empty.
        Assert.Equal(new[] { 1, 2, -1 }, snapshot.PartySlotActorIndices);
        Assert.True(snapshot.Actors[0].IsActivationCandidate);
        Assert.False(snapshot.Actors[1].IsActivationCandidate);   // party lead
        Assert.False(snapshot.Actors[2].IsActivationCandidate);   // party companion
        Assert.Equal(0x0D, snapshot.Actors[1].VisualIndex);

        // Actor 1 is the party lead: table entry 0x02 is the index*2 encoding.
        Assert.Equal(1, snapshot.LeadPlayerActorIndex);
        var lead = Assert.IsType<FieldActorSnapshot>(snapshot.LeadPlayer);
        Assert.Equal(1, lead.Index);
        Assert.Equal(0x11, lead.TileX);
        Assert.Equal(0x1180, lead.FineX);
        Assert.Equal(0x80, lead.FractionX);
        Assert.Equal(0x07, lead.TileY);
        Assert.Equal(0x07FF, lead.FineY);
        Assert.Equal(0xFF, lead.FractionY);
        Assert.Equal(1, lead.Facing);
        Assert.Equal(FieldNavigationCapture.DrawModeDrawn, lead.DrawMode);
        Assert.True(lead.IsDrawn);

        // Actor 2 is hidden; the capture reports it rather than filtering, so callers
        // can apply their own visibility policy.
        Assert.False(snapshot.Actors[2].IsDrawn);
        Assert.Equal(FieldNavigationCapture.DrawModeHidden, snapshot.Actors[2].DrawMode);
    }

    [Theory]
    [InlineData(FieldNavigationCapture.ClassTagPlayerFirst, true)]
    [InlineData(FieldNavigationCapture.ClassTagPlayerThird, true)]
    [InlineData(FieldNavigationCapture.ClassTagPlayerOutOfParty, true)]
    [InlineData(FieldNavigationCapture.ClassTagNpc, true)]
    [InlineData(FieldNavigationCapture.ClassTagEnemy, true)]
    [InlineData(FieldNavigationCapture.ClassTagEnemyPeaceful, true)]
    [InlineData(FieldNavigationCapture.ClassTagDefaultUnused, false)] // never loaded
    [InlineData(9, false)]
    public void ReportsClassTagKnownOnlyForRealActorClasses(int classTag, bool known)
    {
        var memory = CreateValidMemory().AddInt32(
            Actors + FieldNavigationCapture.ActorClassTagOffset, classTag);

        Assert.True(FieldNavigationCapture.TryCapture(memory, Engine, out var snapshot, out var error), error);

        Assert.Equal(classTag, snapshot.Actors[0].ClassTag);
        Assert.Equal(known, snapshot.Actors[0].ClassTagKnown);
        Assert.Equal(classTag <= 3, snapshot.Actors[0].IsPlayerClass);

        // An unknown tag must never suppress the rest of the snapshot.
        Assert.True(snapshot.Actors[0].IsUsable);
        Assert.Equal(3, snapshot.Actors.Count);
    }

    [Theory]
    [InlineData(FieldNavigationCapture.ActorActivationEnabledOffset, 0)]  // 0x176104
    [InlineData(FieldNavigationCapture.ActorActivationBindingOffset, 0)]  // 0x17610D
    [InlineData(FieldNavigationCapture.ActorClassTagOffset,
        FieldNavigationCapture.ClassTagNpc | FieldNavigationCapture.ClassTagRemovedBit)] // 0x176113
    public void ExcludesActorsTheNativeInteractionScanRejects(uint offset, int value)
    {
        var memory = CreateValidMemory();
        if (offset == FieldNavigationCapture.ActorActivationEnabledOffset)
        {
            memory.AddByte(Actors + offset, (byte)value);
        }
        else
        {
            memory.AddInt32(Actors + offset, value);
        }

        Assert.True(FieldNavigationCapture.TryCapture(memory, Engine, out var snapshot, out var error), error);

        Assert.False(snapshot.Actors[0].IsActivationCandidate);
        // Rejection is a target-eligibility verdict, never a capture failure.
        Assert.True(snapshot.Actors[0].IsUsable);
    }

    [Fact]
    public void ReportsEveryPartySlotSoCompanionsAreNeverTargets()
    {
        // 0x1760B0 compares the candidate against all three slots, not just the lead.
        var memory = CreateValidMemory();

        Assert.True(FieldNavigationCapture.TryCapture(memory, Engine, out var snapshot, out var error), error);

        Assert.Equal(FieldNavigationCapture.PartySlotCount, snapshot.PartySlotActorIndices.Count);
        Assert.True(snapshot.Actors[1].IsPartyMember);
        Assert.True(snapshot.Actors[2].IsPartyMember);
        Assert.False(snapshot.Actors[0].IsPartyMember);
    }

    [Fact]
    public void ReportsTheRawVisualIndexWithoutInferringAClass()
    {
        // The class bias is applied in a register by the load helper and never stored,
        // so the capture must expose the raw index and let the caller decide.
        var memory = CreateValidMemory().AddInt32(
            Actors + FieldNavigationCapture.ActorVisualIndexOffset, 0x22);

        Assert.True(FieldNavigationCapture.TryCapture(memory, Engine, out var snapshot, out var error), error);

        Assert.Equal(0x22, snapshot.Actors[0].VisualIndex);
        Assert.Equal(0x29, snapshot.Actors[0].VisualIndex + FieldNavigationCapture.NpcVisualIdBias);
        Assert.Equal(0x129, snapshot.Actors[0].VisualIndex + FieldNavigationCapture.EnemyVisualIdBias);
    }

    [Fact]
    public void ReportsAnAbsentPartyLeadWithoutFailingTheSnapshot()
    {
        // 0x161AD4/0x161AD6 (TEST AL,AL / JNS): a set sign bit on the low byte of the
        // party-slot entry means the member is not present.
        var memory = CreateValidMemory().AddInt32(
            FieldState + FieldNavigationCapture.FieldStatePartySlotTableOffset, 0x80);

        Assert.True(FieldNavigationCapture.TryCapture(memory, Engine, out var snapshot, out var error), error);

        Assert.Equal(-1, snapshot.LeadPlayerActorIndex);
        Assert.Null(snapshot.LeadPlayer);
        Assert.Equal(3, snapshot.Actors.Count);
    }

    [Theory]
    // Actor 2's real values are tile X 0x12 / tile Y 0x08, so these must differ from those.
    [InlineData(FieldNavigationCapture.ActorTileXOffset, 0x77)]     // tile != fine >> 8
    [InlineData(FieldNavigationCapture.ActorFractionXOffset, 0x7F)] // fraction != fine & 0xFF
    [InlineData(FieldNavigationCapture.ActorTileYOffset, 0x77)]
    [InlineData(FieldNavigationCapture.ActorFractionYOffset, 0x00)]
    [InlineData(FieldNavigationCapture.ActorFacingOffset, 4)]
    [InlineData(FieldNavigationCapture.ActorDrawModeOffset, 2)]
    public void ReportsAnUnusableInertActorInsteadOfFailingTheWholeSnapshot(uint offset, int corrupted)
    {
        // Only the two coordinate setters and the draw/facing opcodes are audited, so a
        // slot the scene never loaded can hold anything. That must not destroy the
        // snapshot for every other actor.
        var memory = CreateValidMemory().AddInt32(
            Actors + (2 * FieldNavigationCapture.ActorStride) + offset, corrupted);

        Assert.True(FieldNavigationCapture.TryCapture(memory, Engine, out var snapshot, out var error), error);

        Assert.False(snapshot.Actors[2].IsUsable);
        Assert.False(snapshot.Actors[2].IsDrawn);
        Assert.True(snapshot.Actors[0].IsUsable);
        Assert.True(snapshot.Actors[1].IsUsable);
        Assert.NotNull(snapshot.LeadPlayer);
    }

    [Theory]
    [InlineData(FieldNavigationCapture.ActorTileXOffset, 0x12)]
    [InlineData(FieldNavigationCapture.ActorFractionYOffset, 0x00)]
    [InlineData(FieldNavigationCapture.ActorFacingOffset, 4)]
    [InlineData(FieldNavigationCapture.ActorDrawModeOffset, 2)]
    public void RejectsTheSnapshotWhenThePartyLeadItselfIsIncoherent(uint offset, int corrupted)
    {
        // The walking lead is always a loaded actor, so incoherence there means the
        // pointer chain is wrong rather than that a slot is unused.
        var memory = CreateValidMemory().AddInt32(
            Actors + FieldNavigationCapture.ActorStride + offset, corrupted);

        Assert.False(FieldNavigationCapture.TryCapture(memory, Engine, out var snapshot, out var error));
        Assert.Null(snapshot);
        Assert.Contains("party lead", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReportsTheSceneIdOnlyWhenBothNativeCopiesAgree()
    {
        var mismatched = CreateValidMemory().AddInt32(Renderer + FieldNavigationCapture.RendererSceneIdOffset, 0x2B);

        Assert.True(FieldNavigationCapture.TryCapture(mismatched, Engine, out var snapshot, out var error), error);
        Assert.False(snapshot.SceneIdCoherent);
        Assert.Equal(-1, snapshot.SceneId);

        // A missing renderer is also not a capture failure: the rest of the snapshot
        // stays usable for navigation.
        var noRenderer = CreateValidMemory().AddInt32(Engine + FieldNavigationCapture.EngineRendererPointerOffset, 0);

        Assert.True(FieldNavigationCapture.TryCapture(noRenderer, Engine, out var second, out var secondError), secondError);
        Assert.False(second.SceneIdCoherent);
        Assert.Equal(-1, second.SceneId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(FieldNavigationCapture.MaximumActors + 1)]
    public void RejectsAnActorCountOutsideTheAuditedBounds(int count)
    {
        var memory = CreateValidMemory()
            .AddByte(ScriptData + FieldNavigationCapture.ScriptObjectCountOffset, (byte)count);

        Assert.False(FieldNavigationCapture.TryCapture(memory, Engine, out var snapshot, out var error));
        Assert.Null(snapshot);
        Assert.Contains("actor count", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsAPartyLeadIndexBeyondTheActorCount()
    {
        // Slot table entry 0x40 is actor index 0x20, past the three loaded actors.
        var memory = CreateValidMemory().AddInt32(
            FieldState + FieldNavigationCapture.FieldStatePartySlotTableOffset, 0x40);

        Assert.False(FieldNavigationCapture.TryCapture(memory, Engine, out var snapshot, out var error));
        Assert.Null(snapshot);
        Assert.Contains("party lead", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0u)]                                                     // engine word 0: script data
    [InlineData(FieldNavigationCapture.EngineActorBasePointerOffset)]
    [InlineData(FieldNavigationCapture.EngineFieldStatePointerOffset)]
    public void RejectsNullEnginePointers(uint offset)
    {
        var memory = CreateValidMemory().AddInt32(Engine + offset, 0);

        Assert.False(FieldNavigationCapture.TryCapture(memory, Engine, out var snapshot, out _));
        Assert.Null(snapshot);
    }

    [Fact]
    public void RejectsAZeroEngineAndUnreadableMemory()
    {
        Assert.False(FieldNavigationCapture.TryCapture(CreateValidMemory(), 0, out var zero, out _));
        Assert.Null(zero);

        Assert.False(FieldNavigationCapture.TryCapture(new TestMemory(), Engine, out var empty, out _));
        Assert.Null(empty);

        Assert.False(FieldNavigationCapture.TryCapture(null!, Engine, out var missing, out _));
        Assert.Null(missing);
    }

    [Fact]
    public void RejectsAnActorArrayThatIsNotFullyReadable()
    {
        // Third actor's coordinate block is absent: the capture must not report a
        // partial actor list.
        var memory = CreateValidMemory(actors: 2);
        memory.AddByte(ScriptData + FieldNavigationCapture.ScriptObjectCountOffset, 3);

        Assert.False(FieldNavigationCapture.TryCapture(memory, Engine, out var snapshot, out _));
        Assert.Null(snapshot);
    }

    private static TestMemory CreateValidMemory(int actors = 3)
    {
        var memory = new TestMemory()
            .AddInt32(Engine + 0, (int)ScriptData)
            .AddInt32(Engine + FieldNavigationCapture.EngineActorBasePointerOffset, (int)ActorBase)
            .AddInt32(Engine + FieldNavigationCapture.EngineFieldStatePointerOffset, (int)FieldState)
            .AddByte(ScriptData + FieldNavigationCapture.ScriptObjectCountOffset, 3)
            .AddInt32(FieldState + FieldNavigationCapture.FieldStateControlFlagOffset, 1)
            .AddInt32(FieldState + FieldNavigationCapture.FieldStateInputModeOffset, 0)
            .AddInt32(FieldState + FieldNavigationCapture.FieldStateScriptActorOffset, 4)
            .AddInt32(FieldState + FieldNavigationCapture.FieldStatePartySlotTableOffset, 2)
            .AddInt32(Engine + FieldNavigationCapture.EngineRendererPointerOffset, (int)Renderer)
            .AddInt32(FieldState + FieldNavigationCapture.FieldStateSceneIdOffset, SceneId)
            .AddInt32(Renderer + FieldNavigationCapture.RendererSceneIdOffset, SceneId)
            .AddInt32(FieldState + FieldNavigationCapture.FieldStatePartySlotTableOffset + 4, 4)
            .AddInt32(FieldState + FieldNavigationCapture.FieldStatePartySlotTableOffset + 8, 0x80);

        for (var index = 0; index < actors; index++)
        {
            // Tile 0x10+index, sub-tile 0x80 on X (the "xy" opcode centres X and
            // anchors Y at 0xFF, see 0x16C0A4 and 0x16C0E9).
            AddActor(memory, index,
                fineX: ((0x10 + index) << 8) | 0x80,
                fineY: ((0x06 + index) << 8) | 0xFF,
                facing: index % 4,
                drawMode: index == 2
                    ? FieldNavigationCapture.DrawModeHidden
                    : FieldNavigationCapture.DrawModeDrawn);
        }

        return memory;
    }

    [Theory]
    [InlineData(0xE8)]
    [InlineData(0x30)]
    public void UnreadableScriptCallGateReportsCaptureFailureRatherThanDroppingAPickup(uint offset)
    {
        var memory = CreateValidMemory().Remove(Actors + offset);
        Assert.False(FieldNavigationCapture.TryCapture(memory, Engine, out _, out var error));
        Assert.Contains("not fully readable", error);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 0, false)]
    [InlineData(0, 128, false)]
    public void PersistentScriptCallGatesAreIndependentOfIdleAndCameraBits(byte disabled, byte calls, bool expected)
    {
        var memory = CreateValidMemory();
        memory.AddByte(Actors + 0xE8u, disabled).AddByte(Actors + 0x30u, calls)
            .AddByte(Actors + FieldNavigationCapture.ActorActivationEnabledOffset, 0)
            .AddInt32(Actors + FieldNavigationCapture.ActorActivationBindingOffset, 0);
        Assert.True(FieldNavigationCapture.TryCapture(memory, Engine, out var snapshot, out var error), error);
        Assert.Equal(expected, snapshot.Actors[0].ScriptCallsEnabled);
        Assert.False(snapshot.Actors[0].IsActivationCandidate);
    }

    [Theory]
    [InlineData(-3, 128)]
    [InlineData(4, 4)]
    public void CapturesTheSignedCollisionOffsetAndRawFinalPartySlot(int offset, int slot)
    {
        var memory = CreateValidMemory().AddInt32(Actors + 0x14Cu, offset)
            .AddInt32(FieldState + FieldNavigationCapture.FieldStatePartySlotTableOffset + 8, slot);
        Assert.True(FieldNavigationCapture.TryCapture(memory, Engine, out var snapshot, out var error), error);
        Assert.Equal(offset, snapshot.Actors[0].CollisionOffsetX);
        Assert.Equal(slot, snapshot.LastPartySlotRaw);
    }

    [Fact]
    public void UnreadableCollisionOffsetRejectsTheCapture()
    {
        Assert.False(FieldNavigationCapture.TryCapture(CreateValidMemory().Remove(Actors + 0x14Cu), Engine, out _));
    }

    [Theory]
    [InlineData(0, 160)]
    [InlineData(1, 224)]
    [InlineData(63, 224)]
    [InlineData(64, 160)]
    public void FairUsesItsNativeSpecialRadiusFlag(int flag, int expected)
    {
        var memory = CreateValidMemory().AddInt32(FieldState + FieldNavigationCapture.FieldStateSceneIdOffset, 5)
            .AddInt32(Renderer + FieldNavigationCapture.RendererSceneIdOffset, 5).AddByte(ActorBase + 0x68A0u, (byte)flag);
        Assert.True(FieldNavigationCapture.TryCapture(memory, Engine, out var snapshot, out var error), error);
        Assert.Equal(expected, snapshot.ActorCollisionRadius);
    }

    private static void AddActor(TestMemory memory, int index, int fineX, int fineY, int facing, int drawMode)
    {
        var actor = Actors + (nuint)(index * (int)FieldNavigationCapture.ActorStride);
        memory
            .AddByte(actor + 0xE8u, 0).AddByte(actor + 0x30u, 0)
            .AddInt32(actor + 0x14Cu, 0)
            .AddInt32(actor + FieldNavigationCapture.ActorFractionXOffset, fineX & 0xFF)
            .AddInt32(actor + FieldNavigationCapture.ActorTileXOffset, fineX >> 8)
            .AddInt32(actor + FieldNavigationCapture.ActorFineXOffset, fineX)
            .AddInt32(actor + FieldNavigationCapture.ActorFractionYOffset, fineY & 0xFF)
            .AddInt32(actor + FieldNavigationCapture.ActorTileYOffset, fineY >> 8)
            .AddInt32(actor + FieldNavigationCapture.ActorFineYOffset, fineY)
            .AddInt32(actor + FieldNavigationCapture.ActorFacingOffset, facing)
            .AddInt32(actor + FieldNavigationCapture.ActorDrawModeOffset, drawMode)
            .AddInt32(actor + FieldNavigationCapture.ActorLoadedFlagOffset, 1)
            .AddInt32(actor + FieldNavigationCapture.ActorVisualIndexOffset, 0x0C + index)
            .AddInt32(actor + FieldNavigationCapture.ActorClassTagOffset, FieldNavigationCapture.ClassTagNpc)
            .AddInt32(actor + FieldNavigationCapture.ActorRenderPriorityPackedOffset, 0x2020)
            .AddInt32(actor + FieldNavigationCapture.ActorActivationBindingOffset, 0x900 + index)
            .AddByte(actor + FieldNavigationCapture.ActorActivationEnabledOffset, 1);
    }

    private sealed class TestMemory : IReadableMemory
    {
        private readonly Dictionary<nuint, byte[]> segments = [];

        public TestMemory Remove(nuint address) { segments.Remove(address); return this; }

        public TestMemory AddByte(nuint address, byte value)
        {
            segments[address] = [value];
            return this;
        }

        public TestMemory AddInt32(nuint address, int value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            segments[address] = bytes;
            return this;
        }

        public bool TryRead(nuint address, Span<byte> destination)
        {
            foreach (var (start, bytes) in segments)
            {
                if (address < start || address + (nuint)destination.Length > start + (nuint)bytes.Length)
                {
                    continue;
                }

                bytes.AsSpan(checked((int)(address - start)), destination.Length).CopyTo(destination);
                return true;
            }

            return false;
        }
    }
}
