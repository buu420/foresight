# Dialogue choice transition repair, 0.3.13

The September 13 race-prediction log fails immediately after "Try and guess the next winner?" with:

> Registered dialogue update snapshot is incomplete: Dialogue ordinary phase 0 current line 1 unexpectedly has the 0x10 choice flag.

The reader incorrectly classified an intermediate native state as corrupt, faulting the dialogue reader before it could announce the choices.

## Native evidence

Ghidra decompilation and x86 disassembly were checked against the supported executable SHA-256 `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`. Addresses below are RVAs.

- `MsgWindowUpdate` (`197530`) finishes an ordinary row at `19795B..1979AA`. Unless a page/end flag changes the phase, it increments cursor `+2C0`, resets the reveal timer, and returns with phase `+2CC` still 0 or 2. The new cursor may point at a choice row.
- On the next update, `197722..1977BE` tests flag `0x10`, consumes the contiguous choice rows, records count `+2D4`, builds the controls through `194800` or `194BF0`, and only then sets phase 4.
- Parser `195FB0` sets the choice flag from the choice tags and initializes cursor, page base, and phase to zero. A window whose first row is a choice therefore also opens in phase 0.
- Phase 4 continues to validate the full choice range, flags, nonblank labels, page bounds, and selected index. The exact confirmation call at `19768A` still gates selection announcements.

The installed `Localize/en/msg/mesk0.txt` has a question and two choices in `FLD_MESK0_010` ("Sure!" and "Not this time."). `FLD_MESK0_016` is a separate four-choice window with numbered runner labels and no ordinary prompt row. The [CTViewer dialogue documentation](https://github.com/GitExl/CTViewer/blob/2e5a206e09f0028fd5a1ca6cb9a9ed18bfc64fea/docs/scene_scripts/dialogue.md) supplies the resource-table mapping and choice-tag context; executable analysis establishes the runtime timing.

## Change and validation

Capture accepts choice rows pending in phase 0 or 2 without publishing them as ordinary dialogue or as an active list. Phase 4 publishes the native labels and selection through the existing hooks and narrator. No labels or race outcomes are supplied by the mod.

The regression first failed on the old implementation. It covers both normal and accelerated prompt completion, choice-only opening, both race menus, all four runner focus positions, confirmation, duplicate updates, and dialogue after closing the choices. The fixture supports native heap strings so the full prompt and numbered labels pass through capture and narration. Existing malformed-memory and phase-4 validation tests remain in place.

All 84 focused dialogue checks and all 1,145 solution tests passed in Release configuration.

Research output and test results are under `artifacts/research/race-choice-0313`. The automated replay does not replace an in-game test: launch normally, talk to the bookmaker when betting is open, select "Sure!", move through the four runner choices, and confirm a runner.
