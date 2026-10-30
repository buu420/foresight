# Vortex Entities Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task by task. User-approved behavior and standing release authorization govern execution; routine implementation choices do not require another approval.

**Goal:** Restore usable current-room targets and routes in scenes638 and645, including native chests.

**Architecture:** Keep native capture and collision as authority. Scope any corrected region sampling, watcher ownership or passage handling to independently verified script contracts. Story objectives bind the same spatial targets as the other navigation modes.

**Tech Stack:** C#/.NET9 Windows x86, xUnit, Python native resource inspection, Ghidra and isolated Unicorn x86 checks.

**Spec:** ../specs/2026-10-29-vortex-entities.md

## Global Constraints

- Preserve native locks, battles and player Confirm. Do not reveal randomized rooms ahead.
- No native state, position or facing writes; normal movement only.
- Exact captures and derived initial/post-action states must be labeled separately.
- No live game launch, physical controller or audible-speech claim.
- Preserve user files, saves/settings, narration and unrelated mods.

## Review Focus

- Native narrow or edge-aligned passages must use real standing points and trigger-safe arrival tolerances.
- Disabled, removed, mismatched or absent script watchers must not invent routes.
- Mandatory encounters remain native; route completion must not stop before their trigger.
- Unopened chests stay reachable and collected chests disappear using native state.
- State changes and unknown randomized values must not disclose future rooms or select a backward passage.

### Task1: Reproduce and repair the antiquity approach

**Files:** `Navigation/VortexRoomTargets.cs`, `Navigation/FieldNavigationSource.cs` if verified; tests `Navigation/VortexEntityNavigationTests.cs` and `vortex-entities-0386.json`.

**Interfaces:** Existing `FieldNavigationSource.Build` creates a `NavigationFrame`; Story Events binds catalog IDs. Retain those interfaces.

- [x] Reconstruct Map645 initial terrain, native actor0/8 watcher contract and logged arrival; record provenance.
- [x] Add failing tests: non-note Story target starts in both modes, trigger lies within native bounds, watcher/clear guards fail closed, no invented chest.
- [x] Implement the smallest native-evidenced correction and run focused tests.

### Task2: Repair the captured present-day room and chest coverage

**Files:** same targeted production/test files; native resource catalog only if extraction itself is proven incomplete.

**Interfaces:** Reuse Task1 fixture loader and real `NavigationController` commands. Scene638 passage ID is `script-region:26:597:0:3:4:3`.

- [x] Reproduce all six capture routes and inspect native actor26 passage plus all chest flags/positions.
- [x] Add failing tests for both navigation modes, native trigger completion, guards/disabled watcher and local spoiler-free labels. Native treasure tables contain no unopened or collected records in these rooms; assert no invented chests.
- [x] Apply only the verified correction and pass focused navigation regressions.

### Task3: Validate and release0.3.86

**Files:** version/manifest/changelog/user documentation, release receipts, dated handoff and this ledger.

- [x] Run original x86 terrain checks for the repaired routes, separating movement proof from script execution. Both are spatial triggers; no contact/Confirm control is required.
- [ ] Obtain read-only Opus5.5 max whole-change review and resolve findings.
- [ ] Run full solution tests, package/AMM tests and exact packaged x86 checks.
- [ ] Package, deploy with protected-file verification, publish GitHub prerelease and AMM catalog; anonymously verify assets/index.
- [ ] Commit closure/handoff and report release link plus live-test limits.

## Execution Ledger

- Base a1aa98815bd416e20a69e53eb7c01ab3f5587fff; isolated `fix/vortex-entities-0386` worktree.
- Scope approved in user reply. Public release already authorized.
- Six scene638 captures decoded; scene645 MapTable/ATEL reconstruction is explicitly derived. Log identifies0.3.84; current base0.3.85 did not repair these rooms.
- Ruling: implement inline and use authorized Claude Opus5.5 max for read-only native638 investigation and final review; preserve user's approved execution scope without redundant skill confirmation.
- Ruling: use native Local20 to select the Castle passage before the encounter. The same primary Story Events ID survives the native copy and continues in both modes; no generalized hypothetical-floor graph or live map write is necessary.
- 63 focused checks pass, including persistent-source continuity;56 original-x86 terrain runs pass,35741 frames. Native scripts, combat and room transfers were not replayed.
- Full suite first found one stale ModManifestTests SHA pin after the0.3.86 manifest update; both package/manifest pins now use A74073CF47A4D8957119B04F0FCBA056203933FF53BF4A8042E6C765579951FF. Fresh full run passes5611 checks (Core245,Native1523,Mod3833,Prism10); package4 and AMM6 checks also pass.
