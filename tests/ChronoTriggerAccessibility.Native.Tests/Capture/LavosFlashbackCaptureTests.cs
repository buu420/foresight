using System.Buffers.Binary;
using System.Security.Cryptography;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Tests.Support;
using Xunit;

namespace ChronoTriggerAccessibility.Native.Tests.Capture;

/// <summary>
/// OWNED-MEMORY SCAFFOLDING: a dispatcher context shaped like StoryActionCapture's (ctx+0 data,
/// +24 PC, +40 actors, +850 field, +854 camera, +B9C renderer, +BB4 Atel id; field+1010 scene,
/// +1180 actor*2; renderer+2A0 scene) and actor records at actors+6940+i*154 (class +40, visual
/// +44, draw mode +D0, loaded +D8).
/// ACTUAL DATA EVIDENCE: the script bytes placed at data+12000 are the installed Atel_0310..0318
/// read from the supported resources.bin, and the Installed* tests check every pinned value
/// against those files. Actor states mirror the 0.3.77 tester log actor facts (desert-lavos-0377,
/// lines 6976-8274).
/// </summary>
public sealed class LavosFlashbackCaptureTests
{
    private const uint Context = 0x1000, Data = 0xA0000, Actors = 0x200000, Field = 0x5000, Renderer = 0x7000;

    // (subject, scene, Atel id, anchor runtime PC, actors: (slot, class, visual, drawn)) at the anchor.
    private static readonly Dictionary<LavosFlashbackSubject, (int Scene, int Script, uint Pc, (int Slot, int Class, int Visual, bool Drawn)[] Actors)> Shots = new()
    {
        [LavosFlashbackSubject.DragonTank] = (347, 311, 0xA4, [(1, 6, 0x93, true), (2, 5, 0xAD, true), (3, 5, 0x94, true)]),
        [LavosFlashbackSubject.Guardian] = (346, 310, 0x84, [(1, 5, 0xCF, false), (2, 5, 0xCF, false)]),
        [LavosFlashbackSubject.Heckran] = (348, 312, 0x64, [(1, 5, 0xA9, true)]),
        [LavosFlashbackSubject.Zombor] = (349, 313, 0x84, [(1, 6, 0xB4, true), (2, 6, 0xB3, true)]),
        [LavosFlashbackSubject.MasaAndMune] = (350, 314, 0x64, [(1, 5, 0x99, true)]),
        [LavosFlashbackSubject.Nizbel] = (456, 315, 0x64, [(1, 5, 0x9B, true)]),
        [LavosFlashbackSubject.Magus] = (457, 316, 0x64, [(1, 3, 0x06, true)]),
        [LavosFlashbackSubject.BlackTyrannoAndAzala] = (458, 317, 0x84, [(1, 6, 0xBC, true), (2, 6, 0x9A, true)]),
        [LavosFlashbackSubject.GigaGaia] = (459, 318, 0xA4, [(1, 5, 0xD1, false), (2, 5, 0xD2, true), (3, 5, 0xD3, true)]),
    };

    public static TheoryData<LavosFlashbackSubject> Subjects => new(Enum.GetValues<LavosFlashbackSubject>());

    [Theory]
    [MemberData(nameof(Subjects))]
    public void ActorZerosFirstVisiblePauseInEachFlashbackIsThatFlashback(LavosFlashbackSubject subject)
    {
        var shot = Shots[subject];
        var frame = LavosFlashbackCapture.Capture(Flashback(subject), Context, 0xAD);
        Assert.Equal(new LavosFlashbackFrame(LavosFlashbackKind.Flashback, Context, Data, Actors, Field,
            shot.Scene, shot.Script, subject), frame);
    }

    [Fact]
    public void ACoherentOtherSceneIsAnOutsideFrame()
    {
        // Scene 467 (Atel_0286) after the battle: a located dispatch outside every flashback scene.
        var memory = Located(467, 286, pc: 0x0D60, actor: 8, opcode: 0xBB, actorCount: 29);
        Assert.Equal(new LavosFlashbackFrame(LavosFlashbackKind.Outside, Context, Data, Actors, Field, 467, 286, null),
            LavosFlashbackCapture.Capture(memory, Context, 0xBB));
    }

