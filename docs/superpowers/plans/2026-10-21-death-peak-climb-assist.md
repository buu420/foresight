# Death Peak Climbing Assist Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans for inline implementation. Steps use checkbox syntax for tracking.

**Goal:** Provide the user-approved, explicitly started climb between Death Peak's wind shelters, departing on observed gust-end cues.

**Architecture:** Reuse the live field graph, ordinary navigation controller, precise shelter holds and native Run adjustment. A small Death Peak coordinator projects the current stage into a single selectable assistance destination; automatic continuation is allowed only from an already validated hold. Existing manual destinations remain available.

**Tech Stack:** C# / .NET 9, x86 native capture, xUnit, existing PowerShell/Python packaging.

**Spec:** The user's October 21 approval of the pending climbing-assist question; native behavior is documented in `docs/native-audits/2026-10-20-death-peak-hold.md`, with current evidence in `artifacts/research/crash-and-death-peak-20261021/root-findings.md`.

## Global Constraints

- Scene 244 only; explicit navigation selection starts assistance; no automatic restart after cancellation or knockback.
- Use normal native direction/Run input. Never synthesize Confirm, teleport or change hazards/physics.
- Depart a shelter only after a fresh observed gust-to-calm transition while holding validated cover and with running available. Unknown wind/Run pauses departure while a separately verified hold remains; unknown control/contact cancels input.
- Native control, foreground, input freshness, manual takeover and scene/context gates remain authoritative.
- Unseen guide geometry must not be called visible or discovered. A doll that still needs interaction stops the route for player Confirm.
- Crash diagnosis remains separate; no speculative render or executable patches.

## Review Focus

- Arrival during an existing calm period must wait for a complete subsequent gust-end cue.
- A menu or manual command at a gust-end cue must not cause a late departure into the next gust. An explicit world-map arm may survive native loading but must be consumed by one field attempt.
- Any missing shelter proof, scene change or lost movement authority must release input and never silently resume. Unknown wind/Run while holding must clear cue history and retain only fresh ordinary shelter proof.
- A reloaded tree and a just-transformed tree with retained calm floor must both have a stable, graph-proven holding point.
- Native run modes and player Dash must keep their existing semantics; no action or Confirm input may be introduced.

## Task 1: Verified selectable climb stages

Files: `DeathPeakTargets.cs`, `FieldNavigationSource.cs`, `NavigationModels.cs`; tests: `DeathPeakClimbAssistTests.cs` and existing Death Peak fixtures.

- [x] Write and run a failing real-source test: entry viewport has a clearly named first Exits assistance target without claiming unseen shelters were seen.
- [x] Add fresh automatic-stage metadata, construct lower/upper trunk stages from the live graph and bind the final stage to the existing north exit. Stop at the doll when its native interaction is required.
- [x] Verify absent/unknown prerequisites, scene/layer/collision changes and ordinary shelter visibility behavior.

## Task 2: Wind-aware continuation through existing navigation

Files: new `DeathPeakClimbAssist.cs`, `FieldNavigationRuntime.cs`, `NavigationController.cs`, `WorldNavigationSource.cs`, `Mod.cs`; tests: new assistance/arming runtime tests and existing hold/runtime tests.

The safe preparation option is a non-spatial world-map note, gated by an available native Death Peak entrance and lower protection. Explicitly selecting it arms a single future field entry, with no world movement or Confirm. Native loading may preserve the pending option; cancellation or returning to the world after an attempt cannot restart it.

- [x] Write and observe failing tests for lower hold, complete gust-end departure, upper hold and final departure, plus no departure during already calm wind.
- [x] Implement `NavigationController.ContinueAutomaticRoute(NavigationFrame, long)` with fresh existing-hold authority; add a scoped coordinator and fresh floor-state supplier to the runtime.
- [x] Test cancellation, menus, unknown/stale/mismatched wind, lost proof, manual guidance, run settings, doll interaction and no silent restart.
- [x] Check the actual installed-map route geometry and native movement margin; keep actual-controller/wind-contact verification explicitly pending for the tester.

## Task 3: Review, build and deploy

Files: native audit/release notes, version and package metadata; existing packaging tools.

- [x] Run focused tests then the full solution suite; review the complete diff, with independent Opus 5.5 geometry/review evidence. All 4,460 tests passed. Entry-floor and cancellation-speech regressions cover the arming review findings.
- [ ] Build Release, produce and verify the standalone tester ZIP, include the separately verified collector as a sidecar if still absent from the main archive.
- [ ] Deploy owned mod files and dependencies while the game is closed; verify payload hashes and preserve current saves/config/narration and launch files.
- [ ] Record exact version, ZIP path, backup, checks and unperformed live tests in a handoff. Report tester instructions succinctly.

Execution is authorized by the user's accepted behavior confirmation. Implementation remains in the established installed-game feature checkout so its native assets, test fixtures and deployment paths stay consistent; unrelated untracked files are excluded.
