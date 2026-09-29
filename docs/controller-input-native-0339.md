# Controller input: native audit for navigation by gamepad (0.3.39)

Read-only audit of the installed `Chrono Trigger.exe` (SHA-256 `8FE9D75E…0D7`, identical to the
engine-0332 export) and its bundled `libcocos2d.dll` and `steam_api.dll`. Evidence (decompiles,
disassembly excerpt, table dumps): `artifacts/research/controller-0339/`. No game run.

## 1. Where physical controller buttons are read

**The only gamepad input path into the game is the legacy WINMM joystick API.**

| Path | Status in this build |
| --- | --- |
| WINMM `joyGetNumDevs` / `joyGetPosEx` (IAT RVAs 0x385258 / 0x385254) | **Used.** Polled every `GameController::update` by RVA 0x18F070 (the only caller). |
| XInput | Not imported by the game. `libcocos2d.dll` contains GLFW's XInput/DirectInput backend, but its `XInputGetState` user (FUN_100c3b20) is reached only from `GLViewImpl::GLViewImpl` (GLFW joystick initialisation). No poll of it reaches game input. |
| DirectInput 8 / Raw Input | Only in the same GLFW initialisation in `libcocos2d.dll`; not a game input source. |
| Steam `ISteamController008` | `FUN_0040e600` calls its `Init` (vtable slot 0) once at start-up and aborts on failure. No `RunFrame`, action-set or digital/analog action calls exist. Steam Input therefore reaches the game only as a device that WINMM enumerates. |
| Keyboard | Separate: `GetKeyboardState` scan in 0x18F3F0 through the configurable VK slots. Not affected by anything below. |

### The chain, per frame

1. `GameController::update(float dt)` — RVA **0x18F040**, thiscall, one stack float, `RET 4` —
   calls 0x18F070 and then 0x18F3F0.
2. **0x18F070 (WINMM poll).**
   - It loops over every joystick ID (`joyGetNumDevs`), calling
     `joyGetPosEx(id, &info)` with `dwSize = 0x34` and `dwFlags = 0xFF` (JOY_RETURNALL).
   - Per-pad previous state lives at `0x81EFC8 + id*0xC`: +0 connected, +4 previous `dwButtons`,
     +8 previous POV nibble.
   - Only *changed* bits raise `libcocos2d!win32_nativeControllerButtonEvent`.
   - Button table at 0x7FA12C (mask → cocos key code):

   | WINMM bit (button) | key | cocos-2d `Controller::Key` |
   | --- | --- | --- |
   | 0 (1) | 1004 | A |
   | 1 (2) | 1005 | B |
   | 2 (3) | 1007 | X |
   | 3 (4) | 1008 | Y |
   | 4 (5) | 1015 | left shoulder |
   | 5 (6) | 1016 | right shoulder |
   | 7 (8) | 1021 | START |

   - **Bits 6, 8, 9 and 10+ (buttons 7, 9, 10, 11+) are not in the table: they never produce an
     event.**
   - POV hat: values 0 / 4500 / 9000 / … / 31500 map to a nibble (1 up, 2 right, 4 down, 8 left,
     diagonals combined). Table 0x7FA10C maps them to D-pad keys 1010 up, 1013 right, 1011 down,
     1012 left. Any other value, including JOY_POVCENTERED 0xFFFF, gives 0 (no D-pad).
   - Axes: X → 1000, Y → 1001, U → 1002, R → 1003, each scaled as `v * 2^-15 - 1`.
3. **cocos2d dispatches to the game's `GameController` listeners.** They are registered by
   0x18E8B0 (`EventListenerController` + `EventListenerKeyboard`).
   - **0x18EAD0 onKeyDown** and **0x18EC50 onKeyUp**, each under mutex 0x81EF98, set or clear
     raw bits in `GameController+0x2D4`:
     - A → 0x80, B → 0x08, X → 0x40, Y → 0x01;
     - D-pad up / down / left / right → 0x800 / 0x400 / 0x200 / 0x100 (opposites cleared);
     - left shoulder → 0x20, right shoulder → 0x10, START → 0x02.
   - No case exists for 1019 or 1020 (left and right stick clicks) or 1022 (SELECT).
   - **0x18EDA0 onAxis** handles only 1000 and 1001 (left stick → movement vector at
     +0x2C0/+0x2C4) and 1017 and 1018 (triggers → +0x2CC/+0x2D0 → bits 0x4000/0x8000). The WINMM
     poll never emits 1017 or 1018, and 1002/1003 (right stick) are ignored, so triggers and the
     right stick do nothing in this PC build.
