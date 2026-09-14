# Battle Accessibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Make native battle choices and visible outcomes readable, with 1/2/3 selecting a party member, H reading HP, M reading MP, and K repeating focus.

**Architecture:** Native capture validates battle-owned state and reads only native presentation data. Core owns deterministic narration; Mod owns battle lifetime, foreground shortcuts, hook callbacks and navigation suspension. Claude handles the remaining command/target audit and its capture implementation; Codex handles feedback, narration, runtime and deployment in the existing isolated worktree.

**Tech Stack:** C# 13, .NET 9, xUnit, Reloaded II x86 hooks, Prism, Ghidra and Capstone.

**Spec:** ../specs/2026-09-14-battle-accessibility-design.md

## Global Constraints

- Supported executable SHA256: 8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7.
- Preserve native commands, timing, cursor memory, saved state, navigation controls and tile footsteps.
- Ordinary enemy HP is hidden; do not expose it from memory.
- Native callbacks must call the original once, validate boundaries, and contain managed exceptions.
- Research uncertainty must be resolved from native evidence rather than guessed labels.
- Release is transactional and includes installed hash comparison; offline tests do not prove live coverage.

## Task 1: Battle narration and user shortcuts

Files: create Core/Battle/BattleAccessibility.cs, Core/Battle/BattleNarrator.cs, Mod/Battle/BattleKeyboard.cs and focused tests; modify Core/State/AccessibilityState.cs.

Interfaces: BattleStarted(long), BattleEnded(long), BattleFocusChanged(long,string,string), BattleFeedbackPresented(long,long,string), BattleInspectionRequested(long,string) derive from BattleAccessibilityEvent. BattleKeyboard.Poll returns BattleCommand values SelectFirst, SelectSecond, SelectThird, ReadHp, ReadMp, Repeat.

- [x] Write and run failing narration tests for battle lifetime, same-text distinct outcomes, repeated focus suppression and explicit repeats; keyboard tests cover key edges, background/modifier suppression and rearming.
```csharp
Assert.Single(state.Apply(new BattleStarted(1)));
Assert.Single(state.Apply(new BattleFocusChanged(1, "command:0:0", "Crono. Attack")));
Assert.Empty(state.Apply(new BattleFocusChanged(1, "command:0:0", "Crono. Attack")));
Assert.Single(state.Apply(new BattleFeedbackPresented(1, 1, "Crono takes 8 damage.")));
Assert.Single(state.Apply(new BattleFeedbackPresented(1, 2, "Crono takes 8 damage.")));
```
- [x] Implement events/narration and keyboard, run Core and focused Mod tests. Input checks use NavigationKeyboard's existing foreground and key functions.

## Task 2: Native battle snapshot

Files: create Native/Capture/BattleCapture.cs, BattleSnapshot.cs and Native.Tests/Capture/BattleCaptureTests.cs; update battle-interface-native-audit.md.

Interfaces: BattleCapture(IReadableMemory).Capture(nuint imageBase,nuint menu) returns BattleSnapshot?; BattleSnapshot exposes Party, FocusIdentity, FocusText, BattlerNames. BattlePartyMemberSnapshot exposes Slot, Name, Hp, MaximumHp, Mp, MaximumMp, Status. Missing/transition snapshots return null; valid animation phases may have null focus.

- [x] Verify remaining group highlighting, status display and modern list focus against Ghidra and disassembly.
- [x] Write failing bounded-memory fixtures using independent native addresses for ready commands, Tech/Item choices, targets, party slots and corrupt state.
```csharp
Assert.Equal("Crono", capture.Capture(image, menu)!.Party[0].Name);
Assert.Null(capture.Capture(image, unrelatedObject));
```
- [x] Implement presentation-backed capture and pass focused Native tests; retain audit evidence and unresolved live coverage explicitly.

## Task 3: Displayed messages and popup feedback

Files: create Native/Capture/BattleFeedbackCapture.cs and its tests; add hook contracts/delegates in Native/Hooks/HookContract.cs and GameVersionCatalog.cs with ABI tests.

Interfaces: BattleFeedbackCapture(IReadableMemory).ReadMessage(nuint menu) returns string?; ReadPopups(nuint menu) returns visible slot/text/color records. Mod assigns popup presentation serials on native writer calls and consumes each visible serial once.

- [x] Write failing tests for finished Label text, invalid label layouts, invisible popups and x86 bounds.
```csharp
Assert.Empty(capture.ReadPopups(menu)); // writer data exists, but display flag is zero
Assert.Equal("8", Assert.Single(capture.ReadPopups(visibleMenu)).Text);
```
- [x] Implement guarded reads and audit every native hook prologue, return cleanup and caller arguments.
- [x] Verify presentation kinds for damage/recovery and test overkill and consecutive identical hits independently of party HP deltas. The HP/MP producer tie remains unresolved: unit names are deliberately omitted from popup speech; H/M use proven HUD fields.

## Task 4: Runtime lifetime, inspection and hook composition

Files: create Mod/Battle/BattleRuntime.cs, BattleHookSet.cs and tests; modify Mod.cs and NavigationDispatcher.cs; update CompleteCompositionTests.cs.

Interfaces: BattleRuntime accepts native snapshots and publishes the Core events. It exposes active state, lifecycle entry/exit, Tick, message/popup observations and user command handling. BattleHookSet is an IHookActivationObserver whose registrations are installed with the existing complete composition.

- [x] Write failing runtime tests for selecting member 2 then H/M, missing slots, current value refresh, ended battles, reset on re-entry, and callbacks that throw.
```csharp
runtime.Handle(BattleCommand.SelectSecond);
runtime.Handle(BattleCommand.ReadHp);
Assert.Contains(events, e => e is BattleInspectionRequested r && r.Text == "Marle. HP 50 of 80.");
```
- [x] Implement native lifecycle/update hooks, validated original-call forwarding, foreground keyboard polling, deduplication and state invalidation.
- [x] Suspend navigation/footsteps on battle entry and while active; release after actual teardown, preserving discoveries. Register every new hook/participant and run focused Mod tests.

## Task 5: Review, verification and deployment

Files: update README.md, ModConfig.json, manifest/package tests and release evidence under artifacts/research/battle-0317.

- [x] Review implementation against all seven spec acceptance checks and obtain Claude's bounded code review.
- [x] Run full solution tests, exact hook-byte verification and git diff --check; fix any demonstrated failures and rerun affected checks.
- [x] Bump version to 0.3.17, commit the verified source, package using tools/Package-Mod.ps1 and deploy using tools/Deploy-Mod.ps1 while the game is closed.
- [x] Compare deployed mod/loader/shared-hook hashes, retain transactional backup and merge the verified branch into the clean main checkout without changing unrelated files.
- [x] Report installed version, controls, test evidence and the remaining live battle test. Do not call a feature live-verified from synthetic fixtures.

Final offline verification: 1,304 Release tests (132 Core, 603 Native, 561 Mod, 8 Prism), 134 exact hook signatures. Initial suite failures were stale 0.3.16 manifest expectations; the version, description and canonical LF SHA256 are updated. Claude completed the bounded review; native semantics that remain unproven are not assigned in speech. Live encounter checks remain pending.

Release completed: code commit `72803f41fd7596ef0d061277a5e3e8713e8845df`; installed 0.3.17; 25 deployment checks passed; 28 mod files, 45 loader/shared-hook files and both launchers match; backup `Accessibility/Backups/20260914-030434`. Source integrated into main. Live battle testing remains pending, as reported to the player.
