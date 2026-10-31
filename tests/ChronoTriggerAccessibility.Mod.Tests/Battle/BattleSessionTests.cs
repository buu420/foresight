using System.Buffers.Binary;
using System.Text;
using ChronoTriggerAccessibility.Core.Battle;
using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Mod.Battle;
using ChronoTriggerAccessibility.Native.Memory;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Battle;

public sealed class BattleSessionTests
{
    private const uint Image = 0x400000, Canvas = 0x100000, Menu = 0x200000;
    private const uint Label = 0x500000, Protocol = 0x510000, Getter = 0x520000;

    [Fact]
    public void NativeMemoryToKeyboardAndSpeechUsesTheSelectedMemberAndTheDisplayedHit()
    {
        var m = World(); var events = new List<AccessibilityEvent>(); var keys = new HashSet<int>();
        var keyboard = new BattleKeyboard(keys.Contains, () => true);
        var runtime = new BattleRuntime(events.Add, keyboard, () => true, _ => { });
        var session = new BattleSession(m, runtime, _ => { }); session.BindImageBase(Image);
        session.Tick(Menu);
        keyboard.FilterGameKeyboardState(new byte[256], runtime.IsActive);
        Assert.Equal("Crono: Attack", Assert.Single(events.OfType<BattleFocusChanged>(), e => e.Text is not null).Text);
        keys.Add('2'); session.Tick(Menu); keys.Clear(); session.Tick(Menu);
        m.U16(Canvas + 0x15BA3 + 0x80, 27);
        var snapshot = new byte[256]; snapshot['H'] = 0x80; snapshot[0x10] = 0x80;
        keyboard.FilterGameKeyboardState(snapshot, runtime.IsActive);
        Assert.Equal(0, snapshot['H']);
        session.Tick(Menu);
        keyboard.FilterGameKeyboardState(new byte[256], runtime.IsActive);
        keys.Add('M'); session.Tick(Menu);
        Assert.Equal(["Marle. HP 50 of 80. MP 7 of 12.", "Marle. HP 27 of 80.", "Marle. MP 7 of 12."],
            events.OfType<BattleInspectionRequested>().Select(e => e.Text));

        m.U32(Canvas + 0x140AC, 3); // displayed white damage, even if larger than remaining HP
        m.String(Menu + 0x34, "1200"); m.Bytes[Menu + 0x1B5] = 1;
        m.Bytes[Menu + 0x194] = 255; m.Bytes[Menu + 0x195] = 255; m.Bytes[Menu + 0x196] = 255;
        session.Number(Menu, 0); session.Render(Menu); session.Render(Menu);
        Assert.Equal("Crono takes 1200 damage.", Assert.Single(events.OfType<BattleFeedbackPresented>()).Text);
        session.Close(Menu); Assert.False(runtime.IsActive);
        var count = events.Count; runtime.Handle(BattleCommand.ReadHp); Assert.Equal(count, events.Count);
    }

    [Fact]
    public void AnEmptyItemPanelSpeaksOnceAndCanBeRepeatedWhileActiveBattleUpdatesContinue()
    {
        var m = World(); var events = new List<AccessibilityEvent>();
        m.U32(Menu + 0x6A8, 2); m.U32(Canvas + 0x19E90, 2);
        m.String(0x302030, "Item");
        var runtime = new BattleRuntime(events.Add, new BattleKeyboard(_ => false, () => true), () => true, _ => { });
        var session = new BattleSession(m, runtime, _ => { }); session.BindImageBase(Image);
        for (var i = 0; i < 120; i++) session.Tick(Menu);
        Assert.Equal("Crono: Item. Empty.", Assert.Single(events.OfType<BattleFocusChanged>(), e => e.Text is not null).Text);
        runtime.Handle(BattleCommand.Repeat);
        Assert.Equal("Crono: Item. Empty.", Assert.Single(events.OfType<BattleInspectionRequested>()).Text);
    }

