# Prison skywalk navigation, 0.3.34

The reported scene 28 route failed even though Crono could walk across the bridge.
A read-only capture on 2026-09-26 at 19:07:13 recorded the player at (9094,2559),
story point 46, G198=11 and G199=20. The live bridge permits horizontal movement
with the foot at Y2544..2559; every old four-pixel exit goal missed that strip.
The story target also skipped the bridge's scripted encounter sequence.

## Changes

- Field routing retains one-pixel boundary positions when a four-pixel edge is
  blocked, and joins precise goal coordinates. Existing native terrain, leading
  corner, layer, actor collision, conveyor and exit checks still apply.
- Exit goals include tile offsets 112 and 240, needed by half-tile passages and
  the native seven-pixel leading corners. Goal sets remain capped at 64, graph
  neighbors at eight, and searches at 131,072 distinct positions. The former
  65,536 ceiling rejected two reachable later-game routes after adding precision.
- The upper skywalk first routes to actor 1's live contact. After G198 bit 04 is
  set it routes to that actor's new position, which starts the tank encounter.
  G199 bit 04 advances to the castle exit once the game returns player control.
  The three lower bridges each retain their own onward exit.
- The tank contact's precise goal is specific to scene 28 actor 1. Other audited
  terrain contacts, including the queen's door, retain their approach rules.
- Save points use the game's own save regions and live checker/sparkle actors.
  Claude owned that implementation; see the separate native audit for all 57
  checkers in 54 scenes, disabled-state handling and interaction instructions.

## Evidence and verification

The installed Atel0409 script places actor 1 at (24,9), then (13,9). Its shared
confirm/touch handler begins at file offset 0377. The first event sets G198:04
at 03A8. The second calls actor 5 function 4 at 0591, sets G199:04 at 05AA and
starts encounter C087 at 05D4. The aftermath copies bridge graphics at 0560 and
sets G198:08 through actor 1 function 3 at 03B4. The four bridge rows are 9, 25,
41 and 57, with onward exit IDs 0, 2, 4 and 6.

Ghidra's original movement dispatch (RVA 175E90), leading-corner probes (175EE0)
and contact routine (178980) were checked against the supported executable.
Offline Unicorn replay of that executable, using the captured collision planes,
confirms horizontal movement on Y2544 and Y2559, rejects north into the wall,
and fires actor 1's native contact before the new goal is reached. No game input,
process writes or save changes were needed for those checks. The guide's account
of the guardroom, tank and stairs agrees with the native sequence:
[Thonky, The Trial](https://www.thonky.com/chrono-trigger/the-trial).

Regression fixtures contain the actual skywalk and guardroom captures. Tests cover
both bridge stages, off-grid movement, all four onward exits, missing flags,
automatic walking to the native contact, and the captured save point's route.
Independent review caught an over-broad terrain-contact exemption; narrowing it
preserves the queen's door regression. Original native replay also confirmed that
the cathedral corner allows ten units of approach before its actor blocks further
movement; the existing test now checks that precise boundary.

The expanded static map audit examined 507 scenes and completed 1,480 searches
to reachable exits in 455 scenes with zero failures. The longest measured search
was 178 ms. This includes the formerly over-budget Mountain of Woe Station 3 and
Frozen Cliffs routes. Static maps, captured-state tests and native replay do not
verify every dynamic game state or constitute a complete live playthrough.

Release verification passed 1,118 Mod tests (excluding three unrelated audio
playback tests), 855 Native tests, 172 Core tests and eight Prism tests: 2,153 total.
All 171 hook signatures match the installed supported executable.

Research outputs are under `artifacts/research/skywalk-savepoints-0334/`.
