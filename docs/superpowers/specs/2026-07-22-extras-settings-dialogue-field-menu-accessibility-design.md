# Chrono Trigger Extras, Settings, Dialogue, and Field Menu Accessibility Design

**Date:** 2026-07-22

**Status:** Approved for implementation by the user's instruction to build the requested menus and optional gameplay orientation without further confirmation

**Target:** Steam `Chrono Trigger.exe` version 2.0.0.1, PE32/x86, SHA-256 `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`

## Goal

Extend the deployed Reloaded-II and Prism mod from the title menu into four connected surfaces:

1. the title-screen Extras hub and Ending Log/detail navigation;
2. the shared title/in-game Settings screen;
3. ordinary field dialogue, including page changes and dialogue choices; and
4. the top-level in-game menu with the status information visible beside it.

The output must describe committed, visible game state in the player's selected language. It must not infer controls from key presses, announce text merely because it was loaded, expose later dialogue pages early, automate input, or reveal information a sighted player cannot see.

This milestone intentionally stops at the interaction boundary of paginated Movies/Illustrations/Sound lists and playback, controller/key rebinding, and deep item/tech/equipment/formation/save subpages. If the player crosses one of those boundaries, the mod announces the localized selected control followed by a concise accessibility coverage warning and how to return. It never leaves an entered interactive surface silent.

## Research basis

The installed executable was independently imported into fresh Ghidra 12.1.2 projects and checked against the exact supported SHA-256. MSVC RTTI, vtables, constructors, call sites, and decompiled state transitions establish that this is a native Cocos2d-x game, not Unity.

### Extras

The executable contains dedicated classes for `GalleryScene`, `GalleryNodeTop`, `GalleryNodeMovieTop`, `GalleryNodeIllustTop`, `GalleryNodeSoundTop`, `GalleryNodeEndingTop`, and `GalleryNodeEndingDetail`. Verified anchors include:

- `GalleryScene` vtable RVA `0x3B0EBC`, constructor/factory RVA `0x2A4C00`;
- `GalleryNodeTop` vtable RVA `0x3A5970`, constructor/factory RVA `0x1DB370`;
- Movies node vtable RVA `0x3A52C8`, constructor RVA `0x1D72B0`;
- Illustrations node vtable RVA `0x3A4F2C`, constructor RVA `0x1D5090`;
- Sound node vtable RVA `0x3A5630`, constructor RVA `0x1D8FC0`;
- Ending Log node vtable RVA `0x3A4AE8`, constructor RVA `0x1D3820`;
- Ending detail vtable RVA `0x3A475C`, constructor RVA `0x1D25D0`.

The game loads `msg/extra.txt`, `msg/ex_illust.txt`, and `msg/ex_ending.txt` through the localized message loader at RVA `0x1B7A30`. The visible hub is a two-by-two set of Movies, Illustrations, Sound, and Ending Log plus Back. Locked content and ending detail/review prompts are native localized state, not authored English assumptions.

`GalleryScene::switchNode` at RVA `0x2A52B0` provides a single authoritative construction boundary. Its actions are 0 hub, 1 Movies, 2 Sound, 3 Ending Log, 4 Ending Detail, and 5 Illustrations. The hub callback is RVA `0x1DC610`, Ending Log callback is RVA `0x1D4850`, and Ending Detail callback is RVA `0x1D35A0`.

Official Square Enix patch notes corroborate that Extras was added to the title menu with Movies, Illustrations, Sound, and Endings, and that unlock availability depends on game completion and viewed endings.

### Settings

The title dispatcher at RVA `0x2CFFF0` action 5 calls RVA `0x2D0280`, which creates the Steam configuration node with context flag 1. The in-game menu path at RVA `0x2A8FA0` creates the same node with context flag 0. Both therefore reuse one `nsMenu::MenuNodeConfigSteam` implementation:

- vtable RVA `0x3A702C`;
- constructor RVA `0x1ECB00`;
- init RVA `0x1ECC10`;
- update RVA `0x1ECC70`;
- context flag offset `+0x2F8`;
- row descriptor vector `+0x2C8/+0x2CC/+0x2D0`, stride `0x0C`;
- pager pointer `+0x2D4`;
- `ConfigPager` vtable RVA `0x3AC940`, builder RVA `0x1E1BE0`, init RVA `0x231F70`;
- row builder RVA `0x1E1E60`.

The verified title surface contains Display Settings, Controller Settings, License, and Back. Display Settings exposes Screen Mode (Full Screen, Borderless, Window) plus a runtime-enumerated Screen Size list when applicable. The verified in-game categories are Battle, Sound, Graphics, Controller Settings, Default Settings, and Quit Game. Their right-side rows and values are context-dependent; available resolutions must never be hard-coded.

The shared configuration builder obtains row labels, values, and help from `TextManager`, including battle mode/speed, message speed, movement, graphics, interface, screen mode, controller settings, defaults, and back controls as enabled by context. Official patch notes independently confirm title and game-menu Settings, movement behavior, controller/keyboard settings, default confirmation, screen mode, and menu-adjustable battle speed.

A follow-up data-flow pass resolved the runtime row structure. Each `0x98`-byte row stores its UI type at `+0x00`, localized label at `+0x04`, localized help at `+0x1C`, localized value-string vector at `+0x34/+0x38/+0x3C` with `0x18`-byte strings, and the committed displayed-value index at `+0x90`. The page descriptor vector remains `+0x2C8/+0x2CC/+0x2D0` with `0x0C` descriptors. The authoritative common mutation is RVA `0x1E3980`; it clamps and writes the index, invokes the native setter, refreshes the rendered row, and returns true only when the value changed. This one post-original boundary replaces setting-specific Screen Mode/Screen Size mutation hooks.

### Dialogue

Field dialogue is a dedicated `MsgWindow`, not a global text-loading side effect:

- vtable RVA `0x3A0624`;
- object size `0x340`;
- constructor RVA `0x15BF30` and factory RVA `0x15BE80`;
- field-scene ownership established at RVA `0x2BBB20` through `(scene + 0x290) + 0xBFC`;
- init RVA `0x194210`;
- open/parser trigger RVA `0x195B40`;
- authoritative update RVA `0x197530`;
- close RVA `0x195C70`;
- parser/page construction RVA `0x195FB0`.

Relevant validated state is:

- active byte `+0x2BD`;
- four rendered line labels `+0x298..+0x2A4`;
- parsed line vector begin/end `+0x308/+0x30C`, with 24-byte MSVC UTF-8 strings;
- per-line flags vector `+0x314`;
- current line `+0x2C0`;
- visible-page base `+0x2C4`;
- phase `+0x2CC` (typing, waiting, fast reveal, page transition, or choices);
- selected choice `+0x2D0` and choice count `+0x2D4`.

The update boundary is authoritative because it advances only the line/page currently being rendered and owns choice focus. Reading one current parsed line when its render begins preserves page order and avoids announcing future conversation text.

Per-line flags are also authoritative: `0x10` choice row, `0x08` wait for confirm, `0x02` page transition, and `0x04` close after line. Flag `0x01` controls rendering alignment and is not evidence of a speaker. In choice phase the exact visible range is `currentLine - choiceCount` through the current line, and selected choice may legitimately be `-1` before controller focus is established.

### In-game menu

The game intentionally has separate controller/keyboard and touch/mouse menu implementations, selected at RVA `0x2A83C0`. Verified top-level anchors include:

- `FieldMenu` constructor RVA `0x2BB4E0`, init RVA `0x2BB910`;
- `MenuSteamScene` vtable RVA `0x3B1714`, constructor RVA `0x2A82D0`, init RVA `0x2A8410`;
- controller/keyboard `ClassicMenuNodeTop` vtable RVA `0x3A4024`, factory RVA `0x1D03D0`, builder RVA `0x1D0560`, manager stack at `+0x2C0`, status bar at `+0x2CC`;
- touch/mouse `MenuScene` vtable RVA `0x3B6670`, `MenuNodeTop` vtable RVA `0x3AA058`, factory RVA `0x2215A0`, builder RVA `0x221660`;
- generic `nsMenu::Manager` focus setter RVA `0x1DD3E0`, current key at manager `+0x2C4`.