    [Fact]
    public void PersistentCaptureFailureIsAnnouncedOnceAndTeardownStillWorks()
    {
        var events = new List<AccessibilityEvent>(); var logs = new List<string>();
        var runtime = new BattleRuntime(events.Add, new BattleKeyboard(_ => false, () => true), () => true, _ => { });
        var session = new BattleSession(new Memory(), runtime, logs.Add); session.BindImageBase(Image);
        for (var i = 0; i < 120; i++) session.Tick(Menu);
        Assert.Single(events.OfType<BattleFeedbackPresented>());
        Assert.Single(logs, x => x.Contains("unavailable"));
        session.Close(Menu); Assert.False(runtime.IsActive);
    }

    [Fact]
    public void EnemyIdentityStaysWithItsNativeSlotWhenAnIdenticalEnemyIsRemoved()
    {
        var m = World(); var events = new List<AccessibilityEvent>();
        Monsters(m, 2, (1, "Blue Imp"));
        m.U32(Canvas + 0x1A018 + 3 * 4, 1); m.U32(Canvas + 0x1A018 + 5 * 4, 1);
        Aim(m, [5], 3, 5); // 44B9C0 offered both identical enemies for this selection
        var runtime = Runtime(events); var session = Session(m, runtime);
        session.Tick(Menu);
        Assert.Equal("Target. Blue Imp C.", Assert.Single(events.OfType<BattleFocusChanged>(), e => e.Text is not null).Text);
        m.U32(Canvas + 0x1A018 + 3 * 4, uint.MaxValue); Aim(m, [5], 5); session.Tick(Menu);
        Assert.Single(events.OfType<BattleFocusChanged>(), e => e.Text is not null);
        runtime.Handle(BattleCommand.Repeat);
        Assert.Equal("Target. Blue Imp C.", Assert.Single(events.OfType<BattleInspectionRequested>()).Text);
        Hit(m, session, 5, "9");
        Assert.Equal("Blue Imp C takes 9 damage.", Assert.Single(events.OfType<BattleFeedbackPresented>()).Text);
    }

    [Fact]
    public void AUniqueEnemyIsNotLetteredWhenAnUnusedSlotKeepsTheSameName()
    {
        var m = World(); var events = new List<AccessibilityEvent>();
        Monsters(m, 2, (1, "Blue Imp"));
        // Slot 3 keeps a stale index for the same monster, but the game offers only slot 5.
        m.U32(Canvas + 0x1A018 + 3 * 4, 1); m.U32(Canvas + 0x1A018 + 5 * 4, 1);
        Aim(m, [5], 5);
        var runtime = Runtime(events); var session = Session(m, runtime);
        session.Tick(Menu);
        Assert.Equal("Target. Blue Imp.", Assert.Single(events.OfType<BattleFocusChanged>(), e => e.Text is not null).Text);
        Hit(m, session, 5, "7");
        Assert.Equal("Blue Imp takes 7 damage.", Assert.Single(events.OfType<BattleFeedbackPresented>()).Text);
    }

    [Fact]
    public void AProvenLetterStaysWithItsSlotForLaterHitsAfterTheOtherEnemyFalls()
    {
        var m = World(); var events = new List<AccessibilityEvent>();
        Monsters(m, 2, (1, "Blue Imp"));
        m.U32(Canvas + 0x1A018 + 3 * 4, 1); m.U32(Canvas + 0x1A018 + 5 * 4, 1);
        Aim(m, [3], 3, 5);
        var runtime = Runtime(events); var session = Session(m, runtime);
        session.Tick(Menu);
        Assert.Equal("Target. Blue Imp A.", LastFocus(events));
        // Blue Imp A falls. The next selection offers only C, which keeps the letter it was given.
        m.U32(Canvas + 0x1A018 + 3 * 4, uint.MaxValue); Aim(m, [5], 5); session.Tick(Menu);
        Assert.Equal("Target. Blue Imp C.", LastFocus(events));
        Hit(m, session, 5, "8");
        Assert.Equal("Blue Imp C takes 8 damage.", LastFeedback(events));
    }

