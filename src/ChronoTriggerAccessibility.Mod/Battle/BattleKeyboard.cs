using ChronoTriggerAccessibility.Mod.Navigation;

namespace ChronoTriggerAccessibility.Mod.Battle;

public enum BattleCommand { SelectFirst, SelectSecond, SelectThird, ReadHp, ReadMp, Repeat }

public sealed class BattleKeyboard(Func<int, bool> isDown, Func<bool> isForeground)
{
    private static readonly (int Key, BattleCommand Command)[] Bindings =
    [
        ('1', BattleCommand.SelectFirst), ('2', BattleCommand.SelectSecond), ('3', BattleCommand.SelectThird),
        ('H', BattleCommand.ReadHp), ('M', BattleCommand.ReadMp), ('K', BattleCommand.Repeat),
    ];
    private int previous;
    private bool armed;
    public BattleKeyboard() : this(NavigationKeyboard.IsKeyDown, NavigationKeyboard.IsGameForeground) { }
    public IReadOnlyList<BattleCommand> Poll()
    {
        var current = 0;
        for (var i = 0; i < Bindings.Length; i++) if (isDown(Bindings[i].Key)) current |= 1 << i;
        var pressed = current & ~previous;
        previous = current;
        var wasArmed = armed;
        armed = true;
        if (!wasArmed || !isForeground() || isDown(0x10) || isDown(0x11) || isDown(0x12) ||
            isDown(0x5B) || isDown(0x5C)) return [];
        var commands = new List<BattleCommand>();
        for (var i = 0; i < Bindings.Length; i++) if ((pressed & (1 << i)) != 0) commands.Add(Bindings[i].Command);
        return commands.AsReadOnly();
    }
    public void Suspend() => armed = false;
}
