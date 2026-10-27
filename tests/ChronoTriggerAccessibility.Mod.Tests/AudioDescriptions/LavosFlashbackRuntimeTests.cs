using ChronoTriggerAccessibility.Mod.AudioDescriptions;
using ChronoTriggerAccessibility.Native.Capture;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.AudioDescriptions;

/// <summary>Frame sequences as LavosFlashbackCapture proves them before each field-script
/// instruction. The name lookups stand in for root's loaded-text adapters: bank 0x32
/// (monster.txt) and the current character names.</summary>
public sealed class LavosFlashbackRuntimeTests
{
    [Theory]
    [InlineData(0x11)] [InlineData(0x4A)] [InlineData(0x75)] [InlineData(0xAE)] [InlineData(0xBB)] [InlineData(0xE3)]
    public void IdleOrdinaryOpcodesDoNotReadMemory(int opcode)
    {
        var memory=new CountingMemory();var originals=0;var foreground=0;
        var runtime=new LavosFlashbackRuntime((context,op)=>LavosFlashbackCapture.Capture(memory,(nuint)context,op),
            ()=>{foreground++;return true;},_=>null,_=>null,_=>{},_=>{});
        runtime.Enable();runtime.Dispatch(0x1000,opcode,()=>originals++);
        Assert.Equal(1,originals);Assert.Equal(0,memory.Reads);Assert.Equal(0,foreground);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ASpokenOrWithheldEntryStillObservesAnOrdinaryOpcodeToRearm(bool withheld)
    {
        var h=new Harness();if(withheld)h.Monsters.Remove(0x93);
        h.Run(Flash(LavosFlashbackSubject.DragonTank));
        h.Next.Enqueue(Outside(467));h.Runtime.Dispatch(1,0x75,()=>{});
        h.Runtime.Dispatch(1,0x75,()=>{});Assert.Equal(2,h.Captures);
        h.Monsters[0x93]="Dragon Tank";h.Run(Flash(LavosFlashbackSubject.DragonTank));
        Assert.Equal(withheld?1:2,h.Speech.Count);
    }

    private sealed class CountingMemory:IReadableMemory
    {
        public int Reads;
        public bool TryRead(nuint address,Span<byte> bytes){Reads++;return false;}
    }
    private static readonly Dictionary<int, string> English = new()
    {
        [0x93] = "Dragon Tank", [0xD4] = "Guardian", [0xA9] = "Heckran", [0xB4] = "Zombor", [0x99] = "Masa & Mune",
        [0x9B] = "Nizbel", [0xF9] = "Magus", [0xBC] = "Black Tyranno", [0x9A] = "Azala", [0xD1] = "Giga Gaia",
    };

    [Fact]
    public void AFlashbackIsSpokenOnceWithItsNativeName()
    {
        var h = new Harness();
        h.Run(Flash(LavosFlashbackSubject.DragonTank), Flash(LavosFlashbackSubject.DragonTank), Flash(LavosFlashbackSubject.DragonTank));
        Assert.Equal(["Lavos flashback: Dragon Tank."], h.Speech);
    }

    [Fact]
    public void UnknownDispatchesInsideTheSceneDoNotRearm()
    {
        var h = new Harness();
        h.Run(Flash(LavosFlashbackSubject.Heckran), null, null, Flash(LavosFlashbackSubject.Heckran));
        Assert.Equal(["Lavos flashback: Heckran."], h.Speech);
    }

    [Fact]
    public void AProvenSceneChangeRearmsTheSameFlashback()
    {
        var h = new Harness();
        // A retried battle returns to scene 467 and later replays the same flashback scene.
        h.Run(Flash(LavosFlashbackSubject.DragonTank), Outside(467), Flash(LavosFlashbackSubject.DragonTank));
        Assert.Equal(["Lavos flashback: Dragon Tank.", "Lavos flashback: Dragon Tank."], h.Speech);
    }

    [Fact]
    public void ANewOwnerIsANewEntry()
    {
        var h = new Harness();
        h.Run(Flash(LavosFlashbackSubject.Nizbel, owner: 1), Flash(LavosFlashbackSubject.Nizbel, owner: 2));
        Assert.Equal(["Lavos flashback: Nizbel.", "Lavos flashback: Nizbel."], h.Speech);
    }

    [Fact]
    public void TheWholeSequenceNamesEachFormOnce()
    {
        var h = new Harness { Character = "Janus" };
        foreach (var subject in Enum.GetValues<LavosFlashbackSubject>())
            h.Run(Outside(467), Flash(subject), Flash(subject));
        h.Run(Outside(467), Outside(467)); // the final shell transforms in place: no flashback
        Assert.Equal([
            "Lavos flashback: Dragon Tank.", "Lavos flashback: Guardian.", "Lavos flashback: Heckran.",
            "Lavos flashback: Zombor.", "Lavos flashback: Masa & Mune.", "Lavos flashback: Nizbel.",
            "Lavos flashback: Janus.", "Lavos flashback: Black Tyranno and Azala.", "Lavos flashback: Giga Gaia.",
        ], h.Speech);
    }

    [Fact]
    public void MagusUsesTheCurrentCharacterNameThenTheNativeBossName()
    {
        var named = new Harness { Character = "Janus" };
        named.Run(Flash(LavosFlashbackSubject.Magus));
        Assert.Equal(["Lavos flashback: Janus."], named.Speech);

        var unnamed = new Harness { Character = null };
        unnamed.Run(Flash(LavosFlashbackSubject.Magus));
        Assert.Equal(["Lavos flashback: Magus."], unnamed.Speech);

        var unresolved = new Harness { Character = "<NAME_MAG>" };
        unresolved.Run(Flash(LavosFlashbackSubject.Magus));
        Assert.Equal(["Lavos flashback: Magus."], unresolved.Speech);
    }

    [Fact]
    public void LocalizedNamesComeOnlyFromTheNativeLookup()
    {
        var h = new Harness();
        h.Monsters[0x93] = "Dracopanzer";
        h.Monsters[0xBC] = "Tyrannosaurus";
        h.Monsters[0x9A] = "Azara";
        h.Run(Flash(LavosFlashbackSubject.DragonTank), Outside(467), Flash(LavosFlashbackSubject.BlackTyrannoAndAzala));
        Assert.Equal(["Lavos flashback: Dracopanzer.", "Lavos flashback: Tyrannosaurus and Azara."], h.Speech);
    }

    [Fact]
    public void AnUnavailableNameSpeaksNothingAndRetriesQuietly()
    {
        var h = new Harness();
        h.Monsters.Remove(0x9A);
        h.Run(Flash(LavosFlashbackSubject.BlackTyrannoAndAzala), Flash(LavosFlashbackSubject.BlackTyrannoAndAzala));
        Assert.Empty(h.Speech);
        Assert.Single(h.Diagnostics);
        h.Monsters[0x9A] = "Azala";
        h.Run(Flash(LavosFlashbackSubject.BlackTyrannoAndAzala), Flash(LavosFlashbackSubject.BlackTyrannoAndAzala));
        Assert.Equal(["Lavos flashback: Black Tyranno and Azala."], h.Speech);

        var magus = new Harness { Character = null };
        magus.Monsters.Remove(0xF9);
        magus.Run(Flash(LavosFlashbackSubject.Magus));
        Assert.Empty(magus.Speech);
    }

    [Fact]
    public void ForegroundLossSuppressesAndResets()
    {
        var h = new Harness { Foreground = false };
        h.Run(Flash(LavosFlashbackSubject.Guardian));
        Assert.Empty(h.Speech);
        h.Foreground = true;
        h.Run(Flash(LavosFlashbackSubject.Guardian), Flash(LavosFlashbackSubject.Guardian));
        Assert.Equal(["Lavos flashback: Guardian."], h.Speech);
    }

    [Fact]
    public void TheCaptureRunsBeforeTheOriginalWhichAlwaysRunsOnce()
    {
        var log = new List<string>();
        var diagnostics = new List<string>();
        var runtime = new LavosFlashbackRuntime((_, _) => { log.Add("capture"); throw new InvalidOperationException(); },
            () => true, _ => "x", _ => "x", text => log.Add("speak " + text), diagnostics.Add);
        runtime.Enable();
        runtime.Dispatch(1, 0xAD, () => log.Add("original"));
        Assert.Equal(["capture", "original"], log);
        Assert.Single(diagnostics);
    }

    [Fact]
    public void NothingIsCapturedWhileDisabled()
    {
        var h = new Harness();
        h.Runtime.Disable();
        h.Next.Enqueue(Flash(LavosFlashbackSubject.Heckran));
        var called = 0;
        h.Runtime.Dispatch(1, 0xAD, () => called++);
        Assert.Equal(1, called);
        Assert.Equal(0, h.Captures);
        Assert.Empty(h.Speech);
    }

    [Fact]
    public void SpeechCarriesNoDebugLabelsOrInternalIdentifiers()
    {
        var h = new Harness { Character = "Magus" };
        foreach (var subject in Enum.GetValues<LavosFlashbackSubject>()) h.Run(Outside(467), Flash(subject));
        Assert.Equal(9, h.Speech.Count);
        foreach (var text in h.Speech)
        {
            Assert.StartsWith("Lavos flashback: ", text);
            Assert.DoesNotContain("Past Mode", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Lavos flashback: Lavos", text);
            Assert.False(text.Any(char.IsDigit), text);
        }
    }

    private static LavosFlashbackFrame Flash(LavosFlashbackSubject subject, uint owner = 1)
    {
        var (scene, script) = subject switch
        {
            LavosFlashbackSubject.DragonTank => (347, 311), LavosFlashbackSubject.Guardian => (346, 310),
            LavosFlashbackSubject.Heckran => (348, 312), LavosFlashbackSubject.Zombor => (349, 313),
            LavosFlashbackSubject.MasaAndMune => (350, 314), LavosFlashbackSubject.Nizbel => (456, 315),
            LavosFlashbackSubject.Magus => (457, 316), LavosFlashbackSubject.BlackTyrannoAndAzala => (458, 317),
            _ => (459, 318),
        };
        return new(LavosFlashbackKind.Flashback, owner, 0xA0000, 0x200000, 0x5000, scene, script, subject);
    }

    private static LavosFlashbackFrame Outside(int scene) =>
        new(LavosFlashbackKind.Outside, 1, 0xA0000, 0x200000, 0x5000, scene, 286, null);

    private sealed class Harness
    {
        public List<string> Speech { get; } = [];
        public List<string> Diagnostics { get; } = [];
        public Dictionary<int, string> Monsters { get; } = new(English);
        public Queue<LavosFlashbackFrame?> Next { get; } = new();
        public string? Character = "Magus";
        public bool Foreground = true;
        public int Captures;
        public LavosFlashbackRuntime Runtime { get; }

        public Harness()
        {
            Runtime = new LavosFlashbackRuntime((_, _) => { Captures++; return Next.Count == 0 ? null : Next.Dequeue(); },
                () => Foreground, id => Monsters.GetValueOrDefault(id), id => id == 6 ? Character : null,
                Speech.Add, Diagnostics.Add);
            Runtime.Enable();
        }

        public void Run(params LavosFlashbackFrame?[] frames)
        {
            foreach (var frame in frames)
            {
                Next.Enqueue(frame);
                Runtime.Dispatch(1, 0xAD, () => { });
            }
        }
    }
}
