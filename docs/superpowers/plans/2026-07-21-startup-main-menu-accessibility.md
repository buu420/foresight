# Chrono Trigger Startup and New Game Accessibility Implementation Plan

> **For Codex:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` to implement this plan task by task, with a fresh implementer and code review for every task. Use `superpowers:test-driven-development` for every production behavior and `superpowers:verification-before-completion` before each completion claim.

**Goal:** Build and deploy a Reloaded-II/Prism mod that provides reliable speech and braille output from Chrono Trigger startup through the first name confirmation.

**Architecture:** A small AnyCPU `net9.0-windows` Reloaded mod owns exact-build validation, native x86 hooks, a pure semantic state reducer, and one serialized Prism output session. Native hooks capture localized strings and authoritative scene/menu state; they publish semantic events only. A deterministic reducer handles entry summaries, focus/value changes, duplicate suppression, and movie-description cancellation.

**Tech stack:** C# 13, .NET 9, xUnit 2.9.3, Reloaded.Mod.Interfaces 2.5.0, Reloaded.SharedLib.Hooks 1.9.0, Reloaded.Hooks.Definitions 1.15.0, Prism 0.17.3 x86, PowerShell 7/Windows PowerShell deployment scripts, Ghidra 12.1.2 evidence.

**Pinned build tool:** `C:\Program Files (x86)\Steam\steamapps\common\Yu-Gi-Oh! Legacy of the Duelist Link Evolution\YuGiOhAccessibility\.worktrees\accessibility-native-uia\.tools\dotnet\dotnet.exe` (SDK 9.0.315).

**Pinned target:** `G:\SteamLibrary\steamapps\common\Chrono Trigger\Chrono Trigger.exe`, PE32/i386, SHA-256 `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.

---

## Task 1: Repository and semantic core

**Files:**

- Create: `ChronoTriggerAccessibility.slnx`
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `src/ChronoTriggerAccessibility.Core/ChronoTriggerAccessibility.Core.csproj`
- Create: `src/ChronoTriggerAccessibility.Core/Announcements/Announcement.cs`
- Create: `src/ChronoTriggerAccessibility.Core/Announcements/AnnouncementPriority.cs`
- Create: `src/ChronoTriggerAccessibility.Core/Events/AccessibilityEvent.cs`
- Create: `src/ChronoTriggerAccessibility.Core/Events/ScreenKind.cs`
- Create: `src/ChronoTriggerAccessibility.Core/State/AccessibilityState.cs`
- Create: `tests/ChronoTriggerAccessibility.Core.Tests/ChronoTriggerAccessibility.Core.Tests.csproj`
- Create: `tests/ChronoTriggerAccessibility.Core.Tests/State/AccessibilityStateTests.cs`

**Step 1: Write failing reducer tests**

Cover these exact cases before production code exists:

- `ScreenEntered(TitlePrompt)` emits “Chrono Trigger. Press confirm.” with interrupt priority.
- entering the same screen twice without exit emits only once;
- `FocusChanged("New Game", 2, 7, false)` emits “New Game, 2 of 7” and a repeated identical event emits nothing;
- `ValueChanged("Battle Mode", "WAIT", help)` emits the label, value, and help;
- `NameChanged("Crono")` emits the full resulting name;
- `ConfirmationOpened("Start with the name Crono?", ["Yes","No"], 0)` announces the prompt and “Yes, 1 of 2”;
- leaving the movie increments a generation token so stale timed descriptions are rejected.

Run:

```powershell
& 'C:\Program Files (x86)\Steam\steamapps\common\Yu-Gi-Oh! Legacy of the Duelist Link Evolution\YuGiOhAccessibility\.worktrees\accessibility-native-uia\.tools\dotnet\dotnet.exe' test .\tests\ChronoTriggerAccessibility.Core.Tests\ChronoTriggerAccessibility.Core.Tests.csproj
```

Expected: build/test failure because the event and reducer types do not exist.

**Step 2: Implement the minimum immutable model and reducer**

Use records and a single `AccessibilityState.Apply(AccessibilityEvent)` method returning `IReadOnlyList<Announcement>`. `Announcement` contains `Text`, `Priority`, `Interrupt`, and optional `Generation`. Store only the last semantic identity per active screen; do not suppress a value when the label is the same but the value changes.

**Step 3: Run the focused tests**

Expected: all Task 1 tests pass.

**Step 4: Commit**