    [Fact]
    public void ALetterEndsWhenItsSlotShowsAnotherNameOrANewBattleBegins()
    {
        const uint next = 0x210000; // a later battle's menu
        var m = World(); var events = new List<AccessibilityEvent>();
        Monsters(m, 3, (1, "Blue Imp"), (2, "Nu"));
        m.U32(next, Image + 0x39D884); m.Bytes[next + 0x538] = 1;
        m.U32(Canvas + 0x1A018 + 3 * 4, 1); m.U32(Canvas + 0x1A018 + 5 * 4, 1);
        Aim(m, [5], 3, 5);
        var runtime = Runtime(events); var session = Session(m, runtime);
        session.Tick(Menu);
        Assert.Equal("Target. Blue Imp C.", LastFocus(events));
        Aim(m, [5], 5);
        m.U32(Canvas + 0x1A018 + 5 * 4, 2); session.Tick(Menu); Hit(m, session, 5, "6");
        Assert.Equal("Target. Nu.", LastFocus(events));
        Assert.Equal("Nu takes 6 damage.", LastFeedback(events));
        // The same name returning to the slot is not proof of a duplicate on its own.
        m.U32(Canvas + 0x1A018 + 5 * 4, 1); session.Tick(Menu); Hit(m, session, 5, "5");
        Assert.Equal("Target. Blue Imp.", LastFocus(events));
        Assert.Equal("Blue Imp takes 5 damage.", LastFeedback(events));
        Aim(m, [5], 3, 5); session.Tick(Menu);
        Assert.Equal("Target. Blue Imp C.", LastFocus(events));
        // A new battle owner starts without letters; slot 3's name is stale there.
        Aim(m, [5], 5); session.Tick(next);
        Assert.Equal("Target. Blue Imp.", LastFocus(events));
        Aim(m, [5], 3, 5); session.Tick(next); session.Close(next);
        Aim(m, [5], 5); session.Tick(next);
        Assert.Equal("Target. Blue Imp.", LastFocus(events));
    }

    [Theory]
    [InlineData("repeated")]
    [InlineData("out-of-range")]
    [InlineData("changing")]
    public void WithoutStableCandidateEvidenceOnlyTheCommittedGroupProvesADuplicate(string kind)
    {
        var m = World(); var events = new List<AccessibilityEvent>();
        Monsters(m, 2, (1, "Blue Imp"));
        m.U32(Canvas + 0x1A018 + 3 * 4, 1); m.U32(Canvas + 0x1A018 + 5 * 4, 1);
        void Corrupt()
        {
            if (kind == "repeated") m.U32(Canvas + 0x1A140 + 8, 5);
            if (kind == "out-of-range") m.U32(Canvas + 0x1A140 + 8, 11);
        }
        if (kind == "changing")
            m.BeforeRead = a => { if (a == Canvas + 0x1A140) m.U32(Canvas + 0x1A140, m.Word(Canvas + 0x1A140) == 3 ? 0x80u : 3u); };
        Aim(m, [5], 3, 5); Corrupt();
        var runtime = Runtime(events); var session = Session(m, runtime);
        session.Tick(Menu);
        Assert.Equal("Target. Blue Imp.", LastFocus(events));
        // An all-enemies or area selection commits both, which proves them by itself.
        Aim(m, [3, 5], 3, 5); Corrupt(); session.Tick(Menu);
        Assert.Equal("Targets. Blue Imp A. Blue Imp C.", LastFocus(events));
    }

