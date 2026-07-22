# Chrono Trigger Accessibility

This Reloaded-II mod adds screen-reader output to the Windows Steam release of Chrono Trigger for startup and the title menu, Control Descriptions, New Game settings and naming, the Extras hub and Ending Log, both Options interfaces, the ordinary in-game menu, and field dialogue with choices. Combat, movement, inventory subpages, shops, and maps are not yet accessible; unsupported menu subpages are announced explicitly instead of failing silently.

## Supported game build

The native hooks are fail-closed and support only this executable:

- File: `Chrono Trigger.exe`
- Product version: `2.0.0.1`
- SHA-256: `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`
- Architecture: 32-bit x86

An exact hash mismatch stops the mod before any hook is activated. Do not replace or patch the game executable to bypass this check.

## Screen-reader output

The included 32-bit Prism 0.17.3 build supports these backends: NVDA, UI Automation, Windows OneCore speech, PC Talker, ZDSR, and Boy PC Reader. Prism selects the best available backend and sends each announcement to its speech and braille-capable output path.

With a normal launch, the first expected announcement is “Square Enix.” If Reloaded injects after that brief scene, the first announcement can instead describe the opening movie or the current title screen.

In the field, opening the main menu announces its seven visible commands, current selection, time, currency, and party status. Options announces the localized heading, categories or rows, current values, help text, and confirmation choices for either the classic gamepad/keyboard interface or the touch/mouse interface. NPC and story text is announced when the game presents it, including visible dialogue choices and the selected choice.

## Prerequisites

- Windows 10 or newer.
- Chrono Trigger installed at `G:\SteamLibrary\steamapps\common\Chrono Trigger`.
- Reloaded-II 1.30.2 at `C:\Program Files (x86)\Steam\steamapps\common\Spyro Reignited Trilogy\mod-tools\reloaded-ii\Release`.
- Reloaded shared hooks mod `reloaded.sharedlib.hooks` installed in that Reloaded-II instance.
- 32-bit .NET 9.0.18 runtime and Windows Desktop runtime at `C:\Users\User\AppData\Local\ChronoTriggerAccessibility\dotnet-x86`.

Run your screen reader before launching the game when using a screen-reader-specific Prism backend.

## Build, package, and deploy

From this repository in PowerShell:

```powershell
& '.\tools\Package-Mod.ps1'
& '.\tools\Deploy-Mod.ps1'
& '.\tools\Verify-Deployment.ps1'
```

Packaging creates `artifacts\package\chrono.trigger.accessibility` and writes `SHA256SUMS.txt` over every packaged payload file. Deployment stages and verifies every replacement before committing it. If the mod swap, profile update, launcher update, automatic-startup update, or final verification fails, it restores the preceding mod directory, profile, launcher, `winmm.dll`, and Reloaded bootstrapper byte-for-byte (or removes newly created targets from a first install). It never overwrites the game executable or another original game binary.

## Launch

After deployment, a normal Steam launch from Chrono Trigger's existing library entry starts Reloaded and the accessibility mod. No script, separate Steam shortcut, Steam launch option, or already-running Reloaded window is required.

The deployer installs Reloaded-II's supported Ultimate ASI Loader integration beside the game:

- `winmm.dll` is the reviewed 32-bit Ultimate ASI Loader supplied by this repository. Chrono Trigger imports `WINMM.dll`, so Windows loads this proxy during normal startup.
- `Reloaded.Mod.Loader.Bootstrapper.asi` is the reviewed x86 bootstrapper from the installed Reloaded-II 1.30.2 instance.

`DOTNET_ROOT_X86` must be set in the user environment to `C:\Users\User\AppData\Local\ChronoTriggerAccessibility\dotnet-x86`. If that value was added while Steam was already running, fully exit and restart Steam once so subsequently launched games inherit it.

The PowerShell launcher remains available only as a recovery and diagnostic path:

```powershell
& 'G:\SteamLibrary\steamapps\common\Chrono Trigger\Launch Chrono Trigger Accessible.ps1'
```