```powershell
git add ChronoTriggerAccessibility.slnx Directory.Build.props Directory.Packages.props src tests
git commit -m "feat: add startup accessibility state model"
```

## Task 2: Prism x86 runtime and serialized output

**Files:**

- Create: `native/prism/v0.17.3/win-x86/prism.dll`
- Create: `native/prism/v0.17.3/LICENSE`
- Create: `native/prism/v0.17.3/NOTICE`
- Create: `src/ChronoTriggerAccessibility.Prism/ChronoTriggerAccessibility.Prism.csproj`
- Create: `src/ChronoTriggerAccessibility.Prism/Native/PrismNative.cs`
- Create: `src/ChronoTriggerAccessibility.Prism/Native/PrismLibraryResolver.cs`
- Create: `src/ChronoTriggerAccessibility.Prism/PrismOutput.cs`
- Create: `src/ChronoTriggerAccessibility.Prism/PrismSession.cs`
- Create: `tests/ChronoTriggerAccessibility.Prism.Tests/ChronoTriggerAccessibility.Prism.Tests.csproj`
- Create: `tests/ChronoTriggerAccessibility.Prism.Tests/PrismLibraryContractTests.cs`
- Create: `tests/ChronoTriggerAccessibility.Prism.Tests/PrismOutputTests.cs`
- Create: `tools/Build-PrismWin32.ps1`
- Create: `tools/Test-PrismWin32.ps1`

**Step 1: Write failing library and lifecycle tests**

Tests must prove:

- the staged DLL has PE machine `0x014C`;
- exports include `prism_init`, `prism_shutdown`, `prism_registry_create_best`, `prism_backend_output`, and `prism_backend_free`;
- the resolver accepts only the exact assembly-sibling `prism.dll` whose SHA-256 is embedded at build time;
- initialization calls `init`, `create_best`, then output; disposal calls `backend_free` before `shutdown`;
- calls are serialized and a failed output is surfaced, not swallowed.

Run the Prism test project and confirm it fails before the wrapper exists.

**Step 2: Stage the reviewed Prism build**

`Build-PrismWin32.ps1` must check out tag `v0.17.3`, read/display its `README.md`, verify the tag commit `9911156998B52FEE91FB2CB4F71AC793D4E546C7`, configure Visual Studio 2022 Win32, and build a shared library. Use these options:

```text
PRISM_ENABLE_TESTS=OFF
PRISM_ENABLE_DEMOS=OFF
PRISM_ENABLE_SHIMS=OFF
PRISM_ENABLE_LEGACY_BACKENDS=OFF
PRISM_ENABLE_JAWS_BACKEND=OFF
PRISM_ENABLE_SAPI_BACKEND=OFF
PRISM_ENABLE_SENSE_READER_BACKEND=OFF
PRISM_ENABLE_ZOOM_TEXT_BACKEND=OFF
BUILD_SHARED_LIBS=ON
```

The four disabled backends require ATL, which is absent from the installed compiler. NVDA, UI Automation, OneCore, PC Talker, ZDSR, and Boy PC Reader remain enabled. Stage the already verified artifact with SHA-256 `6A84322E42D1B4123E2E66E9887CFDF0CDEA2A972FA40FC7B7185AEC77F5178A` plus upstream `LICENSE` and `NOTICE`.

**Step 3: Implement the C ABI wrapper**

Use `LibraryImport` with cdecl. Call `prism_init(0)`, then `prism_registry_create_best(context)`. `prism_registry_create_best` returns an already initialized backend. Use `prism_backend_output(backend, utf8, interrupt ? 1 : 0)` for every announcement. Protect the backend with one lock and never call it concurrently. Resolve by exact path with `NativeLibrary.SetDllImportResolver`.

**Step 4: Run focused and native smoke tests**

The native smoke helper must run as x86, create a backend, emit “Chrono Trigger accessibility test,” free it, and shut down with exit code zero. If no supported reader/TTS backend is available, report Prism's exact error and fail.

**Step 5: Commit**

```powershell
git add native src/ChronoTriggerAccessibility.Prism tests/ChronoTriggerAccessibility.Prism.Tests tools/Build-PrismWin32.ps1 tools/Test-PrismWin32.ps1
git commit -m "feat: add verified x86 Prism output"
```

## Task 3: Executable identity and hook contracts

**Files:**