    [Theory]
    [InlineData(0x87u, 0, 0xE3)]  // E3 00 before the fade-in
    [InlineData(0x97u, 0, 0xF2)]  // F2 still waiting for the fade
    [InlineData(0xADu, 0, 0xAD)]  // the second pause, after the global write
    [InlineData(0xA4u, 1, 0xAD)]  // another actor at the anchor PC
    public void OtherInstructionsInsideAFlashbackAreNotAnchorsAndNotOutside(uint pc, int actor, int opcode)
    {
        var memory = Flashback(LavosFlashbackSubject.DragonTank, pc: pc, actor: actor);
        Assert.Null(LavosFlashbackCapture.Capture(memory, Context, opcode));
    }

    [Fact]
    public void TheDispatchedOpcodeMustBeTheByteAtThePc() =>
        // Observed before the original handler: TryLocate needs the live byte at PC.
        Assert.Null(LavosFlashbackCapture.Capture(Flashback(LavosFlashbackSubject.DragonTank), Context, 0xE3));

    public static TheoryData<string> ForeignScripts => new() { "digest", "script id", "anchor operand" };

    [Theory]
    [MemberData(nameof(ForeignScripts))]
    public void AScriptThatIsNotThePinnedFlashbackIsUnknown(string mutation)
    {
        var memory = Flashback(LavosFlashbackSubject.Heckran);
        switch (mutation)
        {
            case "digest": memory.Byte(Data + 0x12000 + 0x7C, 0xAA); break;       // actor 1 would load enemy 0xAA
            case "script id": memory.Word(Context + 0xBB4, 311); break;
            case "anchor operand": memory.Byte(Data + 0x12000 + 0x66, 0x1F); break; // AD 1F
        }
        Assert.Null(LavosFlashbackCapture.Capture(memory, Context, 0xAD));
    }

    [Fact]
    public void ARendererThatHasNotCommittedTheSceneIsUnknown()
    {
        var memory = Flashback(LavosFlashbackSubject.Nizbel);
        memory.Word(Renderer + 0x2A0, 467);
        Assert.Null(LavosFlashbackCapture.Capture(memory, Context, 0xAD));
    }

    [Fact]
    public void AFadeStillInProgressIsUnknown()
    {
        var memory = Flashback(LavosFlashbackSubject.Nizbel);
        memory.Byte(Actors + 0x12120, 0x02); // F0 set the fade bit that F2 waits on
        Assert.Null(LavosFlashbackCapture.Capture(memory, Context, 0xAD));
    }

    public static TheoryData<LavosFlashbackSubject, int, string> MissingPresentation => new()
    {
        { LavosFlashbackSubject.DragonTank, 1, "hidden" },
        { LavosFlashbackSubject.DragonTank, 2, "unloaded" },
        { LavosFlashbackSubject.Heckran, 1, "visual" },
        { LavosFlashbackSubject.Zombor, 2, "class" },
        { LavosFlashbackSubject.Guardian, 2, "unloaded" },
        { LavosFlashbackSubject.Guardian, 1, "visual" },
        { LavosFlashbackSubject.GigaGaia, 2, "hidden" },
        { LavosFlashbackSubject.GigaGaia, 1, "unloaded" },
        { LavosFlashbackSubject.Magus, 1, "hidden" },
        { LavosFlashbackSubject.Magus, 1, "class" },
        { LavosFlashbackSubject.BlackTyrannoAndAzala, 2, "hidden" },
    };

