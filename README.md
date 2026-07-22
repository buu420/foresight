# Chrono Trigger Accessibility

This Reloaded-II mod adds screen-reader output to the Windows Steam release of Chrono Trigger from startup through the title menu, Control Descriptions, New Game settings, and the initial character-name confirmation. It does not yet make gameplay, dialogue, combat, inventory, shops, or maps accessible.

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

Packaging creates `artifacts\package\chrono.trigger.accessibility` and writes `SHA256SUMS.txt` over every packaged payload file. Deployment replaces only that named Reloaded mod directory, updates the Chrono Trigger Reloaded application profile while preserving unrelated properties and mod entries, and copies the accessible launcher to the game directory. It never overwrites a game executable or other original game binary.

## Launch

Start the game with:

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

1. Remove `chrono.trigger.accessibility` from `EnabledMods` and `SortedMods` in `C:\Program Files (x86)\Steam\steamapps\common\Spyro Reignited Trilogy\mod-tools\reloaded-ii\Release\Apps\chrono trigger.exe\AppConfig.json`, or disable it in Reloaded-II.
2. Delete only `C:\Program Files (x86)\Steam\steamapps\common\Spyro Reignited Trilogy\mod-tools\reloaded-ii\Release\Mods\chrono.trigger.accessibility`.
3. Delete `G:\SteamLibrary\steamapps\common\Chrono Trigger\Launch Chrono Trigger Accessible.ps1` if the shortcut is no longer wanted.

The deployer does not alter original game files. Keep the shared-hooks mod and the per-user x86 .NET runtime if another accessibility mod uses them.

Third-party licensing and reviewed binary details are in `THIRD-PARTY-NOTICES.md`, Prism's `LICENSE` and `NOTICE`, and the Reloaded license texts under `LICENSES` in the packaged mod.
