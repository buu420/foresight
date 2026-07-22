# Chrono Trigger startup and New Game accessibility verification

Date: 2026-07-21 (America/Chicago)

## Supported runtime

- Game: Windows Steam `Chrono Trigger.exe`, product version 2.0.0.1, x86.
- Game SHA-256: `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
- Loader: Reloaded-II 1.30.2 with `reloaded.sharedlib.hooks` 1.16.3.
- Screen-reader bridge: Prism 0.17.3 x86.
- Prism SHA-256: `6A84322E42D1B4123E2E66E9887CFDF0CDEA2A972FA40FC7B7185AEC77F5178A`.
- Managed runtime: .NET 9.0.18 x86, including Windows Desktop Runtime.

## Native research

Ghidra 12.1.2 analysis was performed against the exact supported executable. The final catalog contains 21 unique, expected-byte-verified hooks. In addition to startup, title, Control Descriptions, Mode Select, and name-screen hooks, the audit established two distinct direct-name callbacks:

- delayed keyboard/IME open at RVA `0x2C1B50`;
- common keyboard/IME close and action restoration at RVA `0x2C1BA0`.

The close callback is shared by ordinary Enter closure and the menu event path. Its exact payload correlation prevents duplicate or invented closure narration. The final behavior restates the full current name and the authoritative key-2 action focus after IME closure.

## Automated verification

Release test command:

```powershell
dotnet test .\ChronoTriggerAccessibility.slnx -c Release --no-restore
```

Result: 244 passed, 0 failed, 0 skipped.

- Core: 26/26.
- Native: 96/96.
- Prism: 8/8.
- Mod/integration/packaging: 114/114.

The native `Test-PrismWin32.ps1` harness also passed against the reviewed x86 Prism DLL. Packaging completed with zero warnings and zero errors and produced a 27-file, SHA-256-manifested payload.

## Deployment verification

The transactional deployer installed and independently verified:

- mod: `C:\Program Files (x86)\Steam\steamapps\common\Spyro Reignited Trilogy\mod-tools\reloaded-ii\Release\Mods\chrono.trigger.accessibility`;
- Reloaded profile: `C:\Program Files (x86)\Steam\steamapps\common\Spyro Reignited Trilogy\mod-tools\reloaded-ii\Release\Apps\chrono trigger.exe\AppConfig.json`;
- launcher: `G:\SteamLibrary\steamapps\common\Chrono Trigger\Launch Chrono Trigger Accessible.ps1`.

`Verify-Deployment.ps1` and the launcher's `-VerifyOnly` mode both passed. They verify the game and Prism hashes, PE architecture, complete package manifest, Reloaded profile and shared-hook dependency, x86 .NET host/frameworks, and exact launcher copy.

Reloaded-II's complete 24-field mod manifest is packaged up front, including its runtime-computed `HasExports: false` and `CanUnload: false` values. A post-launch verification also passed: source, package, and deployed `ModConfig.json` remained byte-identical at SHA-256 `03E59C99C823A6B2F86A66FADF1A2B3FAB9D99DA40E67F4A0EFA89E88766A499`.

## Live smoke verification

The game was launched through the deployed accessible launcher with NVDA active, then closed at the opening movie without accessing save data. Reloaded log:

`C:\Users\User\AppData\Roaming\Reloaded-Mod-Loader-II\Logs\2026-07-22 04.57.46 ~ Chrono Trigger.txt`

Observed:

- the x86 Reloaded bootstrap loaded the shared-hooks dependency and accessibility mod;
- the exact executable identity and all 21 expected-byte contracts were logged;
- Prism selected the NVDA backend;
- all 21 hooks were logged prepared/inactive before the atomic activation boundary;
- all 21 hooks were logged active only after complete activation;
- accessibility runtime activation completed;
- Square Enix scene entry emitted `Square Enix.`;
- opening-movie entry emitted `Opening movie.`;
- three generation-scoped visual descriptions were dispatched in order;
- no managed exception, coverage failure, fatal accessibility error, or loader error occurred before clean process exit.

The Windows capture helper could identify the unique Chrono Trigger window but could not capture it (`SetIsBorderRequired` returned `0x80004002`), so no blind key input was sent. Final keyboard and screen-reader interaction through the title menu, Control Descriptions, Mode Select, and name screens is reserved for the user's acceptance test.