4. **0x18F3F0 builds the pad word.**
   - Inputs:
     - the controller bits (+0x2D4);
     - the left-stick direction (FUN_00593e40);
     - the triggers;
     - the in-game face-button remap (raw 0x80 / 0x08 / 0x01 / 0x40 through config words at
       `[0x81B4C4]+0x68F4/68F8/68FC/6900`);
     - the keyboard;
     - the `VirtualStick4` at global 0x81C3E0.
   - Outputs: held `+0x2DC`, newly pressed `+0x2E0`, changed `+0x2E4`, and repeat `+0x2E8`
     (0.4 s delay, 0.2 s rate).
   - The bits follow the SNES layout: 0x800/0x400/0x200/0x100 are the D-pad, 0x80 is A, and so on.
5. **Every consumer reads that word** through global **0x81C3DC**: `GameController + 0x290`, or
   the static `VirtualController` at 0x7FB30C.
   - Methods: slot 1 held, slot 2 pressed, slot 3 changed, slot 4 repeat, slot 5 reset (0x18EFE0),
     slot 6 clear repeat (0x18F030).
   - Consumers include field Confirm 0x17D0C0 (tests bit 0x80 of the held word) and the world and
     vehicle consumers 0x264C40, 0x28E1D0 and 0x28A1E0.

### Right-stick click (R3)

- **Unused by the game:** WINMM bit 9 (button 10, R3 on the Xbox layout) is absent from 0x7FA12C,
  and neither handler has a case for key 1020.
- **The in-game configuration cannot bind it:** the remap in 0x18F3F0 covers only the four face
  bits, and the keyboard VK slots cover only keys.
- **A conflict can only come from outside the game.** Steam Input can remap R3 to another output
  (a face button, a keyboard key, and so on). Then no WINMM bit 9 appears; if the output is a game
  button, the game receives that button instead. The existing keyboard keys remain the fallback.

### Physical layouts (native table plus documented device behaviour)

The native table assumes the Xbox button order. Which physical button produces each WINMM bit is
a property of the device and driver, not of the game:

| Device as WINMM sees it | Buttons 1–10 | R3 bit | Notes |
| --- | --- | --- | --- |
| XInput pad (Xbox 360/One/Series), or any pad Steam Input presents as a virtual Xbox 360 controller | A, B, X, Y, LB, RB, Back, Start, L3, R3 | **bit 9** | Microsoft's documented DirectInput view of XInput devices; the triggers share the Z axis, which the game does not read. |
| Sony DualShock 4 / DualSense as raw HID (Steam Input off) | Square, Cross, Circle, Triangle, L1, R1, L2, R2, Share/Create, Options | bit 11 (button 12) | Common HID ordering, **not verified here**. With it the game's own table makes Square = A (Confirm) and Cross = B, and R2 = START. |

The user's phrasing ("Square (Xbox X)", "Cross (Xbox A)") matches the Xbox layout. That is what
Steam Input's translation produces, and what the game itself was built for.

**Recommendation:** use the Xbox layout (R3 = bit 9). Optionally detect a raw Sony device with
`joyGetDevCaps` (vendor 0x054C) and use its layout; that call is not native evidence, it is the
mod's own query. Log each connected pad's caps once, to confirm at runtime which layout the user's
pad actually reports.

## 2. Best boundary to sample and consume navigation-menu input

**Recommended: detour `joyGetPosEx` as the game calls it, and edit its result before returning.**

- **Where:** the call at RVA **0x18F0CF**, `FF 15 54 52 78 00` = `CALL [IAT 0x785254]` at image
  base 0x400000 (only the operand relocates, since ASLR is on). It is the only caller.
- **Signature:** `MMRESULT __stdcall joyGetPosEx(UINT uJoyID, JOYINFOEX *pji)`.
- **JOYINFOEX offsets** (0x34 bytes): +0x08 X, +0x0C Y, +0x10 Z, +0x14 R, +0x18 U, +0x1C V,
  **+0x20 dwButtons**, +0x24 dwButtonNumber, **+0x28 dwPOV**.
