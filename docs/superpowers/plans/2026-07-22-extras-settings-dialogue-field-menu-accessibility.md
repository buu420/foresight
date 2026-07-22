# Chrono Trigger Extras, Settings, Dialogue, and Field Menu Accessibility Implementation Plan

> **For Codex:** REQUIRED SUB-SKILL: execute this plan with `superpowers:subagent-driven-development`, a fresh implementer and task review for every task. Every production behavior follows `superpowers:test-driven-development`; use `superpowers:verification-before-completion` before completion claims.

**Goal:** Extend the exact-build Reloaded-II/Prism mod through the Extras hub and Ending Log, title and in-game Settings, ordinary field dialogue and choices, and the visible top-level in-game menu/status area.

**Design:** `docs/superpowers/specs/2026-07-22-extras-settings-dialogue-field-menu-accessibility-design.md`

**Supported executable:** Steam `Chrono Trigger.exe` 2.0.0.1, PE32/x86, SHA-256 `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`, preferred image base `0x00400000`.

**Test SDK:**

```powershell
$dotnet = 'C:\Program Files (x86)\Steam\steamapps\common\Yu-Gi-Oh! Legacy of the Duelist Link Evolution\YuGiOhAccessibility\.worktrees\accessibility-native-uia\.tools\dotnet\dotnet.exe'
```

## Global Constraints

- Describe only committed information a sighted player can currently see, using localized strings captured from the game. Never infer a speaker, hard-code English menu content, speak future dialogue pages, expose a locked ending title/requirement, or interpret an unmapped save field.
- Missing required interactive information is a coverage failure, not a silent fallback. Validate the complete snapshot before publishing any partial narration. The affected menu/dialogue family faults after its first failure and reports through Prism, the log, and the independent accessible fatal-error sink.
- Preserve the existing exact executable hash check and verify every new hook's RVA, executable section, delegate type, calling convention, and stable byte prefix before atomic activation. Every detour calls its native original exactly once and preserves its return value and x86 stack cleanup.
- One native address has one detour. Shared localized-text, focus, custom-button, and control-binder hooks fan out to ordered observers after the original call. One observer's exception must not prevent later observers or alter native behavior.
- Native readers validate owner/vtable chains before child pointers; vector differences must be ordered, stride-aligned, bounded, and overflow-safe. Strings must be readable, valid bounded UTF-8 MSVC strings. Invalid required state fails closed.
- Hook sets use an activation epoch. Disable increments the epoch and clears owners, builder scopes, caches, managers, and snapshots. Callbacks publish post-original only when the captured epoch is still current. Builder scopes are thread-local, non-nestable, and disposed in `finally`.
- Dialogue is line-by-line from `MsgWindow` state: no progressive-character chatter, no future vector entries, no repeat on fast reveal, later-page repeated text is allowed, choice activation is classified only at the audited confirm caller, and close fully resets identity.
- The accessible scope is complete for the Extras hub, Ending Log/detail controls, main title/in-game Settings rows and values/default confirmation, ordinary field dialogue/choices, and the top field-menu rows plus visible status. Movies/Illustrations/Sound lists and playback, ending replay, License pagination, gamepad/keyboard rebinding, and deep Item/Tech/Equipment/Formation/Save subpages must announce an explicit localized selection plus coverage boundary and Cancel/Back return instruction; they must never go silent.
- Do not add or install a third-party dependency. Keep Reloaded-II, Reloaded.Hooks, Prism 0.17.3 x86, Ultimate ASI Loader, and the existing x86 .NET runtime. Read any dependency README before changing its deployment.
- Work only in the feature worktree. Use `apply_patch` for source/document edits. Preserve unrelated user files and saves. Tests must be warning-free.

## Exact Native Hook Catalog Additions

Add the following exact-build entries. Prefixes are hexadecimal bytes from the installed supported executable; tests must compare every byte.

