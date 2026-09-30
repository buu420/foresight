# Foresight and Accessibility Mod Manager

Checked for the initial public beta on 2026-09-29 using both FFVII chats, the AMM author CLI and the current upstream source.

## Existing publication route

- Manager: <https://github.com/RealAmethyst/AccessibilityModManager>
- Author catalog: <https://github.com/buu420/buu-s-mods>, plugin ID `buu420`.
- Catalog URL: <https://raw.githubusercontent.com/buu420/buu-s-mods/main/index.json>.
- Foresight identity: game ID `chrono-trigger`, Steam app `613830`, executable `Chrono Trigger.exe`, mod name `Foresight (Beta)`.
- Release ZIPs belong in `buu420/foresight` GitHub releases. The catalog points to the completed asset with its SHA-256 and `channel: "beta"`.
- Preserve all FFVII definitions and existing release records when adding the new game.

AMM uses a ZIP with `manifest.json` and a `files/` payload. Its author CLI builds and validates that layout. The Foresight README and full keyboard/controller/battle control tables should also be the game's catalog description. The release's GitHub prerelease flag is separate from its catalog beta channel.

The existing author source is user-added through Developers → Add source, accepting the source warning, then refreshing and selecting beta. It is not in the signed central registry. Central listing needs the registry's application process and maintainer approval: <https://github.com/RealAmethyst/accessibility-mod-manager-registry/blob/main/CONTRIBUTING.md>.

## Release blocker: failed uninstall cleanup

Foresight currently uses a Windows IFEO Debugger entry pointing at `Accessibility/Launcher/ChronoTriggerAccessibility.Launcher.exe`. Removing its files before that entry is removed prevents normal game launch.

In upstream `InstallerEngine.cs`, `TryRunPostUninstall` is best-effort: declined consent, missing/corrupt cached scripts, nonzero exits and exceptions all permit file removal to continue. `UninstallCoreAsync` marks the hook as run and calls rollback/removal afterwards. `FailureFatal` does not provide a required-uninstall gate. Source: <https://github.com/RealAmethyst/AccessibilityModManager/blob/master/src/AccessibilityModManager.Infrastructure/Installer/InstallerEngine.cs>.

Consequently this initial beta publishes the standalone ZIP only. No Foresight game/release entry or unsupported AMM package is published. This is an integration limitation, not an automatic-approval rejection.

Before enabling an AMM package, either the manager must stop uninstall and retain files/receipt when required registry cleanup fails, or Foresight must use an installation architecture that remains launchable when cleanup is skipped. Test declined UAC, missing/corrupt cached scripts, nonzero cleanup exits, successful removal, update rollback and foreign registry ownership. An ordinary successful install test is insufficient.

## Commands after the blocker is resolved

```powershell
amm-author package build --source '<payload>' --game chrono-trigger --version '<version>' --output '<amm.zip>' --project '<fresh catalog checkout>'
amm-author package validate --zip '<amm.zip>' --plugin buu420 --game chrono-trigger --version '<version>'
amm-author index validate --project '<catalog>'
amm-author release publish --game chrono-trigger --version '<version>' --channel beta --repo buu420/foresight --zip '<amm.zip>' --project '<catalog>' --dry-run
```

Install and removal hooks must describe the precise HKLM IFEO values, require administrator access, and operate only on matching Foresight-owned values. Do not reuse Blind Soldier's obsolete-IFEO cleanup script: it targets different games and performs the opposite installation action.