- **Why here:**
  - It is the **rawest native point** that holds *only* gamepad state: physical buttons, before
    the game's table, before the in-game remap, and before keyboard bits are merged in.
    - Suppression here therefore never touches keyboard input.
    - R3, which the game discards at the next step, can still be seen.
  - It is **before every consumer**. The poll (0x18F070) and the word build (0x18F3F0) run back
    to back in `GameController::update`, and all field, menu, world and vehicle consumers read the
    built word afterwards. Field Confirm, menus and world movement all see the suppression.
  - It works for every pad the game supports (WINMM is its only pad path), and it is unaffected by
    in-game face-button rebinding.
- **How:**
  1. Call the original.
  2. If it returns 0 (JOYERR_NOERROR), sample `dwButtons` and `dwPOV` for this pad ID into the
     navigation state: R3, LB (bit 4), RB (bit 5), A (bit 0), B (bit 1), X (bit 2), and the D-pad
     from the POV, including diagonals 4500/13500/22500/31500 as the game does.
  3. While the navigation menu owns the pad, clear the owned bits in `dwButtons`, set `dwPOV` to
     0xFFFF, and centre X/Y (0x7FFF; the game computes ≈0 from that) so the left stick does not
     walk Crono while choosing.
- **Holding inputs across open and close:**
  - The game emits events only on changes against its own previous state at `0x81EFC8+id*0xC`.
  - Masking a button that is already held makes the game see a *release*: correct on open, since
    it stops movement and never fires an action.
  - **Unmasking a still-held button makes the game see a fresh *press*.** For example, the A or X
    that chose a route would become Confirm.
  - So keep a per-pad, per-bit latch: every owned bit, and a non-centred POV, stays masked after
    the menu closes until that input is physically released.
  - R3 never needs masking: the game ignores it.
- **Threading:** 0x18F070 runs on the game's update thread. The onKey handlers lock mutex 0x81EF98
  only around the +0x2D4 writes. The detour should hand edges to the navigation runtime through the
  same single-thread or lock discipline the mod already uses for its per-frame hooks.
- **Hook mechanics:**
  - Either redirect the call at 0x18F0CF, or detour `winmm!joyGetPosEx` itself (no other in-process
    caller exists in the exe; `libcocos2d.dll` imports WINMM but references no `joy*` function).
  - The IAT slot lies in read-only `.rdata` (characteristics 0x40000040), so an IAT patch needs a
    page-protection change.
  - Verify first:
    - bytes `FF 15` at RVA 0x18F0CF, with the operand equal to `imageBase + 0x385254`;
    - `C7 85 C8 FE FF FF 34 00 00 00` at 0x18F084;
    - `C7 85 CC FE FF FF FF 00 00 00` at 0x18F08E.

### Other boundaries considered and rejected

- **onKeyDown/onKeyUp (0x18EAD0 / 0x18EC50):** R3 never arrives there (no table entry, no key
  1020), so the menu could not open with it. Consuming there also needs duplicate edge tracking.
- **The pad word or its 0x81C3DC getters:** by then controller and keyboard bits are merged
  (0x18F3F0 ORs the keyboard in), so controller-only suppression would also eat keyboard input, and
  the physical identity of face buttons is lost after the in-game remap.
- **The existing movement hook (0x175AC…):** field Confirm (0x17D0C0), menus and world consumers
  read the pad word independently, so filtering directions there cannot stop them. This agrees with
  the root's concern.

## 3. Remaining uncertainty (not guessed)

1. **Which WINMM ordering the user's pad reports.** Xbox layout is the documented XInput / Steam
   virtual-controller behaviour; the Sony raw-HID ordering above is typical but unverified. Confirm
   with a one-time `joyGetDevCaps` log (name, vendor/product, button count).
2. **Steam Input and this game's config.** The game `Init`s `ISteamController` but polls no
   actions. Whether Steam then exposes the virtual pad to WINMM is established only by the game
   itself working with a controller. Whether R3 is remapped depends on the user's Steam Input
   configuration.
3. **More than one pad:** the game reads every WINMM ID each frame and ORs their events, so the
   detour must latch per ID.
4. **Not observed at runtime:** everything above is static analysis of the matching binary.

## 4. Addendum: the proposed probe after the call (RVA 0x18F0D5)

The root proposes an `ExecuteFirst` assembly probe at the instruction immediately after the
`joyGetPosEx` call, instead of redirecting the import. **The site is sound.** Full disassembly:
`artifacts/research/controller-0339/18F070-full.asm`.

