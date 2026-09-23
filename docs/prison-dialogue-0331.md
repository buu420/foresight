# Prison countdown dialogue, 0.3.31

The September 23 log places Crono in the prison cell (scene 71, story point 45).
At 10:39:02 the reader announced `Days remaining until execution: 2`. At 10:39:04
it faulted the dialogue family with:

> Registered dialogue update snapshot is incomplete: Dialogue current line 2 is blank.

Waiting is a supported story path, also described in the
[trial guide](https://www.thonky.com/chrono-trigger/the-trial). The failure came from
the mod's ordinary dialogue validation.

## Native evidence

The installed resource `Localize/en/msg/kmes0.txt`, entry `FLD_KMES0_000`, contains:

```text
<CT>\Days remaining until execution: <NUMBER>\<WAIT>0a</WAIT><AUTO_END>
```

Fresh Ghidra inspection used the supported executable, SHA256
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.

- `MsgWindowOpen` at RVA `195B40` calls parser `195FB0`.
- The parser splits on backslash and retains each row, then removes formatting
  tags. It records alignment as flag 1, automatic page advance as flag 2, and
  automatic close as flag 4. WAIT tags become per-row timing records.
- The countdown therefore has an empty leading alignment row, the visible
  countdown, and an empty WAIT/AUTO_END row. The final empty row is legitimate.
- `MsgWindowUpdate` at `197530` processes the timing records and renders the
  current row. Its text-length check permits length zero. It advances the cursor
  and handles the row's page/close flags when the delay expires.

Evidence is saved under `artifacts/research/prison-dialogue-0331/`: the original
session log, exact resource excerpt, and fresh native decompilations. No game
input or save modification was used.

## Change and regression coverage

A structurally valid ordinary row in rendering phase 0 or 2 now returns a complete
snapshot even when its text is empty or whitespace. That snapshot has no spoken
line. The reader keeps the active window and waits for the game's next update;
it does not announce a future row, change the timer, or synthesize dialogue.

Choice labels still require nonblank text. Vtable, memory readability, UTF-8,
vector counts, cursor/page bounds, phase, flags, and choice correlation checks
are unchanged. Blank semantic text is still rejected by the narrator; capture
does not publish a text event for empty rows.

The regression first reproduced the same registered-update error on the final
countdown row. After the repair, the hook harness reads both countdown messages
and subsequent dialogue exactly once, with no failure. Other tests cover leading
and interior blank rows, both rendering phases, Unicode whitespace, no lookahead,
and rejection of blank choices. All 54 capture and 27 hook-family tests passed.

The native code and resource establish the row's purpose; the hook harness replays
its snapshots. This is not a live replay of the full prison sequence.

Release verification passed 1,926 tests: 149 Core, 846 Native, 920 Mod,
3 footsteps run separately, and 8 Prism. All 151 native hook signatures still
match the supported executable.
