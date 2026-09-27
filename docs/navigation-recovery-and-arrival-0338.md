# Navigation recovery and interaction arrival, 0.3.38

## Navigation keys going silent

The September 27 session log (`2026-09-27 18.12.28 ~ Chrono Trigger.txt`) records a stale Name Entry confirmation being read over the fair shop. At 13:43:57 the shop's first two rows were accompanied by `NameConfirmationFocused` events for Yes and No. At 13:43:59 selecting row 2 raised:

> Name confirmation focus key 2 has no correlated localized choice.

The game had reused a manager address saved during Name Entry. `NewGameHookSet.AfterFocusSet` treated pointer equality as proof that the old confirmation was still open. Independently, `NavigationDispatcher.ReportCoverageFailure` permanently disabled field and world navigation for every menu coverage error. Shop and top-menu speech continued, while navigation keys became silent.

The reader now checks the owning native scene before dereferencing either cached Name Entry manager. A manager seen after that scene has ended retires the old Name Entry state. It cannot become valid again just because its address or scene number is reused; a fresh Name Entry initialization must capture it. A menu coverage error cancels active navigation and clears queued walking input, but navigation keys remain available when player control returns. The error is still reported. Actual unmanaged-hook faults and mod shutdown retain their existing disable behavior.

### Native evidence

The Ghidra export for the supported executable supplies the identity check:

| RVA | Evidence |
| --- | --- |
| `0x297860` | `SceneManager::create`, case `0xB`, writes the scene identity at `0x41C3E8` before constructing Name Entry. |
| `0x2BFDE0` | Creates the Name Entry Cocos scene and invokes the layer's native initialization. |
| `0x2C2F00` | Constructs the name-confirmation overlay inside that scene. |

