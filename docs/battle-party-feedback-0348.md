# Battle, party and controller feedback in 0.3.48

The user approved all six items in the October 9 tester report and continued the
earlier authorization to publish test builds and deploy locally. The new log is
`2026-10-09 12.34.02 ~ Chrono Trigger.txt` in Downloads.

## Evidence and resulting behavior

The log queues separate top-menu stat fragments immediately before MenuExited
at 07:35:22 and 07:36:32. MenuNarrator previously returned no speech action on
exit, so Prism could finish that queue after the screen closed. An owning exit
now produces a cancellation command through the pinned Prism C ABI. A stale
parent close cannot stop a newer child menu. Newer field, battle and error
speech also retains its ownership. Owned confirmations participate in the same
rule. No empty speech message is used.

[The pinned Prism header](https://github.com/ethindp/prism/blob/9911156998b52fee91fb2cb4f71ac793d4e546c7/include/prism.h)
declares prism_backend_stop. Its
[NVDA implementation](https://github.com/ethindp/prism/blob/9911156998b52fee91fb2cb4f71ac793d4e546c7/source/backends/nvda.cpp)
cancels controller speech. The existing hash-pinned x86 DLL exports that function;
the dependency was not rebuilt or replaced.

BattleSession previously used only names for native highlighted targets. It now
adds the party HUD's displayed current/maximum HP and MP for highlighted allies.
Enemies retain their existing names and slot letters. Changing HP during an
unchanged selection updates explicit Repeat without repeatedly interrupting
target speech. Keyboard party-slot inspection uses the same description.
Visible item-target and Tech character cards retain their native HP/MP fragments.
HP zero becomes "Knocked out" in those cards, the top-menu overview and battle
inspections. A transition from positive party HP to zero says "was knocked out."
MP zero, unavailable HP and a saved name resembling a stat are not knockouts.

The reference was the user's local Blind Soldier source, specifically
BattleTargetSpeechTracker.Format and BattleStatusSpeechTracker.Format. The
announcement wording is reused where Chrono Trigger exposes equivalent visible
information. Chrono Trigger's native popup/status readers and battle mechanics
remain authoritative.

At 07:37:06–07:37:23 the log repeats the identical Usable Combos panel on every
party focus change. It omits the reserve name at 07:37:15. Fresh read-only Ghidra
analysis of the hash-checked executable and Claude's bounded native investigation
show:

- 23B210/23B070 create a name Label for current members. 23B3D0/239580 create
  reserve stat Labels without a name Label. The visible identity is CharaAnime.
- 23DA10 sets CharaAnime vtable 3AC0B8 and stores its character ID at +280;
  23DB80 does not replace that identity. 1BEAC0 owns the sprite under its card.
- 14830 copies the saved name from `[image+41B4C4]+1908+id*24`. The reader requires
  exactly one visible owned sprite matching the reserve record, a valid nine-slot
  identity and a readable nonempty name. Coherent frame recapture includes the
  sprite, global pointer and saved name. There is no default-name fallback.
- 1BF480 shows the current party's combo list when nothing is held, or a swap
  preview when the held and highlighted members differ. The complete panel is
  spoken on entry and when its content changes; ordinary focus changes read the
  selected member without repeating unchanged combos.

175C90 reads the remapped native Dash action, bit 8, to apply the native run mode.
The field/world navigation cancellation check now excludes that bit while
preserving it in the delivered pad. Native directions, Confirm, menu actions,
scene/focus/controller loss and stale capture still stop routes. The rat chase's
existing native Confirm exception remains. Epoch and Dactyl actions are not
exempted. World input probes preserve the same Dash exception between ticks.
The game owns remapping; the mod does not assume a physical controller button.
[The official controller update](https://store.steampowered.com/oldnews/?appgroupname=Chrono+Trigger&appids=613830&enddate=1790838000&feed=steam_community_announcements)
documents rebindable controls, and the supported executable proves the action bit.

## Tooling and verification limits

The installed AMM Author CLI is 0.30.0 and its executable SHA256 matches the
downloaded 0.30.0 ZIP. That update is already available to this chat. Its bundled
publishing/scripts documentation confirms the --project and JSON workflow and
the staging-only lifecycle fields used by Package-Amm. Release verification uses
the actual updated CLI, final package bytes and a fresh catalog baseline.

Research, failing regression runs and fresh Ghidra output are under
`artifacts/research/battle-party-feedback-0348`. Automated fixtures verify speech
text, native ownership/coherence and input masks. They do not establish live
Prism listening, native hook timing, actual controller delivery or gameplay.
Test those using the release and include a new log for failures.
