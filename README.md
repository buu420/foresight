# Chrono Trigger Accessibility

This mod adds screen-reader output to the Windows Steam release of Chrono Trigger. Version 0.3.28 repairs narrow-door routing across the game, makes Cathedral Story Events follow the required switch and organ sequence, and reads the Tech character selector. See [the repair evidence](docs/cathedral-tech-0328.md) and [the whole-game coverage audit](docs/whole-game-coverage-0328.md). The full story, optional quests, PC bonus areas, vehicle navigation, native menus and battle reader remain included.

Earlier menu, battle, pendant, Telepod, Resume confirmation and race-choice repairs remain included. Current Story Events remain routable before discovery, with optional guide targets in People, Interactable Objects, and Exits. Manual directions continue one leg at a time, with the countable footsteps confirmed by the player. World/local transition recovery and the Settings speech repair remain included. Intro audio descriptions remain pending.

Settings reads native category labels, rows, current values, and nested controls, including Screen Size. This is a test release: automated checks and exact native hook-byte verification do not establish a complete live playthrough. The touch-interface Inventory, Equipment, Tech, Party and shop pages remain outside the implemented coverage.

Shop speech follows Buy/Sell/Equip, selected items and prices, stock, item help, equipment comparisons, quantities, totals and funds. Enemies lists active encounter regions from the game scripts; I/P leads into the selected region using the existing guidance controls. Combat begins normally when its native trigger fires.

## In-game menus

Open the menu with V and use the game's normal direction keys, X to confirm, and C to cancel. Inventory reads the selected item, quantity, and displayed help, including reordering state. Equipment reads the selected character or equipped slot, replacement item names and quantities, and the displayed stat preview with increases or decreases. Techs reads the selected character card and tech category, then the selected row and its rendered description, component names, and MP/requirements panel. Party reads the selected member's card and the displayed combinations. These readers follow native focus and do not select or change anything for you.

Save/Load reads the selected file number, its displayed card text, and the visible party and save details. Empty files do not read an old preview. Save, overwrite, Bookmark, and Resume confirmations use the existing prompt and Yes/No reader. While a confirmation is open, file-list updates cannot interrupt it. Brief redraws are retried; a persistent unreadable selection is announced and logged, with recovery on the next valid capture.

The new submenu coverage applies to the classic keyboard interface and Steam Equipment/Save pages. Automated fixtures check selection changes, ownership, visible text, quantities, details, and confirmation transitions. Live keyboard and speech coverage is still unverified for these additions.

## Battle

Use the game's normal directions, X to confirm, and C to cancel. The reader announces the character whose command window is active, the selected Attack/Tech-or-Combo/Item command, native Tech/Item choices and availability, MP costs, item quantities, and committed targets. Group targets are read together. Enemy letters A through H identify fixed sprite slots, so a target keeps its letter when another enemy falls; the letters do not imply how many enemies remain. Visible messages and popups report outcomes and rewards without exposing ordinary enemy HP. Status reading follows the applied visual effect, including icon, color, and pose changes.

| Key | Action during battle |
|---|---|
| 1 / 2 / 3 | Select the corresponding party member for inspection and read their name |
| H | Read that member's current and maximum HP |
| M | Read that member's current and maximum MP |
| K | Repeat the current battle command or target selection |

The inspection member stays selected when another character's turn starts. Empty slots are announced. These shortcuts do not issue battle commands. Navigation and footsteps stop at battle entry and become available again when the native battle menu is destroyed. Shortcuts run only while the game has focus, without Ctrl, Alt, Shift, or Windows held. Persistent capture failures are spoken and recorded in the Reloaded log.

Live validation still needs a real encounter in each interface, including party reordering, commands, targets, Techs, Items, status effects, defeat, rewards, and return to exploration. Reading observes the game's timing and does not pause combat or select actions. The battle Item panel has no description to read; descriptions from other screens are not inserted into it. Popups announce the recipient and displayed damage or recovery amount. HP versus MP is not appended to popup amounts until that distinction is verified; H and M read the exact party HUD values. Element icons within battle messages still need a separate reader.

## Navigation

Use these keys while controlling the party in a local field area, walking on the world map, or flying a vehicle:

