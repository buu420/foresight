# The Resume confirmation

The title screen's **Resume** row does not resume anything by itself. It leaves the title scene
and opens a save/load node that immediately asks **"Resume bookmarked game?"** with **Yes** and
**No**, with **No** preselected. Before 0.3.14 the mod said nothing at all there, so a player
heard "Resume selected." and then four seconds of silence before the field announcement.

## The native path

Every address is an RVA against image base `0x400000` for the pinned build
`8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`. Full decompiles are in
`artifacts/research/resume-confirmation-0314`.

| step | address | effect |
|---|---|---|
| Title menu builder | `0x2CF7A0` | Seven fixed rows; row *n* dispatches action *n*. Row 0 is "Resume", `getMsg(0x41, 3)`. |
| Title callback | `0x2D12A0` | On `eventType == 0`, calls the dispatcher with the row's action. |
| Row dispatcher | `0x2CFFF0` | Jump table at `0x2D0228`. Action 0 lands at `0x2D0078`. |
| Resume branch | `0x2D0078` | `SceneManager::NextScene(3)`. Quit, which the mod already spoke, is action 6. |
| NextScene | `0x297B60` | From the title scene, argument 3 sets the scene global `0x41C3E8` to `0x0D` and calls `0x2B4080` with mode 1. |
| Scene factory | `0x2B4080` | Mode 1 takes the `SaveLoadGameSteamScene` branch `0x2B40E0`, not the non-Steam `SaveLoadGameScene` at `0x2BCAF0`. |
| Scene init | `0x2B41B0` | Mode 1 maps to node kind **3**, constructs `nsMenu::MenuNodeSaveLoadSteam` and calls `open(3, 0)`. |
| `open` | `0x218A20` | Writes the kind to `this+0x2CC`; for kinds 2 and 3 calls the confirmation builder straight away. |
| Confirmation builder | **`0x21A1D0`** | Builds the prompt and both choices. |

`MenuNodeSaveLoadSteam` is identified by its vtable `0x3A980C`, written by the destructor at
`0x218888` and the constructor at `0x21898C`.

## What the builder produces

```
this+0x2EC = 1                     // confirmation active
switch (this+0x2CC) { 0,4,5: (0x41,0x14) or (0x41,0x15) or Ope (0x23,0x8E)
                      1:     (0x41,0x1B)
                      2:     (0x41,0x26)
                      3:     (0x41,0x2B) }        // the prompt
for i in 0..1 { nsMenu::CustomButton ctor 0x1D2160; getMsg(0x41, 0x11 + i) }
manager 0x1DCCA0; control binder 0x1DD260 per choice; focus setter 0x1DD3E0(key 1)
```

File id `0x41` is `Localize/en/msg/start.txt`. Its shipped English text:

| id | string |
|---|---|
| `0x11` | Yes |
| `0x12` | No |
| `0x14` | Save data to this file? |
| `0x15` | Overwrite existing data? |
| `0x1B` | Load this file? |
| `0x26` | Bookmark your progress and exit the game? |
| `0x2B` | Resume bookmarked game? |

None of these strings exist inside the executable, so the mod never contains them either: the
node's mode only says which message id is *expected*, and the text that is spoken is the string
the game itself just produced for that id.

## How the mod reads it

`SaveLoadConfirmationHookSet` owns two dedicated hooks, `0x21A1D0` and the node destructor
`0x218860`, and observes five hooks the mod already shares: `TextManager::getMsg`, the Ope text
resolver, the nsMenu CustomButton constructor and the nsMenu control binder, plus the nsMenu
focus setter.

Around the builder it opens one thread-scoped capture, exactly like the title Quit confirmation:

1. read the node's mode from `+0x2CC` and reject a mode with no audited prompt;
2. collect the prompt and the two choice labels from the localized results;
3. collect both `CustomButton` instances and the `(manager, key, control)` bindings;
4. take the builder's final focus assignment as the initial selection;
5. publish `ScreenEntered(ScreenKind.SaveLoad)` and then
   `ConfirmationOpened(prompt, [Yes, No], selectedIndex)`.

Later focus moves republish `ConfirmationOpened` with the new index, after re-reading the node's
`+0x2EC` flag so a manager that outlived its node stops answering. The destructor publishes
`ScreenExited(ScreenKind.SaveLoad)`.

The screen event is not cosmetic. `AccessibilityState.ApplyConfirmationOpened` returns no
announcements while no screen is active, and the title hook set has already published
`ScreenExited(TitleMenu)` by the time the save/load node exists, so without an owning screen the
confirmation would still be silent.

### Two details that are easy to get wrong

* `TextManager::getMsg` re-enters the Ope text resolver at `0x1B9150` with the *same* file and
  message. The Ope observation has to be skipped while
  `SharedNativeHookFanoutFactory.IsTextManagerGetMsgActive`, or the nested raw text and the
  processed result look like two prompts and the capture fails closed into silence.
* The initial focus key is 1, so the first thing a player hears after the prompt is
  "No, 2 of 2". That is what a sighted player sees; it is not an off-by-one.

## Scope

The same builder serves the save, overwrite, load and "bookmark and exit" confirmations. These
also use the audited mode-to-prompt map.
The file list those confirmations sit on top of is still unnarrated; only the confirmation is.

## Validation

All 30 save/load confirmation tests and all 1,187 solution tests passed in Release configuration.
The shared text regression follows the nested Ope/getMsg call sequence. Coverage also checks
the selected No, focus changes, node destruction, inactive hooks, invalid keys, and missing labels.
All 128 native hook byte signatures match the installed supported executable. Live testing of
0.3.14 remains pending. Test results and hook-byte proof are in `artifacts/research/release-0314`.