    [Theory]
    [MemberData(nameof(MissingPresentation))]
    public void AFlashbackWithoutItsLoadedOrDrawnPresentationIsUnknown(LavosFlashbackSubject subject, int slot, string mutation)
    {
        var memory = Flashback(subject);
        var record = Actors + 0x6940 + (uint)slot * 0x154;
        switch (mutation)
        {
            case "hidden": memory.Word(record + 0xD0, 0); break;
            case "unloaded": memory.Word(record + 0xD8, 0); break;
            case "visual": memory.Word(record + 0x44, 0x94); break;
            case "class": memory.Word(record + 0x40, 4); break;
        }
        Assert.Null(LavosFlashbackCapture.Capture(memory, Context, 0xAD));
    }

    [Fact]
    public void MapArtFlashbacksNeedOnlyTheirLoadedHiddenActors()
    {
        // Guardian's body and Giga Gaia's head are drawn by the scene map; at the anchor the Pods and
        // the head actor are loaded but still hidden (91), as the 0.3.77 log shows (draw=0).
        Assert.Equal(LavosFlashbackSubject.Guardian, LavosFlashbackCapture.Capture(Flashback(LavosFlashbackSubject.Guardian), Context, 0xAD)?.Subject);
        Assert.Equal(LavosFlashbackSubject.GigaGaia, LavosFlashbackCapture.Capture(Flashback(LavosFlashbackSubject.GigaGaia), Context, 0xAD)?.Subject);
    }

    [Fact]
    public void AnOwnerThatChangesDuringTheReadIsRejected()
    {
        var memory = Flashback(LavosFlashbackSubject.Heckran);
        var reads = 0;
        memory.BeforeRead = address =>
        {
            // TryLocate reads the 0x44-byte head at ctx+0; a separate ctx+0x40 word read can only be
            // an identity reread, and the actor store then moves before it.
            if (address == Context + 0x40 && ++reads == 1) memory.Word(Context + 0x40, Actors + 0x40000);
        };
        Assert.Null(LavosFlashbackCapture.Capture(memory, Context, 0xAD));
        Assert.True(reads >= 1);
    }

    [Fact]
    public void UnreadableDispatchesAreUnknown()
    {
        Assert.Null(LavosFlashbackCapture.Capture(new NavigationMemory(), Context, 0xAD));
        Assert.Null(LavosFlashbackCapture.Capture(Flashback(LavosFlashbackSubject.Heckran), 0, 0xAD));
    }

    public static TheoryData<LavosFlashbackSubject, int[], int?> Labels => new()
    {
        { LavosFlashbackSubject.DragonTank, [0x93], null },
        { LavosFlashbackSubject.Guardian, [0xD4], null },
        { LavosFlashbackSubject.Heckran, [0xA9], null },
        { LavosFlashbackSubject.Zombor, [0xB4], null },
        { LavosFlashbackSubject.MasaAndMune, [0x99], null },
        { LavosFlashbackSubject.Nizbel, [0x9B], null },
        { LavosFlashbackSubject.Magus, [0xF9], 6 },
        { LavosFlashbackSubject.BlackTyrannoAndAzala, [0xBC, 0x9A], null },
        { LavosFlashbackSubject.GigaGaia, [0xD1], null },
    };

    [Theory]
    [MemberData(nameof(Labels))]
    public void EachFlashbackIsNamedByItsOriginalBossesNativeNames(LavosFlashbackSubject subject, int[] monsters, int? character)
    {
        var label = LavosFlashbackCapture.Label(subject);
        Assert.Equal(monsters, label.Monsters);
        Assert.Equal(character, label.Character);
    }

