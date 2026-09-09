# Handoff: replace Reloaded-II with a slim portable loader

Written by a Cowork session on 2026-08-17 so the work can continue in the
Claude Code CLI. Everything below was verified, not assumed. Where something is
unproven it says so.

---

## Objective

Two things, in order:

1. Strip the current Reloaded-II dependency out of the Chrono Trigger
   accessibility mod and replace it with the slim portable loader pattern the
   user already uses for their Final Fantasy VII mod (Blind Soldier).
2. Once the game starts cleanly under the new loader, work the outstanding
   accessibility issues.

Do not start on item 2 until item 1 verifiably works.

---

## Root cause of "the game doesn't start"

Not subtle. The repo is wired to a machine that is not this one:

| Referenced path | Reality |
|---|---|
| `C:\Program Files (x86)\Steam\steamapps\common\Spyro Reignited Trilogy\mod-tools\reloaded-ii\Release` | **Does not exist** |
| `G:\SteamLibrary\steamapps\common\Chrono Trigger` | Game is on `X:` |
| `C:\Users\User\AppData\Local\ChronoTriggerAccessibility\dotnet-x86` | User is `buu42` |

Affected files: `tools\Deploy-Mod.ps1`, `tools\Verify-Deployment.ps1`,
`tools\Build-PrismWin32.ps1`, `Launch Chrono Trigger Accessible.ps1`,
`README.md`.

The user's requirement: **no fixed absolute paths anywhere.** Everything
relative or derived, so any user can install the mod.

---

## Target architecture

