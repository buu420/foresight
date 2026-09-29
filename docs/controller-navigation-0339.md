# Controller navigation, version 0.3.39

The spoken navigation menu uses the existing categories, destinations and path planner.
It works at the field, world-walking, Epoch and Dactyl input boundaries. It does not pause
the game or select dialogue, items or story choices.

| Control | Action |
|---|---|
| Right-stick click / R3 | Open or close navigation |
| L1 / LB | Previous category |
| R1 / RB | Next category |
| D-pad Up / Down | Previous / next destination |
| Square / Xbox X | Close navigation and start automatic walking |
| Cross / Xbox A | Close navigation and start spoken, one-leg-at-a-time guidance |
| Circle / Xbox B | Close navigation |

Opening stops an existing route and reads the category and destination. Shoulders, D-pad
and face-button shortcuts apply while this menu is open. The keyboard shortcuts remain
available. Keyboard navigation takes over if used during controller navigation.

The button that closes the menu or starts a route stays consumed until released, so it
cannot also trigger the game's Confirm. The stick must return to its dead zone before
controller input is returned to the game. Press R3 again to stop a route and reopen the
menu. Ordinary movement also cancels automatic walking after the release fence clears.

## Native input and device support

The matching Ghidra export proves that the game polls WINMM `joyGetPosEx`. R3 has no entry
in its button table or configurable face-button mapping. A probe at RVA `18F0D5`, after
that API returns and before native button, POV and axis events, reads and optionally
centres that snapshot. Keyboard input is merged later and is unaffected by this hook.
The hook requires the supported executable hash and exact seven-byte instruction.
See [the native audit](controller-input-native-0339.md) for disassembly and call chains.

The default layout matches the game's Xbox order, including Steam's virtual gamepad.
Recognized raw Sony devices use the Windows DirectInput ordering documented by
[SDL's controller database](https://github.com/libsdl-org/SDL/blob/SDL2/src/joystick/SDL_gamecontrollerdb.h):
DualShock 4 products `054C:05C4` / `054C:09CC` and DualSense `054C:0CE6`, with at least twelve
reported buttons. Unknown devices retain the game's order. The mod logs each connection's
name, manufacturer, product, button count and selected layout.

The adapter reads the documented [JOYINFOEX fields](https://learn.microsoft.com/en-us/windows/win32/api/joystickapi/ns-joystickapi-joyinfoex)
and [joystick capabilities](https://learn.microsoft.com/en-us/windows/win32/api/joystickapi/nf-joystickapi-joygetdevcapsw).
Capability IDs and raw Sony operation were not observed on hardware in this session:
the read-only inventory found no connected controller. Steam Input must expose R3 as
right-stick click; a Steam remap to a keyboard key or another button cannot be identified
as R3 at this boundary.

## State and verification

Physical edges and release fences are tracked per device. Other devices cannot repeat
the owning controller's menu commands. Closing also fences the other connected devices,
covering the case where a physical pad and its Steam translation report the same press.
A failed poll never proves physical release, so reconnecting with a consumed button held
does not leak it into the game.

The global joystick hook only queues commands under a recent player-input lease. Fresh
navigation capture and command execution happen at the field/world/vehicle boundary,
where task-local data is valid. Queued commands preserve their order. A changed scene,
menu, dialogue, battle, lost focus, invalid frame, disabled mod or disconnected controller
closes the menu and cancels its route. A poll timeout also cancels controller navigation
if the device driver stops invoking the native joystick loop. No input is automatically
resumed after those transitions.

Regression coverage includes all requested controls, held presses and sticks, reconnects,
multiple pads, queued reopen after auto-walk, keyboard takeover, all four movement modes,
capture-boundary enforcement, native buffer handling and the production hook factory.
This is automated and static native verification, not a controller hardware playthrough.

Release verification on 2026-09-29: all 2,393 tests passed (Core 192, Native 861,
Mod 1,332, Prism 8), including the production hook factory and supported executable
verification. Version 0.3.39 was packaged and deployed with 25/25 deployment checks
passing. All 28 installed package files matched their source hashes. The approved
movie narration manifest and movies 001–003 retained their previous hashes. The prior
installation is in `Accessibility/Backups/20260929-141445` beneath the game folder.
Machine-readable evidence is in `artifacts/research/controller-0339/deployment-verification.json`
and the four Release result files in `artifacts/test-results/controller-0339/`.
