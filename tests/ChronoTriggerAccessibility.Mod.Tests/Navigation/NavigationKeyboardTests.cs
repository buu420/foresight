using ChronoTriggerAccessibility.Core.Navigation;
using ChronoTriggerAccessibility.Mod.Navigation;
using Xunit;

namespace ChronoTriggerAccessibility.Mod.Tests.Navigation;

public sealed class NavigationKeyboardTests
{
    [Theory]
    [InlineData('U', NavigationCommand.PreviousCategory)]
    [InlineData('O', NavigationCommand.NextCategory)]
    [InlineData('J', NavigationCommand.PreviousTarget)]
    [InlineData('L', NavigationCommand.NextTarget)]
    [InlineData('K', NavigationCommand.Repeat)]
    [InlineData('I', NavigationCommand.Guide)]
    [InlineData('P', NavigationCommand.ToggleWalk)]
    public void UserKeysFireOncePerPress(char key, NavigationCommand expected)
    {
        var down = new HashSet<int>();
        var keyboard = new NavigationKeyboard(down.Contains, () => true);
        keyboard.Poll();
        down.Add(key);
        Assert.Equal(expected, Assert.Single(keyboard.Poll()));
        Assert.Empty(keyboard.Poll());
        down.Clear(); keyboard.Poll(); down.Add(key);
        Assert.Equal(expected, Assert.Single(keyboard.Poll()));
    }

    [Fact]
    public void BackgroundAndModifiedKeysDoNotTriggerOnReturnOrModifierRelease()
    {
        var foreground = false;
        var down = new HashSet<int>();
        var keyboard = new NavigationKeyboard(down.Contains, () => foreground);
        keyboard.Poll(); down.Add('P'); Assert.Empty(keyboard.Poll());
        foreground = true; Assert.Empty(keyboard.Poll());
        down.Clear(); keyboard.Poll(); down.Add('K'); down.Add(0x11);
        Assert.Empty(keyboard.Poll());
        down.Remove(0x11); Assert.Empty(keyboard.Poll());
        down.Clear(); keyboard.Poll(); down.Add('K');
        Assert.Equal(NavigationCommand.Repeat, Assert.Single(keyboard.Poll()));
        keyboard.Suspend();
        Assert.Empty(keyboard.Poll());
    }
}