- Create: `src/ChronoTriggerAccessibility.Native/ChronoTriggerAccessibility.Native.csproj`
- Create: `src/ChronoTriggerAccessibility.Native/Build/ExecutableIdentity.cs`
- Create: `src/ChronoTriggerAccessibility.Native/Build/ExecutableVerifier.cs`
- Create: `src/ChronoTriggerAccessibility.Native/Build/PeImage.cs`
- Create: `src/ChronoTriggerAccessibility.Native/Hooks/HookContract.cs`
- Create: `src/ChronoTriggerAccessibility.Native/Hooks/GameVersionCatalog.cs`
- Create: `src/ChronoTriggerAccessibility.Native/Memory/MsvcStringReader.cs`
- Create: `tests/ChronoTriggerAccessibility.Native.Tests/ChronoTriggerAccessibility.Native.Tests.csproj`
- Create: `tests/ChronoTriggerAccessibility.Native.Tests/Build/ExecutableVerifierTests.cs`
- Create: `tests/ChronoTriggerAccessibility.Native.Tests/Hooks/GameVersionCatalogTests.cs`
- Create: `tests/ChronoTriggerAccessibility.Native.Tests/Memory/MsvcStringReaderTests.cs`

**Step 1: Write failing exact-build tests**

Tests against the installed executable must assert the SHA-256, i386 machine, image base `0x00400000`, executable `.text` membership, and the following entry bytes:

```text
TextManager::getMsg       0x1B9110  55 8B EC 6A FF 68 A1 AD 76 00 64 A1 00 00 00 00
SceneManager::create      0x297860  55 8B EC 6A FF 68 F8 73 76 00 64 A1 00 00 00 00
SceneManager::NextScene   0x297B60  55 8B EC 6A FF 68 E8 94 77 00 64 A1 00 00 00 00
ModeSelectSteam::init     0x2A9C60  55 8B EC 6A FF 68 B2 AE 77 00 64 A1 00 00 00 00
OpeManualScene::init      0x2ADB50  55 8B EC 6A FF 68 E4 B5 77 00 64 A1 00 00 00 00
NameInputScene::update    0x2C2C50  55 8B EC 83 E4 F8 51 53 56 57 8B F9 80 BF 94 02
TitleMenuMode::enter      0x2CF560  55 8B EC 6A FF 68 84 E4 77 00 64 A1 00 00 00 00
Title row factory         0x2CD7A0  55 8B EC 6A FF 68 11 E2 77 00 64 A1 00 00 00 00
TitleScene::update        0x2D1030  55 8B EC F3 0F 10 45 08 56 8B F1 8B 8E 90 02 00
TitleMenu callback        0x2D12A0  55 8B EC 8B 45 0C 56 57 8B F1 8B 38 8B 45 08 8B
nsMenu focus setter       0x1DD3E0  55 8B EC 83 EC 20 8B C1 53 8B 5D 08 57 8D B8 C4 02 00 00 89 45 FC C6 80 CC 02 00 00 01 89 7D EC
ModeSelect callback       0x2AB9E0  55 8B EC 83 E4 F8 83 EC 14 8B 45 08 53 8B D9 89 5C 24 04 56 57 83 F8 03 0F 87 B2 02 00 00 FF 24
Control Next callback     0x2AE840  55 8B EC 83 E4 F8 51 8B 45 08 56 8B F1 8B 00 83 E8 00 74 05 83 E8 02 75 23 C7 05 CC C3 81 00 18
Name action callback      0x2C1760  55 8B EC 6A FF 68 FF D4 77 00 64 A1 00 00 00 00 50 83 EC 70 A1 D0 A0 7F 00 33 C5 89 45 F0 56 57
Name direct entry         0x2C1B50  56 57 8B F9 6A 01 8B 07 C6 80 90 02 00 00 01 8B 4F 04 E8 29 DB FF FF 33 F6 0F 1F 80 00 00 00 00
Name direct close         0x2C1BA0  56 57 8B F9 33 F6 8B 07 C6 80 90 02 00 00 00 90 8B 47 04 6A 01 8B 0C 06 8B 01 FF 90 B8 02 00 00
```

Corrupt one byte in an in-memory fixture and assert that verification rejects the entire hook transaction.

**Step 2: Implement PE and executable verification**

Use `System.Reflection.PortableExecutable.PEReader` for headers and SHA-256 over a read-only file stream. Convert RVA to file position by section metadata, never by a fixed subtraction. Each hook contract contains name, RVA, expected bytes, and delegate type. Resolve the in-process address only after all file checks pass.

