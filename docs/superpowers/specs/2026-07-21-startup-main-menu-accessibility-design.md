# Chrono Trigger Startup and New Game Accessibility Design

**Date:** 2026-07-21

**Status:** Approved for implementation by the user's instruction to proceed and test the result

**Target:** Steam `Chrono Trigger.exe` version 2.0.0.1, PE32/x86, SHA-256 `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`

## Goal

Provide screen-reader access from process startup through the title screen, title menu, Control Descriptions screen, New Game configuration, and initial character-name confirmation. The mod must report the information a sighted player receives without adding gameplay information or automating input.

This first milestone is deliberately semantic. It reads game state and menu events from native code rather than using OCR, pixels, key guesses, or fixed delays as its primary source of truth.

## Platform and loader

The shipped game is a 32-bit native Cocos2d-x application, not Unity. The mod therefore uses:

- Reloaded-II 1.30.2 as the loader and hook host.
- An AnyCPU Reloaded-II managed mod targeting `net9.0-windows`; Reloaded's x86 loader runs it inside the 32-bit game process.
- Reloaded.Hooks for x86 native detours and signature verification.
- Prism 0.17.3, built from its official source as a 32-bit shared library, for speech and braille output.

MelonLoader is out of scope because the target is not Unity. Prism is accessed through its documented C ABI. The mod creates one best-fit backend using `prism_registry_create_best`, keeps all calls serialized, and frees the backend before `prism_shutdown`.

## Supported executable and safety gate

Hooks are version-specific. Initialization follows a fail-closed sequence:

1. Resolve the loaded main-module path and architecture.
2. Verify the complete executable SHA-256 against the supported hash.
3. Resolve every required function using a version catalog of RVAs and expected instruction bytes.
4. Verify that each resolved address is inside an executable PE section and that every expected byte sequence matches.
5. Initialize Prism and a backend only after a visible top-level game window exists.
6. Install hooks only after all required checks succeed.

No partial accessibility mode is presented as working. A failed executable check, hook check, Prism initialization, or backend creation is written to the Reloaded log and shown in a blocking, topmost native message box with a concrete error. Optional descriptions may be disabled independently only when their absence cannot hide an interactive control.

## Architecture

### Mod entry and lifetime

`Mod` owns initialization, hook lifetime, logging, and orderly Prism shutdown. Reloaded invokes it once. Hook objects and native delegates remain rooted for the process lifetime; the mod does not attempt to unload live native hooks.

### Version catalog and native bridge

`GameVersionCatalog` contains the exact executable identity, RVAs, calling conventions, and byte contracts. `NativeHooks` translates detour callbacks into small semantic events and immediately calls the original game function. It does not construct narration text.

Known native anchors include:

- `SceneManager::create` RVA `0x297860`
- `SceneManager::NextScene` RVA `0x297B60`
- `SceneManager::pushScene` RVA `0x298410`
- `SceneManager::popScene` RVA `0x298470`
- `SceneManager::popAllScenes` RVA `0x2984E0`
- current-scene global RVA `0x41C3E8`
- `SqexLogoScene::create` RVA `0x2CC3C0`
- `TextManager::getMsg` RVA `0x1B92D0`
- `NameInputScene::update` RVA `0x2C2C50`
- generic `nsMenu::Manager` focus setter RVA `0x1DD3E0` (`Manager + 0x2C4` is the current index)
- `ModeSelectSteamScene` callback RVA `0x2AB9E0` (`scene + 0x29C` is the composite focus)
- Control Descriptions Next callback RVA `0x2AE840`
- name-action callback RVA `0x2C1760`
- `TitleScene::init` RVA `0x2D0C50`
- `TapToStartMode::update` RVA `0x2CF440`
- `TitleMenuMode` setup RVA `0x2CF560`
- title-row builder RVA `0x2CF7A0`
- title row factory RVA `0x2CD7A0` (called once per enabled row with its localized string record in `ECX`)
- title selection callback RVA `0x2D12A0`
- title action dispatcher RVA `0x2CFFF0`

The implementation may add stronger focus-change boundaries identified during Ghidra analysis, but it must retain the executable and expected-byte checks.

Startup scene identities for this build are 2 for the Square Enix logo, `0x1E` for `DemoMovieScene`, and 3 for `TitleScene`. Title composition is captured from actual row-factory calls made inside `TitleMenuMode::enter`, not reconstructed from save-data predicates.

All detours use explicit Reloaded x86 calling-convention metadata. In particular, `std::function::_Do_call` hooks treat `ECX` as the closure and preserve whether each stack argument is passed by value or by pointer; an ABI guess is a release-blocking defect.

### Semantic state reducer

`AccessibilityState` is a pure, testable reducer. It accepts events such as scene entry, screen entry, focused row, value change, text edit, button focus, confirmation opening, and screen exit. It returns zero or more `Announcement` values.

The reducer owns:

- first-entry screen summaries;
- focus narration;
- value-change narration;
- duplicate suppression;
- position text such as “2 of 4” when the game exposes a stable ordered list;
- interruption priority;
- cancellation of obsolete scheduled startup descriptions.

Hook callbacks never speak directly. This prevents per-frame chatter and makes missed information testable.

### Prism output