    [Fact]
    public void ABonusMonsterIsNamedAndItsNativeAttackLabelIsNotSpokenAgain()
    {
        var m = World(); var events = new List<AccessibilityEvent>();
        Monsters(m, 0x17C, (0x11A, "Red Ghaj"));
        m.U32(Canvas + 0x1A018 + 3 * 4, 0x11A);
        Aim(m, [3], 3);
        Show(m, "Red Ghaj"); // 44B390 -> 46ED80 -> 19940 type 0x0D, as in the 2026-10-30 log
        var runtime = Runtime(events); var session = Session(m, runtime);
        session.Tick(Menu); session.Message(Menu);
        Assert.Equal("Target. Red Ghaj.", Assert.Single(events.OfType<BattleFocusChanged>(), e => e.Text is not null).Text);
        Assert.Empty(events.OfType<BattleFeedbackPresented>());
        // The label can outlast the selection; it is still the presentation already accounted for.
        m.U32(Canvas + 0x19EEC, 0); session.Tick(Menu);
        Assert.Empty(events.OfType<BattleFeedbackPresented>());
        Show(m, "Flame strengthened!"); session.Tick(Menu);
        Assert.Equal("Flame strengthened!", Assert.Single(events.OfType<BattleFeedbackPresented>()).Text);
        // Outside the native Attack label the same text is ordinary battle text and is spoken.
        Show(m, null); session.Tick(Menu); Show(m, "Red Ghaj"); session.Tick(Menu);
        Assert.Equal("Red Ghaj", LastFeedback(events));
    }

    [Fact]
    public void TheMessageHookSilencesOnlyTheExactNativeNameLabel()
    {
        var m = World(); var events = new List<AccessibilityEvent>();
        Monsters(m, 2, (1, "Blue Imp"));
        m.U32(Canvas + 0x1A018 + 4 * 4, 1);
        Aim(m, [4], 4);
        var runtime = Runtime(events); var session = Session(m, runtime);
        session.Tick(Menu); // the label is not shown yet, so only the hook sees it
        Show(m, "Blue Imp"); session.Message(Menu); session.Tick(Menu);
        Assert.Empty(events.OfType<BattleFeedbackPresented>());
        // 46ECD0's revealed-HP form (battle.txt line 0x30) is new information.
        Show(m, "Blue Imp    120/300"); session.Message(Menu);
        Assert.Equal("Blue Imp    120/300", Assert.Single(events.OfType<BattleFeedbackPresented>()).Text);
    }

    [Theory]
    [InlineData("tech")]
    [InlineData("group")]
    [InlineData("ally")]
    public void MatchingTextOutsideTheNativeAttackNameLabelIsStillSpoken(string kind)
    {
        var m = World(); var events = new List<AccessibilityEvent>();
        Monsters(m, 2, (1, "Blue Imp"));
        m.U32(Canvas + 0x1A018 + 3 * 4, 1); m.U32(Canvas + 0x1A018 + 4 * 4, 1);
        var label = "Blue Imp";
        if (kind == "tech") { m.U32(Canvas + 0x19E90, 1); m.U32(Menu + 0x6A8, 1); Aim(m, [3], 3, 4); }
        else if (kind == "group") Aim(m, [3, 4], 3, 4);
        else { Aim(m, [1], 0, 1); label = "Marle"; }
        Show(m, label);
        var runtime = Runtime(events); var session = Session(m, runtime);
        session.Tick(Menu); session.Message(Menu);
        Assert.Equal(label, Assert.Single(events.OfType<BattleFeedbackPresented>()).Text);
    }

    [Fact]
    public void AnUnreadableTargetNameDoesNotHideTheOtherHighlightedTargets()
    {
        var m = World(); var events = new List<AccessibilityEvent>();
        m.U32(Canvas + 0x19EEC, 1); m.Bytes[Menu + 0x538] = 1;
        for (uint i = 0; i < 11; i++) m.U32(Canvas + 0x1ACD4 + i * 4, 255);
        m.U32(Canvas + 0x1ACD4, 0); m.U32(Canvas + 0x1ACD8, 4); m.U32(Canvas + 0x1ACDC, 1);
        var runtime = new BattleRuntime(events.Add, new BattleKeyboard(_ => false, () => true), () => true, _ => { });
        var session = new BattleSession(m, runtime, _ => { }); session.BindImageBase(Image);
        session.Tick(Menu);
        Assert.Equal("Targets. Crono. HP 43 of 70. MP 8 of 8. Enemy B. Marle. HP 50 of 80. MP 7 of 12.",
            Assert.Single(events.OfType<BattleFocusChanged>(), e => e.Text is not null).Text);
        session.Miss(Menu, 4); m.String(Menu + 0x34 + 4 * 24, "MISS!"); m.Bytes[Menu + 0x1B5 + 4] = 1;
        session.Render(Menu);
        Assert.Equal("Enemy B. MISS!", Assert.Single(events.OfType<BattleFeedbackPresented>()).Text);
    }