**Step 3: Implement safe MSVC x86 string reading**

For the supported build, the string layout is 24 bytes: data/inline buffer at `+0x00`, length at `+0x10`, capacity at `+0x14`; capacity below 16 means small-string storage is inline. Reject lengths over 4,096, capacities smaller than length, unreadable pointers, and invalid UTF-8. Name strings are additionally limited to five visible characters.

**Step 4: Run tests and commit**

```powershell
git add src/ChronoTriggerAccessibility.Native tests/ChronoTriggerAccessibility.Native.Tests
git commit -m "feat: verify Chrono Trigger native hook contracts"
```

## Task 4: Reloaded mod bootstrap and atomic hooks

**Files:**

- Create: `src/ChronoTriggerAccessibility.Mod/ChronoTriggerAccessibility.Mod.csproj`
- Create: `src/ChronoTriggerAccessibility.Mod/Mod.cs`
- Create: `src/ChronoTriggerAccessibility.Mod/ModConfig.json`
- Create: `src/ChronoTriggerAccessibility.Mod/Runtime/AccessibilityRuntime.cs`
- Create: `src/ChronoTriggerAccessibility.Mod/Runtime/GameWindowWaiter.cs`
- Create: `src/ChronoTriggerAccessibility.Mod/Reloaded/ReloadedHookInstaller.cs`
- Create: `src/ChronoTriggerAccessibility.Mod/Diagnostics/ModLog.cs`
- Create: `src/ChronoTriggerAccessibility.Mod/Diagnostics/AccessibleFatalError.cs`
- Create: `tests/ChronoTriggerAccessibility.Mod.Tests/ChronoTriggerAccessibility.Mod.Tests.csproj`
- Create: `tests/ChronoTriggerAccessibility.Mod.Tests/Bootstrap/ModManifestTests.cs`
- Create: `tests/ChronoTriggerAccessibility.Mod.Tests/Bootstrap/AccessibilityRuntimeTests.cs`

**Step 1: Write failing composition tests**

Prove the manifest contains:

```json
{
  "ModId": "chrono.trigger.accessibility",
  "ModDll": "ChronoTriggerAccessibility.Mod.dll",
  "CanUnload": false,
  "ModDependencies": ["reloaded.sharedlib.hooks"],
  "SupportedAppId": ["chrono trigger.exe"]
}
```

Runtime tests must prove the sequence `verify executable -> wait for sole visible game window -> initialize Prism -> prepare all hooks -> activate all hooks`. Inject a failure at every stage and assert no later stage runs, all prepared hooks remain inactive, the error reaches both Reloaded logging and `MessageBoxW`, and no exception crosses an unmanaged hook boundary.

**Step 2: Implement the Reloaded entry point**

Copy the official template's `ModBase`, `IModLoader`, `IStartupScanner`, and shared-hook controller acquisition pattern. The assembly is AnyCPU, targets `net9.0-windows`, permits unsafe blocks, copies lock-file assemblies, and never unloads hooks while the process is alive.

**Step 3: Implement atomic hook activation**

Create every hook and bind its original delegate first. Activate only after all creation succeeds. Use Reloaded's explicit x86 `[Function(...MicrosoftThiscall)]` or `[Function(...MicrosoftFastcall)]` contracts as appropriate; do not rely on CLR `UnmanagedFunctionPointer` metadata to model x86 member functions. Ordinary instance hooks receive `this` in `ECX`. The reviewed `std::function::_Do_call` callbacks receive the closure in `ECX`; their stack arguments must match the concrete callback: the title callback receives pointers to `EventType` and row index, while the Mode Select callback receives event and control/direction values. Each callback calls the original exactly once, catches all managed exceptions, logs them, and moves runtime integrity to a faulted state that emits an accessible error once.

**Step 4: Run tests and commit**

```powershell
git add src/ChronoTriggerAccessibility.Mod tests/ChronoTriggerAccessibility.Mod.Tests
git commit -m "feat: bootstrap Reloaded accessibility runtime"
```

## Task 5: Startup and dynamic title menu

**Files:**

