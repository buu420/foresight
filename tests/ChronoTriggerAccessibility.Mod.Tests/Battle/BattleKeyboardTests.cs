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
    [InlineData('H', BattleCommand.ReadHp)]
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
    public void BackgroundModifiersAndBattleEntryRequireANewPress()
    {
        var down = new HashSet<int> { 'H' };
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
}