    [Fact]
    public void AllyTargetsReadHpAndMpOncePerSelectionAndRepeatReadsTheCurrentHud()
    {
        var m = World(); var events = new List<AccessibilityEvent>();
        m.U32(Canvas + 0x19EEC, 1); m.Bytes[Menu + 0x538] = 1;
        for (uint i = 0; i < 11; i++) m.U32(Canvas + 0x1ACD4 + i * 4, 255);
        m.U32(Canvas + 0x1ACD4, 1);
        var runtime = new BattleRuntime(events.Add, new BattleKeyboard(_ => false, () => true), () => true, _ => { });
        var session = new BattleSession(m, runtime, _ => { }); session.BindImageBase(Image);
        session.Tick(Menu);
        Assert.Equal("Target. Marle. HP 50 of 80. MP 7 of 12.",
            Assert.Single(events.OfType<BattleFocusChanged>(), e => e.Text is not null).Text);
        m.U16(Canvas + 0x15BA3 + 0x80, 27); session.Tick(Menu);
        Assert.Single(events.OfType<BattleFocusChanged>(), e => e.Text is not null);
        runtime.Handle(BattleCommand.Repeat);
        Assert.Equal("Target. Marle. HP 27 of 80. MP 7 of 12.", Assert.Single(events.OfType<BattleInspectionRequested>()).Text);
        m.U32(Canvas + 0x19EEC, 0); session.Tick(Menu);
        m.U16(Canvas + 0x15BA3 + 0x80, 0); m.U32(Canvas + 0x19EEC, 1); session.Tick(Menu);
        Assert.Equal("Target. Marle. Knocked out. MP 7 of 12.", events.OfType<BattleFocusChanged>().Last().Text);
    }

    private static Memory World()
    {
        var m = new Memory();
        m.U32(Image + 0x41B4C4, Canvas); m.U32(Menu, Image + 0x39D884);
        m.U32(Canvas + 0x19FA0, 1); m.U32(Canvas + 0x19FA4, 1);
        m.U32(Canvas + 0x13250, 1); m.String(Canvas + 0x1908, "Crono"); m.String(Canvas + 0x1920, "Marle");
        m.U16(Canvas + 0x15BA3, 43); m.U16(Canvas + 0x15BA5, 70);
        m.U16(Canvas + 0x15BA7, 8); m.U16(Canvas + 0x15BA9, 8);
        m.U16(Canvas + 0x15BA3 + 0x80, 50); m.U16(Canvas + 0x15BA5 + 0x80, 80);
        m.U16(Canvas + 0x15BA7 + 0x80, 7); m.U16(Canvas + 0x15BA9 + 0x80, 12);
        for (uint i = 3; i < 11; i++) m.U32(Canvas + 0x1A018 + i * 4, uint.MaxValue);
        m.U32(Image + 0x41C3D8, 0x300000); m.U32(0x300000, 0x300100); m.U32(0x300004, 0x300200);
        m.U32(0x300100 + 0x3B * 4, 0x301000); m.U32(0x301000, 0x302000); m.U32(0x301004, 0x302060);
        m.String(0x302000, "Attack");
        return m;
    }

    /// <summary>monster.txt as TextManager bank 0x32, with this many lines and these names.</summary>
    private static void Monsters(Memory m, int lines, params (int Index, string Name)[] names)
    {
        m.U32(0x300100 + 0x32 * 4, 0x303000); m.U32(0x303000, 0x304000); m.U32(0x303004, 0x304000 + (uint)lines * 24);
        foreach (var (index, name) in names) m.String(0x304000 + (uint)index * 24, name);
    }