- Create: `src/ChronoTriggerAccessibility.Native/Hooks/TextManagerHook.cs`
- Create: `src/ChronoTriggerAccessibility.Native/Hooks/SceneManagerHooks.cs`
- Create: `src/ChronoTriggerAccessibility.Native/Hooks/TitleSceneHook.cs`
- Create: `src/ChronoTriggerAccessibility.Native/Capture/LocalizedTextCache.cs`
- Create: `src/ChronoTriggerAccessibility.Native/Capture/TitleCapture.cs`
- Create: `src/ChronoTriggerAccessibility.Core/Startup/OpeningMovieTimeline.cs`
- Create: `tests/ChronoTriggerAccessibility.Native.Tests/Capture/TitleCaptureTests.cs`
- Create: `tests/ChronoTriggerAccessibility.Core.Tests/Startup/OpeningMovieTimelineTests.cs`

**Step 1: Write failing recorded-state tests**

Extend the exact-build catalog with `SceneManager::NextScene` and the title row factory using the reviewed bytes above. Test clean, intermediate, and populated title menus with these exact visible row sets:

- New Game, Extras, Settings, Quit;
- Resume, New Game, Extras, Settings, Quit;
- Resume, New Game, Load Game, New Game +, Extras, Settings, Quit.

For every set, assert initial focus, each focus move, wraparound, disabled entries, and confirmation use the captured localized label and actual count. Test that Tap-to-Start emits only when `TitleScene + 0x290` points to a mode whose vtable RVA is `0x3B7FC4`, and title-menu entry only when it points to vtable RVA `0x3B7FA8`.

Test movie narration generation: leaving the opening scene cancels all future scheduled lines, including when the player skips directly to the title.

**Step 2: Implement localized text and title capture**

Hook `TextManager::getMsg` as `std::string* thiscall(TextManager*, std::string* result, int fileId, int msgId)`, call original first, decode the returned MSVC string, and cache by `(fileId,msgId)`. Title-row source IDs are constructed in this order: `(0x41,3)`, `(0x41,0)`, `(0x41,1)`, `(0x41,2)`, `(0x41,6)`, `(0x23,0x24)`, `(0x3A,4)`. Include only rows the native menu actually constructs/enables; do not infer a visible row solely from this superset.

Wrap `TitleMenuMode::enter` in a thread-local capture scope. During that scope, hook the title row factory at RVA `0x2CD7A0`; its `ECX` points at the current 0x1C-byte row record whose first 24 bytes are the localized MSVC string, and the factory is called only for rows the game enabled. Capture each resulting label in call order. This is the authoritative compact visible row list; do not reconstruct availability predicates.

Hook `TitleScene::update`, call original, then validate `this + 0x290` and its vtable against `TapToStart` RVA `0x3B7FC4` or `TitleMenuMode` RVA `0x3B7FA8`. For title-menu focus, follow `TitleMenuMode + 0x10` to the `nsMenu::Manager`, whose authoritative index is at `+0x2C4`; hook the shared focus setter at RVA `0x1DD3E0` and snapshot before/original/after. Suppress the setter's initial event while the row-capture scope is active, then announce the manager's validated current focus after `enter` returns. Hook title callback RVA `0x2D12A0` to announce activation using the already captured visible row label. It is a `std::function` dispatcher: `ECX` is the closure and its two stack arguments are pointers to `EventType` and row index. Event 0 activates; event 1 is non-activating feedback.

**Step 3: Implement startup descriptions**

Scene ID 2 is the Square Enix logo, scene ID `0x1E` constructs `DemoMovieScene`, and scene ID 3 constructs `TitleScene`. Hook `SceneManager::create` for initial creation and `SceneManager::NextScene` for transitions; after `NextScene` returns, read and validate the current-scene global at RVA `0x41C3E8`. Scene hooks announce Square Enix, the opening movie, Chrono Trigger, and the live confirm prompt. Opening descriptions are generation-scoped and dispatched only while scene `0x1E` remains authoritative. Keep the lines concise and visual; do not duplicate dialogue/music or reveal information beyond the movie.

**Step 4: Run tests and commit**

```powershell
git add src/ChronoTriggerAccessibility.Core src/ChronoTriggerAccessibility.Native tests
git commit -m "feat: narrate startup and dynamic title menu"
```

## Task 6: Control Descriptions, New Game settings, and name entry

**Files:**