```
0018F0C0  8D85C8FEFFFF      LEA EAX,[EBP-0x138]            ; &JOYINFOEX (loop head)
0018F0C6  C685C3FEFFFF00    MOV byte [EBP-0x13D],0
0018F0CD  50                PUSH EAX
0018F0CE  53                PUSH EBX                        ; uJoyID
0018F0CF  FF1554527800      CALL [IAT joyGetPosEx]          ; stdcall, pops its 8 bytes
0018F0D5  0FB68DC3FEFFFF    MOVZX ECX,byte [EBP-0x13D]     ; <- probe site (7 bytes)
0018F0DC  85C0              TEST EAX,EAX
0018F0DE  53                PUSH EBX
0018F0DF  8D85FCFEFFFF      LEA EAX,[EBP-0x104]
0018F0E5  0F44CE            CMOVZ ECX,ESI
```

Verified facts:

- **Relocation:** the 7-byte `MOVZX` is EBP-relative, so it relocates into a trampoline
  unchanged. No branch targets 0x18F0D6–0x18F0DB; the loop's only back edge is 0x18F3D6
  `JC 0x58F0C0`, to the loop head.
- **Registers live at the site** (must be preserved):
  - **EAX** = MMRESULT;
  - **EBX** = joystick ID;
  - **ESI = 1**: used by `CMOVZ ECX,ESI` at 0x18F0E5. The loop body reuses ESI as a table pointer
    but resets it with `MOV ESI,1` at 0x18F3C7 before the back edge, and the failure path never
    changes it;
  - **EDI** = per-pad state pointer (`0x81EFC8 + id*0xC`);
  - EBP and ESP.
- **Dead at the site:** ECX (overwritten by the relocated MOVZX), EDX (next use is a definition at
  0x18F21A) and EFLAGS (`TEST EAX,EAX` follows). A cdecl call from a stub that saves EAX (and ECX
  and EDX) is therefore safe.
- **SSE and x87:** no XMM register is live across the site. The first XMM use is 0x18F275, and all
  are loaded fresh there. The caller 0x18F040 reloads XMM1 after the call. No x87.
- **Exceptions:** 0x18F070 has no exception frame, only the /GS cookie. The managed callback must
  catch everything; nothing may unwind through this native frame.
- **Arguments:** EBX = ID, `LEA [EBP-0x138]` = JOYINFOEX, EAX = result.
- **In-place edits after the call are consumed by the rest of this iteration.** The same
  JOYINFOEX local is read afterwards:

  | field | local | read at |
  | --- | --- | --- |
  | `dwButtons` +0x20 | `EBP-0x118` | 0x18F14C and 0x18F3B2 (saved as the previous state) |
  | `dwPOV` +0x28 | `EBP-0x110` | 0x18F197 |
  | X +0x08 | `EBP-0x130` | — |
  | Y +0x0C | `EBP-0x12C` | — |
  | R +0x14 | `EBP-0x124` | — |
  | U +0x18 | `EBP-0x120` | — |

- **Axis centring:** use `0x8000` for an exact 0.0 (`32768 * 2^-15 - 1`); `0x7FFF` gives −0.00003.
- **Bytes to check before hooking:**
  - `0F B6 8D C3 FE FF FF` at 0x18F0D5;
  - `85 C0` at 0x18F0DC;
  - `FF 15` at 0x18F0CF (the operand is imageBase + 0x385254 after relocation).

Gotchas:

1. **The buffer is shared by every pad ID in the loop.** When EAX ≠ 0 (for example
   JOYERR_UNPLUGGED 167 or JOYERR_PARMS 165), the struct still holds the previous ID's data. The
   probe must ignore the contents whenever EAX ≠ 0.
2. **`joyGetNumDevs() == 0` skips the whole loop** (`JZ 0x58F3DE`), so the probe never runs.
   Otherwise it runs once per supported ID per update, whether or not a device is present. On this
   machine `joyGetNumDevs` returns 16. Treat "no successful probe this frame" as "no pad", and do
   not let a latch or menu state wait on probe calls that never come.
3. **The poll runs even when the window is inactive.** 0x18F3F0 drops the controller word when
   there is no active window (with the keyboard flag set), but 0x18F070 still polls. The menu should
   apply the same active-window check, or it will react to a pad while the game is in the
   background.
4. **Held-button latch:** needed per pad ID, as section 2 explains. A disconnect (EAX ≠ 0 after a
   success) should clear that ID's latch, as the game clears its own previous state
   (0x18F12F–0x18F138).
5. **POV:** `dwFlags = 0xFF` requests JOY_RETURNPOV. The game decodes 0, 4500, …, 31500, and
   0xFFFF (centred) gives no D-pad.