| Hook | RVA | Stable prefix |
|---|---:|---|
| `GalleryScene::switchNode` | `0x2A52B0` | `55 8B EC 6A FF 68 38 AB 77 00 64 A1 00 00 00 00 50 83 EC 64 A1 D0 A0 7F` |
| Extras hub callback | `0x1DC610` | `55 8B EC 83 E4 F8 8B 45 08 83 EC 08 56 8B F1 57 83 E8 00 0F 84 9E 00 00` |
| Ending Log callback | `0x1D4850` | `55 8B EC 8B 45 08 56 8B F1 57 83 E8 00 0F 84 92 00 00 00 83 E8 01 74 35` |
| Ending Detail callback | `0x1D35A0` | `55 8B EC 8B 45 08 56 8B F1 83 E8 00 74 59 83 E8 01 74 40 83 E8 01 0F 85` |
| `MenuNodeConfigSteam` constructor | `0x1ECB00` | `55 8B EC 6A FF 68 10 44 76 00 64 A1 00 00 00 00 50 56 A1 D0 A0 7F 00 33 C5 50 8D 45 F4 64 A3 00` |
| `MenuNodeConfigSteam` builder | `0x1ED020` | `55 8B EC 6A FF 68 41 02 77 00 64 A1 00 00 00 00 50 81 EC 64 0E 00 00 A1 D0 A0 7F 00 33 C5 89 45` |
| `MenuNodeConfigSteam` destructor | `0x1EC970` | `55 8B EC 6A FF 68 9E C6 76 00 64 A1 00 00 00 00 50 56 57 A1 D0 A0 7F 00 33 C5 50 8D 45 F4 64 A3` |
| Shared Settings value mutation | `0x1E3980` | `55 8B EC 53 8B D9 56 69 75 0C 98 00 00 00` |
| `MsgWindow` open/parser | `0x195B40` | `55 8B EC 6A FF 68 CF 9C 76 00 64 A1 00 00 00 00 50 83 EC 30 A1 D0 A0 7F 00 33 C5 89 45 EC 53 56` |
| `MsgWindow` update | `0x197530` | `55 8B EC 6A FF 68 A8 9F 76 00 64 A1 00 00 00 00 50 83 EC 54 A1 D0 A0 7F 00 33 C5 89 45 F0 53 56` |
| `MsgWindow` close | `0x195C70` | `55 8B EC 6A FF 68 17 9D 76 00 64 A1 00 00 00 00 50 83 EC 48 A1 D0 A0 7F 00 33 C5 89 45 EC 53 56` |
| Classic top-menu builder | `0x1D0560` | `55 8B EC 6A FF 68 3E CE 76 00 64 A1 00 00 00 00 50 81 EC 94 01 00 00 A1 D0 A0 7F 00 33 C5 89 45` |
| Touch/mouse top-menu builder | `0x221660` | `55 8B EC 6A FF 68 16 48 77 00 64 A1 00 00 00 00 50 81 EC EC 00 00 00 A1 D0 A0 7F 00 33 C5 89 45` |
| Menu UTF-8 text-label factory | `0x2400B0` | `55 8B EC 83 E4 F8 83 EC 0C 8B C2 8B 55 0C 53 56 8B D9 8B C8 57 E8 C6 F9 DC FF 6A 00 83 EC 08 8D` |
| StatusBar text-format scope | `0x22F160` | `55 8B EC 6A FF 68 2F 5D 77 00 64 A1 00 00 00 00 50 81 EC 24 01 00 00 A1 D0 A0 7F 00 33 C5 89 45` |
| StatusBar UTF-16 glyph renderer | `0x22E080` | `55 8B EC 6A FF 68 C1 C5 76 00 64 A1 00 00 00 00 50 83 EC 58 A1 D0 A0 7F 00 33 C5 89 45 EC 53 56` |
| `StatusBar` destructor | `0x22E650` | `55 8B EC 6A FF 68 EE E6 76 00 64 A1 00 00 00 00 50 51 56 57 A1 D0 A0 7F 00 33 C5 50 8D 45 F4 64` |
| Classic top-menu deleting destructor | `0x1D0470` | `55 8B EC 6A FF 68 C7 5B 76 00 64 A1 00 00 00 00 50 56 57 A1 D0 A0 7F 00 33 C5 50 8D 45 F4 64 A3` |
| Touch top/Ending Detail deleting destructor | `0x1D2690` | `55 8B EC 6A FF 68 C7 5B 76 00 64 A1 00 00 00 00 50 56 57 A1 D0 A0 7F 00 33 C5 50 8D 45 F4 64 A3` |

