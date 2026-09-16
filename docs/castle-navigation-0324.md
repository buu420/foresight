# Guardia Castle and pickup navigation, 0.3.24

The September 16 log reproduced an unavailable route to the queen's tower in
Guardia Castle, 600 AD, at story point 15. The two recorded positions were
`(8064,13823,1)` and `(3456,9599,1)`. Both had valid destination cells; the
unopened stair tile separated them from the upper hall.

Navigation now approaches the automatic stair-opening contact, then continues
only when a fresh capture shows the passage is traversable. The preview of the
native tile-copy operation selects the contact; it is never used to move through
a closed tile or written into the game. If the passage does not open, walking
stops with an explanation rather than claiming arrival at the final destination.

Inside the queen's chamber, Story Events first leads to the guard outside her
room. The player presses Confirm to speak. The guard's native completion flag
then changes the objective to approaching the queen. The automatic passage in
this room uses the same stair-opening support.

## Guide and native evidence

The castle sequence follows [The Queen Returns](https://www.thonky.com/chrono-trigger/the-queen-returns):
the main hall, tower stairs, guard conversation, then the queen. Positions,
prerequisites and movement come from the installed PC game, not guide estimates.

- Supported executable SHA-256:
  `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
- Scene120, map66, tile31,40 has collision properties `04 00 01` before opening.
  Atel0240 actors9/10 touch functions copy `(3,0)..(5,2)` to `(30,38)` with
  flags`3B` at offsets082F/0847 while local6 is zero. Actor9's actual fine
  position is `(8064,10495)`. The reachable southern contact is `(8064,10624)`.
- Scene122, Atel0242 actor9 confirm moves the guard and sets global0A0 bit20
  at055A. Actor8's touch at04F5 copies `(3,32)..(5,34)` to `(41,54)` while
  local7 is zero. This is a separate automatic passage after the guard.
- Fresh Ghidra analysis of RVAs17A6C0/17A860 shows actor+20 is a camera cache.
  RVA161560 owns the transient +152 query byte. Neither is a persistent
  interaction-availability flag. Native confirm17FA20 and touch16EF30 instead
  check actor+E8 and bit80 of actor+30. Captured script-call eligibility now uses
  those checks; missing bytes produce a capture error instead of silently removing
  a pickup or assuming that its script is enabled.
- Movement/contact RVAs175E90,175F70,176810 and178980 were replayed from the
  original executable with correctly decoded map properties. Compressed map
  property bit80 is an RLE marker and must be removed before collision testing.

Claude reviewed the research using the requested Opus model and max effort.
Its initial map-decoding conclusion was corrected against the original native
code. The first-visit story binding already combines exits from all three tower
landings. Its tower finding does apply to later visits through the shared scene
router: those now include the same intermediate flights, with a check that the
native destination table still matches the audited connections.

## Pickups and other targets

Scripted pickups, scenery and talkable NPCs no longer require the offscreen
camera cache or temporary interaction-query byte to be nonzero. This repairs
availability throughout the catalog, including the Guardia Forest Shelter bush
and sparkle. Exact script guards, actor identity, draw/retirement state and
native script-call gates still apply. The separately audited pendant gates are
retained. People and objects remain in their existing navigation categories.

The existing capture covers all343 installed treasure records, including active
non-chest pickups, and143 item-giving actor identities. These are source records,
not a count of distinct reachable rewards. Opened treasures and retired pickups
are removed. Item contents and dialogue are not bundled into the catalog.

The regenerated catalog retains669 scenes,1,020 exits,4,732 actor identities and
2,387 spatial records, and now preserves the collision-copy operands. Its SHA-256
is `786F19697E7C41CFF34352F988A2FD4230748F78C7C3947BB0F4582197B8EA8D`.
The existing11 incomplete script expansions and8 unused missing scripts remain
reported rather than silently counted as fully decoded.

## Verification and limits

- Release suite: **1,729 passed**, zero failures/skips (Core139, Prism8,
  Native774, Mod808). The real-time footstep tests run separately from CPU-heavy
  fixtures; their existing audio deadlines are unchanged.
- Catalog compiler: **20 passed**. All **149 native hook signatures** match.
- The catalog delta adds571 tile-copy operand records. Every earlier fact is
  preserved; the one differently simplified predicate passes all65,536
  combinations of its two native flag bytes.
- Eight castle controller replays passed: both logged positions, movement
  speeds16/32, and both tower exits. All selected the native opening contact and
  reached their destination with zero blocked movement ticks.
- All27 tower checks passed on extracted maps468/480, covering each landing,
  applicable upward/downward Story Events and three camera configurations.
- All36 later-visit router checks passed on the same maps, including both
  directions from every landing. A changed native connection cannot enable an
  outdated intermediate flight.
- The extracted queen's-room map provides a route to the guard and, after his
  completion flag, a staged route through actor8 to the queen.
- Regression fixtures cover idle/offscreen pickups, collected/retired targets,
  disabled script calls, unknown/finished conditions, ineffective copies,
  manual and automatic continuation, passage-opening timeout, leaving and
  returning to a contact, and reordered destination approaches. The per-frame
  tile-change check does not allocate preview maps.

The native replay executes movement, camera eligibility and actor contact in
Unicorn. It models the decoded tile copy after contact; it does not execute the
complete game or every NPC's script. These checks are not a live playthrough.
Later multi-step mechanisms and quest sequences still require ordinary in-game
use. No game input or save editing was used for this repair.

Local evidence is under `artifacts/research/castle-0324/`, including Ghidra
decompilations, native replay, map checks, test results and Claude's review.
