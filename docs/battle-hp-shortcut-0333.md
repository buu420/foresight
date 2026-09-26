# Battle HP shortcut, version 0.3.33

HP inspection now uses Shift+H for the party member selected with 1, 2 or 3. Plain H retains its native action. M still reads MP. Press Shift before H; holding H does not repeat HP speech. Ctrl, Alt and Windows combinations are not captured.

The HP command and the decision to consume H come from the same native keyboard snapshot. The capture lasts until that snapshot reports H released, even if Shift is released first, the game loses focus, or battle ends. A held H does not become an HP command when Shift is added later. Battle entry and the initial keyboard snapshot do not create a press from an already held key. Leaving battle discards any pending HP announcement.

## Native evidence

The supported Steam executable remains unchanged, SHA-256 `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.

Ghidra's existing full engine export and a fresh read-only disassembly inspection establish the following:

- GameController update RVA `18F3F0` calls GetKeyboardState at `18F585`, with its 256-byte local buffer at `EBP-110`.
- The new verified instruction hook at `18F58B` covers `33 C9 0F 1F 00` (XOR ECX,ECX and a three-byte NOP), before the scan at `18F590`. No incoming branch enters the middle of these five bytes.
- The callback preserves general registers and flags, passes the local buffer to a Cdecl callback, and cleans its argument. The original instructions execute afterward. Subsequent floating-point operations reload their spilled input.
- Only the high bit of H's buffer byte is cleared for a captured press. Other keys, toggles, physical keyboard state and gamepad input are unchanged.
- The Cocos callback mapper at `18E4E0` also recognizes H, but writes the temporary keyboard mask at controller `+2D8`. The update replaces that mask at `18F5AA` before publishing current/pressed/released/repeated input at `+2DC/+2E0/+2E4/+2E8`. The inspected native getters read those published fields.

Microsoft documents the buffer size, high-bit meaning and message-queue timing of [GetKeyboardState](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getkeyboardstate). [GetAsyncKeyState](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getasynckeystate) samples physical state independently. For that reason, HP speech is queued by the consuming native snapshot; the separate physical sampler cannot generate HP. This avoids announcing HP for a chord whose H later reaches the game without Shift.

Research artifacts are in the ignored local directory `artifacts/research/hp-shortcut-0333`: Ghidra inspection, exact hook-byte proof, failing timing regression and passing test results. The runtime keeps 171 verified hooks after this addition.

## Validation

The focused battle and composition suite passes 52 tests. Regressions cover plain H, Shift+H, repeat prevention, release order, other modifiers, startup/battle/focus transitions, physical/native timing disagreement, exact preservation of other snapshot bytes, hook activation/retirement, callback fault containment and selected-member HP speech through the battle session.

The release suites pass 1,094 Mod tests (excluding the three unrelated audio playback tests) and 851 Native tests. The production hook-factory regression also verifies the exact instruction-site exception and rejects execution after the original instructions. A fresh check of the installed game executable verifies all 171 hook signatures. Independent review found no remaining blocker after the timing and factory corrections.

Automated validation does not establish a live battle keyboard test. Release packaging and installation results are recorded below when completed.