Retain and share the existing `TextManager::getMsg` `0x1B9110`, `nsMenu` focus setter `0x1DD3E0`, CustomButton constructor `0x1D2160`, and control binder `0x1DD260` contracts. The Gallery switch is thiscall with two stack words, native `ret 8`, and a 32-bit zero return that callers consume. The shared Settings mutation is `bool __thiscall(config, page, row, proposedIndex)` and publishes only after the original returns true. Both top builders are thiscall with one raw stack word and native `ret 4`. The text-label factory is caller-clean fastcall with `ECX=position`, `EDX=MSVC UTF-8 string`, followed by anchor and font-size stack arguments, returning the original Label pointer. The glyph renderer is caller-clean fastcall with `ECX=glyph output`, `EDX=MSVC UTF-16 string`, and one stack output argument; it ends in plain `ret`, returns the original glyph-output pointer, and is accepted only under the validated StatusBar scope and return RVA `0x22F3BD`. `MsgWindow` close receives the caller's dummy stack word so the managed delegate preserves native `ret 4`; the audited choice-confirm return address is image-base plus RVA `0x19768F`, from call bytes `E8 E1 E5 FF FF` at RVA `0x19768A`.

The UTF-8 factory accepts only these return RVAs while the corresponding validated top-builder scope is active: classic time `0x1D0AAA`, currency `0x1D0B54`, row caption `0x1D179E`, and member name `0x23B1D0`; touch time `0x221A4F`, currency `0x221B1C`, row caption `0x2221C6`, and member-name branches `0x23A9E6`/`0x23AE3A`; shared party-stat calls `0x23973B`, `0x239891`, `0x239914`, `0x2399AA`, and `0x239A73`. Preserve branch and construction order rather than globally deduplicating equal strings.

## Task 1: Add Pure Menu and Dialogue Semantics

**Files:**

- Create `src/ChronoTriggerAccessibility.Core/Menus/MenuAccessibility.cs`
- Create `src/ChronoTriggerAccessibility.Core/Menus/MenuNarrator.cs`
- Create `src/ChronoTriggerAccessibility.Core/Dialogue/DialogueAccessibility.cs`
- Create `src/ChronoTriggerAccessibility.Core/Dialogue/DialogueNarrator.cs`
- Modify `src/ChronoTriggerAccessibility.Core/State/AccessibilityState.cs`
- Create `tests/ChronoTriggerAccessibility.Core.Tests/Menus/MenuNarratorTests.cs`
- Create `tests/ChronoTriggerAccessibility.Core.Tests/Dialogue/DialogueNarratorTests.cs`
- Modify `tests/ChronoTriggerAccessibility.Core.Tests/State/AccessibilityStateTests.cs`

**Step 1 — RED:** Write tests first for menu entry, focused label/value/position/help, disabled state, visible status queue order, activation, exit/reset, explicit unsupported boundary, confirmation prompt/choice focus, duplicate suppression, and coverage failure before partial output. Dialogue tests cover line queueing, same text suppressed within one line identity, same text spoken on a later page, choices announced in order then focused choice, `-1` no-focus handling, focus changes, activation, close/reopen reset, invalid counts/blank strings, and one-shot family faulting. Verify defensive copies of every list.

```powershell
& $dotnet test .\tests\ChronoTriggerAccessibility.Core.Tests\ChronoTriggerAccessibility.Core.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~MenuNarratorTests|FullyQualifiedName~DialogueNarratorTests'
```

Expected RED: compilation fails because the menu/dialogue event and narrator types do not exist.