Exports are in `artifacts/research/engine-0332/native/functions`. The game executable SHA-256 is `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`. The scene value is read through the existing checked memory interface; an unreadable identity while processing a tracked manager is reported as a coverage error. No Cocos ownership or memory lifetime is inferred solely from a reused pointer. Cocos documents scene/node lifecycle separately from object identity in its [Node API](https://docs.cocos2d-x.org/api-ref/cplusplus/V3.0/d3/d82/classcocos2d_1_1_node.html).

### Regression evidence

Six new failing cases reproduced the old behavior before the fix: shop rows 0, 1 and 2 reusing the confirmation manager; an unreadable retired manager; and navigation after a menu error on both field and world input paths. The tests verify that holding P across an error cannot restart movement, then releasing and pressing K/P restores ordinary navigation use. Existing Name Entry presentation, grid, confirmation and lifecycle tests remain covered.

The combined startup fixture was corrected to model `SceneManager::create` writing `0xB` before Name Entry initialization. This preserves its original focus-reading assertion rather than accepting silence.

Independent review found that suspension itself could throw while announcing the stop and prevent the coverage-error dialog. Four additional regressions reproduced failed speech/logging on field and world routes. Suspension now cancels the controller and suppresses held commands before external callbacks; the dispatcher reports the original coverage error in `finally`. All 21 focused recovery, transition and runtime tests passed after that change.

Name-only retirement also preserves any newly captured Control or Mode screen. A further failing regression initialized Name Entry, reused its manager for a new Mode screen, and changed focus from inside the native Mode callback. Broad cleanup erased the fresh Mode state. The narrowed cleanup preserves two consecutive Mode callbacks; all 116 focused New Game, recovery, transition and runtime tests then passed.

## Finishing at an interaction

Previously, the controller announced arrival using distance from a route goal without checking the leader's facing. In the September 27 Armour seller log, one route ended at `(3680,9855)` after a rightward leg. With the seller at `(3840,9536)`, the game's upward Confirm test reaches the seller, while its rightward test does not. This is an inference from the logged coordinates and native geometry; the log does not prove that Confirm was pressed at that point.

Confirm destinations now carry the native interaction rule, and the frame carries the leader's live facing. Automatic walking finishes with ordinary directional input to face the selected interaction. Manual guidance announces the final turn and checks the resulting facing. An unreadable facing does not produce a ready-to-interact arrival. K reports reach and facing rather than a misleading distance to the sprite. The player still presses Confirm.

Actor approaches use the native X collision offset, including counter markers that invoke a shopkeeper's script. Every offered standing point accounts for the controller's arrival tolerance. Story objectives that combine alternatives retain the finish rule for the selected goal. Existing contact movement, save-point standing positions, exits and passage-opening stages keep their distinct activation rules.

Planning can approach a known target before its camera-dependent activation byte becomes active. Final arrival requires the live native scan to select that target or an equivalent interaction marker. The scan follows descending actor priority and the exact carry-dependent distance comparisons; another actor on the edge of the native range can therefore exclude a standing point. When a touched actor overrides the facing scan, readiness instead checks the native actor selected for that contact. Unknown or temporarily inactive interaction state pauses the final approach rather than announcing arrival.

Equivalent scenery markers keep their combined interaction identity. Treasure approaches also include the native second-row upward probe where the adjacent tile is blocked and the intervening row holds no treasure record. This check reads the complete native treasure grid: an already opened chest still shadows the farther chest, even though it no longer belongs in the destination list.

### Native interaction evidence

| RVA | Evidence |
| --- | --- |
| `0x17CFD0`, `0x17D0C0` | Read the leader's facing at actor offset `0x60`; the Confirm path also has a contact-actor override. |
| `0x17D230` | Scans actors in native priority order and dispatches by facing. |
| `0x17FA20` | Executes the selected actor only when script-call gates allow it and the current script priority at actor `0xE4` is greater than 1. |
| `0x164970`, `0x1648D0` | Preempt and resume script priority slots; Confirm uses priority 1. |
| `0x17D4C0`, `0x17D610`, `0x17D760`, `0x17D8B0` | Up, down, left and right interaction geometry, including X minus 16 times the collision offset at actor `0x14C`. |
| `0x179940` | Separate treasure/pickup activation path, also dependent on facing. |
| `0x179C30`, `0x179CA0`, `0x179CF0`, `0x179D40` | Treasure tile probes: one/two rows above, one below, one left, one right. The nearer treasure shadows the farther upward probe. |
| `0x179D90` | Reads the current treasure grid. |

These exports are in the same verified Ghidra corpus as the scene-ownership evidence. Tests use captured shop, Cathedral and pendant state plus constructed cases for offsets, nearby competing actors and mixed Story Event destinations. Constructed geometry tests are not represented as completed live interactions.

Review also reproduced a final turn continuing to hold movement while readiness was pending, plus remembered targets retaining a previous capture's Confirm callback. The regression suite covers releasing the turn immediately, starting a fresh turn deadline when readiness returns, and refreshing interaction state even when the destination retains its last discovered position. A disabled actor still wins the scan when the native priority rules select it; only its ability to execute Confirm is blocked.

The final dispatch check captures the current script priority at actor `0xE4`. Priorities 0, 1, and unreadable state remain pending; priorities above 1 can accept Confirm when the other native gates pass. Ten failing assertions reproduced the missing capture/readiness checks before implementation. Tests cover both facing-scan and contact dispatch, and verify that a busy actor in a higher slot still blocks a different target. Constructed actor fixtures default to idle priority 7; live captures explicitly store null when that word cannot be read.

## Deployment paths

The deployment preflight exposed a UNC-path issue: PowerShell's `Resolve-Path.Path` included a provider prefix that .NET file APIs rejected. `ModPaths.psm1` now resolves literal paths to `ProviderPath`. The existing installation's verification changed from 23 passes and two failures to 25 passes and no failures after this correction. This allows the deployment scripts to operate directly from the game's network path.

Version 0.3.38 was packaged from the tested Release binaries and deployed with the game closed. All 25 deployment checks passed, including the existing launcher registration; no re-registration was needed. All 27 installed package payload hashes matched. The game executable, approved own-voice movie manifest, and narrated movies 001/002/003 retained their verified hashes. The prior mod, loader, hooks and launcher are preserved under the game's `Accessibility/Backups/20260927-144745` directory.

## Validation limits

The final Release run passed all 2,324 tests with no failures or skips:

| Project | Passed |
| --- | ---: |
| Core | 192 |
| Mod | 1,263 |
| Native | 861 |
| Prism | 8 |

Command: `dotnet test ChronoTriggerAccessibility.slnx -c Release --no-restore --logger trx --results-directory artifacts/tests/navigation-0338-final --verbosity minimal`. The Native suite checks the actual installed executable's supported hash and every audited hook's exact bytes. Results are saved in `artifacts/tests/navigation-0338-final`.

The game was closed during development. Automated regression tests and the native decompilation establish the reproduced failure and the implemented rules; they do not establish a new live playthrough or successful interaction with every moving actor and scripted object.
