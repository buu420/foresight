# Native Name Entry: localized defaults and observation limits

## Report and identity

The tester reports a New Game failure after selecting settings: `Name Entry capture is incomplete: Name exceeds the audited maximum of five UTF-16 code units.` The October 13 `Logs (1).zip` contains only `bootstrap.log` and `proxy.log`, which confirm loader activation but contain neither the failure nor the captured name. The tester's language and any translation mods are unknown.

Fresh installed identities:

- `Chrono Trigger.exe` SHA-256: `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
- `libcocos2d.dll` SHA-256: `8A53EF5BFFD345D4EEE0E2D91152C539FCEB71725DB44A72D2E7513DF55ECD2F`.

Ghidra 12.1.2 exports came from the matching executable project, in read-only mode. The private proof set is `artifacts/research/name-entry-0359`: Root's `ReviewNameRules.java` and `native-audit`, plus Claude's decompilations and exact DLL disassembly. These are native pseudocode and assembly evidence, not recovered original source.

## The displayed string is the native name

`NameEdit::getText` at game RVA `0x2BF880` reads the text field's LabelProtocol. Its validated DLL getter at `0x2D6762` is `8D 81 0C 03 00 00 C3`: `LEA EAX,[ECX+0x30C]; RET`. With the LabelProtocol at text-field offset `0x278`, the current MSVC string is at `+0x584`.

DLL setter `0x2D6EB6` stores the input at `+0x30C`. Placeholder text at `+0x324` is a separate value used when the input is empty. The reader's current-name address is correct. Scene offset `+0x350` is a saved grid-entry name and remains unsuitable as a fallback.

The naming builder `0x2C08D0` reads the character index from scene `+0x290`, then calls `0x2BEC30` to request bank `0x38`, line equal to that index. The loader `0x1B8731–0x1B8753` maps `msg/player.txt` to bank `0x38`. `NameEdit::setText`, `0x2BF7E0`, installs that localized default without a length check.

## Editing rules are not a universal capture bound

The grid append body `0x2C1BE0` converts the current string to UTF-16 and appends only when it has fewer than five code units. This rule does not limit initial defaults.

Keyboard processing in `NameEdit::update`, `0x2BF170`, compares edited text against **all eight** names in the current language's bank `0x38` through `0x2BEC60`. A matching default remains whole, including a different character's default. Other edited names are cut to five UTF-16 units by `0x2BE9C0`; it backs off if the cut would split a surrogate pair. The Defaults action sets the current character's own default. Native Accept requires a nonempty name; the game builds its own confirmation.

The builder calls `UICCTextField::setMaxLength(8)` through IAT `0x3857DC`, DLL export `0x2866CA`, but this is **not an enabled eight-character limit**. That setter stores `+0x5F8`; insertion checks it only when `+0x5F4` is set by `setMaxLengthEnabled`, DLL `0x2866F0`. The constructor initializes that flag to zero, and the audited game imports/writes do not enable it. The DLL's insertion limit, when enabled, counts UTF-8 characters. Native keyboard text can temporarily exceed five before the game update normalizes it, including while Enter delays processing. The reader must not permanently fault because it observes such text.

## Installed default-name coverage

| Game language | Defaults exceeding five UTF-16 units |
| --- | --- |
| English | None |
| German | Chrono, Frosch: six each |
| Spanish | None |
| French | Chrono: six |
| Italian | None |
| Japanese | None |
| Korean | None |
| Simplified Chinese | None |
| Traditional Chinese | None |

This reconstructs the exact reported error for French/German default names. It does not establish the tester's language. The same mistaken guard could reject another native or modified default.

## Implementation and verification boundary

Capture observes actual native name text rather than enforcing a grid or keyboard editing limit. Entry and by-value confirmation retain the existing 4,096-byte allocation bound, capacity checks, exact NUL termination, strict UTF-8 decoding, Unicode control restrictions, and native owner/vtable/getter validation. Core narration trusts the validated native name and no longer imposes a second five-unit rule. The inaccurate global “Maximum five characters” announcement is removed. No native input, confirmation arguments, names, language flags, saves, or physics are changed.

Regressions cover localized defaults, current text instead of stale scene text, longer native text including supplementary Unicode, keyboard narration, and unchanged forwarding of all six native 32-bit string fields to confirmation. Fault tests continue to cover malformed capacity, allocation, UTF-8, terminators, controls, and unverified native pointers. These are automated fixtures and native inspection, not live controller or speech tests. The original reproduction failed before the change; the affected suites pass after it.

Native localized labels and Unicode names remain readable. Mod-owned announcements and existing recorded descriptions are mainly English. Full mod localization and a live naming run in all nine game languages have not been verified.

Independent review found another instance of the same mistake in `MsvcStringReader.TryReadName`: a five-grapheme limit rejected stored defaults after accepting them. Battle party capture, formation reserve identity and navigation character substitution use this reader. Its read-time count limit is removed while its allocation/UTF-8/visible-text validation is retained. Failing-before regressions reproduce the loss of Chrono/Frosch in battle and navigation; the shared reader and visible reserve-card fixtures verify the corrected behavior. Stored-name inspection uses the already audited native `global+0x1908+character*24` table; no saved name is changed.

For engine-source context, Cocos's original [TextFieldTTF implementation](https://raw.githubusercontent.com/cocos2d/cocos2d-x/cocos2d-x-3.5/cocos/2d/CCTextFieldTTF.cpp) distinguishes input text from its placeholder. Installed-binary evidence above establishes this build's layout and game-specific naming behavior.