**Step 2 — GREEN:** Add immutable event records and separate stateful narrators. A menu focus formats only present fields as `Label: Value, n of count. Help.` and appends `unavailable` when native state says so. Menu entry interrupts; status details queue. Dialogue lines queue; choice presentation interrupts once, queues all choices, then selection; later choice movement interrupts only the new selection. Validate entire payloads before creating announcements. Integrate both event bases into `AccessibilityState` without changing startup/New Game behavior.

```powershell
& $dotnet test .\tests\ChronoTriggerAccessibility.Core.Tests\ChronoTriggerAccessibility.Core.Tests.csproj -c Release --no-restore
```

Expected GREEN: all Core tests pass, zero warnings.

**Step 3 — review and commit:** Check exact prose, interruption priorities, fault isolation, duplicate/reset identities, and list snapshots. Run `git diff --check`, then commit:

```powershell
git add src/ChronoTriggerAccessibility.Core tests/ChronoTriggerAccessibility.Core.Tests
git commit -m 'feat: add menu and dialogue narration semantics'
```

## Task 2: Add Bounded String-Vector and Dialogue Capture

**Files:**

- Create `src/ChronoTriggerAccessibility.Native/Memory/MsvcStringVectorReader.cs`
- Create `src/ChronoTriggerAccessibility.Native/Capture/DialogueCapture.cs`
- Create `tests/ChronoTriggerAccessibility.Native.Tests/Memory/MsvcStringVectorReaderTests.cs`
- Create `tests/ChronoTriggerAccessibility.Native.Tests/Capture/DialogueCaptureTests.cs`

**Step 1 — RED:** Use fake readable memory to test inline and heap MSVC UTF-8 strings in a vector, exact `0x18` stride, empty vectors, negative/reversed spans, misalignment, multiplication/addition overflow, count/byte/string limits, unreadable headers/elements, invalid UTF-8, and defensive snapshots. Dialogue tests model a `MsgWindow` with vtable RVA `0x3A0624`, size `0x340`, active `+0x2BD`, current line `+0x2C0`, page base `+0x2C4`, phase `+0x2CC`, selected choice `+0x2D0`, choice count `+0x2D4`, parsed strings `+0x308/+0x30C/+0x310` stride `0x18`, and flags `+0x314/+0x318/+0x31C` stride `4`. Cover phases `0..4`, current-line bounds, equal string/flag counts, flags `0x10/0x08/0x02/0x04`, choice range `current-choiceCount..current-1`, valid selected `-1`, and every malformed-state failure returning no partial snapshot.

```powershell
& $dotnet test .\tests\ChronoTriggerAccessibility.Native.Tests\ChronoTriggerAccessibility.Native.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~MsvcStringVectorReaderTests|FullyQualifiedName~DialogueCaptureTests'
```

Expected RED: compilation fails because both readers are absent.

**Step 2 — GREEN:** Implement bounded readers over `IReadableMemory`; reuse `MsvcStringReader` rather than duplicate string decoding. `DialogueCapture` produces only complete immutable open/current-line/choice snapshots and never speaks. It accepts active ordinary line state and phase-4 choice state, treats flag `0x01` as alignment only, tolerates choice focus `-1`, and returns a diagnostic result on validation failure.

```powershell
& $dotnet test .\tests\ChronoTriggerAccessibility.Native.Tests\ChronoTriggerAccessibility.Native.Tests.csproj -c Release --no-restore
```

Expected GREEN: all Native tests pass, zero warnings.

**Step 3 — review and commit:** Audit every checked arithmetic operation, maximum, pointer read, failure path, and defensive copy; run `git diff --check`; commit:

```powershell
git add src/ChronoTriggerAccessibility.Native tests/ChronoTriggerAccessibility.Native.Tests
git commit -m 'feat: capture committed field dialogue state'
```

## Task 3: Add Validated Extras, Settings, and Top-Menu Capture Models

**Files:**

- Create `src/ChronoTriggerAccessibility.Native/Capture/MenuCaptureModels.cs`
- Create `src/ChronoTriggerAccessibility.Native/Capture/BuilderCaptureScope.cs`
- Create `src/ChronoTriggerAccessibility.Native/Capture/ExtrasCapture.cs`
- Create `src/ChronoTriggerAccessibility.Native/Capture/SettingsCapture.cs`
- Create `src/ChronoTriggerAccessibility.Native/Capture/TopMenuCapture.cs`
- Create corresponding tests under `tests/ChronoTriggerAccessibility.Native.Tests/Capture/`