| Keys | Action |
|---|---|
| U / O | Previous / next category: People, Exits, Interactable Objects, Story Events, Enemies |
| J / L | Previous / next destination |
| K | Repeat the destination and current manual leg, or its direction and distance when idle |
| I | Start counted spoken directions while you move |
| P | Start or stop automatic walking |
| F8 | Turn footsteps off or on for the current game session |

Browsing to another destination stops the old route. Manual movement, menus, dialogue, loss of focus, area changes, unreadable state, and blocked movement stop automatic walking. Press P again to restart it. Walking uses the game's ordinary directional input and does not interact, choose dialogue, or bypass collisions. At a person or object, use K to hear its direction, face it, and press the game's confirm button yourself.

The local maps are 2D. One navigation step means one 16-pixel map tile, not one key press. Directions use left, right, up, and down. Partial tiles use quarter steps. Routes prefer fewer turns among equally short paths, and walking follows cardinal edges with the native body clearance.

Press I for manual guidance. It speaks only the current leg: "Right 5 steps." When you reach the turn, it gives the next instruction, such as "Up 1 step." K repeats the destination and the distance remaining in that leg. A K press that coincides with a new turn or correction produces one instruction. If you leave the route, guidance asks you to stop. Release the movement keys; once Crono stops, it recalculates from your position and speaks the first corrected leg. I supplies speech only and leaves movement under your control. Automatic walking with P keeps its initial route overview.

The world map is also 2D. Its native walking step is eight pixels. Choose Exits with U/O to browse visible entrances and optional guide areas, or Story Events for an available current objective. World entrances use the game's loaded labels, including custom character names. On arrival, press the game's Confirm button to enter; automatic walking never presses it. The live camera and node transforms determine visibility at the current resolution. Story routes select the next native entrance or passage across areas. When the next step requires a vehicle or another era, navigation points to an available vehicle and reads the travel instruction. The time gauge reads its selected era. Vehicle movement and landings use the native flight and collision rules; Confirm still belongs to the player.

Local targets come from live field actors, the current exit grid, and the native unopened treasure table. People includes creatures such as cats. Active interaction targets, scenery, treasure chests, and item pickups can be selected before discovery throughout the installed field areas. They use current native positions and disappear when retired, disabled, or collected. Reward contents are not listed. Script gates and encounter regions use their native coordinate and progress tests.

For an automatic passage such as the castle stairs, navigation first approaches its native opening trigger. It continues only after the live collision map allows the next leg. A guard who requires conversation is a separate objective; press Confirm yourself when instructed.

Story Events follows the story counter, quest flags and held items from Mother and the Millennial Fair through the ending. Later chapters include separate actions for multi-stage locations and quests. Routes choose an onward native passage toward the current action, including offscreen exits and scripted gates. Discovery is not required. Optional conversations, rescue opportunities, quests, signs, supplies and healing machines stay in their proper categories. Narrative scenes and manual puzzle instructions can still be nonspatial notes. See [story objectives](docs/full-story-objectives.md) for the current catalog and [future-story-navigation.md](docs/future-story-navigation.md) for the rat pursuit, console and crane inputs, and Spekkio lesson. Actual progress, closed passages and collision still apply.

Footsteps follow actual movement, with one sound per navigation step: 16 pixels locally, eight pixels on the world map. When I, K, a turn, or a corrected manual route gives an instruction, start counting afresh. "Right 5 steps" means five sounds for that leg. Fractional instructions include a partial final distance: "Right 2.5 steps" means two full sounds and a half step before the turn. The turn/arrival announcement covers that final fraction. Stopping briefly preserves distance already walked; holding movement against a wall produces no extra beats. F8 announces the new setting; footsteps start enabled on each launch. The five existing recordings can overlap during faster movement, and use one general surface sound. Menu, dialogue, focus, and area transitions clear pending movement. Movement, capture, reset, and audio-result counters are written to the Reloaded log at most once every two seconds per component. See [footstep-distance-counting.md](docs/footstep-distance-counting.md) for verification and limits.

