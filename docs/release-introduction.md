# Foresight — Chrono Trigger Accessibility Beta

**Foresight 0.3.40 is the first public beta** of a screen-reader accessibility mod for the Windows Steam edition of Chrono Trigger. It provides menu and dialogue speech, battle information, footsteps, spoken route guidance and automatic walking with keyboard or controller navigation.

Story and optional-destination data span the game, but this is **not a completed end-to-end accessibility playthrough**. Some routes, menus, encounters and minigames still need testing or further work. Please report missing information and failed routes as bugs.

[Download the beta](https://github.com/buu420/foresight/releases/tag/v0.3.40) · [Report a problem](https://github.com/buu420/foresight/issues)

## Install the beta download

You need Windows 10 or 11, your own Steam copy of Chrono Trigger, and a screen reader. The ZIP includes the portable Reloaded-II loader, hook library, Prism and a private **32-bit .NET 9 runtime**. No SDK, separate Reloaded installation or global runtime setting is needed.

1. Close Chrono Trigger.
2. Download **Foresight-v0.3.40-beta-win-x86.zip**. GitHub's automatic “Source code” downloads are for developers.
3. Extract it and copy its contents into the game folder, beside `Chrono Trigger.exe`. Steam's Properties → Installed Files → Browse opens that folder. `Accessibility` and `Reloaded-II` should sit directly beside the executable.
4. Run **Install Foresight.cmd**. Approve the Windows administrator prompt, then choose Yes in the Foresight installer. Wait for the installed confirmation.
5. Start your screen reader, then launch the game normally from Steam.

Use the classic game interface. Touch-interface Inventory, Equipment, Tech, Party and shop menus are not implemented. Testing has primarily used English game text. The supported Steam app ID is **613830**.

For updates, close the game, copy the new ZIP's contents over the mod files and run Install Foresight again. Internal names still contain `ChronoTriggerAccessibility` and `chrono.trigger.accessibility` for compatibility. Saves and any separately installed narration pack are preserved.

## Remove the beta

Close the game and run **Uninstall Foresight.cmd**. Approve the administrator prompt and removal confirmation. **Wait for the success message before deleting files.** Cancellation or failed removal leaves files available for retry. Never delete a registered launcher first.

The uninstaller removes the matching mod-owned launch redirect and leaves files in place. After success, the files listed in `Foresight-SHA256SUMS.txt` can be removed. Preserve shared mod files, saves, backups and any separate audio-description pack. Restore modified movie files through that pack's own recovery process before deleting its backups.

## Beta scope

The mod reads startup/main menus, Settings, Extras, New Game/name entry, confirmations, dialogue, choices, classic Inventory/Equipment/Tech/Party/Save screens, shops and supported battle commands, targets, Items and Techs. Navigation covers fields, world maps, vehicles, people, exits, objects, save points, enemies and story/optional objectives. Native content data accounts for all 331 treasure records; this does not prove every pickup route has been successfully played through.

Minigames are not comprehensively accessible. Some icons, battle effects and result states remain unverified. Silent scene actions and NPC gestures do not yet have comprehensive descriptions. Routes can fail or get stuck in live story states. Automatic walking does not fight, choose dialogue or press Confirm for you.

Movie-description playback works with a separately prepared local pack. **This public download does not include the development tester's voice pack, private recordings, game movies or replacement movie files.** It preserves an existing local pack; those descriptions play alongside the original soundtrack without ducking.

Controller navigation was tested in game and reported working by the user. This does not verify every controller or Steam Input configuration. The keyboard layout used for testing is WASD or arrows to move, X to confirm, C to cancel and V for the game menu; those game bindings remain configurable. All mod hotkeys and controller buttons are listed below.

## Mod-manager status

The FFVII / Blind Soldier chats confirmed the publication process for [Accessibility Mod Manager](https://github.com/RealAmethyst/AccessibilityModManager) and the existing [buu420 catalog](https://github.com/buu420/buu-s-mods). **Foresight is not listed yet.** The manager currently continues deleting files when its elevated uninstall cleanup is declined or fails. Foresight needs that cleanup to remove its launch redirect, so the catalog package is deferred until failed cleanup can preserve the launcher. Use the standalone ZIP for now. See [integration details](https://github.com/buu420/foresight/blob/main/docs/mod-manager-release.md).

Foresight is unofficial and is not affiliated with or endorsed by Square Enix. Its original code uses GPL-3.0-only; dependencies retain their own licenses. See [LICENSE](https://github.com/buu420/foresight/blob/main/LICENSE) and [third-party notices](https://github.com/buu420/foresight/blob/main/THIRD-PARTY-NOTICES.md).

## Detailed controls and technical notes