`PrismOutput` converts UTF-16 .NET strings to UTF-8 and calls `prism_backend_output` so a single announcement reaches both speech and braille-capable backends. Focus changes and error messages interrupt previous output. Longer screen instructions queue only when they remain relevant. Calls are made through one synchronized dispatcher because a Prism backend instance is not thread-safe.

The backend is created after the game window is visible so the UI Automation backend remains viable. If a backend disappears during play, the mod attempts one controlled recreation; failure becomes an explicit accessible error rather than silent loss.

## Narration behavior

### Startup

The mod announces scene changes that otherwise rely on visuals:

- “Square Enix.” when the publisher logo scene begins.
- “Opening movie.” when the cinematic begins, followed by concise authored descriptions of major visual-only beats at verified points in that scene.
- “Chrono Trigger.” on the title end card/title reveal.
- “Press confirm.” when the title prompt is actually accepting input.

Timed opening-movie descriptions are scoped to the movie scene and canceled immediately if it is skipped. They describe imagery only and do not reveal later gameplay state. Audio that already communicates an event does not need redundant narration unless the visual meaning would otherwise be unclear.

### Title menu

The menu is dynamic across clean, intermediate, and populated profiles. The mod reads or captures the game’s constructed localized rows rather than assuming a fixed count. Observed variants include:

- clean profile: New Game, Extras, Settings, Quit;
- intermediate profile: Resume, New Game, Extras, Settings, Quit;
- populated profile: Resume, New Game, Load Game, New Game +, Extras, Settings, Quit.

On menu entry, the focused item is announced with its position. Each focus change announces the localized label, position, and disabled state if applicable. Activating an item announces the transition only when useful and never substitutes a guessed action ID for the visible label.

### Control Descriptions

On screen entry the mod announces “Control Descriptions” and the selected input layout. It makes every visible binding available in reading order. For the observed gamepad layout this includes:

- Confirm / Talk
- Cancel / Walk
- Open menu / Auto Battle
- Warp / Toggle window position
- Toggle page / Toggle tab
- Flee from battle
- Pause battles temporarily
- Directional movement

The selected Next button is announced as an actionable control. Labels come from game state/localized text where available; the authored fallback is used only for the exact supported build.

### New Game configuration

On entry the mod announces the screen purpose, then the focused row, current value, position, and contextual help. It covers:

- Battle Mode, including ACTIVE and WAIT and the visible explanation;
- Graphics, including Original and available alternatives;
- Interface, including Gamepad/Keyboard and available alternatives;
- Start Game.

Left/right value changes are announced immediately. Repeated polling of an unchanged row or value produces no output. Selecting Start Game announces the resulting transition.

### Name entry and confirmation

On entry the mod announces “Enter a name,” the current/default name, the five-character limit, and the visible language/symbol restriction. It announces:

- the currently focused character or editing control;
- the complete resulting name after an edit, deletion, default restoration, or cursor operation that changes visible state;
- Defaults and Accept buttons when focused;
- the full proposed name when Accept is chosen;
- the Yes/No confirmation prompt and focused choice.

The mod does not announce only the pressed key: the resulting visible name is the authoritative output.

## Localization

Interactive labels are obtained from the game’s `TextManager` or captured as the game builds the relevant row. This preserves the player’s selected language and dynamic title-menu composition. Authored visual descriptions and emergency errors are English in this milestone. If text extraction fails for any required interactive screen, initialization or that transition fails loudly rather than becoming silent.

## Error handling and observability

The mod writes a timestamped diagnostic log containing:

- executable identity and version-gate result;
- each required hook name, resolved address, and byte-verification result;
- Prism library/backend selection and errors;
- semantic events and emitted announcement text;
- hook exceptions and backend recreation attempts.

The log is suitable for diagnosing the user’s first live test without screen capture. It never records save contents beyond the small visible strings that were announced.

## Packaging and deployment

The release package contains:

- the Reloaded-II mod manifest and 32-bit mod assembly;
- all required Reloaded managed dependencies declared in the manifest;
- the 32-bit Prism shared library and backend libraries required by the official build;
- Prism license/notices and a third-party notice file;
- a per-user x86 .NET 9 runtime prerequisite and launcher environment that make Reloaded's x86 host resolvable without changing the game binaries;
- a README with supported executable hash, launch instructions, expected first announcements, troubleshooting, and uninstall steps.

Deployment registers `Chrono Trigger.exe` with the existing Reloaded-II installation, installs/enables the mod for that application, and leaves a convenient launch entry in the game directory. It does not overwrite original game binaries.

## Verification

Automated verification includes:

- reducer tests for every required screen and transition;
- duplicate/interruption/cancelation tests;
- executable contract tests against the installed game image;
- PE architecture, RVA range, executable-section, and expected-byte tests;
- Prism ABI/load/output/shutdown tests in a 32-bit helper process;
- package-manifest and dependency-completeness tests;
- deployment checks against the Reloaded application and mod configuration.

Live verification launches the game through Reloaded-II, confirms the mod and every required hook in logs, confirms a Prism backend is active, and exercises as much of the startup path as possible without altering or destroying the user’s saves. The user performs the final screen-reader interaction test.

## Non-goals for this milestone

- gameplay navigation, combat, inventory, dialogue, shops, or maps;
- OCR as a fallback for unsupported versions;
- input automation or macros;
- information unavailable to a sighted player;
- support for executable hashes other than the exact installed build.
