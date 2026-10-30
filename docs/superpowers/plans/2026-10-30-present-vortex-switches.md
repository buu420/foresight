# Present Vortex Switches Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans inline, with authorized
> read-only Claude Opus5.5 max native research and whole-change review. User scope
> confirmation and durable public-release authorization already cover execution.

**Goal:** Reach and identify native Volcano switches through the actual map sections.

**Architecture:** Add scoped present-vortex control contracts and story-route
selection alongside existing navigation helpers. All movement points come from
current captured terrain and native connection data. The switch's native choice
menu and scene/terrain changes remain untouched.

**Tech Stack:** C#/.NET9 Windows x86, xUnit, native resources/Ghidra, private
Unicorn movement checks and existing packaging/deployment/AMM tools.

**Spec:** ../specs/2026-10-30-present-vortex-switches.md

## Global Constraints

- No live game/Steam/input/audio session. No native position, facing or state writes.
- Preserve native puzzle choices, locks, timers, battles and Confirm.
- Identify current visible controls; do not reveal hidden outcomes or rewards.
- Distinguish exact capture, partial logged facts and static-derived states.
- Preserve saves, settings, narration, unrelated mods and the two user source files.

## Review Focus

- A goal on a separate ledge must use the correct entrance, without an out-and-back loop.
- Confirm-dependent switch consequences must not become automatic exit terminals.
- Unknown/disabled native watcher state must fail closed without hiding a usable verified control.
- Narrow passage goals must admit the player's full native body and a safe arrival box.
- Unopened chests remain reachable; collected records and mandatory encounters retain native behavior.

### Task1: Controls and route reproduction

**Files:** create `PresentVortexTargets.cs`, tests `PresentVortexNavigationTests.cs`
and `present-vortex-0387.json`; integrate in `FieldNavigationSource.cs`.

**Interfaces:** reuse `FieldNavigationSource.Build`, `NavigationController`, native
catalog region IDs and current-frame approach points; no new input API.

- [x] Decode the five route captures and reconstruct explicitly marked neighboring states/treasures.
- [x] Write failing tests for switch identity, native confirmation reach, inactive watcher/state cases and false exits.
- [x] Implement only the native-proven control contract; pass focused tests.

### Task2: Section-aware Story Events and native passages

**Files:** create `PresentVortexRoutes.cs`, integrate in `FullStoryTargets.cs`;
extend the same tests with native arrival/topology cases and chest routes.

**Interfaces:** consume the filtered current targets and the exact first/second
native switch goals; produce a `FullStoryObjective` for the actual next local leg.

- [x] Prove the route to the first ledge and the next switch from native scene transfers.
- [x] Add failing Story/manual/automatic tests for each leg and narrow doorway; preserve optional branches.
- [x] Implement the scoped route and sampled-standing-point correction; pass native walking replays.
- [x] Audit real treasure records and the logged614 exit; repair only demonstrated defects.

### Task3: Review, validation and0.3.87 release

- [ ] Read-only Opus5.5 max review; verify and resolve findings.
- [ ] Full solution, package/AMM, exact packaged x86 and original native movement checks.
- [ ] Build/version/package, protect files, deploy and compare every installed payload byte.
- [ ] Publish GitHub beta and AMM catalog, anonymously verify assets/index, preserve history.
- [ ] Record a dated handoff, closure receipt and live-test limits.

## Execution Ledger

- Base ca90e13ffa3b49def4e85e0f8d68091e35d1b2b7, prior public0.3.86.
- User confirmed: Yes, fix the switches and their routes. Public release authorized.
- Log SHA a7ebca873e050b13643286a69698bbda219a25a028615dd69d082367987fed9c,
  1244302 bytes/4588 lines; texture diagnostic confirms0.3.86.
- Native EXE/resources hashes freshly match the retained Ghidra corpus.
- Ruling: desktop worktree tool rejects this nested repository; use the established
  ignored `.worktrees/present-vortex-0387` Git worktree instead. Native tool refusal retained in tool history.
- Baseline focused vortex/bonus suite passes238 tests. The first no-restore command
  ran no tests in the fresh checkout; the actual restored run is the baseline proof.
- Exact608 capture confirms disconnected switch ledge and a separate unrouteable
  entrance opening. Current script-switch region is story-only and its confirmation
  consequences are misrepresented as warp exits. Native input/choice semantics
  and the full neighboring route remain under investigation before implementation.
- Implemented the verified608/613 controls, suppressed their confirmation effects
  as exits, and selected608A->610->614->615->608B for the first ledge.609 is
  connected after the first switch; existing routing reaches611 then613.
- Native24 writes Local3, while the script checks Local0. All four facings are
  accepted; no synthetic facing requirement or Confirm is introduced.
- Initial control/routing regressions:20 failed/7 passed before the fix;27 passed
  after it. A preliminary614 gait adjustment passed its checks but review proved
  that native Run can follow the corridor. That candidate and its walking-speed
  advice were removed before deployment/publication.
- Final focused suite:81 pass, including later-switch Story routes, both modes,
  all six native chest records, unknown/removed watcher and spent-switch cases.
  The fixture has five exact captures and12 explicitly static-derived states.
- Sixty-eight original-x86 terrain traces pass in private Unicorn memory, with
  native treasure grids and unmodified walking/running speed. Actor scripts,
  encounters, Confirm and room transfers are not executed.
- The exact614 Run32 capture fails in both modes before the steering repair.
  A narrowly enabled32-unit shortfall policy now changes to the graph-proven
  next leg only after an observed stall at a same-layer ordinary corner. It does
  not widen terminal or interaction arrival. Bounded next-leg steering avoids
  correcting back into the refused corner. Plan/control-loss/Stop reset authority.
- The first steering prototype still corrected back after one frame; the native
  trace exposed a later stall at9097,14293. Bounded leg handling resolves it.
  Tests also cover no opt-in, manual waiting, control loss and scene coherence.
- Earlier pre-review full suites pass5699 then5705 tests for the private gait
  candidate. The final steering build requires its own full/exact-payload checks.
- One overlapping build attempt met the active testhost's DLL lock; it changed
  no implementation. Sequential retry passes. Initial private chest-replay
  receipts incorrectly demanded a spoken Confirm/32-unit point box; corrected
  receipts use native treasure reach/facings and arrival. No chest change was
  made to satisfy that receipt error.
