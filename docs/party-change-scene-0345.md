# End of Time party change scene, 0.3.45

At the End of Time the game opens its party-change screen as a separate scene. Foresight read
the Party page only when it was opened from the main menu, so this screen was silent. In the
October 7 tester log, "Lucca: Who'll it be, Crono?" closed at 23:45:47 and nothing was announced
until "Robo: I will be happy to assist you again" at 23:45:56. The player had to choose who stays
behind without hearing the roster.

The screen now uses the existing Party reader. When it opens, Foresight says "Party." and then
what the page shows: the focused member's side, position, card, lock and picked-up state, and the
Usable Combos panel. If the member the game would focus is story-locked, the game parks focus on
key 999. The reader then says "Please select party members." and lists the visible current and
reserve rosters. Movement, pick-up and swaps use the same speech as the main-menu Party page.

## Native evidence

Executable SHA-256 `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`; RVAs
against preferred base `400000`. The full listings are in
`artifacts/research/end-of-time-0345/claude/native-evidence.txt`.

| Step | Evidence |
| --- | --- |
| Script request | Script opcode `C8` (handler `1703E0`) stores its operand at state `+12154` and sets bit `0x40` at `+1211C`. The End of Time script uses `C8 00` after the old man's "Who'll it be" line and in six other actors (29–34), each after a choice prompt. |
| Field request | `15DA60` turns that bit with operand zero into field request 5. `15C800` sends request 5 to `NextScene(8)` (`15CC65`). |
| Scene push | `NextScene` (`297B60`): in field state `0x11`, action byte 8 pushes scene `0x19` with argument 0 (`2981C4`). States `0x18`, `0x19` and `0x1C` pop on action -1 (`298041`). |
| Scene class | `SceneManager::create` case `0x19` calls `2A4910`. A nonzero `[81B4C4]+13FDC` selects `FormationSteamScene` (`2A4860`, vtable `3B0A48`); zero selects the touch `FormationScene` (`3B5764`). |
| Page | `FormationSteamScene::init` (`2A49A0`, vtable slot 158) builds `ClassicMenuNodeFormation` (`1BE730`) with builder `1BE850(0)`, stores its close callback at node `+298`, and adds the node to the scene. |
| Close | Cancel with nothing held (`1BF150`) calls node `+298`. The scene's lambda calls `NextScene(-1)` 1/120 second later (`2A4BD0`). The deleting destructor `2A4960` runs when the scene is released. |

Only slots 0 and 158 of `FormationSteamScene` differ from `cocos2d::Layer`. The main-menu Party
page uses the same class with builder argument 1 (`2A8D80`). It is attached through the
field-menu replacement `2A5270`, which Foresight already observes. Argument 0 skips only
`1C0E90`, which supplies the header's back callback. Roster records, cards, buttons, the input
manager and the combo panel are built by the same functions, so the audited Party layout
(`docs/party-0321-native-audit.md`) applies.

Root cause: the submenu session was entered only by the field-menu replacements `2A5270` and
`2BD050` and by the save-file opener `218A20`. The standalone scene uses none of them.

## Change

Two new hook contracts, both checked against the installed executable:

- `FormationSteamScene::init` at `2A49A0`: thiscall, result in AL, plain `RET`.
- `FormationSteamScene::deletingDestructor` at `2A4960`: thiscall with one flags word, `RET 4`.
  It reinstalls vtable `3B0A48`, clears `[81B4C4]+10F84` and frees 0x290 bytes when bit 0 is
  set.

Both detours always call the original with unchanged arguments and return its result. After a
successful init, `FieldSubmenuCapture.TryFindStandaloneFormation` accepts a page only if it is the
scene's single visible `ClassicMenuNodeFormation` child and its parent is that scene. The scene's
class must also be `FormationSteamScene`. That page then enters the existing submenu session, and
`MenuManagerUpdate` and `MenuManagerDispatch` refresh it as before. The destructor detour closes
the session before the original frees the scene and its page. The new hooks belong to
`FieldSubmenuHookSet`, whose registrations `Mod.cs` already includes. The catalog grows from 174
to 176 contracts.

If native init succeeds but strict page discovery fails, the reader announces
"Party. Unable to read the party selection." and records the failed context. It retains speech
ownership until scene teardown without fabricating a roster or relaxing parent/visibility
checks. The native init result and destructor calls remain unchanged. Successful menu and
failure announcements both suspend navigation through the existing dispatcher, releasing
the mod's controller menu back to the native game.

## Verification and limits

The verification is automated and static only. Nobody has listened to this screen in game yet.

- A regression test replays the 0.3.21 live Party frame under a synthetic `FormationSteamScene`.
  The real reader produces `Please select party members. Current party. Crono LV 1. HP 43/70.
  MP 8/8. Locked. Reserve. Empty. Usable Combos. None.`
- Other tests reject the touch scene, a page with another parent, a hidden page, and a scene with
  two Party pages.
- Hook-set tests check the order: native init, then speech; on teardown, closing, then the native
  destructor. They also check that a failed init is not entered and that another scene's
  destruction leaves an open menu alone.
- A successful init with a rejected page parent announces unavailable selection, keeps its
  context until destruction, and forwards both original calls with their return values.
- Teardown matches the recorded page or failure-context owner, so destroying an older scene
  cannot close another submenu that has replaced that context.
- The 0.3.21 live frame had one locked member. Multi-member rosters, the reserve and swaps in this
  scene remain synthetic checks, as they were for the main-menu page.
- The touch-mode `FormationScene` (`MenuNodeFormation`) is still unsupported, as is the touch
  main-menu Party page.
