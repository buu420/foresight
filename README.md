# Chrono Trigger Accessibility

This mod adds screen-reader output to the Windows Steam release of Chrono Trigger. Version 0.3.8 fixes world automatic walking being rejected before reaching the movement code, and stabilizes spoken directions after a missed turn. It retains footsteps, world/local transition recovery, and the Settings speech repair. Intro audio descriptions remain pending.

Settings reads native category labels, rows, current values, and nested controls, including Screen Size. This is a test release: automated checks and exact native hook-byte verification do not establish live gameplay coverage. Combat, vehicles, shops, and the Inventory, Equipment, and Bookmark subpages remain outside the implemented coverage.

## Navigation

Use these keys while controlling the party in a local field area or walking on the world map:

| Keys | Action |
|---|---|
| U / O | Previous / next category: People, Exits, Interactable Objects, Story Events |
| J / L | Previous / next destination |
| K | Repeat the destination and remaining route, or its direction and distance when idle |
| I | Start counted spoken directions while you move |
| P | Start or stop automatic walking |
| F8 | Turn footsteps off or on for the current game session |

Browsing to another destination stops the old route. Manual movement, menus, dialogue, loss of focus, area changes, unreadable state, and blocked movement stop automatic walking. Press P again to restart it. Walking uses the game's ordinary directional input and does not interact, choose dialogue, or bypass collisions. At a person or object, use K to hear its direction, face it, and press the game's confirm button yourself.

The local maps are 2D. One navigation step means one 16-pixel map tile, not one key press. Directions use left, right, up, and down: for example, "Left 3 steps, then down 2 steps." Partial tiles use quarter steps. Guidance announces turns; K repeats the remaining instructions. Routes prefer fewer turns among equally short paths, and walking follows cardinal edges with the native body clearance.

On long manual legs, an advance cue announces the coming turn. If you miss the route, guidance asks you to stop. Release the movement keys; once Crono stops, it gives a fresh route. It no longer recalculates on every animation frame while you are still moving. I supplies speech only and leaves movement under your control.

The world map is also 2D. Its native walking step is eight pixels. Choose Exits with U/O to browse visible area entrances, or Story Events for an available current objective. World entrances use the game's loaded labels, including custom character names. On arrival, press the game's Confirm button to enter; automatic walking never presses it. The live camera and node transforms determine visibility at the current resolution. The first version supports walking and routes within one world map, but does not route across its wrapping outer edges or through intermediate local areas.

Local targets come from live field actors, the current exit grid, and rendered unopened chests. People includes creatures such as cats. Appearance labels are deliberately general where the game supplies no name. Partially visible exit and chest tiles are included. Previously seen targets retain their last seen positions when outside the current view; hidden pickups and chest contents are not listed. Discovery is kept for the current area visit. Local fields use the native camera window conservatively and may omit targets at the outer widescreen margins. Special field movement rules still need individual coverage and testing.

Story Events follows the current story counter and local progress flags, from Mother and the Millennial Fair through the cathedral, the return to the present, the trial, and the prison escape. Routes use visible or discovered native targets. An objective whose destination is not discovered remains readable as a note; I and P explain the missing route. The cathedral's visible map objects, including its organs, switches, and sign, are also available. An undisplayed sparkle is not revealed. Research and guide attribution are in [story-events-and-footsteps.md](docs/story-events-and-footsteps.md).

Footsteps follow actual movement, with five recorded variations, and remain silent against a wall or during menus and dialogue. Their natural stride is independent of the 16-pixel tile used in spoken directions. F8 announces the new setting; footsteps start enabled on each launch. The current bank uses one general surface sound. The player confirmed hearing footsteps in 0.3.7. Movement, capture, reset, and audio-result counters are written to the Reloaded log at most once every two seconds per component.

The player confirmed the earlier local navigation repair and reported world auto-walk failures in 0.3.7. Version 0.3.8 passes the observed missed-turn replay and native input-dispatch checks; gameplay movement still needs live verification. Navigation commands, active positions at most four times a second, input-delivery counters, and stop reasons are recorded in the Reloaded log. Target counts and actor facts are logged when the inventory changes. Moving actors and special field mechanics may still interrupt routes. Automatic interaction, dialogue choices, puzzles, and combat are not implemented. See [world-navigation.md](docs/world-navigation.md) and [navigation-manual-turn-recovery.md](docs/navigation-manual-turn-recovery.md) for evidence and validation limits.

It ships a self-contained, loader-only copy of Reloaded-II inside the game folder. You do not need to install Reloaded-II, run its launcher, or configure anything by hand.

## Supported game build

The native hooks are fail-closed and support only this executable:

- File: `Chrono Trigger.exe`
- Product version: `2.0.0.1`
- SHA-256: `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`
- Architecture: 32-bit x86

Both the launcher and the mod verify this hash and refuse to hook anything else. If it does not match, the game still starts — silently, with an explanatory dialog. Do not patch the executable to bypass the check.

## Screen-reader output

The included 32-bit Prism 0.17.3 build supports NVDA, UI Automation, Windows OneCore speech, PC Talker, ZDSR, and Boy PC Reader. Prism selects the best available backend and sends each announcement to its speech and braille-capable output path.

Start your screen reader **before** launching the game. The first expected announcement is "Square Enix."

## Prerequisites

- Windows 10 or newer.
- Chrono Trigger installed through Steam.
- A **32-bit .NET 9 Desktop Runtime**. Any 9.0.x revision works; no specific patch version is required, and `DOTNET_ROOT_X86` does not need to be set.
- Visual Studio 2022 Build Tools with the C++ x86 toolset, and the .NET SDK — only if you are building from source.

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

Earlier versions injected Reloaded through Ultimate ASI Loader, deployed as `winmm.dll` beside the game together with `Reloaded.Mod.Loader.Bootstrapper.asi`. The native launcher replaces both, and `Deploy-Mod.ps1` deletes them — but only when their SHA-256 matches the files this mod installed, so a proxy DLL belonging to some other mod is left alone and reported instead. Leaving them in place would load Reloaded twice.

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

- `<game>\Accessibility\Launcher\ChronoTriggerAccessibility.Launcher.log` — the launcher's own record: hash check, pointer lease, injection, exit code.
- `%APPDATA%\Reloaded-Mod-Loader-II\Logs\` — the newest `Chrono Trigger` log. Search it for `[chrono.trigger.accessibility]`; it records the executable check, each hook, the selected Prism backend, semantic events, and the exact announcement text.

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