Both builders create exactly seven localized top rows; the classic layout can conditionally disable two. Their status blocks contain localized visible play time, currency, a conditional status/location-like label whose semantic category must not be guessed, and up to nine party cards in native order: zero to three active cards followed by zero to six reserve cards. Active cards render a name plus values. Reserve cards render a character portrait plus compact values, so accessible identity must pair that portrait's character ID with the game's own runtime/player-renamed name rather than leaving an anonymous stat block or using a fixed character table. The deeper Item, Tech, Equipment, Formation, Settings, and Save/Load nodes use distinct managers, pagers, and grids. This milestone validates the top node and its visible status block only; it does not pretend that generic focus alone makes each deep grid accessible.

The follow-up renderer trace establishes how those strings reach the screen. UTF-8 menu Labels are created at RVA `0x2400B0`; exact return-address whitelists inside the validated classic/touch builder scopes identify seven row captions, rendered active-member names, localized stat rows and values, play time, and currency. Compact party formatter RVA `0x239580` serves touch active cards and both reserve-card layouts. Classic active cards instead use formatter RVA `0x23A140`, whose three rows have a separate audited branch grammar. Both builders construct active cards first and then scan reserve slots `3..8`. Reserve identity is a portrait keyed by the slot's character ID: the capture reads global object pointer RVA `0x41B4C4`, reproduces the exact native display gate, follows the roster pointer at `G+0x28`, and pairs each present ID `0..7` with the runtime/player-renamed MSVC string at `G+0x1908+id*0x18`, the same storage used by resolver RVA `0x014830`. The conditional status/help text enters glyph renderer RVA `0x22E080` as an x86 MSVC UTF-16 string, only from return RVA `0x22F3BD` inside validated `StatusBar` scope RVA `0x22F160`. Capturing immediate strings and a mutation-checked reserve ID/name snapshot is authoritative; retaining Cocos child pointers would be stale after rebuild.

## Chosen architecture

### Semantic event families

Add two pure event/narrator families routed by `AccessibilityState`:

- `MenuAccessibilityEvent` for screen presentation, row focus/value/help, visible status summary, activation, exit, and explicit unsupported-subpage boundaries;
- `DialogueAccessibilityEvent` for dialogue open, current visible line, choice presentation/focus, and close.

Events contain immutable copies of localized visible strings and validated positions/counts. Narrators validate the complete payload before emitting anything, suppress exact duplicates, reset identities on exit, and fault their own family after one coverage failure.

Menu focus text uses this shape when fields are present:

`Label: Value, 3 of 10. Help text.`

Dialogue lines queue rather than interrupt one another. A newly presented choice list interrupts the typewriter queue once, announces every visible choice in order, then announces the selected choice and position. Subsequent choice movement interrupts with only the newly selected choice.

### Native capture layer

Add pure readers in the Native project:

- a bounded MSVC string-vector reader with pointer, stride, count, UTF-8, and maximum-length validation;
- `DialogueCapture`, which snapshots only active committed line/choice state from the audited `MsgWindow` layout;
- `MenuCapture`, which correlates constructed controls, manager keys, exact row descriptors, their committed value index, and localized strings during an exact builder scope;
- a top-menu render capture that accepts copied UTF-8/UTF-16 strings only from audited return addresses under a validated classic/touch builder or owned `StatusBar` scope, then validates the complete ordered party/time/currency/status snapshot.

No reader dereferences a pointer until its containing object vtable and owner chain match the exact supported build. Vector differences must be non-negative, aligned to the audited stride, and bounded. Counts, focus keys, phases, and party values have explicit maximums. A malformed required snapshot reports a coverage failure instead of returning partial narration.

### Hook ownership and shared native fanout

One native original must be called exactly once for each shared hook. Generalize `SharedNativeHookFanoutFactory` from a single New Game observer to an ordered observer list and add the already-owned custom-button/control-binder boundaries. Each observer runs after the native original and is isolated by the unmanaged exception guard. An observer failure is reported without changing the native return value or preventing later observers from receiving the event.

