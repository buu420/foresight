# Black Omen elevator Story Events implementation plan

> Execute inline with superpowers:executing-plans. Use test-driven development and one focused Opus 5.5 max review.

**Goal:** General Story Events in the elevator follows the current native forward control in both navigation modes.

**Approved behavior:** The user confirmed the October 26 proposal to correct this route. Global1A7 uses native check order: bit01 selects actor3, bit02 or08 selects actor1, and bit04 selects actor2. Missing, zero or irrelevant stop state produces a neutral non-spatial status. If the selected live target is unavailable, do not choose another stop's control or leave the dungeon. Native controls, rides, battles, collision, progression and player Confirm remain authoritative.

**Architecture:** Restrict the change to `BlackOmenTargets.ForRoom` for `full:lavos` in scene99. Bind the existing live target through `FullStoryTargets`; known local goals prevent generic routing, and empty goals for unknown state prevent the End of Time fallback. Reuse the current scene98 forward binding. No new coordinates, input generation or physics changes.

**Tech stack:** C#/.NET9, xUnit, native Atel0455 evidence and existing FieldNavigationSource/NavigationController pipeline. The canonical checkout remains the source workspace; unrelated untracked files are excluded.

- [x] Confirm behavior and inspect native branches, existing catalog controls and router empty-goal behavior.
- [x] Add RED regressions for all four stops, precedence, unknown/unavailable state, both guidance modes and the 98/99 loop. Clearly label synthetic geometry.
- [x] Implement the scene99 binding, run focused tests and resolve the focused Opus5.5 review.
- [x] Version 0.3.77, update both manifest hashes, run all required checks and commit the payload source.
- [x] Build Release, package, verify exact DLLs in x86, deploy locally and preserve saves/settings/narration/other mods.
- [x] Put the tester ZIP/checklist in Downloads, publish the next tester beta through the existing authorized release workflow, verify public assets/catalog and record the handoff.

Native evidence: `artifacts/research/black-omen-vortex-20261026/claude-black-omen-native-tuples.json`, Atel0455 and the existing `ClassicOptionalObjectives` stop ordering. New implementation evidence goes under `artifacts/research/black-omen-elevator-20261026`.

The latest two logs ran 0.3.71. The existing 0.3.76 pad/door fixes remain included; the separate vortex report still needs a fresh live world-map log. Do not claim a vortex or rendering-crash fix. Offline fixtures do not establish live gameplay, physical controller, audible speech or recipient-PC installation.

Completion: payload commit `3c9d6de4d8eeb680a410c9c490c5ddfb3cf53cbf`; 4803 tests, zero Release warnings/errors, 79 exact-package x86 checks, 303 deployment checks and 229 protected files unchanged. Public beta and catalog verified. See `artifacts/handoffs/2026-10-26-foresight-0377.md` and `docs/releases/v0.3.77-publication.md` for receipts and live-test limits.