**Step 1 — RED:** Write structure-driven tests before implementation. A builder scope must accept localized text/control/binder/focus/render observations only on its owning thread, reject nesting, dispose in `finally`, and correlate each manager key to exactly one visible control and localized label.

Extras tests validate hub vtable `0x3A5970` and five visible native keys `0..4`; keys `0..3` require localized label/help plus native availability, and key `4` is Back. Ending Log vtable `0x3A4AE8` requires exactly 19 ordered ending row keys `0..18` plus an independently binder-correlated appended Back control. Back is not native row key `19`, and callback action `4` is not evidence that its manager key is `4`; retain any actual noncolliding captured key while assigning only semantic presentation position 20 after the rows. A locked row may expose only its visible numbered position and visible question-mark text, never a separately supplied hidden title/requirement. Ending Detail vtable `0x3A475C` requires visible title, Requirements label/text, and Review/Back controls at native keys `0/1`. Test incomplete/duplicate/ambiguous correlations before success.

Settings tests cover constructor context `+0x2F8` (`1` title, `0` in-game), descriptor vector `+0x2C8/+0x2CC/+0x2D0` stride `0x0C`, pager vtable `0x3AC940` with page/count/transition state `+0x2D0/+0x2D4/+0x2E1`, and each descriptor's `0x98` rows. A row has UI type `+0x00`, localized label string `+0x04`, help string `+0x1C`, value-string vector `+0x34/+0x38/+0x3C` with `0x18` stride, and selected displayed index `+0x90`. Keys below 1000 decode as `row=key/4`, `subcontrol=key%4`; require complete label/value/help/key/position snapshots, dynamic row/resolution counts, and refusal of ambiguous/stale correlations.

Top-menu tests cover validated classic vtable `0x3A4024`, alternate vtable `0x3AA058`, classic manager stack `+0x2C0` vector `+4/+8/+C`, manager key `+0x2C4`, exactly seven localized rows with actual enabled state, and complete rendered status strings in native screen order: member names and LV/HP/MP, play time, currency, then the conditional visible status/help text. Classify UTF-8 observations only by these return RVAs: classic time `0x1D0AAA`, currency `0x1D0B54`, row `0x1D179E`, member `0x23B1D0`; touch time `0x221A4F`, currency `0x221B1C`, row `0x2221C6`, member `0x23A9E6`/`0x23AE3A`; shared party-stat calls `0x23973B`, `0x239891`, `0x239914`, `0x2399AA`, `0x239A73`. Conditional UTF-16 text is accepted only for renderer caller RVA `0x22F3BD` under an owned StatusBar vtable `0x3ABCB8`. Unknown return addresses, incomplete member groups, or unknown displayed tokens must fail rather than be guessed or relabelled.

```powershell
& $dotnet test .\tests\ChronoTriggerAccessibility.Native.Tests\ChronoTriggerAccessibility.Native.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~BuilderCaptureScopeTests|FullyQualifiedName~ExtrasCaptureTests|FullyQualifiedName~SettingsCaptureTests|FullyQualifiedName~TopMenuCaptureTests'
```

Expected RED: compilation fails because the menu capture types do not exist.

**Step 2 — GREEN:** Implement small pure capture/correlation types. Use the audited descriptors plus localized text/control/render observations; do not use fixed English. Validate complete builder ownership and object vtables before publishing. Settings reads the selected index from `row+0x90` and the displayed value from `values[selectedIndex]`; it is refreshed after the shared post-original mutation. Do not invoke the stored getter or retain page controls across rebuild. Unavailable values or ambiguous correlations produce a diagnostic instead of a partial row. Top status preserves strings copied at the audited render boundaries in native order and never interprets backing save fields. Extras uses native visible state and unlock flags only to decide whether the visible row is unavailable; it never resolves a locked ending's hidden title.

