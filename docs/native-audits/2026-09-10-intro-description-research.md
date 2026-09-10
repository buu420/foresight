# New Game opening description research

## Player result

The player reported reaching the first controllable moment on 2026-09-10 with
0.2.6. The local `2026-09-10 17.14.53 ~ Chrono Trigger.txt` log records the
rendered naming question, Yes/No focus, and opening dialogue from 12:15:54 through
12:17:02. At 12:17:20 it records a separate top-menu missing-marker error.
The report confirms progression through the opening, not complete narration of
its visual content or a successful in-game menu test.

The player chose brief automatic descriptions between dialogue. These descriptions
must follow executed scene events and must not advance through player pauses.

## Visual reference and proposed wording

Primary visual reference: [Carnoth's Steam-version playthrough](https://www.youtube.com/watch?v=SQ9EF3FsKSM),
published 2025-01-11, approximately 00:32 through 01:43. Only the first five minutes
were downloaded to the local research directory for inspection. The video and
contact sheets are research material and are not distributed with the mod.

The reference has a streamer overlay outside the game picture; its timer and
decoration are excluded. Its playback times are inspection coordinates, not
runtime triggers. Names must not be guessed from a player's chosen text.

| Observed picture | Draft description |
| --- | --- |
| 00:32-00:42: overhead water, moving white birds, castle and town on the coast | White birds fly over the sea. The view glides past a castle and a coastal town. |
| 00:44-00:55: fairground, colorful balloons rise above trees | Colorful balloons rise from a fairground beside the woods. |
| Around 01:12: bedroom becomes visible, boy in bed on the right, mother beside bed, cat nearby | In a small bedroom, a red-haired boy sleeps in a bed on the right. His mother stands beside him, with a cat nearby. |
| Around 01:18-01:21: mother faces the window, curtains open and room brightens | His mother opens the curtains, filling the room with sunlight. |
| Around 01:31-01:38: mother moves to the stairs and descends | His mother heads downstairs. |
| Around 01:38-01:43: boy rises from bed and stretches | The boy climbs out of bed and stretches. |
| Actual first input-enabled state, separately verified | You can move now. |

## Native audit status

Claude located the field opcode dispatcher at RVA `0x1619E0`, its opcode table at
`0x163620`, and the field engine's current script address at `+0x24`. The context
is passed in ECX with one callee-cleaned stack argument. The engine is allocated
by `FieldScene` and stored at `FieldScene+0x290`.

The native control flag at field-state `+0x108C` also changes around other
operations. Neither a generic nonzero flag nor entry to FieldScene is sufficient
to claim first player control. The apparent location value `+0x105C` is written
as a destination during transitions; it cannot yet serve as the sole current-map
identity. A follow-up audit is mapping the installed Atel opening scripts and
the exact final control-grant sequence.

Research artifacts are under
`C:\Users\buu42\Documents\Codex\2026-09-09\chrono-trigger-intro`, including
`intro-anchors-review-claude.md`, its Ghidra evidence, and the two inspected
`reference-opening-*.png` contact sheets. The [CTViewer script documentation](https://github.com/GitExl/CTViewer/blob/main/docs/scene_scripts/script_execution_flow.md)
is a reference for candidate opcode meanings; each used PC operation still needs
verification against the installed executable.

This document contains proposed cues. It does not claim they are installed.
