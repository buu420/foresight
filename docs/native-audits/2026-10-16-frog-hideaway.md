# Frog's hideaway entry navigation

The tester's October 16 log enters scene141 at13:49:19, story point102,
position2304,895,layer1. People is empty, the only object is chest42, and
`story:full:frog-medal` is a non-spatial reminder. At13:50:19 the game's
"Who's there!?" dialogue begins. At13:50:31 actor4 is drawn at745,2016 and
the People list contains Frog. This matches the reported search for him.

The log is1,098,183bytes/5,740lines; SHA256
`3b89c91a4796d20bc09bb209800797983fac15e4d1dd077c8bd484acd6314eda`.
It logs neither the complete local flag array nor live scene141 terrain
planes. The native gates below are script evidence, not claimed log captures.

## Native evidence

The supported executable SHA256 remains
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
Scene141 uses MapTable0115 and Atel0186. The installed script SHA256 is
`f55699e303ad900df851c881ca3263fe072ceb1b0a3165c3a90277672931029c`;
the map SHA256 is
`ab42bdb50b6ea0af029073fbf2010c2e600c3f13a7a42d6485353f702c749ff0`.
Resource reading follows [CTViewer's PC format reference](https://github.com/GitExl/CTViewer),
with operand behavior checked against the supported executable.

Offsets below are raw Atel file offsets, including its0x1E1 header.

- Actor0's post-initialization loop at023C runs `22 00 06 07`, copying the
  leader's tile coordinates into locals06/07.
- Actor8 initialization at0727 requires local08=0. At072C, globalFF bit80
  sends it to the inactive path. Otherwise it returns at0738 and runs its
  update loop. The inactive path at0735 hides and stops actor8.
- At0739, `12 06 08 03 08` requires local06<8. At0740, `04 08 23` calls
  actor4 function3 automatically; this is not a Confirm dispatch. At0743,
  `0B 10` stops actor8's own processing.
- Actor4 function3 begins at061C with dialogue6F, places Frog at0623, shows
  him at0628 with opcode90, and runs his entrance animation. The later
  dialogue and scene remain the native game's responsibility.

Fresh Ghidra12.1.2 read-only exports under the private research directory
verify coordinate reader RVA165E20, comparison handler1651A0, script
runner161560 and scheduling164D10, plus hide/show and processing opcode
assembly windows. These are native pseudocode/disassembly, not recovered
managed source. Ghidra checks the program executable hash before exporting.

## Repair

`FrogHideawayTargets` admits only the coherent scene141 state with local08=0,
known globalFF80clear, active native controller8, and the loaded, concealed
non-party Frog actor4 with its exact native class/visual identity. Unknown
flags do not mean clear. A paused/removed controller or revealed actor retires
the target.

The controller has no physical position: the log parks it at65280,65280.
The repair uses the native X<8 floor region, filtered by the current native
map, actor collision and exit rules. Goals sit at tile centers at least128
units inside the strict boundary, beyond the controller's32-unit arrival
tolerance. It labels the visible area **Left side of the room** under Objects,
and adds that target as an alternative local binding for the existing
`frog-medal` objective. A concealed character is not exposed in People.
After the reveal the existing visible-actor path takes over, including the
runtime character name, live position and player Confirm readiness.

## Validation limits

The new entry/reveal regressions initially failed because the area target
was absent. The tests use Map115's installed initial terrain and synthetic
actor snapshots derived from the logged values; they are not live replay.
Both commands must find a route, enter X<8 and avoid exits; loss of native
player control stops movement. Negative controls cover missing/paused/removed
controllers, wrong identities, active-party Frog, revealed Frog, later local
mode, unknown local/global values and incoherent or different scenes.

No game launch, live playthrough, physical controller input, native dialogue
confirmation or audible screen-reader test was performed for this repair.