The player confirmed the earlier local navigation repair and reported world auto-walk failures in 0.3.7. Version 0.3.8 passed the observed missed-turn replay and native input-dispatch checks. The September 13 live test recorded automatic walking to Leene Square, and the player confirmed that X entered the fair. Navigation and footsteps remained active after entering. The player reports that 0.3.9 manual navigation works; the player also confirmed accurate counting with the 0.3.10 footsteps. The future areas and 0.3.12 guide availability changes still need a live playthrough. Navigation commands, active positions at most four times a second, input-delivery counters, and stop reasons are recorded in the Reloaded log. Target counts and actor facts are logged when the inventory changes. Moving actors and special field mechanics may still interrupt routes. Automatic interaction, dialogue choices, puzzles, and automatic battle decisions are not implemented. See [world-navigation.md](docs/world-navigation.md) and [navigation-manual-turn-recovery.md](docs/navigation-manual-turn-recovery.md) for evidence and validation limits.

It ships a self-contained, loader-only copy of Reloaded-II inside the game folder. You do not need to install Reloaded-II, run its launcher, or configure anything by hand.

## Supported game build

The native hooks are fail-closed and support only this executable:

- File: `Chrono Trigger.exe`
- Product version: `2.0.0.1`
- SHA-256: `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`
- Architecture: 32-bit x86

Both the launcher and the mod verify this hash and refuse to hook anything else. If it does not match, the game still starts â€” silently, with an explanatory dialog. Do not patch the executable to bypass the check.

## Screen-reader output

The included 32-bit Prism 0.17.3 build supports NVDA, UI Automation, Windows OneCore speech, PC Talker, ZDSR, and Boy PC Reader. Prism selects the best available backend and sends each announcement to its speech and braille-capable output path.

Start your screen reader **before** launching the game. The first expected announcement is "Square Enix."

## Prerequisites

- Windows 10 or newer.
- Chrono Trigger installed through Steam.
- A **32-bit .NET 9 Desktop Runtime**. Any 9.0.x revision works; no specific patch version is required, and `DOTNET_ROOT_X86` does not need to be set.
- Visual Studio 2022 Build Tools with the C++ x86 toolset, and the .NET SDK â€” only if you are building from source.

## Install

From the repository, in PowerShell:

```powershell
& '.\tools\Package-Mod.ps1'
& '.\tools\Deploy-Mod.ps1'
```

`Deploy-Mod.ps1` builds the native launcher, lays out the portable tree, removes the superseded ASI proxy, and then runs the installer. Windows shows a security prompt because registering the launch redirect writes to `HKEY_LOCAL_MACHINE`; approve it, then choose **Yes** in the installer dialog.

Deployment is transactional. Existing directories are moved aside first and restored if any step fails. It never writes to `Chrono Trigger.exe`, and re-checks its hash afterwards.

To verify an installation at any time:

```powershell
& '.\tools\Verify-Deployment.ps1'
```

Every path is derived at runtime, so the repository works from any location and for any user. Pass `-GameRoot` if the repository does not live inside the game folder.

## Launch

A normal Steam launch is all that is required. No script, no separate shortcut, no Steam launch option, no Reloaded window.

### Upgrading from an earlier version

Earlier versions injected Reloaded through Ultimate ASI Loader, deployed as `winmm.dll` beside the game together with `Reloaded.Mod.Loader.Bootstrapper.asi`. The native launcher replaces both, and `Deploy-Mod.ps1` deletes them â€” but only when their SHA-256 matches the files this mod installed, so a proxy DLL belonging to some other mod is left alone and reported instead. Leaving them in place would load Reloaded twice.

Earlier versions also required a machine-wide `DOTNET_ROOT_X86` environment variable and a `Launch Chrono Trigger Accessible.ps1` script. Neither is used any more; the variable can be deleted and the script has been removed from the repository.

## How it works

```
Steam starts "Chrono Trigger.exe"
  -> Windows redirects to ChronoTriggerAccessibility.Launcher.exe   (IFEO "Debugger" value)
       -> verifies the game's SHA-256
       -> borrows %APPDATA%\Reloaded-Mod-Loader-II\ReloadedII.json and aims it
          at <game>\Reloaded-II  (restored on every exit path)
       -> starts the real game, bypassing the redirect so it cannot recurse
       -> injects <game>\Reloaded-II\Loader\X86\Bootstrapper\...Bootstrapper.dll
       -> waits for the game, forwards its exit code, gives the pointer back
```

Deployed layout:

```
<game>\Reloaded-II\Loader\X86\**                        Reloaded-II 1.30.3, x86 only
<game>\Reloaded-II\Mods\reloaded.sharedlib.hooks\**      hook implementation, 1.16.3
<game>\Reloaded-II\Mods\chrono.trigger.accessibility\**  this mod, plus prism.dll
<game>\Reloaded-II\Apps\chrono trigger.exe\AppConfig.json
<game>\Accessibility\Launcher\*.exe                      launcher and installer
<game>\Accessibility\Backups\<timestamp>\**              previous copy, kept by deployment
```

Two design points are worth knowing before changing the launcher:

- **The AppData pointer swap is unavoidable.** Reloaded hardcodes `%APPDATA%\Reloaded-Mod-Loader-II\ReloadedII.json` in both the managed loader (`Reloaded.Mod.Loader.IO/Paths.cs`) and the C++ bootstrapper. `portable.txt` only takes effect *after* that file is located, and `ReloadedPortable.txt` means "relaunch synchronously through the launcher exe", which a launcher-less tree does not have. So the launcher borrows the pointer and gives it back, guarded by a named mutex, a durable backup, and recovery of a leftover backup on the next start. The mutex name is shared with the author's Blind Soldier mod deliberately, so two portable Reloaded installs cannot swap that file concurrently.
- **The game's primary thread is resumed before injection, on purpose.** Injecting into a pristine `CREATE_SUSPENDED` process, or while the process is parked at its initial loader breakpoint, deadlocks: that breakpoint is raised from inside ntdll's process initialisation, so the primary thread owns the loader lock and a remote `LoadLibraryW` waits on an owner that can never release it. This was measured, not assumed. See the comment in `launcher.cpp`.

## Uninstall

Close Chrono Trigger first, then:

```powershell
& '<game>\Accessibility\Launcher\ChronoTriggerAccessibility.Installer.exe' /uninstall
```

Approve the security prompt and choose Yes. The game then starts normally without the mod. The uninstaller removes the launch redirect only if the mod created it, so it can never delete another tool's redirect.

Afterwards you may delete `<game>\Accessibility` and `<game>\Reloaded-II`. Nothing else is added to the game folder and no original game file is modified.

## Logs and troubleshooting

Two logs matter:

- `<game>\Accessibility\Launcher\ChronoTriggerAccessibility.Launcher.log` â€” the launcher's own record: hash check, pointer lease, injection, exit code.
- `%APPDATA%\Reloaded-Mod-Loader-II\Logs\` â€” the newest `Chrono Trigger` log. Search it for `[chrono.trigger.accessibility]`; it records the executable check, each hook, the selected Prism backend, semantic events, and the exact announcement text.

| Symptom | Cause and fix |
|---|---|
| A dialog titled **"Oh Noes!"** | That is Reloaded's own error box, never the game's. It means Reloaded could not find its app configuration. Run `Verify-Deployment.ps1`. |
| Game starts but says nothing | Check the launcher log for the injection result, then the Reloaded log for the Prism backend. Start your screen reader before the game. |
| "not the build the accessibility mod supports" | Verify the game files in Steam. The mod deliberately refuses to hook an unknown build. |
| Game will not start at all | The launch redirect may point at a missing launcher. Run the installer with `/uninstall`, then deploy again. |
| No x86 .NET 9 runtime | Install the 32-bit .NET 9 Desktop Runtime. Do not substitute an x64-only runtime; the game is 32-bit. |
| A blocking accessibility error | Stop testing that screen and keep the newest log. The mod treats missing interactive information as a fatal defect rather than continuing silently. |

## Build from source

```powershell
& '.\tools\Build-Native.ps1'      # x86 launcher and installer; asserts PE machine 0x14C
& '.\tools\Package-Mod.ps1'       # managed mod + Prism into artifacts\package
& '.\tools\Deploy-Mod.ps1'        # lay out, register, verify
& '.\tools\Verify-Deployment.ps1'
```

`Package-Mod.ps1` writes `SHA256SUMS.txt` over every packaged payload file.

Third-party licensing and reviewed binary details are in `THIRD-PARTY-NOTICES.md`. Provenance and per-file hashes for the vendored Reloaded payload are in `native/reloaded-ii/v1.30.3/SOURCE.md`.