The launcher sets `DOTNET_ROOT_X86` only for its own process, verifies both required 9.0.18 frameworks, and then invokes this exact Reloaded command:

```powershell
& 'C:\Program Files (x86)\Steam\steamapps\common\Spyro Reignited Trilogy\mod-tools\reloaded-ii\Release\Reloaded-II.exe' --launch 'G:\SteamLibrary\steamapps\common\Chrono Trigger\Chrono Trigger.exe'
```

To check the launcher's game hash, x86 host, hostfxr, and framework prerequisites without starting the game, add `-VerifyOnly`.

## Logs and troubleshooting

Reloaded writes timestamped game logs to `%APPDATA%\Reloaded-Mod-Loader-II\Logs`. Search the newest `Chrono Trigger` log for `[chrono.trigger.accessibility]`; it records the executable check, each required hook, selected Prism backend, semantic events, exact announcement text, and any fatal accessibility failure.

- “Unsupported executable” or a SHA-256 error: in Steam, verify the game files, then rerun `Verify-Deployment.ps1`. The mod intentionally does not hook an unknown build.
- Missing .NET framework: confirm both `shared\Microsoft.NETCore.App\9.0.18` and `shared\Microsoft.WindowsDesktop.App\9.0.18` exist below the configured x86 runtime root. Do not substitute an x64-only runtime.
- Missing shared hooks: install or repair `reloaded.sharedlib.hooks` in the same Reloaded-II instance, then rerun deployment verification.
- No speech: start NVDA or the intended screen reader before launching. If no screen reader is active, Prism may select UI Automation or OneCore. Check the log for the selected backend and any Prism error.
- A blocking accessibility error: stop testing that screen and keep the newest log. The mod treats missing interactive information as a fatal defect rather than continuing silently.

## Uninstall

Close Chrono Trigger and Reloaded-II first. Then:

Only perform steps 3 and 4 if those files were absent before this accessibility mod was deployed **and** no other ASI or Reloaded mod uses them. If you are unsure, leave both loader files in place; disabling and removing the accessibility mod is sufficient.

1. Remove `chrono.trigger.accessibility` from `EnabledMods` and `SortedMods` in `C:\Program Files (x86)\Steam\steamapps\common\Spyro Reignited Trilogy\mod-tools\reloaded-ii\Release\Apps\chrono trigger.exe\AppConfig.json`, or disable it in Reloaded-II.
2. Delete only `C:\Program Files (x86)\Steam\steamapps\common\Spyro Reignited Trilogy\mod-tools\reloaded-ii\Release\Mods\chrono.trigger.accessibility`.
3. Delete `G:\SteamLibrary\steamapps\common\Chrono Trigger\winmm.dll` only if its SHA-256 is `A51C630B2EA3D78AD55A330EA64D510C8C0737F620BE65AD7503B61840D59E37`.
4. Delete `G:\SteamLibrary\steamapps\common\Chrono Trigger\Reloaded.Mod.Loader.Bootstrapper.asi` only if its SHA-256 is `1A9F704549F66E357C0D22C395B57FE4E7BD5248521DBB40E566D2EE1CA809AB`.
5. Delete `G:\SteamLibrary\steamapps\common\Chrono Trigger\Launch Chrono Trigger Accessible.ps1` if the recovery launcher is no longer wanted.

The deployer ensures that the two reviewed loader files are present but leaves matching pre-existing copies untouched; it does not alter original game files. Keep the shared-hooks mod and the per-user x86 .NET runtime if another accessibility mod uses them.

Third-party licensing and reviewed binary details are in `THIRD-PARTY-NOTICES.md`. The package includes Prism's root `LICENSE` and `NOTICE`, the exact `LICENSES` subtree from pinned Prism source commit `9911156998b52fee91fb2cb4f71ac793d4e546c7`, the Reloaded license texts, and the Ultimate ASI Loader MIT license under `LICENSES`.