- Create: `src/ChronoTriggerAccessibility.Native/Hooks/NewGameHooks.cs`
- Create: `src/ChronoTriggerAccessibility.Native/Capture/ControlDescriptionCapture.cs`
- Create: `src/ChronoTriggerAccessibility.Native/Capture/ModeSelectCapture.cs`
- Create: `src/ChronoTriggerAccessibility.Native/Capture/NameInputCapture.cs`
- Create: `src/ChronoTriggerAccessibility.Core/NewGame/NewGameNarrator.cs`
- Create: `tests/ChronoTriggerAccessibility.Native.Tests/Capture/NewGameCaptureTests.cs`
- Create: `tests/ChronoTriggerAccessibility.Core.Tests/NewGame/NewGameNarratorTests.cs`

**Step 1: Write failing screen-coverage tests**

Control Descriptions tests require every visible binding and the selected Next button. Settings tests require Battle Mode, Graphics, Interface, Start Game, current values, focus positions, contextual help, and left/right changes. Name tests require entry instructions, current full name, five-character limit, language restriction, Defaults, Accept, and Yes/No confirmation.

Add a fatal coverage test: if a required screen enters but its authoritative focus/value source cannot be validated, the runtime must emit one accessible failure and log the missing source; it must not remain silent.

**Step 2: Install the confirmed screen hooks**

- `OpeManualScene::init` RVA `0x2ADB50`, post-success entry capture.
- `ModeSelectSteamScene::init` RVA `0x2A9C60`, post-success entry capture.
- `NameInputScene::update` RVA `0x2C2C50`, post-call state capture.
- Optional component init hooks only when runtime construction proves they are active: Battle Mode `0x29C280`, Graphics `0x2A74C0`, Interface `0x2D1490`.

Mode Select owns three `0x88`-byte row records in the vector at scene `+0x290/+0x294/+0x298`. Its authoritative composite focus is scene `+0x29C`, encoded as `row * 10 + subfocus`, where subfocus 0/1 is left/right and Start Game is 30. Hook callback RVA `0x2AB9E0`, snapshot state, call original, then publish the validated result. Event 1 focuses, event 3 changes a value (direction 3 left, 4 right), event 0 activates, and event 2 cancels. Read localized text IDs: Battle `(0x23,0x5A)` with values `(0x23,0xC0/C1)` and help `(0x3F,5/6)`; Graphics `(0x3F,0x31)` with values `(0x23,0xC7/C6)` and help `(0x3F,0x32/0x33)`; Interface `(0x42,0x1A)` with values `(0x41,0x55/0x54)` and help `(0x42,0x1C/0x1D)`.

Name text comes from the MSVC string at `NameInputScene + 0x350`; length is at `+0x360`, capacity at `+0x364`, and the supported maximum is five. Compare the complete validated string after each native update and emit only changes. The authoritative grid focus is column `+0x29C` (0..10), row `+0x2A0` (0..7), and page `+0x298`; capture only while `+0x294 != 0` and page is non-negative. Read the focused UTF-8 glyph from the pointer table at RVA `0x39B708` (VA `0x79B708`) using index `(row + page * 8) * 11 + column`, validating all pointers. Page-zero column 10 changes character page (`ABC`, `かな`, `カナ`) and the name-action dispatcher is RVA `0x2C1760`. Its action 2 schedules direct keyboard/IME activation at RVA `0x2C1B50`; the common close/restoration callback is RVA `0x2C1BA0`. Hook both delayed boundaries so the grid close, IME activation, resulting full-name edits, and every native IME close path are narrated in order. The close callback re-enables the three name-action widgets and preserves manager focus key 2; it does not reopen the character grid or construct confirmation. The name action manager has exactly three correlated controls (Defaults, Accept, and Keyboard name entry); confirmation has exactly two correlated choices.

Use the reviewed nsMenu focus setter RVA `0x1DD3E0` and Manager focus offset `+0x2C4` for generic menus. Control Descriptions' only action is Next through callback RVA `0x2AE840` (events 0 or 2 transition). Use runtime-observed localized labels for Defaults, Accept, and Yes/No; do not guess unresolved static action indices. Runtime sanity checks must validate ranges, row counts, pointers, and label/value presence before publishing any event.

**Step 3: Implement authored fallback text for the exact build**

The fallback covers only static visible instructions verified from the supported build. Dynamic labels and values always come from native state. The gamepad control list includes Confirm/Talk, Cancel/Walk, Open menu/Auto Battle, Warp/Toggle window position, page/tab switching, flee, pause, and directional movement.

**Step 4: Run tests and commit**

```powershell
git add src tests
git commit -m "feat: narrate New Game setup and naming"
```

