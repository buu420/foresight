namespace ChronoTriggerAccessibility.Native.Capture;

/// <summary>
/// One party slot exactly as the native HUD formatter at RVA 0x1BDD0 presents it.
/// <para><see cref="Status"/> is a placeholder in this HUD capture. BattleSession
/// decorates it using BattlePresentationCapture's separately audited applied visual
/// status, retaining the distinction between no status and an unreadable frame.</para>
/// </summary>
public sealed record BattlePartyMemberSnapshot(
    int Slot,
    string Name,
    int Hp,
    int MaximumHp,
    int Mp,
    int MaximumMp,
    string Status);

/// <summary>
/// A single coherent read of the visible battle interface.
/// <para><see cref="FocusIdentity"/> is a stable machine key for the current selection so a
/// consumer can suppress unchanged frames; <see cref="FocusText"/> is the text the game itself
/// loaded for it, or null when the native label source is not proven. Both are null during
/// animation phases that present no selection, which is a valid state and not a failure.</para>
/// </summary>
public sealed record BattleSnapshot(
    IReadOnlyList<BattlePartyMemberSnapshot> Party,
    string? FocusIdentity,
    string? FocusText,
    IReadOnlyDictionary<int, string> BattlerNames);
