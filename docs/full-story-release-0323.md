# Version 0.3.23: story, optional areas and pickup navigation

Installed September 15, 2026. Navigation now extends from the first End of Time
visit through Lavos, with the classic sidequests, Lost Sanctum, Dimensional
Vortices and Time's Eclipse. Current story steps use Story Events. Optional
conversations, pickups, switches and passages use People, Interactable Objects
or Exits as appropriate, with the game's prerequisites and completion flags.
Available guide targets do not require camera discovery.

The installed-PC resource index covers 669 scene records, 1,020 static exits,
240 world entrances, all 343 treasure records and 143 item-giving actor identities.
This is resource coverage, not a claim that every record is a distinct reachable
pickup. Live actors, treasure flags, exit destinations and collision determine
what is available. Unopened rewards and game dialogue are not embedded in the index.

The route changes include scripted floor switches and gates, same-scene drops,
disconnected Ocean Palace rooms, Geno Dome's conveyor circuit, Giant's Claw,
Black Omen doors and lifts, Epoch/Dactyl flight and landing, and the Epoch time
gauge. Temporary cinematic visits are not used as Ocean Palace shortcuts.
Manual guidance still gives one leg at a time. The existing controls, counted
footsteps, menus and battle readers are retained.

Guides and community format references were cross-checked against the installed
PC scripts and Ghidra output. See [story objectives](full-story-objectives.md),
[classic optional objectives](classic-optional-objectives.md),
[bonus objectives](bonus-story-objectives.md), [native navigation](whole-game-native-navigation.md),
and [vehicle navigation](vehicle-navigation.md) for the native evidence and limits.

## Verification

- Release suite: **1,705 passed**, zero failures/skips: Core134, Prism8,
  Native769 and Mod794. The final suite uses the regenerated stable catalog.
- Compiler suite: **19 passed**, including all256 three-flag truth tables and
  separate processes with different hash seeds. Condition reduction is now
  deterministic; its earlier unordered reduction could produce equivalent but
  differently divided spatial records.
- All **149 native hook signatures** match the supported executable.
- Installed-x86 vehicle geometry replay: **49/49 expected results**.
- Package build: zero warnings/errors. All **25 deployment checks passed**.
- Exact installed file sets and hashes match **28 mod files, 45 loader/shared-hook
  files and two native launcher/installer files**. The game executable is unchanged.

The final catalog has 4,732 actor identities and 2,387 spatial records. Its
SHA-256 is `7D597EC1F6596F91A2A7E0BA83C1C6A8A0B99FE880070895323EE8B743C21412`.
Eight unused scene records lack scripts. Eleven bounded script expansions are
recorded as incomplete, with audited replacements for the required bonus-area
interactions; incomplete general expansions are not presented as proven routes.

These are automated, resource and native-code checks. This update has **not**
received a complete live playthrough. Vehicle hook timing, moving platforms,
scripted encounters and quest sequences still require ordinary in-game use.
Navigation does not make dialogue choices, enter puzzle button sequences or
select battle commands. No game input or save editing was used for this task.

Build, test, native replay, coverage and deployment evidence is retained locally
under `artifacts/research/release-0323/` and `artifacts/research/full-story-0323/`.
The final test results are in `release-0323/test-results-stable-catalog/`.

## Installed build

Implementation commit: `d9659a4dea039d5bf9361ad15c5a6e7983529485`.
Installed DLL product version: `1.0.0+d9659a4dea039d5bf9361ad15c5a6e7983529485`.

DLL SHA-256:
`240CBB0DCC5249820703DD9FFEDE1A536351CB97FF7C3B7FD8905AA652FE91DB`.

Manifest SHA-256:
`36D15B4CBE4F2A80DA7EE1FA31A2036C01ED0C64EF2C65DF79FF488776D1B5D3`.

Previous installation:
`X:\SteamLibrary\steamapps\common\Chrono Trigger\Accessibility\Backups\20260915-215439`.

Deployment occurred while the game was closed. The existing launch redirect and
x86 .NET runtime passed verification. Start the screen reader and launch normally.
U/O changes categories, J/L selects a destination, K repeats, I starts manual
guidance, P toggles automatic walking, and F8 toggles footsteps.
