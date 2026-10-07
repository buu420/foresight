# End of Time and jet bike targets, 0.3.45

The October 7 tester log reports no route from the End of Time's pillar platform to
the old man, no route into his back room, and an unavailable Spekkio story target.
The tester also confirmed clearing the Factory on 0.3.44 and reported that the bike
race works, while asking for the bike itself to be trackable.

## Native evidence

The executable SHA-256 is
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
Native addresses below are RVAs against preferred image base `00400000`.

- Scene 464 uses map table `0085` and script `Atel0283`. The tester arrives at
  `(3456,3064,1)`, story point 72. Actor 24, at `(6528,4351)`, opens the stair
  terrain with a guarded automatic tile copy. Actor 25, at `(8064,4095)`, opens
  the doorway with a different touch copy after story point 75. Its earlier
  Confirm dialogue does not make the later touch copy a Confirm operation.
- Fresh read-only Ghidra decompilation of `178980` confirms that the leading
  movement probe can dispatch touch before terrain blocks that movement. The
  reachable contact on the upper platform is `(6272,4288,1)`; the lower room
  approach is `(8064,4352,1)`. The untouched native map has no standing points
  inside the closed doorway's exit cell.
- Pillar actors 15–23 remain parked at `(65280,65280)` in the log. Their script
  loops check native player-tile regions instead. The map draws the starting
  three lights; global `A6` bits 1, 2 and 4 draw and enable the other six. Local
  dialogue guards still apply. The labels remain "Pillar of light": era and
  destination speech comes from the player's native Confirm prompt.
- Scene 465's Spekkio is class 6, visual `E1`, at `(1920,2559)` in the log.
  `Atel0284` uses the high-bit load mode of `load_enemy`; native `16B220`
  converts class 5 to class 6 without changing the visual identity. The actor
  and story binding now accept both classes.
- The bike uses native class-7 Confirm markers: `Atel0345` actor 8 initializes
  at tile `(43,5)` in scene 223; `Atel0346` actor 9 initializes at `(56,9)` in
  scene 225. Tracking uses each live marker's coordinates and interaction
  approach points. The bike appears in Interactable Objects on later visits too.

The original game manual describes the character-switch action as a native field
and map action; the current screen layout and hook contracts come from the PC
executable, not the manual's controller mapping. [Manual transcription](https://www.world-of-nintendo.com/manuals/super_nes/chrono_trigger.shtml).

## Movement and information boundaries

Tile-copy previews prove that a native touch contact opens a continuation. They
never authorize walking on the preview floor. The first leg uses the captured
floor; manual guidance tells the player to continue toward the contact and
automatic walking sends only movement. A fresh floor capture must open the next
leg. The existing bounded passage wait stops walking if it never opens.

The closed room's exit goals can come from its currently enabled native copy, so
the planner can select the doorway before it opens. The live graph still blocks
the closed floor. No input is sent to activate a pillar, bike or menu, and no
coordinates, story flags or game resources are written.

## Verification and remaining live checks

The new regression cases use the reported player and actor positions, the
installed map's collision shapes, flags and layers, and native script guards.
They cover routes across the initially closed stair and doorway, inactive-door
rejection, class-6 Spekkio, the three initial and six optional pillars, and bikes
in both parking lots before and after the early story. Core cases cover manual
guidance and automatic movement through a native touch stage.

These are automated and static checks. Live listening and complete End of Time
routes were not performed during this repair. Test both navigation modes from
the initial pillar platform, the old man's room, Spekkio's lesson and the return
trip; read the native destination prompt in an active pillar and track each bike.
The separate party-change repair is documented in [party-change-scene-0345.md](party-change-scene-0345.md).
