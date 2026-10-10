# Visible story actions in 0.3.53

Supported gestures and silent actions now produce short screen-reader descriptions.
They use Prism's existing noninterrupting speech output, preserving the current
dialogue in the queue. No online service or API key is needed to play them.

## Coverage and limits

The character catalog contains 57 individually reviewed sprite/animation pairs
for the first six party characters. It describes getting up, nodding, shaking the
head, laughing or rocking, covering the face, crouching, raised or waving arms,
and specific visible weapon poses. These mappings are available wherever the
native field scripts apply them during a scene or dialogue, including the first
prehistoric visit, Ioka and Reptite Lair, and subsequent scenes involving these
characters. This is not a claim that every action in those chapters is described.
Ordinary walking, unknown animations and unreviewed NPC graphics stay silent.

Current chosen character names are used after conservative native introduction
boundaries. Before that, descriptions use visible appearances such as a blonde
girl or a robot. The observer does not read future identities, reveal puzzle
solutions or supply inferred emotions.

Seven specific scene cues cover Mother opening the curtains and heading
downstairs; the boy leaving bed and stretching; the collision at the fair; both
characters getting to their feet; and the girl's hopping after the invitation.
The girl's stand-up is anchored to the actual standing pose, rather than the
earlier sitting-up pose in the initial preview. These are selected verified cues,
not a complete scene-by-scene narration of the game.

The previously installed movie descriptions remain separate. The public package
does not contain private narration recordings, game video or credentials.

## Native evidence

The observer shares the existing field opcode hook (`1619E0` RVA). It calls the
game once and then proves the applied native change. It operates independently
of the diagnostic New Game trace's finite recording budget.

Supported executable SHA-256:
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
Reviewed resource SHA-256:
`09914BF4A8944C0708C947E7EB5683AEE4ED48BC6D5D1D75FA07B68131653C7E`.
The resource hash is checked asynchronously before descriptions become available.
If it differs, the feature logs that these descriptions are unavailable instead
of interpreting unreviewed graphics or scripts.

Fresh read-only Ghidra exports verify the handlers below. Runtime PCs are one
less than original Atel file offsets.

| Handler | Required applied state |
| --- | --- |
| AA, `16E590` | Loop animation operand, mode 1, correct advanced PC |
| AB, `16E7A0` | New play-once start, mode 2; wait/finish ticks do not speak |
| AC, `16EA90` | Static frame operand, mode 3, correct yielded PC |
| B7, `16E920` | Counted sequence finishes and standing pose is restored |
| AE, `16E720` | Animation and mode reset to standing |
| A0/96 helper, `16CE00` | Correct movement target and yielded PC |
| E5, `171C40` | Exact copy operands queued and native request flag set |

The actual executing actor is `field+1180 / 2`. `context+BB4` is the script
number; it must not be presented as an actor index. Trace records include both.

Only drawn actors whose anchors are inside the actual native camera qualify.
The wider native processing-window flag is not used as a visibility test. This
conservative rule can omit a partially visible sprite whose anchor is outside
the camera. No widescreen margin is guessed. The curtain cue checks visible
Mother rather than its invisible scenery controller; its copy request is the
native start of the lighting change, not an independent rendered-pixel check.

## Specific scene anchors

| Scene action | Scene / Atel | Actor | Runtime PC |
| --- | --- | --- | --- |
| Curtains open | 2 / 324 | 10; visible subject 8 | 7C4 |
| Mother heads downstairs | 2 / 324 | 8 | 676 |
| Boy leaves bed and stretches | 2 / 324 | 1 | 426 |
| Fair collision | 439 / 74 | 3, partner 1 | 6CE |
| Girl stands up | 439 / 74 | 3 | 69B |
| Boy stands up | 439 / 74 | 1 | 539 |
| Girl hops | 439 / 74 | 3 | 63C |

Full loaded-script digests are checked for these cues:
Atel 324, 2504 bytes,
`7AAC21A8D18D4908F4655D070CDD47ECA95939472EE66625CF12FC713B19C765`;
Atel 74, 3010 bytes,
`574098067D910D56A9B30707F3DC837C56990AF788CAE55374A9C90CF4C04307`.
Story gates exclude the trial flashback and the later window interaction.
The alternate curtain branch remains excluded because it uses different sprites.
These exact scene proofs do not depend on an assumed scene-load control value;
the broader animation catalog still requires scripted control or an open textbox.

PC animation assemblies were rendered from the installed cells, PNG atlases,
slot and interval data in all four facings. The reader was checked against the
[CTViewer source](https://github.com/GitExl/CTViewer). The same animation number
is not assigned a universal meaning: Robo's empty 11 slot is excluded; Ayla's
11 raises her arms then crouches; Frog's arms spread outward.

Visual scene review used the Steam recordings
[opening/fair](https://www.youtube.com/watch?v=SQ9EF3FsKSM),
[future and End of Time](https://www.youtube.com/watch?v=ywbO1BUoFec),
[mountains and Ioka](https://www.youtube.com/watch?v=BlkeIx_XR20), and
[prehistoric visit onward](https://www.youtube.com/watch?v=gQ9yO2t6yWQ).
Coarse contact sheets locate footage; close sequences and individual sprite
assemblies establish motions. Descript review compositions are separate from
the original previews. Uncertain objects or motions are not enabled as facts.

## Speech and lifecycle

Actions observed while a textbox is open wait until its next line, choices or closure,
then enter the existing speech queue before the next dialogue line. This does
not assume closing a native textbox means NVDA has finished speaking.
Repeated animation ticks and script loops are coalesced, including loops that
return to idle or finish a play-once animation. Generic gestures can speak again
after the same native field restores player control with E3 01, or an ordinary
conversation closes while captured player control is enabled. Cutscene textbox
pages do not reset deduplication. Repeated scripted movement cues speak once per
scene visit. An aborted textbox flushes at the next captured closed-textbox state.
The opening boy's head-shake slot is excluded while the bed blanket hides it.
Title, battle, menu, scene/owner changes, disable and background state discard pending
actions. A failing old observation cannot clear a reactivated session's queue.
Capture, proof or speech failures cannot skip or duplicate the native call.

## Testing

Native tests use the installed scripts, including changed-byte, wrong actor,
wrong scene, wrong story gate, hidden/offscreen, incorrect movement/copy result,
and unapplied animation cases. Runtime tests cover dialogue order, foreground,
resources unavailable, repeated ticks, exact native execution, exceptions and
observer lifetime. Release and live-test results are recorded in the release
notes and publication audit once completed.
