# Sunken Desert Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans for inline implementation. Steps use checkbox syntax for tracking.

**Goal:** Make native chest interactions reachable on Sunken Desert's shifting sand in both navigation modes.

**Architecture:** Reuse field capture and native pad input. Correct Run/floor lifecycle and interaction tracking; rate-limit background sand speech and retain explicit status reads.

**Tech Stack:** C#/.NET9, xUnit, Ghidra12.1.2, offline x86 Unicorn movement replay, existing package/deploy tools.

**Spec:** `docs/superpowers/specs/2026-10-25-sunken-desert.md`; behavior approved in the user's current reply.

## Global constraints

- Native timing, movement, collisions, encounters and player Confirm remain in control; no native state/preferences or synthesized toggle/Confirm edges.
- Shifting-sand policies are limited to verified Sunken Desert scenes159/160/161; chest interaction policy to160/161.
- Use the canonical task checkout with ignored corpus/dependencies. Claude Opus5.5 max owns a bounded read-only native audit; no concurrent source writer or new worktree is needed.
- Preserve saves, settings, narration, other mods, `.claude/` and `docs/claude-code-cli-setup.md`.

## Task1: Evidence and regression

Files: research under `artifacts/research/sunken-desert-20261025`; native map/chest fixture and focused Mod/Core tests.

- [x] Read supplied log, current handoff/source and native scene/script/map/chest records.
- [x] Fresh matching-executable Ghidra proof for175C90 and178FF0; consult web only as mechanical cross-check.
- [x] Complete bounded Opus5.5 native Run/floor audit and offline actual-code movement replay.
- [x] Reproduce the relevant failures with meaningful failing tests, retaining terrain/actor/scene assumptions explicitly.

## Task2: Movement, interaction and speech

Files: `FieldFloorMovement.cs`, `FieldFloorFeedback.cs`, `FieldNavigationRuntime.cs`, `FieldNavigationSource.cs`, a scoped Sunken Desert helper, `NavigationController.cs`/models and speech event policy only as required.

- [x] Correct native Run input validation and precise manual-toggle advice; refresh reset floor state at the accepted input boundary.
- [x] Preserve a usable native chest interaction window after automatic arrival, and repair replanning when the floor moves the player. Keep manual facing/Confirm under player control.
- [x] Add accurate shifting-sand labels and current repeat status; coalesce automatic messages and preserve route-speech priority.
- [x] Verify native motion cases, all reported room chest contacts, lifecycle/cancellation and ordinary Factory/Death Peak/rat interactions.

## Task3: Review, package and deploy

- [x] Obtain one bounded fresh Opus5.5 whole-change review and resolve important findings with regressions.
- [ ] Run full tests, version/hash contracts and Release build; commit source before final payload generation.
- [ ] Replay exact final DLLs in x86, verify tester ZIP, deploy with the game closed, check payload hashes and protected files.
- [ ] Put tester ZIP/notes in Downloads and write handoff with unperformed live gameplay/controller/speech limits. No public publication without a separate request.

## Rulings and evidence ledger

- Some logged controller-input suspensions precede native battles; preserving those handoffs is required, not a route bypass.
- Root is testing the interaction-maintenance policy before coding it. The supplied log has no live terrain-plane dump; shipped terrain fixtures and offline game-code emulation will be labelled accordingly.
- The native audit's open questions about Run return polarity and additive force are resolved by175A40 and1734D0. Ordinary controller angle masks do not emit weak-axis bits1000/2000; none are synthesized. The implementation uses normal eight-direction masks and native Dash only.
- Early interception of any Confirm window and hypothetical precision-speed probes were research spikes, not final evidence. Final movement verification must use the packaged implementation's actual pad result. Some contacts have recurring windows; held Confirm remains the player's native input.
- Manual cardinal-only replay initially reached8/11 contacts. Persisting failed-approach evidence across release pauses reached10/11; diagonal counter-drift advice plus retaining the different-floor approach reached11/11. No live/human timing inference is made.
- One initial full-suite run had a pre-existing voice-boundary timeout under load; its isolated rerun passed. The final complete run is recorded separately and must pass before deployment.
- Whole-change review reproduced one Critical capture gap and three Important speech issues. Corrections retain the bounded committed force after refused Desert probes, restore other-floor manual policy, bound idle replans, preserve native manual reach and give accurate Run-only feedback. All six earlier findings are resolved in the Opus5.5 delta review. Its two optional residuals were then covered by regressions: committed-push repeat remains readable, and unchanged qualitative drift advice is not repeatedly interrupted.
- Final oracle includes original175DA0 normalization, carried probe138, actual FieldFloorCapture and explicit synthetic ownership. Prior direct-snapshot results are superseded; native physics and owner validation are still not live gameplay proof.
