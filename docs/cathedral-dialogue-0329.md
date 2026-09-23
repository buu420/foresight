# Cathedral navigation and dialogue lifecycle follow-up

## Reported dialogue error

The September 23 session records the shopkeeper's goodbye at 01:48:18.
No dialogue-close event follows before leaving the market. At 01:48:53,
while entering the Cathedral again, the registered window's update reports:

> Registered dialogue update snapshot is incomplete: Dialogue MsgWindow is not yet active.

The reader previously accepted an inactive window only while waiting for an
opening animation. If a formerly active window became inactive without its
animated close hook, it permanently faulted the dialogue hook set.

Ghidra verified the following RVAs against the supported executable SHA256
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`:

- `195C70`, the animated close function, writes zero to active byte `+2BD`.
- `197530`, the update function, skips dialogue processing when that byte is zero.
- `15BF30`, the constructor, initializes `+2BC/+2BD` to zero.
- `1984F0`, the opening-animation callback, sets `+2BD` to one.
- `15C030/15C060`, the deleting destructor and destructor body, release the
  message's resources and base layer without calling the animated close function.

Together with the log, these support a stale registered address across scene
teardown and later window creation. The session did not record window addresses
or a destructor trace, so exact address reuse remains an inference. The logged
inactive state and the mistaken fault branch are directly established.

When an active registered window is subsequently validated as inactive, the
reader now ends that interaction once and clears its text, choices and confirm
marker. It does not announce a selection. Pending opening animations continue
waiting normally; a later interaction still requires the native open hook.
Invalid memory, invalid active-byte values and malformed choices retain their
existing error checks. The game is never changed by this observation.

Two lifecycle regressions first failed with the reported error or missing close.
They now pass, together with the existing dialogue hook checks and a regression
that an unrelated inactive window cannot end the current dialogue. The initial
focused run passed 25 tests. This is a native-state replay, not a live rerun of
the shopping trip.

Research and test output are retained in
`artifacts/research/cathedral-dialogue-0329/`.

## Cathedral navigation diagnosis

The old session fails immediately after the right switch at `(9225,4982,1)`.
The Story Event becomes `Play the organ`, but has no native destination attached.
A separate route to the front hall repeatedly stalls around `(8073,10758,1)`,
while attempting to move north through columns 31..33.

Fresh read-only captures on September 23 confirm the current scene 131 planes
and the loaded organ actor 36, class 4, visual 100, at `(4384,4959)`. The native
scene's actor 0 repeatedly executes `22 00 06 07` at packet offset `07AC`,
copying the lead party member's tile X/Y into locals 6/7. The organ's activation
script rejects X greater than 17. The catalog had treated that positional
condition as a prerequisite to offering the object, so the object disappeared
from navigation while the player was by the right switch. Replaying the capture
with only local 6 changed from 7 to 36 reproduces the missing binding. That
second value is an explicit synthetic variation, not an additional live capture.

The route failure is separate. The strip has property byte 1 equal to `0D`,
which means southward floor movement at 32 native units per update. Ghidra's
178FF0 decompilation assigns that velocity to the mover's `+88/+8C` fields.
An isolated run of that actual function from the supported executable confirms
all four force directions and speeds 8, 16 and 32. Standing on the tile is
legal, but northward progress against its strongest push is not. The old graph
checked shape and layer alone, omitting this directional constraint.

These investigations use installed scripts and executable code. They do not
write to game memory, save data, or controls.

## Navigation changes and validation

The field graph now rejects movement against a speed-32 floor, checking the
starting tile and every sampled point along the edge. It preserves movement
with the floor, ordinary floors, and the previous collision and exit rules.
The organ is offered using its script's availability at a valid approach;
its global progression flags and native actor checks remain required. Its
goals allow for the controller's arrival tolerance and keep Crono on tile X
17 or less, where the native activation handler accepts Confirm.

The native map fixture contains the actual already-applied terrain copies.
Only the right strip is moving in this capture; applying both mutually
exclusive startup copies would create an incorrect test map. Regression
starts from the earlier failure log are combined with that capture explicitly,
not represented as an atomic capture of each failed position.

The tests cover all four logged stalls, all four strong-force directions,
travel with the floor, the usable left strip, and the missing organ binding.
They also run the actual navigation controller to completion on the corrected
graph for the front hall and organ routes. That controller test advances one
legal graph edge per command; it does not emulate every native physics detail.
Separate Unicorn execution verifies the real game's floor-force outputs.
The still-sealed northern passage remains physically unreachable before the
organ's FF:04 flag and native tile copy.

Claude independently reviewed the navigation causes and added ten regression
cases, including the original failure when the terrain plane is discarded.
The initial blocked-route hypothesis was corrected against the live capture
and native force routine before release; speculative obstacle learning and
adjacent-cell exit goals are not part of this implementation.

All 1,882 Release tests pass: Core 149, Native 831, Mod 891, footsteps 3,
and Prism 8. All 151 native hook signatures match the supported executable.
The installed update still requires normal in-game confirmation; no automated
live traversal or complete playthrough is claimed.

## Remaining navigation coverage limits

Claude's additional static survey found strong floor flags on 17 of 338 maps.
Forty of 42 tested scene/arrival pairs retained their reachable exits. The
Blackbird's rear and forward passages (368/369) need live-state checks: their
base-map results omit scripted terrain changes. This is not evidence of a
completed live test of those areas. Weaker moving floors retain their previous
behavior; a walking-speed interaction with them is outside this Cathedral fix.

## Installed release

Version 0.3.29 was deployed on September 23, 2026 from implementation commit
`6406bd826c4f42240ff1bddc6d5c87ffab8f51ae`. All 25 deployment checks passed,
including the existing launch redirect. Hash comparison verified all 28 mod
files, 45 loader/shared-hook files and both native launcher files. The game
executable is unchanged.

Installed mod DLL SHA256:
`47DD21C986C37E0116198C208C171508D1ABD53D29F217253FCE232A0180A448`.
Installed manifest SHA256:
`8661372CB123A7901B0CA0D30725C9229F876073936D98EB2908DF082A7D4C9D`.

The previous 0.3.28 installation is retained under
`Accessibility/Backups/20260923-024447/`. Full hashes, test totals and the exact
implementation commit are in `deployment-proof.json` in the research directory.
