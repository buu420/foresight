# Encounter and content coverage, 0.3.35

## Confirmed omission

The navigation compiler previously extracted battle regions from coordinate checks
in startup scripts. It did not publish battles reached through ordinary actor
Touch/Confirm handlers, or through a local signal sent by a creature to a separate
controller. Both versions of Guardia Forest consequently lacked those encounter
destinations. A shortest story route could pass through the forest without giving
the player a way to find its optional fights.

The guide describes avoidable forest battles, a sparkling trap, a real item pickup,
and bushes with different results. These are separate interactions, not reasons
to force every optional battle onto the story route. Reference:
[The Queen Returns](https://www.thonky.com/chrono-trigger/the-queen-returns).

## Compiler and runtime changes

- The compiler emits an Encounter action when an actor's native handler reaches
  D8. It stops at that battle boundary, so rewards and other post-battle effects
  are not offered as though they happen before the fight.
- Immediate local signals can connect a contact handler to a controller's battle.
  The consumer must require the exact written value. Negative bypass/history flags
  are insufficient. Other progress, party and position conditions remain intact.
- Scheduled calls invalidate locals they can change. Paired native word storage
  belongs to one operand; it does not overwrite the next local index.
- Enemies includes currently available direct and signalled encounters. Native
  identity, call gates, controller state, event conditions and terrain still apply.
  No battle is activated merely because an actor looks like an enemy.
  Moving a creature out of People preserves its existing Story Events binding.
- A trap drawn as an ordinary sparkle stays a Sparkle under Interactable Objects,
  with its native interaction route. It does not reveal its hidden outcome through
  a duplicate Enemies row. Service keepers retain their People destinations.
- Contact routing includes usable positions around an occupied or blocked tile.
  Automatic walking makes a bounded final movement toward the native contact
  center instead of reporting arrival outside the activation radius. Manual
  guidance speaks that final direction. Confirm-only encounters ask the player
  to press Confirm; navigation does not choose a dialogue option.
- If no contact position is available but the actor also has an available Confirm
  handler, its valid Confirm approaches remain usable.
- Spekkio's optional challenge has an audited override for its bounded symbolic
  expansion. It retains the native progress, introduction and active-party magic
  conditions.
- Flea's visible first form sends a signal to a hidden controller. Its audited
  override retains the keep-progress, unfinished fight, unconsumed signal and
  non-ending conditions. The ending branch warps away before the battle wait.

## Evidence and limits

The generated catalog contains 793 actors with encounter actions across 196
scenes; 147 of those actors use a controller signal. This is extracted coverage,
not a count of distinct battles or proof of a complete playthrough.

An independent reachability audit found 780 native D8 sites across 297 scenes and
855 physical Touch/Confirm handler roots that reach a direct battle. Every such
root has a catalogued encounter action. Another 47 sites belong to other functions
or occur after an earlier battle. The audit reports them separately; it does not
claim that all their possible runtime states have been exercised.

The compiler retains three bounded-expansion warnings: scene465 actor10 Confirm,
scene587 actor0 startup, and scene598 actor0 startup. Audited overrides cover their
supported navigation contracts; a warning is not silently converted to success.

Guardia Forest tests use collision planes extracted from the installed scenes19
and119. They check present-day controller signals, past-era contact battles,
cleared local flags, disabled calls, stopped controllers, the trap and real item,
and the Confirm-only ambush. Arrival-box edge tests pass their final movement
through the native contact-rule implementation.

Further integration fixtures preserve Story Events for Slash, Ozzie and the
Mountain of Woe summit after their encounter rows move to Enemies. Denadoro
scene146 actor12 remains absent at its hidden staging position and becomes
routable at its shown arrival position; the native show/movement callback was
checked rather than treating a hidden slot as an available destination.

Black Tyranno's story objective previously named scene301 without an actor or
region. That room has no startup Progress/Encounter region, so the local objective
could only become a note. It now binds to actors15..20 across row10, whose identical
Confirm/Touch entries start the encounter. The installed collision map has a route
from the southern entrance neighborhood to their native Confirm approaches.

Flea's story destination previously pointed at hidden controller12 instead of
visible actor11, whose Confirm handler actually starts the first encounter.
Death Peak's summit objective now binds to actor0's guarded revival region;
arriving in scene265 alone does not trigger it. The earlier Ocean Palace visit
now binds to actor11's approach region in scene415 while local08 is2. The later
automatic collapse phase (local08 is3) does not offer that approach. Installed
collision-map fixtures verify these story routes and their phase conditions.

Native references are in the local Ghidra corpus under
`artifacts/research/engine-0332/native/functions`: RVA1619E0 (script dispatch),
169280/169300/1693A0 (local signals), 16C040 (8B coordinates), 16EF30 (Touch),
178980 (contact geometry), and 17D230 (Confirm). Research and machine-readable
audit results are under `artifacts/research/content-coverage-0335`.

These checks establish extraction and specific native route behavior. They do
not establish a complete live playthrough or certify every item, NPC, minigame,
menu and conditional encounter in the game. Full-game decompilation supplies a
reference for this work; it is not itself a completed accessibility implementation.

The accompanying [field content audit](field-content-coverage-0335.md) checks
scripted items, people, counter interactions and the treasure reader across the
installed resources, and records the remaining route and labeling limits.
