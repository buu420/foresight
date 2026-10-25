# North Cape and Bekkler Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans for inline implementation. Steps use checkbox syntax for tracking.

**Goal:** Identify the North Cape encounter and make Bekkler's visual copy-game cues readable at the native pace.

**Architecture:** Extend the existing scene-scoped navigation labels and observe verified script/animation completion through the existing opcode dispatcher. Reuse interrupting Prism announcements; preserve all native inputs and timing.

**Tech Stack:** C#/.NET9, x86 memory capture, xUnit, Ghidra12.1.2, existing package/deploy tooling.

**Spec:** `docs/superpowers/specs/2026-10-24-north-cape-bekkler.md`, approved behavior in the user's current reply.

## Global Constraints

- Only native visible demonstrations can authorize a cue; never read a future random command as visible information.
- The encounter point, actual Magus and doppel doll are different native actors.
- Preserve saves, settings, narration, other mods and unrelated untracked files.
- Work in the established task checkout `engine-accessibility-0332`, retaining its ignored native corpus/dependencies. The Opus5.5 max task is read-only; no concurrent source writer or new worktree is needed.

## Review Focus

- Consecutive identical poses must each receive a cue, without repeats for one instruction.
- Pre-demonstration choices and other fair games must stay silent.
- Callback failure, foreground loss, disable/unload and changed scene/context must not produce stale speech or alter original execution.
- An encounter resolved into a pickup must not keep the encounter label.
- Urgent cues must not queue behind narration; startup/result dialogue must remain readable.

## Task1: North Cape

Files: a scoped `Navigation/NorthCapeTargets.cs`, `FieldNavigationSource.cs`, and `NorthCapeTargetsTests.cs`.

- [x] Inspect the actual log actors, sprite sheet and installed Atel0406 branches.
- [x] Reproduce the generic sparkle label using a faithful actor/guard fixture; add resolved-item, unknown-state, hidden/removed and other-scene negatives.
- [x] Add the scoped native-name encounter label and appearance instruction; preserve actual character visibility/category and ordinary navigation.
- [x] Run the focused tests and record native evidence.

## Task2: Bekkler

Files: `Native/Capture/BekklerMimicCapture.cs`, `Mod/Minigames/BekklerMimicRuntime.cs`, `IntroTraceHookSet.cs`, `Mod.cs` and corresponding capture/runtime/hook tests. Exact event predicates follow the audit.

- [x] Audit Atel0071 visible demonstration, signal/input-window and completion state with matching native/Ghidra evidence.
- [x] Write and observe meaningful failing capture/runtime tests, including repeated identical cues, unknown/mismatched authority and lifecycle races.
- [x] Implement the minimum observer and short immediate cues, calling native execution once and preserving buttons/timers.
- [x] Verify hook integration and focused tests; separate observed facts from unlogged assumptions.

## Task3: Verify and deploy

- [x] Obtain one fresh Opus5.5 whole-change review and resolve important findings with regressions.
- [x] Run the full solution and appropriate package checks, bump version, write release/native audit notes and commit source.
- [ ] Build Release, replay the exact final DLLs in x86, package a tester ZIP, deploy with the game closed and verify installed hashes/protected files.
- [ ] Put the tester ZIP in Downloads and write a handoff with unperformed live-test limits. Do not publish without a release request.