Copy the pattern in `C:\Users\buu42\Downloads\stuff.zip` (the user's DSTS mod).
Read `DSTS.Launcher\launcher.cpp` and `DSTS.Common\common.h` in that zip first
- they are well commented and are the reference implementation.

**Portable Reloaded-II inside the game folder.** No GUI, no `Reloaded-II.exe`,
no shared install:

```
<game>\Reloaded-II\Loader\X86\...          12 files incl. Bootstrapper\
<game>\Reloaded-II\Apps\chrono trigger.exe\AppConfig.json
<game>\Reloaded-II\Mods\chrono.trigger.accessibility\...
<game>\Reloaded-II\Mods\reloaded.sharedlib.hooks\...
```

**IFEO launcher** registered as the "debugger" for `Chrono Trigger.exe`, so a
normal Steam launch runs the launcher, which then:

1. `CreateProcessW(DEBUG_ONLY_THIS_PROCESS | CREATE_SUSPENDED)` - the debug
   flag bypasses IFEO for the child so it does not recurse into itself.
2. `DebugActiveProcessStop` to detach - the IFEO bypass already took effect.
3. `CreateRemoteThread` + `LoadLibraryW` to inject
   `Reloaded.Mod.Loader.Bootstrapper.dll` while the main thread is still
   suspended. Hooks install before any game code runs.
4. `ResumeThread`, then wait and forward the exit code.

**`AppDataSwap` RAII is mandatory, not optional.** The launcher repoints
`%APPDATA%\Reloaded-Mod-Loader-II\ReloadedII.json` at the portable root and
restores the original on every exit path. On this machine that file currently
points at `C:\Users\buu42\AccessXI\external\Reloaded-II`. **Clobbering it
breaks the user's AccessXI mod.** Back up, restore, and test the restore.

This replaces: the Spyro Reloaded-II install, `winmm.dll` (Ultimate ASI
Loader), `Reloaded.Mod.Loader.Bootstrapper.asi`, and the machine-wide
`DOTNET_ROOT_X86` environment variable. All of those go away.

Side benefit: injecting at a suspended entry point should fix the mod missing
the first "Square Enix" announcement, which the README documents as a known
gap caused by late injection.

---

## Decisions already made by the user

1. **Swap the loader only.** Keep the Reloaded mod API - `IMod`,
   `Reloaded.Hooks.Definitions`, `ModConfig.json`, and the
   `reloaded.sharedlib.hooks` dependency. Do not rewrite the mod logic.
2. **No fixed paths.** Relative/derived everywhere, portable for any user.
3. **IFEO launcher**, matching DSTS and Blind Soldier.

---

## Ruled out - do not revisit

**`Blind-Soldier-Setup.exe` does not contain the Reloaded loader.** Verified by
parsing it: it is a .NET single-file bundle (marker at `0x7951A8`, header at
`162160977`, format v6) containing **449 entries totalling 151.7 MB**, all .NET
runtime assemblies plus one 313 KB `Blind-Soldier-Setup.dll`. The `Loader\X86`
strings inside it belong to an embedded PowerShell validator
(`Assert-BlindSwordsmanPrerequisiteBundle`) that only *checks* a prerequisite
tree supplied separately. No payload, no download URL.

**The x64 loader cannot be converted to x86.**
`Reloaded.Mod.Loader.Bootstrapper.dll` is pure native x64 with no .NET
metadata. The other eight are ReadyToRun (PE32+, `magic 0x20b`,
`machine 0x8664`) - managed metadata present but x64 native code baked in, so
architecture-locked. Nothing was converted for Blind Soldier either; Reloaded-II
ships `Loader/X86` and `Loader/X64` side by side and Blind Soldier consumed the
prebuilt X86 folder.

---

## Immediate next step

Stage the x86 payload. `C:\Users\buu42\Downloads\Stage-ReloadedX86.ps1` is
written and ready - read it before running it. It is read-only against sources
and writes only to `C:\Users\buu42\Downloads\ct-reloaded-x86`.

Sources it will consider, best first:

| Path | Reloaded-II version |
|---|---|
| `C:\Games\Final Fantasy VII\Reloaded-II` | **1.30.3** - most complete |
| `C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\Reloaded-II` | 1.30.1 |
| `C:\Users\buu42\AccessXI\external\Reloaded-II` | 1.30.2 |

The user declined to grant the Cowork session access to the `C:\Games` path.
Confirm they are happy for the CLI to read it before using it as the source.

`reloaded.sharedlib.hooks` is version **1.16.3**, byte-identical across all
three installs. It is a **universal mod shipping both x86 and x64 payloads** -
copy it whole. Its x64 files are expected and correct. Only the *loader*
payload must be pure x86.

---

## Environment (verified 2026-08-17)

- **MSVC**: VS 2022 Build Tools at
  `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools`, x86 `cl.exe`
  at `...\MSVC\14.44.35207\bin\Hostx64\x86\cl.exe`. Sufficient to build the
  launcher and installer.
- **.NET**: SDK 10.0.203. x86 runtimes at `C:\Program Files (x86)\dotnet`:
  `Microsoft.NETCore.App` 8.0.0, 9.0.17, 10.0.10; `WindowsDesktop.App` same.
- **OPEN ISSUE**: the repo pins **9.0.18**, which is **not installed** - 9.0.17
  is. `Launch Chrono Trigger Accessible.ps1` hard-asserts 9.0.18 and will throw.
  Either retarget or ship a private runtime beside the game. The user's
  no-fixed-paths requirement argues for a private runtime. **Unresolved.**
- **PATH**: `git` yes, `curl` yes, `7z` no, `pwsh` no (Windows PowerShell only).
- **Drive `X:` is a mapped network drive.** Codex could not see it at all. If
  `Set-Location X:\...` throws `DriveNotFoundException`, either the shell is
  elevated (mapped drives are per-logon-token) or the mapping needs
  reconnecting via File Explorer. Consider switching the scripts to the UNC
  path - it is more robust and fits the portability requirement.

---

## Ghidra scripts - reviewed, two defects

Seven read-only evidence dumpers in
`.worktrees\extras-settings-dialogue-menu\.superpowers\sdd\`. None are part of
the runtime mod; they decompile fixed RVAs and write text reports.

1. **No build validation.** Every script hardcodes RVAs off the image base with
   no SHA-256 or version check. Run against a different `Chrono Trigger.exe`
   they silently emit garbage. This contradicts the fail-closed contract the
   mod itself honours. Add a hash guard that aborts on mismatch.
2. **Unchecked `getFunctionAt`.** `SettingsCallSitesGhidra.java` and
   `QuitBinderGhidra.java` dereference the result without a null check, so a
   missing function is an NPE instead of a clear message.
   `SettingsSubmenusGhidra.java` and `SettingsDeepGhidra.java` handle this
   correctly - copy their pattern.

Neither blocks the loader swap.

---

## Work plan

1. Stage the x86 loader payload (script ready).
2. Vendor `Loader\X86` + `reloaded.sharedlib.hooks` into the repo under
   `native\reloaded-ii\`. Record version + SHA-256 per file in
   `THIRD-PARTY-NOTICES.md`.
3. Port `launcher.cpp` / `installer.cpp` / `common.h` from `stuff.zip` to x86
   Chrono Trigger. All paths relative to the launcher's own directory.
4. Strip old wiring: Spyro dependency, `winmm.dll`, `Bootstrapper.asi`,
   `DOTNET_ROOT_X86`, and every hardcoded path in the four scripts + README.
5. Resolve the .NET 9.0.18 vs 9.0.17 gap.
6. Build, deploy, launch from Steam, and confirm from
   `%APPDATA%\Reloaded-Mod-Loader-II\Logs` that
   `[chrono.trigger.accessibility]` loads and the first announcement fires.
7. Verify `ReloadedII.json` was restored and AccessXI still works.

---

## Constraints from the user

- Screen-reader parity is the goal: convey what a sighted player sees, no more
  and no less. **Missing information is treated as worse than a crash.**
- Never patch or replace `Chrono Trigger.exe`. The mod is fail-closed against
  SHA-256 `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`.
  Bypassing that check is not an acceptable fix.
- Confirm understanding before implementing, and say so up front if a request
  is not possible rather than working around it silently.
- Collaborate with Codex for cross-review. Note it cannot see `X:`.
