# Temporal Research Lab Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans task by task.

**Goal:** Repair verified Temporal Research Lab passages and progression in both navigation modes and publish0.3.85.

**Architecture:** Retain native scene routing and collision authority. Extend scoped
door metadata/contact approaches, exclude capture transfers from passage targets,
follow native Story flags, and debounce current belt feedback.

**Tech Stack:** C#/.NET9, xUnit, retained Ghidra/ATEL, private x86 Unicorn replay,
PowerShell packaging, GitHub releases and AMM author CLI.

**Spec:** `docs/superpowers/specs/2026-10-29-temporal-lab-design.md`.

## Global constraints

- Supported EXE SHA2568fe9d75e4cdc279645c5bc932fc163fd67147255fc0c673ac45bbf0a6d2e00d7.
- No native state/position/facing writes, automatic Confirm/combat, timer or lock changes.
- Preserve native treasures, user saves/settings/narration and unrelated mods/catalog history.
- Claude collaboration uses actual Opus5.5/max; Root owns implementation.
- User's explicit scope and release approval cover routine design and reversible worktree choices; no further approval gate.

## Tasks

1. [x] Retain exact617/618 snapshots as test fixtures with provenance. Establish
   existing focused baseline. Add failing capture regressions for omitted touch
   doors, actual exit boundaries, false capture exits and cross-band routes.
   Extend `TemporalLabTargets`, `FieldTerrainGraph`, `FieldNavigationSource` and
   the router's region filter only where the native scripts prove it. Verify both
   modes reach the current door contact and actual exit; maintain call/guard gates.
2. [ ] Add failing tests for current native security, rescue, registration, release
   and onward stages. Verify retained scripts including background continuation.
   Correct `BonusStoryObjectives` and useful lab room/control labels without
   puzzle answers. Retain optional side-room/chest connections and native Confirm.
3. [ ] Add failing rapid belt-boundary/direction/Repeat tests. Scope debounce to
   the lab, retain current force immediately and keep native32-speed direction
   restrictions. Verify existing Factory/Desert/Death Peak/Cliffs feedback.
4. [ ] Review full changes with Opus5.5/max, resolve supported findings, update
   docs/version/release contract pins. Run full suite and exact packaged x86 checks,
   native offline checks, package/deploy/preservation validation. Publish standalone,
   AMM and catalog; verify anonymous assets/index and closing preservation.

## Review focus

- Off-camera door binding0 is not proof of contact readiness; fresh near-camera state is needed.
- E2A unknown, native locks and busy/disabled door handlers cannot authorize an opening.
- Removing false robot exits must not disable the real capture hazard or label it as a doorway.
- A goal in exit tile0 is not reached while still standing in tile1.
- E2A0x1A after release must not point back at an inactive console; chest branches stay available.

## Execution ledger

-2026-10-29: user confirmed scope. Design preserves existing controls and native hazards.
- Worktree tool rejected the nested repository as outside its exact workspace roots;
  used ignored project-local `.worktrees/temporal-lab-0385`, branch`fix/temporal-lab-0385`.
- No live game/Steam/input/audio launch authorized or needed for offline validation.
- Task1: existing112 focused checks passed. New capture regressions failed12/17
  before implementation. All17 now pass, including actual exit-boundary identity,
  both modes, native door guards and the side-room branch only after release.
  Ruling: original E2A0 side-room positive was corrected to a derived release
  fixture after native scripts proved a real barrier; no gameplay barrier removed.
  Native618 radius224 requires a standable door contact320 below actor16.
