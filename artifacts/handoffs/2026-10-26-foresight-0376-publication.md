# Foresight 0.3.76 public release and latest report audit

## Current status

The user's October 26 message, “You'll have to release it so they can,” explicitly authorized publication of the already built/deployed 0.3.76 update. It is now public at https://github.com/buu420/foresight/releases/tag/v0.3.76 and available through the buu420 author source's beta channel. See `docs/releases/v0.3.76-publication.md` for hashes and receipts. No new product code was changed in this turn. Source canonical checkout is the accessibility-mod folder; the parent game folder is deployment only. Unrelated `.claude/` and `docs/claude-code-cli-setup.md` remain untouched by root.

Tag/source payload: `427af76d3f89f5fe27894e4ea89911ef18485fd7`. Standalone SHA256 `0cd7f5c6fc6e6eed7e463bb7fc312ecaa7dc585debd46741621a9013b8ae5a66`. AMM SHA256 `40d85e8f1bdec4a0fcda5115bdc937e693e2909373464290709f58c0b2c371c2`. The existing Downloads ZIP is unchanged. Both formats exclude the crash collector. Catalog commit `7ef14953c4f54565c8965c6b113a30db1ebb0d3c`; five games and all 197 historical releases preserved. Public downloads and anonymous raw index were verified.

## Latest input and findings

Input ZIP: `C:\Users\buu42\Downloads\2026-10-26 01.37.14 ~ Chrono Trigger.zip`, SHA256 `d47aca1b4e04ee8cb5a1423449c92bb184d672e74c49a708993929c74f1ad480`. Safely extracted under `artifacts/research/black-omen-vortex-20261026/logs`, with per-file hashes in `log-source.json`. It contains two complete text files (214209 and 156659 bytes), not one. Texture-ready records identify **0.3.71** in both (lines 379/378). The Black Omen runs traverse scenes 324, 99, 100 and 101. They contain no post-clear world-map vortex observation.

The reported northern-door failure in 324 matches behavior fixed in 0.3.72 and included in 0.3.76. The logged map matches the existing native-map fixture, and live actor/local state satisfies the current door contract. The existing pad recognizer covers native contacts in scenes 97/98/320/321. Room 99 has elevator controls; rooms 100/101 are scripted rides with battles. The tester reached listed room-99 controls with auto-walk in the old build. Do not infer that current 0.3.76 has reproduced the missing-pad/door bug from these logs.

Root ran 102 existing relevant tests and 18 exact-package x86 vortex entry/route checks successfully. The latter use native text/catalog identities and script tile updates, selected raw map/property fixtures in worlds 0/2 and explicit synthetic open floor in world 6. Enabled flags, state, viewport and player/vehicle positions are synthetic. A direct Map_0006/Id_0006 pairing was not a proven native resource binding and failed; it was not treated as a product bug. No live gameplay, controller or audible-speech test was performed.

Read-only Ghidra reconfirmed native clear-save setup at RVA 2B2120 (the write at 2B214E) sets actor+1189C, Global1FB, to 45. Transfer handlers 270720/2707F0 connect it to world System1BB2. Native world scripts enable vortex entries from masks 01/04/40 for worlds 0/2/6, with name index 92 and destinations 595/661/662. Current code lists enabled records under Exits and has gated optional entry objectives. No state was written or unlocked. The fresh-log request asks the tester to install 0.3.76, load their completed save and browse Exits/Story Events on foot and in the Epoch. The user clarified the report is secondhand and that these testers have beaten the game; do not portray this as the user personally testing or assume they have not finished it.

## New current-code issue, pending confirmation

Both root and Claude independently found that `BlackOmenTargets.ForRoom` lacks scene 99. General Story Events can choose the backward route; since room 98 routes forward into 99, following only Story Events can loop. Local elevator controls and the corresponding optional quest entries remain available, so the public 0.3.76 notes disclose the workaround.

A pending async question asks whether to fix this elevator Story Events route in the next tester update, explaining the posted AGENTS.md confirmation requirement. Do not interpret elapsed time as approval. No implementation was started. If approved, use native script check order: Global1A7 bit01 → actor3 left sparkle; bits02 or08 → actor1 doorway; bit04 → actor2 right sparkle. Missing/unknown stop state should yield a non-spatial note rather than the generic backward route. Preserve native interactions and player Confirm; use the same binding in both navigation modes. Do not mutate the public 0.3.76 payload; a later product change needs a new version.

Needed tests include all four elevator stops and unknown state, both navigation modes, no 98/99 Story Events loop, and a room-324 replay from the newly logged actor/map state. There is no logged room-99 collision snapshot; distinguish any reconstruction or resource fixture from a live map. Separate follow-ups suggested by the audit (not authorized/implemented here): version in installer/startup text and premature boss names in later Omen room/quest labels.

## Claude and retained evidence

Claude Teammate alias `foresight-black-omen-regression-20261026`, session `7564582b-24db-45a1-9a20-acdd2a9d3c6a`, performed the read-only Black Omen audit. Bridge requested max effort; local message metadata confirms only actual `claude-opus-5-5`. Reports: `claude-black-omen-audit.md`, `claude-black-omen-native-tuples.json`, `claude-model-verification.json`. Root owned vortex checks, Ghidra reconfirmation and release/catalog verification. No subagents, memory edits, source/game edits or live launches were delegated in this task.

Publication used the authenticated gh CLI because the connected GitHub tools lack release-asset upload. Assets were prepared/checked before publication; draft and public digests matched. CLI 0.30.0 cloned the current author catalog, used structured game/release JSON, preserved all history, checked the remote base before publishing and completed indexPublished/liveVerified/directorySubmitted. No messages were sent to other people.
