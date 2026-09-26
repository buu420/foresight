using ChronoTriggerAccessibility.Mod.Navigation;

namespace ChronoTriggerAccessibility.Mod.Battle;

public enum BattleCommand { SelectFirst, SelectSecond, SelectThird, ReadHp, ReadMp, Repeat }

public sealed class BattleKeyboard(Func<int, bool> isDown, Func<bool> isForeground)
{
    private static readonly (int Key, BattleCommand Command)[] Bindings =
    [
        ('1', BattleCommand.SelectFirst), ('2', BattleCommand.SelectSecond), ('3', BattleCommand.SelectThird),
        ('M', BattleCommand.ReadMp), ('K', BattleCommand.Repeat),
    ];
    private readonly object gate = new();
    private int previous;
    private bool armed;
    private bool nativeSnapshotSeen, nativeHWasDown, suppressHUntilRelease, pendingHp;
    public BattleKeyboard() : this(NavigationKeyboard.IsKeyDown, NavigationKeyboard.IsGameForeground) { }
    public IReadOnlyList<BattleCommand> Poll()
    {
        lock (gate)
        {
            var current = 0;
            for (var i = 0; i < Bindings.Length; i++) if (isDown(Bindings[i].Key)) current |= 1 << i;
            var pressed = current & ~previous;
            previous = current;
            var wasArmed = armed;
            armed = true;
            var readHp = pendingHp;
            pendingHp = false;
            if (!wasArmed || !isForeground()) return [];
            var commands = new List<BattleCommand>();
            // HP was already accepted and consumed from the game's own snapshot. Checking
            // asynchronous Shift/H here could disagree with it or lose a quick release.
            if (readHp) commands.Add(BattleCommand.ReadHp);
            if (!isDown(0x10) && !isDown(0x11) && !isDown(0x12) && !isDown(0x5B) && !isDown(0x5C))
                for (var i = 0; i < Bindings.Length; i++)
                    if ((pressed & (1 << i)) != 0) commands.Add(Bindings[i].Command);
            return commands.AsReadOnly();
        }
    }
    public void Suspend() { lock (gate) { armed = false; pendingHp = false; } }

    /// <summary>Filters only the native GameController's temporary GetKeyboardState buffer.
    /// Accepts HP and consumes H from the same snapshot. A captured H stays suppressed until
    /// native release, even if Shift is released first or battle ends during the press.</summary>
    public void FilterGameKeyboardState(Span<byte> state, bool battleActive)
    {
        if (state.Length != 256) throw new ArgumentException("Expected the native 256-key snapshot.", nameof(state));
        lock (gate)
        {
            var hDown = (state['H'] & 0x80) != 0;
            var freshH = nativeSnapshotSeen && hDown && !nativeHWasDown;
            nativeSnapshotSeen = true;
            nativeHWasDown = hDown;
            if (!hDown) { suppressHUntilRelease = false; return; }
            if (freshH && armed && battleActive && isForeground() &&
                (state[0x10] & 0x80) != 0 && (state[0x11] & 0x80) == 0 &&
                (state[0x12] & 0x80) == 0 && (state[0x5B] & 0x80) == 0 && (state[0x5C] & 0x80) == 0)
            {
                suppressHUntilRelease = true;
                pendingHp = true;
            }
            if (suppressHUntilRelease) state['H'] &= 0x7F;
        }
    }

    public void ResetGameInputSuppression()
    {
        lock (gate) { nativeSnapshotSeen = nativeHWasDown = suppressHUntilRelease = pendingHp = false; }
    }
}
