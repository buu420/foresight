# Field Navigation Implementation Plan

> **For agentic workers:** Use the executing-plans workflow for local integration.
> Claude owns the explicitly delegated native capture audit and capture tests.

**Goal:** Provide the approved category keys, spoken route guidance, and automatic
walking using the game's ordinary directional input.

**Architecture:** Core consumes immutable target snapshots and a graph whose edges
are verified passable. Native capture resolves live state with bounded reads; Mod
samples navigation keys at the audited movement-input boundary and outputs speech.

**Tech Stack:** C# 13, .NET 9, Reloaded x86 hooks, Prism, Ghidra 12.1.2.

**Spec:** `../specs/2026-09-10-field-navigation.md`.

## Global constraints

- Supported EXE SHA256: `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
- No Computer Use, live debugger attachment, game launch/close, or agent game input.
- No hidden destinations, guessed names, position writes, or unknown passability.
- P supplies only a one-read native direction; manual input and loss of control stop it.
- Keep earlier user files and menu/intro work intact. Deploy only after fresh tests.

## 1. Native evidence and capture

- [x] Verify baseline: `dotnet test ChronoTriggerAccessibility.slnx -c Release` (802 passed).
- [x] Read CTViewer PC scene/map/exit formats and audit native loaders DAB90, DB1F0, 179F90.
- [x] Claude: implement `FieldNavigationCapture.cs` with proven player coordinates,
  actor visibility/class, scene ownership and bounded memory reads, plus malformed
  memory and layout tests in `FieldNavigationCaptureTests.cs`.
- [x] Root: verify capture constants and resolve live collision/exit/current-map
  ownership from the native consumers before building the production graph.

## 2. Selection, paths and guidance

Files: `Core/Navigation/NavigationModels.cs`, `NavigationPathfinder.cs`,
`NavigationController.cs`, and matching Core tests.

Interfaces: `NavigationPoint(int X, int Y, int Layer)` uses frame-scaled coordinates;
the production frame specifies 256 native fixed-point units per 16-pixel tile.
`INavigationGraph.Neighbours(point)` returns only traversable directed edges;
`NavigationTarget` carries an ID, label, category, visible/discovered state and
audited approach points. `NavigationFrame` carries scene identity, player, graph,
targets and a control gate. `NavigationController.Handle(command, frame, now)`
and `Update(frame, now)` return speech and one-read requested movement.

- [x] RED: verify category/target wrapping, hidden-target exclusion, explicit repeat,
  routing around a wall, layer isolation, bounded search, manual cancellation,
  scene/control loss, target disappearance, arrival, and stuck timeout.
- [x] GREEN: add bounded path search and deterministic controller. Use only graph
  edges, cancel before any category/target switch, keep stopped movement at zero,
  and never restart without an explicit I/P command.
- [x] Run Core Release tests (72 passed as part of the complete suite).

## 3. Runtime adapter and input

Files: `Mod/Navigation/FieldNavigationHookSet.cs`, `FieldNavigationRuntime.cs`,
`NavigationKeyboard.cs`; native graph/capture helpers; `Mod.cs`,
`GameVersionCatalog.cs`, `HookContract.cs` and relevant tests.

- [x] Confirm complete instruction lengths, ABI and saved-register stack offsets
  for the hook after the field input getter at RVA175A85; intercept its returned
  mask only after the game's mode and input-acceptance checks have succeeded.
- [x] RED: runtime tests check pad preservation, manual control, and inactive-state
  stops; key tests check U/O, J/L, K, I, P edges and foreground gates. Separate
  compiled-assembly emulation checks arguments, flags, registers and stack in 48
  cases. Ghidra and exact call-site bytes establish the original call boundary.
- [x] GREEN: wire capture/controller/Prism into one hook owner. Read current
  snapshots synchronously; do not retain native pointers across scene lifetimes.
- [x] Test all native hook contracts against the installed executable: 122 matched.

## 4. Review and delivery

- [x] Run the complete Release suite: 896 passed, zero failed or skipped.
- [x] Run `git diff --check`, including newly added files.
- [x] Review final native state/control gates and coverage limitations with Claude.
  Claude reviewed the control and routing path; root reviewed the native map,
  viewport, target selection, appearance labels, and composition separately.
- [x] Package, verify all target paths are contained and non-reparse, and deploy
  when the game is closed. Verify installed manifest, DLL hashes, and dependencies.
  Installed 0.3.0; deployment verification passed 25 checks. Compared all 27 mod
  payload files and 45 loader/shared-hooks files with the reviewed package sources.
- [x] Document exactly which live gameplay checks remain for the user. Do not
  claim game-wide navigation or live movement verification from harness tests.