## Task 7: Package, deploy, and launch

**Files:**

- Create: `README.md`
- Create: `THIRD-PARTY-NOTICES.md`
- Create: `tools/Package-Mod.ps1`
- Create: `tools/Deploy-Mod.ps1`
- Create: `tools/Verify-Deployment.ps1`
- Create: `Launch Chrono Trigger Accessible.ps1`
- Create: `tests/ChronoTriggerAccessibility.Mod.Tests/Packaging/PackageContractTests.cs`

**Step 1: Write failing package-contract tests**

The package test must require the mod DLL/deps, `ModConfig.json`, x86 `prism.dll`, Prism `LICENSE`/`NOTICE`, README, and exact SHA-256 manifest. Assert no x64 Prism binary and no game executable are packaged.

**Step 2: Implement packaging and deployment**

Build Release, stage to:

`C:\Program Files (x86)\Steam\steamapps\common\Spyro Reignited Trilogy\mod-tools\reloaded-ii\Release\Mods\chrono.trigger.accessibility`

Create/update:

`C:\Program Files (x86)\Steam\steamapps\common\Spyro Reignited Trilogy\mod-tools\reloaded-ii\Release\Apps\chrono trigger.exe\AppConfig.json`

with the exact game location, game working directory, and `chrono.trigger.accessibility` in both enabled and sorted mod arrays. Preserve unrelated existing profile properties and enabled mods.

The launch script sets process-local `DOTNET_ROOT_X86=C:\Users\User\AppData\Local\ChronoTriggerAccessibility\dotnet-x86`, verifies both 9.0.18 frameworks, and invokes:

```powershell
& 'C:\Program Files (x86)\Steam\steamapps\common\Spyro Reignited Trilogy\mod-tools\reloaded-ii\Release\Reloaded-II.exe' --launch 'G:\SteamLibrary\steamapps\common\Chrono Trigger\Chrono Trigger.exe'
```

Copy the launch script to the game root. Never overwrite an original game binary.

**Step 3: Read and verify deployment instructions**

README must state the supported hash, that NVDA/UIA/OneCore and listed Prism backends are included, first expected announcement, launch command, log location, troubleshooting, and uninstall steps. `Verify-Deployment.ps1` checks JSON, dependency files, PE architecture, hashes, x86 runtime, and Reloaded shared-hook dependency.

**Step 4: Run package tests and commit**

```powershell
git add README.md THIRD-PARTY-NOTICES.md tools 'Launch Chrono Trigger Accessible.ps1' tests
git commit -m "feat: package and deploy Chrono Trigger accessibility mod"
```

## Task 8: End-to-end verification and handoff

**Files:**

- Create: `docs/verification/2026-07-21-startup-main-menu-verification.md`
- Modify only if evidence requires it: source/tests/scripts from Tasks 1-7

**Step 1: Run the full automated gate**

```powershell
& 'C:\Program Files (x86)\Steam\steamapps\common\Yu-Gi-Oh! Legacy of the Duelist Link Evolution\YuGiOhAccessibility\.worktrees\accessibility-native-uia\.tools\dotnet\dotnet.exe' test .\ChronoTriggerAccessibility.slnx -c Release
& .\tools\Test-PrismWin32.ps1
& .\tools\Package-Mod.ps1
& .\tools\Deploy-Mod.ps1
& .\tools\Verify-Deployment.ps1
```

Record exact command output, test counts, DLL hashes, profile paths, and runtime versions.

**Step 2: Live Reloaded smoke test**

Launch through the accessible launcher. Verify from fresh logs that:

- Reloaded selected its X86 bootstrapper;
- the exact executable passed the hash/byte gate;
- all required hooks prepared and activated atomically;
- Prism initialized and named an active backend;
- the startup/title announcement event was emitted;
- no managed exception, hook-integrity fault, or missing dependency occurred.

Close the game without changing saves. If capture is unavailable, use semantic logs rather than treating image capture as proof.

**Step 3: Review requirements line by line**

Compare the design document and this plan against deployed behavior. Missing narration is a release-blocking defect. Run a final independent code review, fix all Critical and Important findings, and rerun the full gate.

**Step 4: Commit verification evidence**

```powershell
git add docs/verification
git commit -m "test: record deployed accessibility verification"
```

Keep the completed feature branch/worktree available for the user's live screen-reader test; do not delete it.