The existing startup/title and New Game hook sets retain ownership of their current hooks. A new `MenuDialogueHookSet` owns only the new class-specific entries and observes the shared localized-text/focus/control events. This avoids duplicate detours at `TextManager::getMsg`, `nsMenu` focus, custom-button construction, and control binding.

The new class-specific hooks are:

- Extras hub/submenu construction or on-enter boundaries needed to bracket dynamic capture;
- `MenuNodeConfigSteam` constructor, builder, and destructor for context, complete dynamic construction, and lifetime, plus common value mutation RVA `0x1E3980` for post-refresh changes;
- `MsgWindow` open, update, and close boundaries for visible line/page/choice transitions, plus an exact five-byte assembly probe at the audited confirm-close call site for choice activation classification;
- both controller/keyboard and touch/mouse top-menu builders for field-menu construction, the menu Label factory RVA `0x2400B0` for scoped UTF-8 row/status strings, `StatusBar` scope/UTF-16 render RVAs `0x22F160`/`0x22E080` for the conditional visible text, and audited top-root/StatusBar destructors for immediate lifetime invalidation.

Every function-entry hook receives a `GameVersionCatalog` entry with its exact calling convention, RVA, and stable prologue byte contract. The choice-confirm assembly call site is represented separately as non-callable executable code with exact bytes. All function and assembly-site contracts are verified against the installed PE before any hook activates. Preparation remains atomic and fail-closed. Reloaded.Hooks 4.3.2 must use explicit `ExecuteFirst`, five-byte relative-jump options at that call site; its default x86 absolute jump is six bytes and would overwrite the return instruction boundary.

### Lifecycle

The menu/dialogue hook set keeps an activation epoch. Every callback captures the epoch before the original, calls the original exactly once, and publishes only if the epoch remains current afterward. Disable increments the epoch, clears all active owners/managers/snapshots, and emits no late output.

Builder scopes are thread-local and non-nestable. A scope records only messages, controls, and managers produced during the audited builder call. It is always disposed in `finally`, including when the native original throws.

## Narration behavior

### Extras

On entry, announce `Extras`, then the localized focused tile and its position among the five hub controls. Focus movement announces Movies, Illustrations, Sound, Ending Log, or Back with position and unavailable state when the game exposes it.

On a locked activation, re-announce the native localized locked help. The supported build only plays invalid feedback and remains on the hub; it does not show a visual dialog, so the mod must not invent a prompt or dismissal control. Never describe locked content as available.

Ending Log announces its localized title and all 19 ordered row positions. Locked rows speak their visible numbered position plus `locked, question marks`; hidden ending titles and requirements are never revealed. An unlocked ending detail announces its visible title, Requirements text, localized requirement, and Review/Back controls. Crossing into ending replay announces the selected localized entry and a concise coverage boundary.

Movies, Illustrations, and Sound announce the selected hub tile followed by the current milestone's explicit coverage boundary and return instruction. Their 10, 32, and 65 entries use paginated slot-to-content mappings that are not yet sufficiently validated; pretending a visible-slot index is a final content index could announce the wrong title. Those lists and media playback are a later milestone.

### Settings

On entry, announce `Settings`, its title/in-game context only when that context is visibly meaningful, and the focused localized row with value, position, and help. Every focus or value change publishes a complete fresh row snapshot. Defaults confirmation announces its localized prompt and both choices; cancel/back announces closure.

Title Settings covers its four main categories, all Screen Mode values, the dynamic Screen Size row/list, and the Controller Settings category choices. In-game Settings covers its six main categories and their visible Battle, Sound, Graphics, Interface, Movement, and Default confirmation rows. Main Settings rows are dynamically captured, so context-dependent row counts, connected-device state, supported resolutions, and the selected language are preserved.

Entering Gamepad Settings, Keyboard Settings, or License content announces the selected control followed by the current milestone's explicit coverage boundary and return instruction. The key-rebinding `Default`/`OK` workflow and license pagination require their own validated modal/page correlations before they can claim parity.

### Dialogue

