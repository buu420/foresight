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
        Assert.Equal(["Marle.", "Marle. HP 27 of 80.", "Marle. MP 7 of 12."],
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
        m.U32(0x300100 + 0x32 * 4, 0x303000); m.U32(0x303000, 0x304000); m.U32(0x303004, 0x304030);
        m.String(0x304018, "Blue Imp");
        m.U32(Canvas + 0x1A018 + 3 * 4, 1); m.U32(Canvas + 0x1A018 + 5 * 4, 1);
        m.U32(Canvas + 0x19EEC, 1); m.Bytes[Menu + 0x538] = 1;
        for (uint i = 0; i < 11; i++) m.U32(Canvas + 0x1ACD4 + i * 4, 255);
        m.U32(Canvas + 0x1ACD4, 5);
        var runtime = new BattleRuntime(events.Add, new BattleKeyboard(_ => false, () => true), () => true, _ => { });
        var session = new BattleSession(m, runtime, _ => { }); session.BindImageBase(Image);
        session.Tick(Menu);
        Assert.Equal("Target. Blue Imp C.", Assert.Single(events.OfType<BattleFocusChanged>(), e => e.Text is not null).Text);
        m.U32(Canvas + 0x1A018 + 3 * 4, uint.MaxValue); session.Tick(Menu);
        Assert.Single(events.OfType<BattleFocusChanged>(), e => e.Text is not null);
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
        Assert.Equal("Targets. Crono, Enemy B, Marle.",
            Assert.Single(events.OfType<BattleFocusChanged>(), e => e.Text is not null).Text);
        session.Miss(Menu, 4); m.String(Menu + 0x34 + 4 * 24, "MISS!"); m.Bytes[Menu + 0x1B5 + 4] = 1;
        session.Render(Menu);
        Assert.Equal("Enemy B. MISS!", Assert.Single(events.OfType<BattleFeedbackPresented>()).Text);
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

    private sealed class Memory : IReadableMemory
    {
        public byte[] Bytes { get; } = new byte[0x900000];
        public void U32(uint address, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Bytes.AsSpan((int)address), value);
        public void U16(uint address, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Bytes.AsSpan((int)address), value);
        public void String(uint address, string value)
        {
            var data = Encoding.UTF8.GetBytes(value); Assert.True(data.Length <= 15);
            data.CopyTo(Bytes, (int)address); U32(address + 16, (uint)data.Length); U32(address + 20, 15);
        }
        public bool TryRead(nuint address, Span<byte> destination)
        {
            if (address == 0 || (ulong)address + (uint)destination.Length > (ulong)Bytes.Length) return false;
            Bytes.AsSpan((int)address, destination.Length).CopyTo(destination); return true;
        }
    }
}
