# Castle return and actor collision, 0.3.30

The September 23 report exposed two real gaps in navigation verification. The
story catalog covered this chapter, but its return-to-Marle binding was wrong.
The Cathedral route also ignored a character blocking a small steering correction.
Catalog coverage is not a completed live playthrough.

## Return to Marle

The user reached scene 122 at story point 28, position `(14208,11903,1)`. Story Events
selected `landmark:24`, which the native script had disabled. It therefore reported
that it could not locate the destination. The castle hall and three stair landings
had already worked in that session.

The corrected sequence follows the [guide's return to the queen's chamber](https://www.thonky.com/chrono-trigger/queen-is-gone)
and the installed PC game's Atel0242:

1. Actor 8, at `(10880,14591)`, opens the doorway by copying map properties from
   `(3,32)..(5,34)` to `(41,54)` at script offset 04F5. Its contact then sets global
   A1 bit04 and enables actor24's startup loop. This flag means the event is armed,
   not complete.
2. With A1:04 set, the objective leads toward contact25/26, where Marle disappeared.
   Actor27's coordinate watcher usually triggers first, when the player enters
   columns 0..46, rows 0..47. It sets local0C; contact25/26 can set the same local.
3. Actor24's loop responds to local0C with the return scene, then clears A1:04 at
   0A94 and sets story point 33 at 0A97. The existing exit objective then takes over.

The objective uses the native point 28..32 window. It handles both an already-open
doorway and re-entry with the event armed but the doorway closed. The latter stages
through actor8's automatic opening contact using the current collision map.
Unknown progress produces an explicit note instead of inventing a phase.

The full script packet establishes both phases, including the startup continuation
after the first return. A1 was already captured in the 512-word global snapshot;
this update adds it to the compact diagnostic log.

## The Cathedral stall

The log shows the route starting at `(8959,5626,1)` and stopping at
`(9471,2074,1)`. It announced a left turn while continuing to press up to correct
a 26-unit offset. Actor20, at `(9344,1791)`, blocked that correction. Script calls
were disabled for this actor, but its native body was still solid.

Field graphs now include the actor-contact checks from native RVA178980:

- descending slot precedence, including unloaded contact markers;
- the camera cache, removed bit, all three party exclusions and loaded bit;
- the native 160/224-unit extents, special scene radii and fair flag;
- actor+14C's signed horizontal offset and the final party-slot carry;
- the direction-specific leading contact probes used by native movement.

The offset is now captured and logged. The ordinary game input and collision code
still perform movement. The graph is rebuilt from current actors, so a newly
occupied route can be replanned. Approaches to solid people and objects include
standing room within the existing native Confirm range; the player still confirms.

Some switches and pickups activate when their solid body is bumped. A selected,
currently available touch-only target permits that final contact. Other actors
remain obstacles, and native first-contact precedence is preserved. The graph
binds this exception to the selected approach goals for both search and steering;
staged searches rebind it to their own goals. It never changes the game's collision
state or injects Confirm.

## Evidence and limits

Local evidence: `artifacts/research/castle-return-0330/`.
Executable SHA256: `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.

- Fresh Ghidra decompilation of movement and contact routines, plus the 187527
  assignment proving that the fair's global actor block is engine+40.
- 3,147 contact vectors executed in the original executable, including all 669
  scene ids, both party-slot carry states, boundary values, offsets, removal,
  unloaded marker precedence and the fair flag. The C# rules match those results.
- The old planner reproduces the exact reported Cathedral stall at both 16- and
  32-unit movement speeds. The corrected planner completes the route with zero
  blocked frames. Restarting from the reported stall also completes at both speeds.
- Six castle movement/contact replays reach the native return-event region with
  zero blocked frames: both speeds, closed/open initial doorway, and armed re-entry.
- Regression tests cover both story phases, native chapter boundaries, unknown
  progress, closed-door staging, collision capture, and Confirm approaches from
  all four sides of a narrow corridor.
- Eight controller/native movement replays reach a solid touch-only switch's native
  contact from all four sides at both speeds, without announcing arrival early.
  Regression tests also keep an overlapping character blocking and preserve the
  switch as an obstacle when routing elsewhere.
- A synthetic crowded fair-radius search with 50 actors and an unreachable goal
  hit the 65,536-node cap in 208 ms (104 ms without actors) on this machine. This
  measures a bounded stress case, not frame timing in a live game.
- All 151 hook signatures match the supported executable.
- Release tests: 1,917 passed, zero failed (149 Core, 839 Native, 918 Mod,
  3 footsteps run separately, and 8 Prism). Claude's final focused review found
  no blocking issues after the solid touch-target correction.

The replays execute original movement/contact code offline. They model the audited
tile copies and event flags; they do not execute the entire cutscene or simulate
every moving NPC. Cathedral geometry comes from the earlier live capture plus the
organ's actual door copy; actor20's location/cache/load came from the latest log,
and its zero horizontal offset from the earlier capture. Castle geometry is the
extracted map with its audited doorway copy. No input or save editing was used.
Live confirmation of this build and the rest of the game's routes remains separate
from these regression results.

## Installed release

Version 0.3.30 was installed while the game was closed on September 23, 2026.
Implementation commit: `d7891af0b79ff0b69db9675391ba260076587e5a`. Product version: `1.0.0+d7891af0b79ff0b69db9675391ba260076587e5a`.

- Mod DLL SHA256: `479DE0A42C300DFE8840E5F0F7F74D9B5C6D3155838C247F0B9FE341E0D4956D`.
- Manifest SHA256: `2893740FD96A1C9A900B755C3C0EF0DA05B6062126D4984B3C81C1F65864048D`.
- All 28 packaged mod files, 45 loader/shared-hook files, and both native launchers
  match their sources byte for byte. The game executable remains unchanged.
- Deployment verification: 25 passed, zero failed. Existing Steam launch registration
  and the installed x86 runtime passed their checks.
- Previous installation: `X:\SteamLibrary\steamapps\common\Chrono Trigger\Accessibility\Backups\20260923-095535`.

Machine-readable evidence is saved in `deployment-proof.json`, with the full
release logs, native replays, and hook-byte proof beside it. No live play session
was started after installation.
