using ChronoTriggerAccessibility.Mod.Battle;
using ChronoTriggerAccessibility.Mod.Navigation;
using ChronoTriggerAccessibility.Core.Battle;
using ChronoTriggerAccessibility.Core.Events;
using ChronoTriggerAccessibility.Core.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Battle;

public sealed class BattleKeyboardTests
{
    [Theory]
    [InlineData('1', BattleCommand.SelectFirst)]
    [InlineData('2', BattleCommand.SelectSecond)]
    [InlineData('3', BattleCommand.SelectThird)]
    [InlineData('M', BattleCommand.ReadMp)]
    [InlineData('K', BattleCommand.Repeat)]
    public void ApprovedShortcutFiresOncePerPress(int key, BattleCommand command)
    {
        var down = new HashSet<int>();
        var keyboard = new BattleKeyboard(down.Contains, () => true);
        keyboard.Poll(); down.Add(key);
        Assert.Equal(command, Assert.Single(keyboard.Poll()));
        Assert.Empty(keyboard.Poll());
        down.Clear(); keyboard.Poll(); down.Add(key);
        Assert.Equal(command, Assert.Single(keyboard.Poll()));
    }

    [Fact]
    public void PhysicalChordBetweenNativeSnapshotsCannotSpeakWithoutConsumingH()
    {
        var down = new HashSet<int> { 0x10 };
        var keyboard = new BattleKeyboard(down.Contains, () => true);
        keyboard.Poll();
        var snapshot = new byte[256]; snapshot[0x10] = 0x80;
        keyboard.FilterGameKeyboardState(snapshot, battleActive: true);
        down.Add('H');
        Assert.Empty(keyboard.Poll()); // No native H edge has been accepted yet.
        down.Remove(0x10);
        snapshot[0x10] = 0; snapshot['H'] = 0x80;
        keyboard.FilterGameKeyboardState(snapshot, battleActive: true);
        Assert.Equal(0x80, snapshot['H']);
        Assert.Empty(keyboard.Poll());
    }

    [Fact]
    public void ShiftHReadsHpOncePerFreshHPress()
    {
        var down = new HashSet<int>();
        var keyboard = new BattleKeyboard(down.Contains, () => true);
        keyboard.Poll();
        Snapshot(keyboard);
        down.UnionWith([0x10, 'H']);
        Assert.Equal(0, Snapshot(keyboard, true, 0x10, 'H')['H']);
        Assert.Equal(BattleCommand.ReadHp, Assert.Single(keyboard.Poll()));
        Assert.Equal(0, Snapshot(keyboard, true, 0x10, 'H')['H']);
        Assert.Empty(keyboard.Poll());
        down.Remove('H'); keyboard.Poll(); down.Add('H');
        Snapshot(keyboard, true, 0x10);
        Snapshot(keyboard, true, 0x10, 'H');
        Assert.Equal(BattleCommand.ReadHp, Assert.Single(keyboard.Poll()));
    }

    [Fact]
    public void PlainHStaysWithTheGameAndAddingShiftToHeldHDoesNotInventAPress()
    {
        var down = new HashSet<int>();
        var keyboard = new BattleKeyboard(down.Contains, () => true);
        keyboard.Poll(); Snapshot(keyboard); down.Add('H');
        Assert.Equal(0x80, Snapshot(keyboard, true, 'H')['H']);
        Assert.Empty(keyboard.Poll());
        down.Add(0x10); Assert.Empty(keyboard.Poll());
        Assert.Equal(0x80, Snapshot(keyboard, true, 0x10, 'H')['H']);
        Assert.Empty(keyboard.Poll());
        down.Remove('H'); keyboard.Poll(); down.Add('H');
        Snapshot(keyboard, true, 0x10);
        Snapshot(keyboard, true, 0x10, 'H');
        Assert.Equal(BattleCommand.ReadHp, Assert.Single(keyboard.Poll()));
    }