```powershell
& $dotnet test .\tests\ChronoTriggerAccessibility.Native.Tests\ChronoTriggerAccessibility.Native.Tests.csproj -c Release --no-restore
```

Expected GREEN: all Native tests pass, zero warnings.

**Step 3 — review and commit:** Verify no English tables, no ambiguous fallback, and all vector/object limits; run `git diff --check`; commit:

```powershell
git add src/ChronoTriggerAccessibility.Native tests/ChronoTriggerAccessibility.Native.Tests
git commit -m 'feat: capture supported menu surfaces'
```

## Task 4: Extend Exact Hook Contracts and Shared Observer Fanout

**Files:**

- Modify `src/ChronoTriggerAccessibility.Native/Hooks/HookContract.cs`
- Modify `src/ChronoTriggerAccessibility.Native/Hooks/GameVersionCatalog.cs`
- Modify `src/ChronoTriggerAccessibility.Mod/NewGame/SharedNativeHookFanoutFactory.cs`
- Modify `src/ChronoTriggerAccessibility.Mod/NewGame/NewGameHookSet.cs`
- Modify exact-contract/fanout tests in Native and Mod test projects

**Step 1 — RED:** Add catalog tests for every row in “Exact Native Hook Catalog Additions,” unique IDs/RVAs, image-base resolution, executable-section membership, exact installed-file bytes, delegate types, and x86 calling conventions. Add fanout tests proving ordered post-original delivery for TextManager, focus, CustomButton construction, and control binding; one original call; return preservation; observer isolation and later delivery after an observer throws; no delivery on original failure; and unchanged New Game observations. The `MsgWindow` close delegate test must prove the dummy stack word is represented.

```powershell
& $dotnet test .\tests\ChronoTriggerAccessibility.Native.Tests\ChronoTriggerAccessibility.Native.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~GameVersionCatalogTests'; if ($LASTEXITCODE) { exit $LASTEXITCODE }
& $dotnet test .\tests\ChronoTriggerAccessibility.Mod.Tests\ChronoTriggerAccessibility.Mod.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~SharedNativeHookFanoutFactoryTests'
```

Expected RED: new hook IDs/delegates/contracts and multi-observer fanout are absent.

**Step 2 — GREEN:** Add narrowly typed delegates matching the audited thiscall ABIs; `MsgWindow` close includes the dummy word. Generalize the observer interface to four post-original methods with safe no-op defaults and accept an ordered immutable observer list. Route each existing shared hook ID through one detour and isolate observer exceptions with the supplied coverage reporter. Update New Game to implement the common interface without changing behavior.

```powershell
& $dotnet test .\tests\ChronoTriggerAccessibility.Native.Tests\ChronoTriggerAccessibility.Native.Tests.csproj -c Release --no-restore
& $dotnet test .\tests\ChronoTriggerAccessibility.Mod.Tests\ChronoTriggerAccessibility.Mod.Tests.csproj -c Release --no-restore
```

Expected GREEN: all Native and Mod tests pass, zero warnings.

**Step 3 — review and commit:** Compare delegates and every byte to the supported file, check each original is called once, run `git diff --check`, commit:

```powershell
git add src/ChronoTriggerAccessibility.Native src/ChronoTriggerAccessibility.Mod tests/ChronoTriggerAccessibility.Native.Tests tests/ChronoTriggerAccessibility.Mod.Tests
git commit -m 'feat: register shared menu and dialogue hooks'
```

## Task 5: Implement the Field Dialogue Hook Set

**Files:**

- Create `src/ChronoTriggerAccessibility.Mod/Dialogue/DialogueHookSet.cs`
- Create `tests/ChronoTriggerAccessibility.Mod.Tests/Dialogue/DialogueHookSetTests.cs`

**Step 1 — RED:** With fake native hooks and memory, test prepare-before-activate, open/update/close original-call-once behavior, post-open generation registration, first active current line, line-index/page-base progression, fast-reveal de-duplication, phase-4 choices and `-1` focus, focus changes, exact choice activation only when the close caller RVA is `0x19768F`, ordinary close/auto-end non-activation, close/reopen reset, invalid snapshot coverage failure before semantic dispatch, original exceptions, active-epoch suppression, disable clearing state, and no callback exception crossing unmanaged boundaries.

