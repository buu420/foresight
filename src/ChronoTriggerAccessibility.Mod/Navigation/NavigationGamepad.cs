namespace ChronoTriggerAccessibility.Mod.Navigation;

// Physical positions normalized by the native input adapter. Values follow XINPUT_GAMEPAD.
[Flags]
public enum NavigationPadButtons : ushort
{
    None = 0, Up = 0x0001, Down = 0x0002, RightStick = 0x0080,
    LeftShoulder = 0x0100, RightShoulder = 0x0200,
    South = 0x1000, East = 0x2000, West = 0x4000,
}

public enum NavigationPadAction
{
    Open, Close, PreviousCategory, NextCategory, PreviousTarget, NextTarget, Guide, Walk,
}

/// <summary>Owns the spoken navigation menu and the physical presses it consumes.</summary>
public sealed class NavigationGamepad
{
    private readonly object gate = new();
    private readonly List<NavigationPadAction> pending = [];
    private sealed class PadState
    {
        public NavigationPadButtons Previous;
        public bool Armed, SuppressUntilNeutral;
    }
    private readonly Dictionary<uint, PadState> pads = [];
    private bool open;
    private uint? owner;

    public bool IsOpen { get { lock (gate) return open; } }
    public bool HasOwner { get { lock (gate) return owner.HasValue; } }
    public bool OwnsDevice(uint deviceId) { lock (gate) return owner == deviceId; }

    /// <summary>Called on the game's physical snapshot before mapping it to actions.
    /// True consumes the entire controller snapshot, including sticks and triggers.
    /// Neutral means every button is released and the game's movement axes are in
    /// their native dead zone. It is independent of our command subset.</summary>
    public bool Filter(NavigationPadButtons buttons, bool neutral, bool available, bool foreground = true,
        bool connected = true, uint deviceId = 0)
    {
        lock (gate)
        {
            // WINMM identifiers are 0..15. Separate edges and release fences prevent
            // the other devices polled in the same frame from releasing this one.
            if (deviceId > 15) return false;
            if (!pads.TryGetValue(deviceId, out var pad)) pads.Add(deviceId, pad = new());
            var pressed = buttons & ~pad.Previous;
            pad.Previous = buttons;
            var wasArmed = pad.Armed;
            if (!connected)
            {
                if (owner == deviceId) Suspend();
                pad.Armed = false;
                return false;
            }
            // A failed poll says nothing about physical release. Keep a consumed
            // closing press fenced until a successful neutral sample, even on reconnect.
            if (neutral) pad.SuppressUntilNeutral = false;
            if (!available || !foreground)
            {
                Suspend();
                return pad.SuppressUntilNeutral;
            }
            pad.Armed = true;
            if (open && owner != deviceId)
            {
                pad.SuppressUntilNeutral = !neutral;
                return true;
            }
            if (!wasArmed || pad.SuppressUntilNeutral && !open) return pad.SuppressUntilNeutral;
            if ((pressed & NavigationPadButtons.RightStick) != 0)
            {
                owner = deviceId;
                open = !open;
                if (!open) FenceAllPads();
                pad.SuppressUntilNeutral = true;
                Queue(open ? NavigationPadAction.Open : NavigationPadAction.Close);
                return true;
            }
            if (!open) return false;
            pad.SuppressUntilNeutral = true;
            if ((pressed & NavigationPadButtons.East) != 0)
            {
                open = false;
                FenceAllPads();
                Queue(NavigationPadAction.Close);
                return true;
            }
            var actions = new List<NavigationPadAction>();
            if ((pressed & NavigationPadButtons.LeftShoulder) != 0) actions.Add(NavigationPadAction.PreviousCategory);
            if ((pressed & NavigationPadButtons.RightShoulder) != 0) actions.Add(NavigationPadAction.NextCategory);
            if ((pressed & NavigationPadButtons.Up) != 0) actions.Add(NavigationPadAction.PreviousTarget);
            if ((pressed & NavigationPadButtons.Down) != 0) actions.Add(NavigationPadAction.NextTarget);
            if ((pressed & NavigationPadButtons.South) != 0) actions.Add(NavigationPadAction.Guide);
            if ((pressed & NavigationPadButtons.West) != 0) actions.Add(NavigationPadAction.Walk);
            if (actions.Count == 1)
            {
                var action = actions[0];
                if (action is NavigationPadAction.Guide or NavigationPadAction.Walk)
                {
                    open = false;
                    FenceAllPads();
                }
                Queue(action);
            }
            return true;
        }
    }

    private void Queue(NavigationPadAction action)
    {
        // A stalled consumer cannot accumulate an unbounded sequence of routes.
        if (pending.Count >= 32) Suspend();
        else pending.Add(action);
    }

    private void FenceAllPads()
    {
        // Steam can expose a physical pad and its translated device in the same
        // poll. The second copy of a closing press must not become native Confirm.
        foreach (var pad in pads.Values) pad.SuppressUntilNeutral = true;
    }

    public IReadOnlyList<NavigationPadAction> Poll()
    {
        lock (gate)
        {
            var result = pending.ToArray();
            pending.Clear();
            return result;
        }
    }

    public void Suspend()
    {
        lock (gate)
        {
            if (open && owner is { } id && pads.TryGetValue(id, out var pad)) pad.SuppressUntilNeutral = true;
            open = false;
            owner = null;
            foreach (var state in pads.Values) state.Armed = false;
            pending.Clear();
        }
    }
}