    [Fact]
    public void InstalledFlashbackScriptsMatchEveryPinnedValue()
    {
        Assert.Equal(9, LavosFlashbackCapture.Scripts.Count);
        foreach (var pinned in LavosFlashbackCapture.Scripts)
        {
            var shot = Shots[pinned.Subject];
            var script = InstalledFieldScripts.Atel(pinned.ScriptId);
            Assert.Equal((shot.Scene, shot.Script, shot.Pc), (pinned.Scene, pinned.ScriptId, pinned.AnchorPc));
            Assert.Equal(pinned.Length, script.Length);
            Assert.Equal(pinned.Sha256, Convert.ToHexString(SHA256.HashData(script)));
            Assert.Equal(pinned.ActorCount, script[0]);
            // Runtime PC p executes file byte p+1. The anchor is the first AD 1E, after F2 finished the
            // F0 F1 fade-in and the two 2E palette steps.
            var fadeThenPause = Convert.FromHexString("F22E50001C0F062E501FE00F06AD1E");
            Assert.Equal("F22E50001C0F062E501FE00F06AD1E", Convert.ToHexString(script, (int)pinned.AnchorPc + 1 - 13, 15));
            // ...and it is the first such pause in the script: nothing earlier is a visible anchor.
            Assert.Equal((int)pinned.AnchorPc + 1 - 13, IndexOf(script, fadeThenPause));
        }
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var start = 0; start + needle.Length <= haystack.Length; start++)
            if (haystack.AsSpan(start, needle.Length).SequenceEqual(needle)) return start;
        return -1;
    }

    [Fact]
    public void InstalledFlashbackActorsLoadThePinnedSprites()
    {
        foreach (var subject in Enum.GetValues<LavosFlashbackSubject>())
        {
            var shot = Shots[subject];
            var script = InstalledFieldScripts.Atel(shot.Script);
            var presence = LavosFlashbackCapture.Presence(subject);
            Assert.Equal(shot.Actors.Select(a => (a.Slot, a.Visual, a.Class == 3, a.Drawn)),
                presence.Select(p => (p.Actor, p.Visual, p.PlayerCharacter, p.Drawn)));
            foreach (var actor in presence)
            {
                var init = BinaryPrimitives.ReadUInt16LittleEndian(script.AsSpan(1 + actor.Actor * 32)) + 1;
                // 83 = load enemy (helper 16B220 binds visual+0x107); 81 06 = character 6 (Magus).
                var expected = actor.PlayerCharacter ? $"81{actor.Visual:X2}" : $"83{actor.Visual:X2}00";
                Assert.StartsWith(expected, Convert.ToHexString(script, init, 4));
            }
        }
    }

    private static NavigationMemory Flashback(LavosFlashbackSubject subject, uint? pc = null, int actor = 0)
    {
        var shot = Shots[subject];
        var script = InstalledFieldScripts.Atel(shot.Script);
        var memory = Located(shot.Scene, shot.Script, pc ?? shot.Pc, actor, opcode: null, actorCount: script[0]);
        memory.Add(Data + 0x12000, script);
        foreach (var (slot, @class, visual, drawn) in shot.Actors)
        {
            var record = Actors + 0x6940 + (uint)slot * 0x154;
            memory.Word(record + 0x40, (uint)@class);
            memory.Word(record + 0x44, (uint)visual);
            memory.Word(record + 0xD0, drawn ? 1u : 0u);
            memory.Word(record + 0xD8, 1);
        }
        return memory;
    }

    private static NavigationMemory Located(int scene, int script, uint pc, int actor, int? opcode, int actorCount)
    {
        var memory = new NavigationMemory();
        memory.Add(Context, new byte[0xBB8]);
        memory.Word(Context, Data);
        memory.Word(Context + 0x24, pc);
        memory.Word(Context + 0x40, Actors);
        memory.Word(Context + 0x850, Field);
        memory.Word(Context + 0xB9C, Renderer);
        memory.Word(Context + 0xBB4, (uint)script);
        memory.Add(Field + 0x1000, new byte[0x200]);
        memory.Word(Field + 0x1010, (uint)scene);
        memory.Word(Field + 0x1180, (uint)actor * 2);
        memory.Add(Renderer + 0x290, new byte[0x20]);
        memory.Word(Renderer + 0x2A0, (uint)scene);
        memory.Add(Actors + 0x12120, new byte[4]);
        for (var slot = 0; slot < actorCount; slot++) memory.Add(Actors + 0x6940 + (uint)slot * 0x154, new byte[0x154]);
        memory.Add(Data + 0x12000, [(byte)actorCount]);
        if (opcode is { } op) memory.Add(Data + 0x12001 + pc, [(byte)op]);
        return memory;
    }
}
