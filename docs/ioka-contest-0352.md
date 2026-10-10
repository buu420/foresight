# Ioka drinking contest feedback in 0.3.52

The October 10 09.34.36 tester log contains scene 280, Ioka Village Center at
night. Native dialogue already speaks the rules and the result, including
"Mash [B] many time" at 05:04:11. The active contest then has about fifteen
seconds without mod feedback. The second supplied log ends at startup.

## Verified native behavior

The installed scene uses Atel_0371 and map 152. Its 2,732-byte script has SHA-256
`FDE1FB1BA5F6E58B43E782FBFC41693BB73CD735D3948D0BA5A0B602691B33CA` and matches
the earlier extracted script. Research uses the supported executable with hash
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.

Fresh read-only Ghidra exports verify the opcode dispatcher at RVA 1619E0,
local-bit writes at 169280/1693A0 and animation writes at 16E590. The existing
field input callback depends on movement control, which the scene disables;
the opcode dispatcher continues to execute the contest. No new native hook
or input injection is required.

| Original script offset | Actor | Visible action |
| --- | --- | --- |
| 07BE, `75 0E` | 6 | Contest starts, local 0E changes from 0 to 1 |
| 03E9, `AA 3B` | 2 | Player character's drinking animation starts |
| 07D0 / 07EB, `AA 50` | 6 | Ayla's drinking animation starts |
| 07F6, `77 0E` | 6 | Contest ends, local 0E changes from 1 to 0 |

Runtime PCs omit the original file's leading actor-count byte. Native AA
advances the dispatch PC by one; the two local-bit handlers advance it by two.
The reader checks these post-call changes, scene 280, script header and exact
instruction bytes, the executing actor, native character identity, drawn state,
the active flag and the resulting animation before announcing an action.

Installed sprite headers, cel assemblies and animation tables show Crono's
3A holding pose and 3B drinking motion, and Ayla's 4F holding pose and 50
drinking motion. The separate c101 bowl sprite changes with the actions.
The decoder follows the primary [CTViewer PC sprite implementation](https://github.com/GitExl/CTViewer/blob/main/src/filesystem/sprite.rs)
and its [animation opcode documentation](https://github.com/GitExl/CTViewer/blob/main/docs/scene_scripts/sprite_animation.md),
checked against the installed data and native handlers. Private images and
Ghidra exports remain in artifacts/research/ioka-lair-0352.

## Player-facing behavior

The mod announces a distinct start and explains rapid presses of the native
Confirm action to drink more than Ayla. It uses her current character name.
Brief drinking-action descriptions are grouped at least two seconds apart;
the start instruction gets a 3.5-second interval before action feedback.
The mod announces the contest ending, and native dialogue reports the outcome.

These cues describe observed animations. They do not invent a visible numerical
score, equate animation loops with drinks won, or expose internal thresholds.
The original native call always executes once. Reader, focus, speech or logging
failures cannot skip or retry it. Disabled hooks and background play are silent;
returning during a verified active contest gives current instructions.
After a temporary speech or name-reader failure, the next verified action
retries current instructions.

## Verification and limits

Native memory tests cover the five action sites, incomplete native writes,
wrong scene/script/actor, inactive and hidden actors, and owner changes.
Runtime tests cover speech spacing, foreground recovery, native name use,
hook disabling and all observer failures. The shared hook test proves contest
feedback works while intro tracing is inactive and retains one native call.

Live contest timing, Prism listening and physical controller delivery still
need tester verification. Story-action narration work remains paused.