```powershell
& $dotnet test .\tests\ChronoTriggerAccessibility.Mod.Tests\ChronoTriggerAccessibility.Mod.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~DialogueHookSetTests'
```

Expected RED: `DialogueHookSet` and its registrations do not exist.

**Step 2 — GREEN:** Own only `MsgWindow` open `0x195B40`, update `0x197530`, and close `0x195C70`. Call the original once, capture post-original committed state, translate complete snapshots into dialogue events, and dispatch through `ISemanticEventDispatcher`. Do not announce on open merely because future lines were parsed. Read the close caller address before the original while preserving the dummy argument/stack cleanup, classify selection only under all audited conditions, then clear after close. Implement epoch and fail-closed behavior. Keep this hook set independently constructible and tested; Task 6 performs the single production composition change after every menu observer exists.

```powershell
& $dotnet test .\tests\ChronoTriggerAccessibility.Mod.Tests\ChronoTriggerAccessibility.Mod.Tests.csproj -c Release --no-restore
```

Expected GREEN: all Mod tests pass, zero warnings.

**Step 3 — review and commit:** Audit native exception boundaries, current-line identity and all close callers; run `git diff --check`; commit:

```powershell
git add src/ChronoTriggerAccessibility.Mod/Dialogue tests/ChronoTriggerAccessibility.Mod.Tests/Dialogue
git commit -m 'feat: narrate ordinary field dialogue and choices'
```

## Task 6: Implement Extras, Settings, and Top-Menu Hooks and Composition

**Files:**

- Create `src/ChronoTriggerAccessibility.Mod/Menus/MenuHookSet.cs`
- Create focused helper files under `src/ChronoTriggerAccessibility.Mod/Menus/` when responsibility is distinct
- Create `tests/ChronoTriggerAccessibility.Mod.Tests/Menus/MenuHookSetTests.cs`
- Modify `src/ChronoTriggerAccessibility.Mod/Mod.cs`
- Modify `src/ChronoTriggerAccessibility.Mod/Runtime/RuntimeContracts.cs` only if composition contracts require it
- Modify composition/installer tests under `tests/ChronoTriggerAccessibility.Mod.Tests/`

**Step 1 — RED:** Test each class-specific registration and shared-observer path. Extras cases: hub entry/focus, locked activation re-announcement, authoritative switch actions `0..5`, Ending Log unlocked/locked rows, Ending Detail Review/Back, and explicit boundaries for Movies/Illustrations/Sound/replay. Settings cases: constructor context, complete builder scope, title and in-game differing dynamic rows, focus/value/help refresh, shared `0x1E3980` mutation returning false/true, Default confirmation, Back/destructor reset, and explicit rebinding/License boundaries. Top-menu cases: classic and alternate builders, seven rows/disabled state, UTF-8 factory captures accepted only for the audited builder return-address whitelist, member LV/HP/MP grouping, time/currency, StatusBar owner-scoped UTF-16 conditional text at return RVA `0x22F3BD`, focused row plus status queue, Settings transition, deep-page boundary, and malformed/incomplete status failure. Lifecycle tests cover thread-local non-nesting scopes, `finally`, one shared detour per address, observer order `[NewGame, Menu]`, atomic registration count, original exceptions, epoch/disable, and observer isolation.

```powershell
& $dotnet test .\tests\ChronoTriggerAccessibility.Mod.Tests\ChronoTriggerAccessibility.Mod.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~MenuHookSetTests|FullyQualifiedName~ModCompositionTests|FullyQualifiedName~ReloadedHookInstallerTests'
```

Expected RED: menu hook set/composition and new registrations are absent.

