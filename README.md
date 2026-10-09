# Foresight — Chrono Trigger Accessibility Beta

**Foresight 0.3.50 is a beta** of a screen-reader accessibility mod for the Windows Steam edition of Chrono Trigger. It provides menu and dialogue speech, battle information, footsteps, spoken route guidance, automatic walking with keyboard or controller navigation, and bike-race feedback.

Story and optional-destination data span the game, but this is **not a completed end-to-end accessibility playthrough**. Some routes, menus, encounters and minigames still need testing or further work. Please report missing information and failed routes as bugs.

[Published beta downloads](https://github.com/buu420/foresight/releases) · [Report a problem](https://github.com/buu420/foresight/issues)

Version 0.3.50 corrects premature drinking-contest guidance at the Ioka feast. Companions who have native NPC conversations appear in People under their current character names. Unnamed people use reviewed appearance labels, and repeated appearances receive stable letters such as A and B. See [this build's changes and testing notes](docs/releases/v0.3.50.md).

## Install the beta download

You need Windows 10 or 11, your own Steam copy of Chrono Trigger, and a screen reader. The ZIP includes the portable Reloaded-II loader, hook library, Prism and a private **32-bit .NET 9 runtime**. No SDK, separate Reloaded installation or global runtime setting is needed.

1. Close Chrono Trigger.
2. Download the standalone **Foresight-v[version]-beta-win-x86.zip** from the release or the supplied test build. GitHub's automatic “Source code” downloads are for developers.
3. If another mod already uses `winmm.dll`, resolve that loader conflict before copying files. Extract the ZIP and copy its contents into the game folder, beside `Chrono Trigger.exe`. Steam's Properties → Installed Files → Browse opens that folder. `Accessibility` and `Reloaded-II` should sit directly beside the executable.
4. Run **Install Foresight.cmd** to verify the files. Fresh installations need no registry entries or administrator prompt. Upgrading an older Foresight installation may request administrator permission once to remove its old launch registration.
5. Start your screen reader, then launch the game normally from Steam.

Use the classic game interface. Touch-interface Inventory, Equipment, Tech, Party and shop menus are not implemented. Testing has primarily used English game text. The supported Steam app ID is **613830**.

For updates, close the game, copy the new ZIP's contents over the mod files and run Install Foresight again. Internal names still contain `ChronoTriggerAccessibility` and `chrono.trigger.accessibility` for compatibility. Saves and any separately installed narration pack are preserved.

## Remove the beta

Close the game and run **Uninstall Foresight.cmd**, or uninstall through Accessibility Mod Manager if it installed the mod. Removal deletes the package files. The standalone uninstaller preserves files whose contents have changed, along with saves, backups and separately installed narration. No registry cleanup is needed for the new loader.

The game loads Foresight through `winmm.dll` in its own folder. Removing that mod-owned file disables Foresight; Steam can then launch the original game normally. Do not remove another mod's proxy or shared files. Existing legacy launch registrations are cleared during upgrade, before this file-based installation is accepted.

## Beta scope

The mod reads startup/main menus, Settings, Extras, New Game/name entry, confirmations, dialogue, choices, classic Inventory/Equipment/Tech/Party/Save screens, shops and supported battle commands, targets, Items and Techs. Navigation covers fields, world maps, vehicles, people, exits, objects, save points, enemies and story/optional objectives. Native content data accounts for all 331 treasure records; this does not prove every pickup route has been successfully played through.

Minigames are not comprehensively accessible. Some icons, battle effects and result states remain unverified. Silent scene actions and NPC gestures do not yet have comprehensive descriptions. Routes can fail or get stuck in live story states. Automatic walking does not fight, choose dialogue or press Confirm for you.

Movie-description playback works with a separately prepared local pack. **This public download does not include the development tester's voice pack, private recordings, game movies or replacement movie files.** It preserves an existing local pack; those descriptions play alongside the original soundtrack without ducking.

Controller navigation was tested in game and reported working by the user. This does not verify every controller or Steam Input configuration. The keyboard layout used for testing is WASD or arrows to move, X to confirm, C to cancel and V for the game menu; those game bindings remain configurable. All mod hotkeys and controller buttons are listed below.

## Mod-manager status

Foresight's beta is available in the author's [buu420 catalog](https://github.com/buu420/buu-s-mods) for [Accessibility Mod Manager](https://github.com/RealAmethyst/AccessibilityModManager). In the manager, add the catalog source below through Developers → Add source, refresh, and choose the beta release for **Chrono Trigger — Foresight**. If you already use this source for Blind Soldier, refresh it.

`https://raw.githubusercontent.com/buu420/buu-s-mods/main/index.json`

The manager installs and removes files normally. No registry entry or uninstall cleanup hook is required. The only optional elevated action is removing an older Foresight-owned launch registration during migration. This is a custom author catalog, not a listing in the manager's signed central registry. See [integration details](docs/mod-manager-release.md).

Foresight is unofficial and is not affiliated with or endorsed by Square Enix. Its original code uses GPL-3.0-only; dependencies retain their own licenses. See [LICENSE](https://github.com/buu420/foresight/blob/main/LICENSE) and [third-party notices](https://github.com/buu420/foresight/blob/main/THIRD-PARTY-NOTICES.md).

## Detailed controls and technical notes

## In-game menus

Open the menu with V and use the game's normal direction keys, X to confirm, and C to cancel. Inventory reads the selected item, quantity, and displayed help, including reordering state. Equipment reads the selected character or equipped slot, replacement item names and quantities, and the displayed stat preview with increases or decreases. Techs reads the selected character card and tech category, then the selected row and its rendered description, component names, and MP/requirements panel. Party reads the selected member's card and the displayed combinations. These readers follow native focus and do not select or change anything for you.

The classic party-change screen opened at the End of Time also uses the Party reader. Use the game's normal controls to choose and exchange members; the reader follows the visible roster, focus, locks and held member. See [party-change evidence and limits](docs/party-change-scene-0345.md).

Save/Load reads the selected file number, its displayed card text, and the visible party and save details. Empty files do not read an old preview. Save, overwrite, Bookmark, and Resume confirmations use the existing prompt and Yes/No reader. While a confirmation is open, file-list updates cannot interrupt it. Brief redraws are retried; a persistent unreadable selection is announced and logged, with recovery on the next valid capture.

The save list also reads its displayed instruction when the page opens or returns from a confirmation, including when saving is unavailable. Saving, completion, load-error, and other native notices are read when shown.

Extras reads the Movies, Music, and Illustrations lists, their available controls and instructions, Music playback status, and ending-detail review controls. Returning from playback restores the list reader. Ending results read the displayed result and save question, then saving/completion notices. With the description pack installed, movies 001–003 use the same narrated audio when reached in the story or viewed in Extras. Other movies, illustrations, and ending-replay visuals remain undescribed.

The new submenu coverage applies to the classic keyboard interface and Steam Equipment/Save pages. Automated fixtures check selection changes, ownership, visible text, quantities, details, and confirmation transitions. Live keyboard and speech coverage is still unverified for these additions.

## Battle

Use the game's normal directions, X to confirm, and C to cancel. The reader announces the character whose command window is active, the selected Attack/Tech-or-Combo/Item command, native Tech/Item choices and availability, MP costs, item quantities, and committed targets. Group targets are read together. Enemy letters A through H identify fixed sprite slots, so a target keeps its letter when another enemy falls; the letters do not imply how many enemies remain. Visible messages and popups report outcomes and rewards without exposing ordinary enemy HP. Status reading follows the applied visual effect, including icon, color, and pose changes.

| Key | Action during battle |
|---|---|
| 1 / 2 / 3 | Select the corresponding party member for inspection and read their name |
| Shift+H | Read that member's current and maximum HP |
| M | Read that member's current and maximum MP |
| K | Repeat the current battle command or target selection |

The inspection member stays selected when another character's turn starts. Empty slots are announced. These shortcuts do not issue battle commands. Navigation and footsteps stop at battle entry and become available again when the native battle menu is destroyed. Shortcuts run only while the game has focus, without Ctrl, Alt, or Windows held. Shift is required for HP; use the other shortcuts without Shift. Press Shift before H. A captured HP press blocks the game's H action until H is released, including when Shift is released first. Plain H keeps its normal game action. Persistent capture failures are spoken and recorded in the Reloaded log.

Live validation still needs a real encounter in each interface, including party reordering, commands, targets, Techs, Items, status effects, defeat, rewards, and return to exploration. Reading observes the game's timing and does not pause combat or select actions. The battle Item panel has no description to read; descriptions from other screens are not inserted into it. Popups announce the recipient and displayed damage or recovery amount. HP versus MP is not appended to popup amounts until that distinction is verified; Shift+H and M read the exact party HUD values. Element icons within battle messages still need a separate reader.

## Site 32 bike race

Acceleration is automatic. Use the game's **Up/Down** controls to steer and your configured **Dash** action to boost. You have three boosts in the standard race, with a recharge between uses. The alternate race mode has no boosts. Foresight observes the race; you control the bike.

- **High tone:** Johnny is above your lane. **Low tone:** below. **Middle tone:** aligned with your lane. These cues follow the visible track overview even if the camera rotates.
- Speech announces the start, stable lead changes, boosts remaining, recharge readiness, distance milestones, pause, and the result. Distances use the game's displayed counter.
- **K** or **right-stick click (R3)** reads the current lead, Johnny's lane, distance to the finish, score, and boost status. R3 reads race status while racing; it opens the navigation menu during exploration.

Tones stop during pause, loss of game focus, and race exit. Capture failures are announced and retried. Race diagnostics are added to the usual Reloaded log. A tester reported completing the race on 0.3.44; broader timing and playability testing remains needed. **Interactable Objects → Jet bike** now tracks the native bike interaction in either Site 32 parking lot. Press Confirm yourself after arriving. See [race research and verification](docs/bike-race-research.md).

## Navigation

At the End of Time, **Exits → Pillar of light 1–9** distinguishes the lights currently drawn on the platform. Each light keeps its number as more appear. Complete the old man's introduction and Spekkio's lesson before using them; afterwards, press Confirm in the light to hear the game's destination prompt. Routes can approach the touch triggers that open the platform stairs and the room behind the old man, then continue after the game opens the floor. The door approach finishes by moving up, even if you started facing another direction. These changes still need live testing. See [End of Time navigation evidence](docs/end-of-time-navigation-0346.md).

The hub is announced as **End of Time, Main Room**. In Spekkio's room, **Objects → Door to the main room** identifies the return doorway; **Exits → End of Time platform** routes through it. The return door opens by moving down into it. Verified automatic tile-copy passages use a reachable opening contact and continue only after the game supplies opened floor. Doors requiring a key, switch, story event or Confirm retain those requirements. These changes need live testing. See [closed-door navigation evidence](docs/closed-door-navigation-0347.md).

Repeated exit names receive letter suffixes, such as **Exit A**, **Exit B**, or **Outside, exit A**, in field, world-map and flight navigation. Once assigned, a suffix stays with that target for the current area and navigation mode. A name can gain its first suffix when another exit with that name becomes available.

Use these keys while controlling the party in a local field area, walking on the world map, or flying a vehicle:

| Keys | Action |
|---|---|
| U / O | Previous / next category: People, Exits, Interactable Objects, Story Events, Enemies |
| J / L | Previous / next destination |
| K | Repeat the destination and current manual leg, or its direction and distance when idle |
| I | Start counted spoken directions while you move |
| P | Start or stop automatic walking |
| F8 | Turn footsteps off or on for the current game session |

Controller navigation uses the same destinations and routes:

| PlayStation / Xbox button | Action |
|---|---|
| Right-stick click (R3) | Open or close the spoken navigation menu |
| L1 / LB and R1 / RB | Previous and next category while the navigation menu is open |
| D-pad Up / Down | Previous and next destination |
| Square / X | Close the menu and start automatic walking |
| Cross / A | Close the menu and start spoken step-by-step guidance while you move |
| Circle / B | Close the navigation menu |

Opening the navigation menu stops the current route and reads the category and destination. Release the closing or start button before using the controller for the game again. Controller input is consumed while browsing; keyboard controls remain available. R3 is unused by the supported game build. Steam Input must pass through right-stick click, rather than remapping it to a keyboard or game action. Xbox layouts and recognized raw DualShock 4 / DualSense layouts are supported; see the controller report for device identification limits. The user confirmed controller navigation working in game; other device layouts remain unverified.

Browsing to another destination stops the old route. Manual movement, menus, dialogue, loss of focus, area changes, unreadable state, and blocked movement stop automatic walking. Press P again to restart it. Walking uses the game's ordinary direction controls, plus its Dash action when needed on moving floors, and does not interact, choose dialogue, or bypass collisions. At a person or object activated with Confirm, automatic walking makes the final turn toward its interaction point; manual guidance announces that turn and checks the live facing. K reports whether the target is within reach and which way to face. Press the game's Confirm button yourself. Touch pickups and floor triggers use their contact positions instead. A menu-reading error cancels the current walk but no longer permanently disables navigation keys.

Factory conveyors announce their movement direction when you step on them, change direction, or step off. K includes the current moving-floor status. Conveyor robots remain readable as moving hazards; guidance and automatic walking will not target one. Getting caught starts the westward inspection ride along the upper belt. On the lower east-moving belt, run west and step south into the gaps to avoid robots. After the west-end inspection drop, select **Reach the warehouse walkways** under Story Events: it leads south through the conveyor passage, then back to the upper walkway through its other door. Further objectives use the separate room before the crane to reach its control alcove. Automatic walking respects your native run setting. If moving against a belt requires a manual toggle, the mod stops and asks you to enable running with your Dash control. In the laboratory, Story Events guides to the entrance fight while the hatch terminal is hidden, then to the visible terminal and the opened ladder. You still fight and press Confirm yourself. See [Factory repair evidence and limits](docs/factory-conveyors-and-hatch.md).

During the Arris Dome rat chase, select **Catch the rat** under Story Events, or the **Rat** entry, and start automatic walking. Then press and hold your usual game Confirm button. Pursuit stays active when you reach the moving rat, announces when Confirm can reach it, and follows again as it moves. Confirm alone does not cancel this chase; movement keys, other game actions, P, or opening the controller navigation menu still stop it. The game performs the catch. If the rat escapes, leave the rafters and return to retry. Manual guidance also continues tracking the rat; you supply movement and Confirm.

The local maps are 2D. One navigation step means one 16-pixel map tile, not one key press. Directions use left, right, up, and down. Partial tiles use quarter steps. Routes prefer fewer turns among equally short paths, and walking follows cardinal edges with the native body clearance.

Press I for manual guidance. It speaks only the current leg: "Right 5 steps." When you reach the turn, it gives the next instruction, such as "Up 1 step." K repeats the destination and the distance remaining in that leg. A K press that coincides with a new turn or correction produces one instruction. If you leave the route, guidance asks you to stop. Release the movement keys; once Crono stops, it recalculates from your position and speaks the first corrected leg. I supplies speech only and leaves movement under your control. Automatic walking with P keeps its initial route overview.

The world map is also 2D. Its native walking step is eight pixels. Choose Exits with U/O to browse visible entrances and optional guide areas, or Story Events for an available current objective. World entrances use the game's loaded labels, including custom character names. On arrival, press the game's Confirm button to enter; automatic walking never presses it. The live camera and node transforms determine visibility at the current resolution. Story routes select the next native entrance or passage across areas. When the next step requires a vehicle or another era, navigation points to an available vehicle and reads the travel instruction. The time gauge reads its selected era. Vehicle movement and landings use the native flight and collision rules; Confirm still belongs to the player.

Local targets come from live field actors, the current exit grid, and the native unopened treasure table. People includes creatures such as cats. Active interaction targets, scenery, treasure chests, and item pickups can be selected before discovery throughout the installed field areas. They use current native positions and disappear when retired, disabled, or collected. Reward contents are not listed. Script gates and encounter regions use their native coordinate and progress tests.

For an automatic passage such as the castle stairs, navigation first approaches its native opening trigger. It continues only after the live collision map allows the next leg. A guard who requires conversation is a separate objective; press Confirm yourself when instructed.

Spekkio's walking lesson gives one checkpoint at a time, using the game's own lap counters. Near a wall, it may say to continue in a direction until the checkpoint registers. I leaves this movement to you; P makes a short final movement and stops if the checkpoint does not register. Once all three laps register, the objective returns to Spekkio.

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

The bootstrap helper and mod verify this hash and refuse to hook anything else. A startup failure displays an error and closes the game. Do not patch the executable to bypass the check.

## Screen-reader output

The included 32-bit Prism 0.17.3 build supports NVDA, UI Automation, Windows OneCore speech, PC Talker, ZDSR, and Boy PC Reader. Prism selects the best available backend and sends each announcement to its speech and braille-capable output path.

Start your screen reader **before** launching the game. The first expected announcement is "Square Enix."

## Build and deploy from source

Building requires Visual Studio 2022 Build Tools with the C++ x86 toolset and the .NET 9 SDK. Public downloads already contain the native loader, managed mod and private x86 .NET 9.0.20 runtime. Source deployment expects that runtime under `Accessibility/Runtime/dotnet/x86`.

```powershell
& '.\tools\Build-Native.ps1'
& '.\tools\Package-Mod.ps1'
& '.\tools\Deploy-Mod.ps1'
& '.\tools\Verify-Deployment.ps1'
```

Deployment backs up replaced files and restores them if verification fails. It never modifies `Chrono Trigger.exe`. Pass `-GameRoot` when the repository is outside the game folder. Unknown proxy DLLs are preserved and reported. The developer migration removes the reviewed old `Reloaded.Mod.Loader.Bootstrapper.asi`; public installation refuses an unresolved old ASI loader to prevent double injection. Old Foresight-owned IFEO values are removed once; the new loader never creates them. Historical launcher/installer sources remain for migration research and regression tests and are excluded from public packages.

## How startup works

A normal Steam launch starts the original game. Windows loads Foresight's local `winmm.dll`, which forwards Windows multimedia and controller calls to the system DLL. A worker starts `Accessibility/Bootstrap/Foresight.Bootstrap.exe` to attach the bundled Reloaded loader to that same game process. API forwarding becomes ready before accessibility injection begins, avoiding an initialization dependency cycle.

Startup also waits for the managed mod to acknowledge that Prism and the required hooks are active; successful DLL injection alone is not treated as accessibility readiness.

The helper verifies the executable, process path and architecture. It temporarily leases `%APPDATA%/Reloaded-Mod-Loader-II/ReloadedII.json`, using the same mutex as Blind Soldier, then restores the original file when the game exits. This is a configuration file, not the Windows registry. Runtime environment settings exist only inside the game process; no global environment changes are made.

The files used to load Foresight are:

- `winmm.dll`
- `Accessibility/Bootstrap/Foresight.Bootstrap.exe`
- `Accessibility/Runtime/dotnet/x86/`
- `Reloaded-II/Loader/X86/`
- `Reloaded-II/Mods/chrono.trigger.accessibility/`
- `Reloaded-II/Mods/reloaded.sharedlib.hooks/`

The public package contains `Foresight-SHA256SUMS.txt` with every shipped file's hash. It includes no game executable, game data, private recordings or replacement movies.

## Logs and troubleshooting

- `Accessibility/Logs/proxy.log` records native startup.
- `Accessibility/Logs/bootstrap.log` records validation, injection and the configuration lease.
- The newest Chrono Trigger log in `%APPDATA%\Reloaded-Mod-Loader-II\Logs\` records hooks, the Prism backend and announcements. Search for `[chrono.trigger.accessibility]`.

If startup reports missing or changed files, close the game and reinstall the complete package. If another mod already owns `winmm.dll`, do not overwrite it; the loaders need a compatibility arrangement. Start the screen reader before launching the game. The first expected announcement is “Square Enix.”

For an accessibility error or failed route, keep the latest log and report the screen, location and action that preceded it. Beta coverage is described above; a complete catalog is not proof of a complete successful playthrough.

Third-party licensing and reviewed binary details are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Vendored Reloaded provenance and hashes are in `native/reloaded-ii/v1.30.3/SOURCE.md`.