    /// <summary>
    /// A target selection: the phase gate, a drawn arrow, the committed cells and the candidate
    /// cells that 44B770 resets to FF before 44B9C0 fills them.
    /// </summary>
    private static void Aim(Memory m, uint[] committed, params uint[] candidates)
    {
        m.U32(Canvas + 0x19EEC, 1); m.Bytes[Menu + 0x538] = 1;
        for (uint i = 0; i < 11; i++)
        {
            m.U32(Canvas + 0x1ACD4 + i * 4, i < committed.Length ? committed[i] : 255);
            m.U32(Canvas + 0x1A140 + i * 4, i < candidates.Length ? candidates[i] : 255);
        }
    }

    /// <summary>The battle message label at menu + 0x234, showing this text, or hidden.</summary>
    private static void Show(Memory m, string? text)
    {
        m.U32(Menu + 0x234, Label); m.U32(Label + 0x278, Protocol); m.U32(Protocol + 8, Getter);
        m.Bytes[Getter] = 0x8D; m.Bytes[Getter + 1] = 0x41; m.Bytes[Getter + 2] = 0x28; m.Bytes[Getter + 3] = 0xC3;
        m.Bytes[Label + 0x1AD] = (byte)(text is null ? 0 : 1);
        if (text is not null) m.String(Label + 0x2A0, text);
    }

    /// <summary>One white damage popup (kind 3, result row 0) drawn once over this slot.</summary>
    private static void Hit(Memory m, BattleSession session, int slot, string number)
    {
        m.U32(Canvas + 0x140AC + (uint)slot * 16, 3);
        m.String(Menu + 0x34 + (uint)slot * 24, number); m.Bytes[Menu + 0x1B5 + (uint)slot] = 1;
        session.Number(Menu, slot); session.Render(Menu);
        m.Bytes[Menu + 0x1B5 + (uint)slot] = 0;
    }

    private static BattleRuntime Runtime(List<AccessibilityEvent> events) =>
        new(events.Add, new BattleKeyboard(_ => false, () => true), () => true, _ => { });

    private static BattleSession Session(Memory m, BattleRuntime runtime)
    {
        var session = new BattleSession(m, runtime, _ => { }); session.BindImageBase(Image);
        return session;
    }

    private static string? LastFocus(List<AccessibilityEvent> events) =>
        events.OfType<BattleFocusChanged>().Last(e => e.Text is not null).Text;

    private static string LastFeedback(List<AccessibilityEvent> events) =>
        events.OfType<BattleFeedbackPresented>().Last().Text;

    private sealed class Memory : IReadableMemory
    {
        private uint heap = 0x600000;
        public byte[] Bytes { get; } = new byte[0x900000];
        public Action<nuint>? BeforeRead { get; set; }
        public void U32(uint address, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Bytes.AsSpan((int)address), value);
        public void U16(uint address, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Bytes.AsSpan((int)address), value);
        public uint Word(uint address) => BinaryPrimitives.ReadUInt32LittleEndian(Bytes.AsSpan((int)address));
        public void String(uint address, string value)
        {
            var data = Encoding.UTF8.GetBytes(value);
            if (data.Length <= 15) { data.CopyTo(Bytes, (int)address); U32(address + 20, 15); }
            else { data.CopyTo(Bytes, (int)heap); U32(address, heap); U32(address + 20, (uint)data.Length); heap += 0x1000; }
            U32(address + 16, (uint)data.Length);
        }
        public bool TryRead(nuint address, Span<byte> destination)
        {
            BeforeRead?.Invoke(address);
            if (address == 0 || (ulong)address + (uint)destination.Length > (ulong)Bytes.Length) return false;
            Bytes.AsSpan((int)address, destination.Length).CopyTo(destination); return true;
        }
    }
}