**Step 2 — GREEN:** Implement `MenuHookSet` as the sole owner of new Extras/Settings/top-menu detours and as an observer of the four shared boundaries. Scope builder observations exactly around each audited original call. Publish only complete native capture snapshots. Compose in this order: create Menu; create New Game through fanout that includes Menu for custom-button/binder hooks; create Startup through fanout `[NewGame, Menu]` for TextManager/focus; then concatenate class-specific registrations once. Installer participants disable in reverse-safe order and every hook contract appears exactly once.

Use native actions and visible localized controls, not raw action IDs in speech. Hub callback event `0` is activation, event `2` is Back; event `1` is not keyboard focus. Ending Detail indices are Review `0`, Back `1`. Settings values refresh only after the shared native mutation returns true. For unsupported interactive boundaries, supply `MenuUnsupported` with the selected localized control, the mod-authored coverage warning, and a return instruction containing the captured localized Cancel/Back control name; the Core narrator must not hard-code the control label.

```powershell
& $dotnet test .\ChronoTriggerAccessibility.slnx -c Release --no-restore
```

Expected GREEN: the entire solution passes, zero warnings, and all required addresses are registered exactly once.

**Step 3 — review and commit:** Audit all scope cleanup, complete-snapshot checks, localization, lock privacy, and shared detours; run `git diff --check`; commit:

```powershell
git add src tests
git commit -m 'feat: narrate extras settings and the field menu'
```

## Task 7: Package, Deploy, and Verify the Milestone

**Files:**

- Modify `README.md`
- Modify packaging/deployment contract tests as needed
- Modify `tools/Package-Mod.ps1` or `tools/Deploy-Mod.ps1` only if a new project output is not already included
- Create `docs/verification/2026-07-22-extras-settings-dialogue-field-menu-verification.md`

**Step 1 — RED:** Update package tests first to require the rebuilt assemblies and README scope, exact SHA-256 manifest coverage, no x64 Prism, no game executable, and no omitted dependency. Add deployment-contract assertions that normal Steam launch still uses the existing verified ASI/bootstrapper path and that transactional rollback targets remain unchanged.

```powershell
& $dotnet test .\tests\ChronoTriggerAccessibility.Mod.Tests\ChronoTriggerAccessibility.Mod.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~PackageContractTests|FullyQualifiedName~DeploymentContractTests'
```

Expected RED: README/package expectations still describe the older startup-only scope.

**Step 2 — GREEN:** Update README supported scope, coverage boundaries, expected narration, log diagnostics, and testing guidance. Do not change dependency versions. Update scripts only where tests prove necessary. Run the complete suite from a fresh Release build:

```powershell
& $dotnet test .\ChronoTriggerAccessibility.slnx -c Release
& '.\tools\Package-Mod.ps1'
& '.\tools\Deploy-Mod.ps1'
& '.\tools\Verify-Deployment.ps1'
```

Expected GREEN: all tests pass with zero failures/skips/warnings; package hashes verify; transactional deployment succeeds; exact game executable hash remains unchanged.

**Step 3 — live verification:** Close any existing game instance safely. Preserve saves. Start Chrono Trigger through its normal Steam/ASI path, not the recovery script. Confirm fresh Reloaded logs show the exact executable, all required hooks prepared then atomically active, Prism's NVDA backend, no duplicate detour address, no managed exception, and no coverage failure. Navigate the title Extras hub, a locked or available tile without changing saves, and title Settings/Back. If and only if an already-safe gameplay state is available without altering progress, smoke-test one ordinary dialogue line and the top menu; otherwise record that the user's New Game test is the remaining interaction verification. Graphics capture error `0x80004002` is an external Windows capture limitation, not a game/mod graphics failure; do not use OCR or blind navigation to compensate.

Record exact commands, test counts, package/deployed DLL hashes, profile paths, newest log path, hook count, backend, safe interactions, and any untested boundary in the verification document.

**Step 4 — final review and commit:** Run `git diff --check`, verify `git status --short` contains only intended documentation/package changes, and commit:

```powershell
git add README.md tools tests/ChronoTriggerAccessibility.Mod.Tests docs/verification
git commit -m 'test: deploy and verify menu dialogue accessibility'
```

Keep the feature worktree and deployed package available for the user's NVDA/New Game test. Do not delete or reset it.