6. **Timing:** the probe runs inside `GameController::update`, before `onKeyDown` takes mutex
   0x81EF98, so there is no lock interaction.

## 5. Addendum: physical layouts, from primary sources

**Live inventory (read-only, `devcaps.py`, 2026-09-29):** `joyGetNumDevs` = 16, but no ID answered
`joyGetDevCaps` or `joyGetPosEx`. No controller was connected while Steam was running and the game
was closed, so no live device could be sampled.

### Sources

- **SDL2 `src/joystick/SDL_gamecontrollerdb.h`** (downloaded 2026-09-29, SHA-256
  `0db821d7…3bb2b`; copy in the evidence folder). The `SDL_JOYSTICK_DINPUT` block gives Windows
  DirectInput button indices (0-based) per device:
  - line 329, **Steam Virtual Gamepad, VID 0x28DE PID 0x11FF**:
    `a:b0,b:b1,x:b2,y:b3,leftshoulder:b4,rightshoulder:b5,back:b6,start:b7,leftstick:b8,rightstick:b9`,
    triggers combined `+a2/-a2`;
  - lines 258–259, **PS4 Controller, VID 0x054C PID 0x05C4 / 0x09CC**:
    `x:b0,a:b1,b:b2,y:b3,leftshoulder:b4,rightshoulder:b5,back:b8,start:b9,leftstick:b10,rightstick:b11,guide:b12`
    (SDL's names a/b/x/y are Xbox positions, so b0 = Square, b1 = Cross, b2 = Circle,
    b3 = Triangle);
  - line 260, **PS5 Controller, VID 0x054C PID 0x0CE6**: the same indices, R3 = b11.
- **Microsoft, "Comparison of XInput and DirectInput features":**
  - XUSB (XInput) controllers "are properly enumerated on DirectInput", with the triggers combined
    into one axis;
  - an XInput device is identified by `IG_` in its PnP device ID.

  It does not list button order.
- **Wine `dlls/winmm/joystick.c`** (a reimplementation, used here as corroboration, not Windows
  proof):
  - WINMM is built on DirectInput 8;
  - `dwButtons` bit *i* = DirectInput button *i*;
  - `JOYCAPS.wMid`/`wPid` = LOWORD/HIWORD of DirectInput's VID/PID property;
  - a centred hat is reported as 0xFFFF.
- **The game's own table** (0x7FA12C: bits 0–5 = A, B, X, Y, LB, RB; bit 7 = Start) matches the
  Steam Virtual Gamepad and XInput DirectInput order exactly: it was built for that layout.

### Requested actions by layout (WINMM `dwButtons` bit = DirectInput 0-based index)

| Action | Xbox pad / Steam virtual pad | Raw PS4/PS5 (Steam Input off) |
| --- | --- | --- |
| Open/close menu (R3) | bit 9 | bit 11 |
| Guidance (Cross / A) | bit 0 | bit 1 |
| Auto-walk (Square / X) | bit 2 | bit 0 |
| Close (Circle / B) | bit 1 | bit 2 |
| Previous/next category (L1/R1, LB/RB) | bits 4 / 5 | bits 4 / 5 |
| Destinations (D-pad) | POV hat | POV hat |

On a raw Sony pad the game's own table already mis-assigns buttons: Square (b0) is the game's A
(Confirm), Cross is B, Circle is X, R2 (b7) is Start, and Options and R3 do nothing. Only the
menu's buttons must follow the table above.

### Can the layout be identified safely?

- **Probably:** by `joyGetDevCaps(id).wMid/wPid`, read once when an ID turns connected:
  - 0x28DE / 0x11FF → Steam virtual pad (Xbox order);
  - 0x045E → Microsoft XInput pad (Xbox order);
  - 0x054C with PID 0x05C4, 0x09CC or 0x0CE6 → raw Sony order.
- **Limitation:** that Windows' own WINMM fills `wMid`/`wPid` with the USB VID/PID is shown here
  only by Wine's reimplementation and SDL's DirectInput GUIDs, not observed on this machine
  (nothing was connected). Log each connected ID's name, `wMid`, `wPid` and button count on the
  first run, and fall back to the Xbox order when the IDs are unknown. That is also the layout the
  game itself assumes.
- **Unverified risk:** if Steam Input is enabled and WINMM still lists the physical Sony pad next
  to Steam's virtual pad, one press would appear on two IDs with different bit orders. The game
  would already act on both in that case. Suggested mitigation: when a 0x28DE/0x11FF device is
  present, ignore 0x054C devices for menu input.