    [Theory]
    [InlineData(0x11)]
    [InlineData(0x12)]
    [InlineData(0x5B)]
    [InlineData(0x5C)]
    public void ExtraModifiersDoNotBecomeHpCommandsWhenReleased(int modifier)
    {
        var down = new HashSet<int>();
        var keyboard = new BattleKeyboard(down.Contains, () => true);
        keyboard.Poll(); Snapshot(keyboard); down.UnionWith([0x10, 'H', modifier]);
        Assert.Equal(0x80, Snapshot(keyboard, true, 0x10, 'H', modifier)['H']);
        Assert.Empty(keyboard.Poll());
        down.Remove(modifier);
        Assert.Equal(0x80, Snapshot(keyboard, true, 0x10, 'H')['H']);
        Assert.Empty(keyboard.Poll());
        down.Remove('H'); keyboard.Poll(); down.Add('H');
        Snapshot(keyboard, true, 0x10);
        Snapshot(keyboard, true, 0x10, 'H');
        Assert.Equal(BattleCommand.ReadHp, Assert.Single(keyboard.Poll()));
    }

    [Fact]
    public void NativeSnapshotFilterConsumesOnlyHAndQueuesHpEvenIfShiftIsReleasedBeforePoll()
    {
        var down = new HashSet<int>();
        var keyboard = new BattleKeyboard(down.Contains, () => true);
        keyboard.Poll(); Snapshot(keyboard); down.UnionWith([0x10, 'H']);
        var snapshot = new byte[256];
        snapshot['H'] = 0x81; snapshot[0x10] = 0x80; snapshot['X'] = 0x80; snapshot[0x14] = 1;
        var expected = snapshot.ToArray(); expected['H'] = 1;

        keyboard.FilterGameKeyboardState(snapshot, battleActive: true);

        Assert.Equal(expected, snapshot);
        down.Remove(0x10);
        Assert.Equal(BattleCommand.ReadHp, Assert.Single(keyboard.Poll()));
        Assert.Equal(0, Snapshot(keyboard, true, 'H')['H']);
        Assert.Empty(keyboard.Poll());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void OtherContextsRetainTheirHInput(bool battleActive, bool foreground)
    {
        var keyboard = new BattleKeyboard(_ => false, () => foreground);
        keyboard.Poll(); Snapshot(keyboard, battleActive);
        var snapshot = new byte[256]; snapshot['H'] = 0x80; snapshot[0x10] = 0x80;
        var expected = snapshot.ToArray();
        keyboard.FilterGameKeyboardState(snapshot, battleActive);
        Assert.Equal(expected, snapshot);
        Assert.Empty(keyboard.Poll());
    }

    [Theory]
    [InlineData(0x11)]
    [InlineData(0x12)]
    [InlineData(0x5B)]
    [InlineData(0x5C)]
    public void NativeFilterLeavesUnownedModifierChordsAlone(int modifier)
    {
        var keyboard = new BattleKeyboard(_ => false, () => true);
        keyboard.Poll(); Snapshot(keyboard);
        var snapshot = new byte[256];
        snapshot['H'] = 0x80; snapshot[0x10] = 0x80; snapshot[modifier] = 0x80;
        var expected = snapshot.ToArray();
        keyboard.FilterGameKeyboardState(snapshot, battleActive: true);
        Assert.Equal(expected, snapshot);
        Assert.Empty(keyboard.Poll());
    }

    [Fact]
    public void ReleasingShiftOrLeavingBattleCannotLeakCapturedHBeforeItsRelease()
    {
        var keyboard = new BattleKeyboard(_ => false, () => true);
        keyboard.Poll(); Snapshot(keyboard);
        var snapshot = new byte[256]; snapshot['H'] = 0x80; snapshot[0x10] = 0x80;
        keyboard.FilterGameKeyboardState(snapshot, battleActive: true);
        Assert.Equal(0, snapshot['H']);

        snapshot['H'] = 0x80; snapshot[0x10] = 0;
        keyboard.FilterGameKeyboardState(snapshot, battleActive: false);
        Assert.Equal(0, snapshot['H']);
        keyboard.FilterGameKeyboardState(new byte[256], battleActive: false);
        snapshot['H'] = 0x80;
        keyboard.FilterGameKeyboardState(snapshot, battleActive: false);
        Assert.Equal(0x80, snapshot['H']);
    }

    [Fact]
    public void PendingHpDoesNotCarryAcrossBattleOrFocusAndHeldHDoesNotRearm()
    {
        var foreground = true;
        var keyboard = new BattleKeyboard(_ => false, () => foreground);
        keyboard.Poll(); Snapshot(keyboard);
        Snapshot(keyboard, true, 0x10, 'H');
        keyboard.Suspend();
        Assert.Empty(keyboard.Poll());
        Assert.Equal(0, Snapshot(keyboard, true, 0x10, 'H')['H']);
        Assert.Empty(keyboard.Poll());
        Snapshot(keyboard); Snapshot(keyboard, true, 0x10, 'H');
        foreground = false;
        Assert.Empty(keyboard.Poll());
        foreground = true;
        Assert.Empty(keyboard.Poll());
        Assert.Equal(0, Snapshot(keyboard, true, 'H')['H']);
    }

    [Fact]
    public void InitialSnapshotAndBattleEntryDoNotAcceptAnAlreadyHeldChord()
    {
        var keyboard = new BattleKeyboard(_ => false, () => true);
        keyboard.Poll();
        Assert.Equal(0x80, Snapshot(keyboard, true, 0x10, 'H')['H']);
        Assert.Empty(keyboard.Poll());
        Snapshot(keyboard, false);
        Snapshot(keyboard, false, 0x10, 'H');
        keyboard.Suspend(); keyboard.Poll();
        Assert.Equal(0x80, Snapshot(keyboard, true, 0x10, 'H')['H']);
        Assert.Empty(keyboard.Poll());
        Snapshot(keyboard);
        Assert.Equal(0, Snapshot(keyboard, true, 0x10, 'H')['H']);
        Assert.Equal(BattleCommand.ReadHp, Assert.Single(keyboard.Poll()));
    }

    [Fact]
    public void BackgroundModifiersAndBattleEntryRequireANewPress()
    {
        var down = new HashSet<int> { 'H', 0x10 };
        var foreground = true;
        var keyboard = new BattleKeyboard(down.Contains, () => foreground);
        Assert.Empty(keyboard.Poll());
        down.Clear(); keyboard.Poll(); foreground = false; down.Add('M');
        Assert.Empty(keyboard.Poll()); foreground = true; Assert.Empty(keyboard.Poll());
        down.Clear(); keyboard.Poll(); down.UnionWith(['1', 0x11]);
        Assert.Empty(keyboard.Poll()); down.Remove(0x11); Assert.Empty(keyboard.Poll());
        down.Clear(); keyboard.Poll(); down.Add('2'); keyboard.Suspend();
        Assert.Empty(keyboard.Poll()); down.Clear(); keyboard.Poll(); down.Add('2');
        Assert.Equal(BattleCommand.SelectSecond, Assert.Single(keyboard.Poll()));
    }

    [Fact]
    public void HeldRepeatCannotCarryAcrossBattleEntryOrReturnToNavigation()
    {
        var down = new HashSet<int>(); var events = new List<AccessibilityEvent>();
        var navigation = new NavigationKeyboard(down.Contains, () => true);
        var runtime = new BattleRuntime(events.Add, new BattleKeyboard(down.Contains, () => true),
            () => true, _ => navigation.Suspend());
        var frame = new BattleFrame([], "command:0:0", "Crono: Attack", new Dictionary<int,string>());
        navigation.Poll(); down.Add('K');
        Assert.Equal(NavigationCommand.Repeat, Assert.Single(navigation.Poll()));
        runtime.Observe(100, frame); runtime.Observe(100, frame);
        Assert.Empty(events.OfType<BattleInspectionRequested>());
        down.Clear(); runtime.Observe(100, frame); down.Add('K'); runtime.Observe(100, frame);
        Assert.Equal("Crono: Attack", Assert.Single(events.OfType<BattleInspectionRequested>()).Text);
        runtime.End(100);
        Assert.Empty(navigation.Poll()); Assert.Empty(navigation.Poll());
        down.Clear(); navigation.Poll(); down.Add('K');
        Assert.Equal(NavigationCommand.Repeat, Assert.Single(navigation.Poll()));
    }

    private static byte[] Snapshot(BattleKeyboard keyboard, bool battleActive = true, params int[] keys)
    {
        var state = new byte[256];
        foreach (var key in keys) state[key] = 0x80;
        keyboard.FilterGameKeyboardState(state, battleActive);
        return state;
    }
}
