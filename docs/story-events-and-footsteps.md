# Story Events and footsteps, 0.3.6

The user approved local Story Events through the trial and prison escape, and
footsteps tied to actual movement with a listening sample. The existing U/O,
J/L, K, I, and P navigation controls remain the same. F8 toggles footsteps for
the current game session. The user accepted the revised recorded sample for now.

## Evidence and boundaries

The sequence was checked against the user's [GameFAQs PC guide by vinheim and
Bkstunt_31](https://gamefaqs.gamespot.com/pc/233789-chrono-trigger/faqs/64344),
version 1.6, opening chapters through the prison escape. The user opened the
guide in Chrome; read-only page text access succeeded after automated web access
was refused. Guide prose and game assets are not redistributed here.

Scene IDs, actors, exits, and progression gates come from the installed PC
resources and the supported executable, SHA-256
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
Claude owned the native scene/script inventory and prison connections; Codex
checked the script startup paths, implemented the catalog/audio, and ran tests.
The [CTViewer source](https://github.com/GitExl/CTViewer) provided the PC script
decoder structure, checked against native Ghidra handlers.

Important research corrections made before release:

- Function 0's first Return yields to startup, so that following entry must also
  be decoded. Stopping at that Return omitted castle, cathedral, return, and
  trial progress changes. Unconditional branches stop sequential fallthrough.
- Branch targets use the opcode address plus the decoded offset. Native handlers
  at RVAs 161D48 and 161E6F support that addressing.
- MapJump table records have global indices, but live exit-grid IDs are local to
  the scene. Bedroom record 4 is local exit 0; plaza record 15 is local exit 1.
- The first trial is scene 27. Scene 438 belongs to the later trial.
- Scene 8 actor 11, NPC visual 99, is the fallen pendant, not the Telepod.

The scene table is selected natively at RVA 15984D (Atel index) and 15986C
(map index). MapJump's native expansion at 17A1DD uses each scene's record range,
source tile, destination scene, and destination tile. Route endpoints still come
from the live collision and exit grids, never from copied guide coordinates.

## Current story context

The story counter is the low byte of `ActorBase+110B0`. Expanded global bytes have
a four-byte stride from that address. The native `gbiton` handlers at
168BD8/168BF5 confirm the indexing. Optional flags are read twice and omitted if
missing or unstable; unknown is not interpreted as a clear bit. The final
counter, Mother flag, actor base, and scene are rechecked for coherence.

| Progress | Native evidence and use |
|---|---|
| 3 | Bedroom/kitchen and the native Mother introduction flag at global 140 bit 0 |
| 6, 8 | Rear fair scene 439 actor 3, bump then return pendant/join |
| 10, 12 | Scene 8, Lucca demonstration then Marle's disappearance |
| 15, 18, 28 | Scene 120 actors 16/17/18, file offsets 0A85/0ACC/0BA2 |
| 16, 33 | Scene 122 actors 23/24, file offsets 096D/0A97 |
| 21 | Cathedral scene 129 actor 15, file offset 07D2 |
| 27 | Queen Leene return, scene 198 actor 12, file offset 053E |
| 36, 39 | Truce Canyon Gate and present exhibit startup |
| 42, 45 | Castle trial trigger and scene 27 trial completion startup |
| 46, 48 | Prison rescue and castle escape |

Numbers in the first column are decimal; script offsets and global indices below
are hexadecimal. Objectives stop after story point 48.

| Global / mask | Meaning used by the catalog |
|---|---|
| 54 / 10, 20 | Fallen fair pendant visible / picked up |
| 55 / 80 | Fairgoer has announced Lucca's exhibit is ready |
| 56 / 02 | Crono's Telepod demonstration completed |
| FF / 10 | Chapel organ opened the passage, scene 129/13 at 06C5 |
| FF / 01 | Front cathedral switch, scene 130/35 at 0B83 |
| FF / 02 | Inner right switch, scene 131/24 at 0CBD |
| FF / 08 | Inner left switch, scene 131/46 at 1182 |
| FF / 04 | Inner organ opened passage, scene 131/36 at 0F6A |
| 190 / 04 | Crono escaped the cell, scene 71/20 function 11 at 0CA0 |

## Discovery and route behavior

Current objectives remain readable before their destination is discovered. Such
notes have no coordinates and cannot start pathfinding or automatic walking.
Once the native target is observed, the same objective becomes routable. Notes
are restricted to Story Events and cannot reveal hidden people or pickups in
other categories.

Scenery markers are limited to audited script slots: the left Telepod, the
castle encounter areas, the chapel organ and sparkle trigger, cathedral wall
switches, and the sign. They use live actor coordinates. Static scenery uses
class 7 markers because the map draws the object; ordinary hidden actors are
still excluded. Interactable scenery requires the native activation filter.
The sparkle additionally requires its separate NPC visual 112 to be drawn.
Invisible encounter triggers are internal route anchors, not invented objects
in the Interactable Objects list. Their goals lie inside the contact tile.

Objects require positive camera visibility to be discovered. Discovered targets
retain their last seen positions for this field visit; hidden/removed pickups
and retired markers are dropped. Multiple staircase targets prefer the current
view over a previously visited floor. The live graph still determines whether
a discovered approach can actually be reached.

Scene 72 actor 22's rescue handler at file offset 09AF parks the freed prisoner
at tile FF,FF. Actors moved beyond the map bounds are retired from discovery
even if their draw mode remains set, preventing a stale prisoner objective.

The catalog supplies local objectives and readable notes, not automatic puzzle
solutions, jury answers, secret rewards, or battle actions. The overworld,
combat, special field movement, and later chapters remain outside this change.

Prison routing uses the directed floor connections, not whichever exit is closest.
The four skywalks in scene 28 continue through local exits 6, 4, 2, then 0 as
the route climbs. Scene 72's corresponding onward exits are 12, 6, 2, then 0.
For example, bridge row 25 must go west through exit 2 into the left stairwell;
exit 3 returns east to the floor below. The first draft included that backward
exit and was corrected before deployment. Cell-area exit 8 leaves the separate
lower room instead of sending the player into it again through exit 7.

Independent read-only connectivity runs used the actual native movement
dispatcher in an emulator and the mod's graph over decoded MapTable collision
planes. No live game process was started or controlled. Static collision data
does not include every scripted door change, guard, or special movement; runtime
routes therefore still require the live graph. Prison side-room wall climbs
remain manual.

The guardroom's lower and upper sections are separated until its script opens
the door with map-copy operations at file offsets 01B8/01D0. Startup actor 2
checks the party's row at 01FF and requests the visible guards' encounter at
0209. While those guards remain available, their live row supplies the approach;
the objective advances to the upper bridge once they leave. This avoids directing
the player to an inaccessible upper exit before the scene's own event runs.

## Footsteps

The accepted bank is derived from [Kenney Impact Sounds 1.0](https://kenney.nl/assets/impact-sounds),
CC0. Five wood/carpet recordings were mixed, filtered, shortened, and faded into
48 kHz mono 16-bit PCM clips. `tools/Prepare-Footsteps.py` reproduces the bank
and seven-second walking/running preview from the source pack. The audio is
embedded in the mod; see THIRD-PARTY-NOTICES.md for attribution.

The existing accepted-input hook passes its combined manual/navigation pad
to a separate movement observer. A small coherent capture reads only the
leader's native actor block and control context. Ghidra at RVA 175A40 confirms
the directional mask F00, input mode 0, and native walking/running speeds 16/32.
No new hook site or movement write was added.

The tracker attributes displacement to the preceding accepted input. A beat
requires 384 native units (24 rendered pixels); navigation's spoken step remains
256 native units (16 pixels). Held input without displacement produces no sound.
Context, actor, scene, control, pause, and teleport changes reset accumulation.
At least 250 ms separates beats so a recording can finish at faster input tick
rates. Five variations alternate without a new random
generator or device allocation in the input hook.

A bounded worker queue owns pinned audio buffers and WinMM playback. The hook
only posts requests. Stale/cancelled requests are dropped; SND_NODEFAULT prevents
a system beep and SND_NOSTOP yields to an existing WinMM sound. Buffers remain
valid during asynchronous playback, as required by Microsoft's
[PlaySound documentation](https://learn.microsoft.com/en-us/previous-versions/dd743680(v=vs.85)).
F8 is foreground-only, edge-triggered, and ignored with modifier keys. Audio
failure reports the problem and does not stop navigation.

The player reported no footstep audio in 0.3.4. The live log confirms F8 off/on
and movement, but that build did not log movement decisions or PlaySound results.
Version 0.3.5 added two-second diagnostic counters for capture failure stages,
tracker decisions (including repeated clock values and resets), and audio
acceptance/rejection/stale requests. A separate 32-bit .NET 9 probe using the real embedded bank and
audio worker loaded all five waves and reported seven accepted, zero rejected,
and zero stale requests at its last periodic report. This proves the worker's
direct playback path; the player also confirmed hearing this probe.

The user's 0.3.5 trace then established the cause: scene 12 reported 62 normal
samples and 62 zero-clock resets after two seconds, rising to 364 clock resets
when the player started walking. Capture remained ready and displacement was
observed, but the partial stride was repeatedly cleared before reaching 384
units. Version 0.3.6 accepts zero elapsed time; only a backward clock or a long
gap resets timing. Unchanged positions remain silent, and the 250 ms beat limit
still prevents bursts. Regression tests first reproduced silence with paired
same-clock samples, then passed with the fix. In-game audibility needs a retest.

The same session's exit was scene 12 local exit 0, MapJump record 24, destination
496 (world 0), tile (50,38). That transition leaves the field-input hook entirely.
The user's loss of navigation keys there is the existing world-map coverage gap,
not evidence that local navigation stopped working. World-map integration remains
outstanding; the next footstep test should stay within the inn.

## Validation

The 0.3.6 Release suite passed 1,006 tests: Core 101, Prism 8, Native 514, and Mod 383.
The eight prison regressions reject the closer backward exit on each audited
bridge/stair floor. The manifest remains pinned by its exact SHA-256.

Regression tests cover actual displacement, blocked movement, running cadence,
release ticks, scripted motion, discontinuities, focus/menu resets, F8, embedded
PCM headers, stable optional flags, story progression, missing destinations,
nonspatial notes, cathedral discovery, sparkle hiding, and current-floor binding.
The new features have not been exercised in the live game by Codex. The next user
test repeats manual/P walking, blocked input, F8, and menus to verify in-game
footstep playback. The fair's Story Events remain a separate gameplay check.