When `MsgWindow` becomes active, enter dialogue state without a generic preamble. As each parsed line begins rendering, queue that line once. Do not announce later vector entries until the native current-line index reaches them. Reset line identity when the visible-page base advances so repeated visible text on a later page is announced again.

When phase 4 exposes choices, validate choice count and contiguous flagged line records, announce all visible choices in order, then the selected choice and position when selection is nonnegative. Focus movement announces the new choice. Choice activation is classified only when the audited five-byte call at RVA `0x19768A` sets a thread-local, window-correlated marker immediately before the native close; ordinary close and auto-end callers must not be announced as a selected choice. The normal close detour does not attempt to inspect its caller's return address. On close, clear the marker and the entire dialogue snapshot so the next NPC can repeat identical text legitimately.

No speaker name is inferred from a nearby sprite or portrait. A name is included only if the game exposes it as visible text in the committed message state.

### In-game top menu

On entry, announce `Menu`, the focused localized top-level row and position, then queue the visible status summary in on-screen order. Each party card is announced with its active rendered name or its reserve portrait's runtime/player-renamed identity followed by the three localized stat rows; active cards precede reserves exactly as displayed. Capture localized text from the validated top root/status subtree and use only the audited reserve identity mapping; do not reinterpret other backing save fields. This preserves character/party values, currency, play time, and every conditional localized line without inventing its semantic category.

Top-level focus changes announce the localized row and position. Settings enters the supported main Settings surface. Selecting another deep subpage announces the selected localized row and the explicit coverage boundary; the user is told to press Cancel to return to the accessible top menu.

## Error handling

The following are fatal coverage defects for the affected family:

- unknown vtable/owner/manager chain;
- unreadable or misaligned vector;
- missing localized label for an interactive control;
- focus key without exactly one correlated control;
- invalid dialogue phase, line index, choice range, or string;
- incomplete Settings row/value/help snapshot;
- a class-specific hook invoked with an unexpected context.

The dispatcher logs the exact diagnostic, outputs it through Prism with interruption, and shows the independent native accessible error sink. It never silently falls back to OCR, fixed English menu labels, raw action IDs, or the last known value.

## Testing

Development is test-first. Automated coverage includes:

- narrator entry/focus/value/summary/choice/exit and duplicate-reset behavior;
- all invalid payloads failing before partial output;
- MSVC vector and native snapshot validation, including unreadable pointers and overflow;
- each builder's dynamic localized row correlation;
- dialogue line-by-line progression, repeated later-page text, choices, and close/reopen;
- original-call-once, pre/post exception, activation epoch, disable, and observer isolation behavior;
- exact hook catalog RVAs, byte prefixes, function delegate types/calling conventions, assembly-site kind and options, executable ranges, and installed-file bytes;
- composition proving every shared native address is detoured once;
- package and deployment completeness.

Live verification uses normal Steam startup, confirms Reloaded x86 injection and Prism's NVDA backend in fresh logs, navigates Extras and Settings without changing saves, and checks semantic events rather than OCR. Dialogue and the in-game menu receive a non-destructive smoke test only if an existing safe gameplay state is available; the user's New Game run remains the final interaction test.

## Alternatives rejected

- **OCR or graphics capture:** unavailable for this OpenGL/full-screen window in NVDA and the automation capture stack, and cannot reliably express focus, values, locked state, or dialogue page boundaries.
- **Global `TextManager` speech:** would announce strings merely loaded for construction, including hidden and future content.
- **Per-frame scene-graph scraping without class validation:** could correlate stale labels from inactive nodes and would hide layout/version drift.
- **Treating all `nsMenu` screens as accessible from one focus hook:** deep grids expose values and context outside the generic manager; focus alone would omit sighted information.
- **Fixed English tables:** would break localization and dynamic availability.

## Packaging and deployment

No new third-party dependency is required. The existing reviewed Reloaded-II, Reloaded.Hooks, Prism 0.17.3 x86, Ultimate ASI Loader, and .NET x86 runtime remain in place. Packaging updates the mod assemblies, README scope, hashes, tests, and verification record. Deployment uses the existing transactional scripts and preserves normal Steam launch behavior.
