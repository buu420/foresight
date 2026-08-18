# Portable Reloaded Loader Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the mod's dependency on an external Reloaded-II GUI installation with a self-contained portable loader inside the game folder, injected by an IFEO launcher, using no absolute paths anywhere.

**Architecture:** A trimmed `Reloaded-II\Loader\X86` tree, a private x86 .NET runtime, and the mod all live under the game folder. A native C++ launcher is registered as the IFEO debugger for `Chrono Trigger.exe`; on launch it creates the game suspended, injects `Reloaded.Mod.Loader.Bootstrapper.dll`, then resumes. Every path is derived from the launcher's own location at runtime. `DOTNET_ROOT_X86` is set process-locally and inherited by the game, never written to the machine.

**Tech Stack:** C++17 (MSVC v143, x86), .NET 9 (`net9.0-windows`, AnyCPU), Reloaded-II 1.30.3 loader, PowerShell 5.1, xunit.

**Spec:** `docs/HANDOFF-reloaded-swap.md`

## Global Constraints

- **No absolute paths in any committed file.** Scripts derive roots from `$PSScriptRoot`; native code derives from `SelfDir()`. Paths may be *parameters* with no default, or defaults computed at runtime — never literals like `G:\...` or `C:\Users\...`.
- **Never modify `Chrono Trigger.exe`.** Fail-closed SHA-256 `8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7`. Bypassing this check is not an acceptable fix for anything.
- **`%APPDATA%\Reloaded-Mod-Loader-II\ReloadedII.json` must be backed up and restored on every exit path.** It currently points at the user's AccessXI install. Clobbering it breaks an unrelated accessibility mod.
- **Loader payload must be pure x86** (PE machine `0x14C`). `reloaded.sharedlib.hooks` is a universal mod and legitimately ships x64 files — do not strip them.
- **Target framework stays `net9.0-windows`. Do not pin a patch version.** The current pin of 9.0.18 is not installed on the target machine (9.0.17 is) and is the reason the existing launcher throws.
- Missing screen-reader information is treated as a defect of the same severity as a crash.

---

## File Structure

**Created:**

| Path | Responsibility |
|---|---|
| `src/ChronoTriggerAccessibility.Launcher/common.h` | Shared logging, path, PE and JSON helpers for both native exes |
| `src/ChronoTriggerAccessibility.Launcher/launcher.cpp` | IFEO debugger: suspend, inject, resume, restore |
| `src/ChronoTriggerAccessibility.Launcher/Launcher.vcxproj` | x86 build of the launcher |
| `src/ChronoTriggerAccessibility.Installer/installer.cpp` | Registers/unregisters the IFEO key |
| `src/ChronoTriggerAccessibility.Installer/Installer.vcxproj` | x86 build of the installer |
| `tools/ModPaths.psm1` | Single source of truth for path derivation |
| `tools/Stage-ReloadedPayload.ps1` | Copies loader + hooks + x86 runtime from a source install |
| `tools/Build-Native.ps1` | Locates MSVC via vswhere and builds both exes |
| `native/reloaded-ii/v1.30.3/SOURCE.md` | Provenance and per-file SHA-256 |

**Modified:**

| Path | Change |
|---|---|
| `Directory.Packages.props` | Remove any 9.0.18 pin |
| `tools/Deploy-Mod.ps1` | Full rewrite: portable layout, IFEO, no ASI proxy |
| `tools/Verify-Deployment.ps1` | Full rewrite against the new layout |
| `tools/Build-PrismWin32.ps1` | Remove the two hardcoded paths |
| `README.md` | Prerequisites, launch, uninstall all change |
| `THIRD-PARTY-NOTICES.md` | Add Reloaded-II loader and shared hooks |

**Deleted:**

| Path | Reason |
|---|---|
| `Launch Chrono Trigger Accessible.ps1` | Superseded by IFEO; also the source of the 9.0.18 assertion |
| game folder `winmm.dll`, `Reloaded.Mod.Loader.Bootstrapper.asi` | ASI proxy no longer used (removal is a deploy step, guarded by hash) |

---

## Task 1: Stage the x86 payload

**Requires Windows.** Cannot be completed from a Linux sandbox.

**Files:**
- Create: `tools/Stage-ReloadedPayload.ps1`
- Output (untracked): `artifacts/staging/reloaded-x86/`

**Interfaces:**
- Produces: `artifacts/staging/reloaded-x86/Loader/X86/**`, `.../Mods/reloaded.sharedlib.hooks/**`, `.../Runtime/dotnet/x86/**`, and `MANIFEST.txt` (relative path, size, SHA-256, PE machine per file).

- [ ] **Step 1: Write the script**

Parameters, no literal defaults for machine-specific paths:

```powershell
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ReloadedSourceRoot,
    [string]$DotnetX86Root = (Join-Path ${env:ProgramFiles(x86)} 'dotnet'),
    [string]$Destination = (Join-Path $PSScriptRoot '..\artifacts\staging\reloaded-x86')
)
```

Reuse `Get-PeMachine`, `Test-IsManagedAssembly` and the manifest writer already
written and reviewed in `C:\Users\buu42\Downloads\Stage-ReloadedX86.ps1` —
copy them verbatim rather than rewriting.

Copy three trees:
1. `$ReloadedSourceRoot\Loader\X86\*` → `$Destination\Loader\X86\`
2. the newest `reloaded.sharedlib.hooks` found under `$ReloadedSourceRoot\Mods` → `$Destination\Mods\reloaded.sharedlib.hooks\`
3. `$DotnetX86Root\{host,shared\Microsoft.NETCore.App\9.*,shared\Microsoft.WindowsDesktop.App\9.*}` plus `dotnet.exe` → `$Destination\Runtime\dotnet\x86\`

- [ ] **Step 2: Run it**

```powershell
.\tools\Stage-ReloadedPayload.ps1 -ReloadedSourceRoot '<path to a Reloaded-II install>'
```

Best known source is Reloaded-II **1.30.3**. **Confirm with the user before
reading `C:\Games\Final Fantasy VII\Reloaded-II`** — they declined to grant a
previous session access to that path. Alternatives: the FFX/FFX-2 Steam folder
(1.30.1) or `AccessXI\external\Reloaded-II` (1.30.2).

- [ ] **Step 3: Verify the manifest**

Open `MANIFEST.txt`. Required, or stop and fix before continuing:
- Every file under `Loader\` reports `x86 (0x14c)`. Any `0x8664` is a failure.
- `Loader\X86\Reloaded.Mod.Loader.dll`, `.runtimeconfig.json`, `.deps.json` and `Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll` are all present.
- `Runtime\dotnet\x86\dotnet.exe` reports `x86 (0x14c)`.
- x64 files under `Mods\reloaded.sharedlib.hooks\` are **expected** — do not remove them.

- [ ] **Step 4: Commit the script only**

```bash
git add tools/Stage-ReloadedPayload.ps1
git commit -m "feat: add portable Reloaded payload staging script"
```

`artifacts/` stays untracked.

---

## Task 2: Vendor the payload

**Files:**
- Create: `native/reloaded-ii/v1.30.3/win-x86/Loader/**`, `native/reloaded-ii/v1.30.3/SOURCE.md`
- Modify: `THIRD-PARTY-NOTICES.md`

**Interfaces:**
- Consumes: `artifacts/staging/reloaded-x86/` from Task 1.
- Produces: a committed loader payload that `Deploy-Mod.ps1` copies from.

- [ ] **Step 1: Copy the staged loader into the repo**

```powershell
$src = Join-Path $PSScriptRoot 'artifacts\staging\reloaded-x86\Loader'
$dst = Join-Path $PSScriptRoot 'native\reloaded-ii\v1.30.3\win-x86\Loader'
New-Item -ItemType Directory -Path $dst -Force | Out-Null
Copy-Item -Path (Join-Path $src '*') -Destination $dst -Recurse -Force
```

Do **not** vendor the .NET runtime — it is large and machine-sourced. Deploy
copies it from the staging folder instead.

- [ ] **Step 2: Write `SOURCE.md`**

Record: Reloaded-II version, the install it came from, retrieval date, and the
full SHA-256 table from `MANIFEST.txt`. State explicitly that these are
unmodified upstream binaries.

- [ ] **Step 3: Add licence entries to `THIRD-PARTY-NOTICES.md`**

Two entries: Reloaded-II loader (GPL-3.0, Sewer56) and `reloaded.sharedlib.hooks`.
Follow the format the file already uses for Prism and Ultimate ASI Loader.

- [ ] **Step 4: Commit**

```bash
git add native/reloaded-ii THIRD-PARTY-NOTICES.md
git commit -m "feat: vendor Reloaded-II 1.30.3 x86 loader payload"
```

---

## Task 3: Path-portability module and framework retarget

**Files:**
- Create: `tools/ModPaths.psm1`
- Modify: `Directory.Packages.props`, `tools/Build-PrismWin32.ps1`
- Test: `tests/ChronoTriggerAccessibility.Mod.Tests/Packaging/NoHardcodedPathsTests.cs`

**Interfaces:**
- Produces: `Get-ModRepositoryRoot`, `Get-GameRoot -RepositoryRoot <path>`, `Get-PortableReloadedRoot -GameRoot <path>`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void RepositoryScriptsContainNoAbsoluteMachinePaths()
{
    var root = TestPaths.RepositoryRoot;
    var offenders = new List<string>();
    var pattern = new Regex(@"[A-Za-z]:\\(Users|Program Files|Games)|[A-Za-z]:\\SteamLibrary",
        RegexOptions.IgnoreCase);

    foreach (var file in Directory.EnumerateFiles(root, "*.ps1", SearchOption.AllDirectories)
                 .Concat(Directory.EnumerateFiles(root, "*.psm1", SearchOption.AllDirectories))
                 .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}"))
                 .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}")))
    {
        foreach (var (line, index) in File.ReadAllLines(file).Select((l, i) => (l, i)))
        {
            if (line.TrimStart().StartsWith("#")) continue;
            if (pattern.IsMatch(line)) offenders.Add($"{file}:{index + 1}: {line.Trim()}");
        }
    }

    Assert.True(offenders.Count == 0,
        "Absolute machine paths found:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
}
```

- [ ] **Step 2: Run it and confirm it fails**

```powershell
dotnet test tests/ChronoTriggerAccessibility.Mod.Tests --filter RepositoryScriptsContainNoAbsoluteMachinePaths
```

Expected: FAIL, listing `Build-PrismWin32.ps1`, `Deploy-Mod.ps1`,
`Verify-Deployment.ps1` and `Launch Chrono Trigger Accessible.ps1`.

- [ ] **Step 3: Write `tools/ModPaths.psm1`**

```powershell
Set-StrictMode -Version Latest

function Get-ModRepositoryRoot {
    [CmdletBinding()] param()
    return (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}

function Get-GameRoot {
    [CmdletBinding()]
    param([string]$RepositoryRoot = (Get-ModRepositoryRoot))

    # The repository lives inside the game folder as <game>\accessibility-mod.
    $candidate = (Resolve-Path (Join-Path $RepositoryRoot '..')).Path
    if (-not (Test-Path -LiteralPath (Join-Path $candidate 'Chrono Trigger.exe') -PathType Leaf)) {
        throw "Could not locate 'Chrono Trigger.exe' in the expected game root: $candidate. Pass -GameRoot explicitly."
    }
    return $candidate
}

function Get-PortableReloadedRoot {
    [CmdletBinding()] param([Parameter(Mandatory = $true)][string]$GameRoot)
    return (Join-Path $GameRoot 'Reloaded-II')
}

function Get-AccessibilityRoot {
    [CmdletBinding()] param([Parameter(Mandatory = $true)][string]$GameRoot)
    return (Join-Path $GameRoot 'Accessibility')
}

Export-ModuleMember -Function Get-ModRepositoryRoot, Get-GameRoot,
    Get-PortableReloadedRoot, Get-AccessibilityRoot
```

- [ ] **Step 4: Fix `Build-PrismWin32.ps1`**

Replace both literal defaults:

```powershell
param(
    [string]$SourceDirectory = (Join-Path ([IO.Path]::GetTempPath()) 'prism-source-research'),
    [string]$ReviewedArtifact = (Join-Path $PSScriptRoot '..\.build\prism-dist-win32\bin\prism.dll')
)
```

- [ ] **Step 5: Remove the framework pin**

In `Directory.Packages.props`, confirm no `9.0.18` appears. If a
`RuntimeFrameworkVersion` is set anywhere, delete it so the SDK resolves the
installed 9.0.x.

- [ ] **Step 6: Re-run the test**

Expected: still FAILS, now only for `Deploy-Mod.ps1`, `Verify-Deployment.ps1`
and `Launch Chrono Trigger Accessible.ps1` — all rewritten or deleted in Tasks
7 and 8.

- [ ] **Step 7: Commit**

```bash
git add tools/ModPaths.psm1 tools/Build-PrismWin32.ps1 Directory.Packages.props tests/
git commit -m "feat: add path derivation module and hardcoded-path guard test"
```

---

## Task 4: Native launcher

**Files:**
- Create: `src/ChronoTriggerAccessibility.Launcher/common.h`, `launcher.cpp`, `Launcher.vcxproj`

**Interfaces:**
- Produces: `ChronoTriggerAccessibility.Launcher.exe` (x86), invoked by Windows as `<launcher> "<game exe path>" [args...]`.

- [ ] **Step 1: Write `common.h`**

Port from `stuff.zip` → `DSTS.Common/common.h`. Keep `Logger`, `Utf8ToWide`,
`WideToUtf8`, `JsonEscape`, `SelfPath`, `SelfDir`, `AppDataRoaming`,
`ReloadedIIPointerFile`, `WriteUtf8File`, `WriteReloadedIIPointer`,
`WriteAppConfig` unchanged except:

- namespace `dsts` → `cta`
- `WriteAppConfig` must emit **two** entries in `EnabledMods`:

```cpp
L"  \"EnabledMods\": [\n"
L"    \"reloaded.sharedlib.hooks\",\n"
L"    \"" + std::wstring(modId) + L"\"\n"
L"  ],\n"
```

The mod declares `reloaded.sharedlib.hooks` as a dependency; with no shared
install it must be enabled explicitly or the mod fails to load.

- [ ] **Step 2: Add SHA-256 verification to `common.h`**

The launcher must refuse to inject into an unexpected build. Add:

```cpp
inline bool FileSha256(const fs::path& path, std::wstring& outHex) {
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr,
                              OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    BCRYPT_ALG_HANDLE alg = nullptr; BCRYPT_HASH_HANDLE hash = nullptr;
    bool ok = false;
    std::vector<UCHAR> digest(32), buffer(64 * 1024);
    if (BCryptOpenAlgorithmProvider(&alg, BCRYPT_SHA256_ALGORITHM, nullptr, 0) == 0 &&
        BCryptCreateHash(alg, &hash, nullptr, 0, nullptr, 0, 0) == 0) {
        DWORD read = 0; ok = true;
        while (ReadFile(file, buffer.data(), (DWORD)buffer.size(), &read, nullptr) && read > 0) {
            if (BCryptHashData(hash, buffer.data(), read, 0) != 0) { ok = false; break; }
        }
        if (ok && BCryptFinishHash(hash, digest.data(), (ULONG)digest.size(), 0) == 0) {
            wchar_t hex[65];
            for (int i = 0; i < 32; ++i) swprintf_s(hex + i * 2, 3, L"%02X", digest[i]);
            outHex.assign(hex, 64);
        } else ok = false;
    }
    if (hash) BCryptDestroyHash(hash);
    if (alg) BCryptCloseAlgorithmProvider(alg, 0);
    CloseHandle(file);
    return ok;
}
```

Add `#include <bcrypt.h>` and `#pragma comment(lib, "bcrypt.lib")`.

- [ ] **Step 3: Write `launcher.cpp`**

Port `DSTS.Launcher/launcher.cpp` with these changes:

```cpp
constexpr const wchar_t* DIALOG_TITLE = L"Chrono Trigger Accessibility";
constexpr const wchar_t* MOD_ID       = L"chrono.trigger.accessibility";
constexpr const wchar_t* LOG_NAME     = L"ChronoTriggerAccessibility.Launcher.log";
constexpr const wchar_t* RECURSION_ENV_VAR = L"CTA_LAUNCHER_ACTIVE";
constexpr const wchar_t* SUPPORTED_SHA256 =
    L"8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7";
```

`X86` replaces `X64` throughout the payload paths:

```cpp
fs::path reloadedRoot    = gameRoot / L"Reloaded-II";
fs::path bootstrapperDll = reloadedRoot / L"Loader" / L"X86" / L"Bootstrapper"
                         / L"Reloaded.Mod.Loader.Bootstrapper.dll";
fs::path loaderDll       = reloadedRoot / L"Loader" / L"X86" / L"Reloaded.Mod.Loader.dll";
fs::path modDll          = reloadedRoot / L"Mods" / MOD_ID
                         / L"ChronoTriggerAccessibility.Mod.dll";
```

`gameRoot` is `gameExe.parent_path()` — never `SelfDir()`, because the launcher
sits in a subfolder.

**Build verification, before injecting:**

```cpp
std::wstring actual;
if (!FileSha256(gameExe, actual)) {
    log.A("LaunchWithMod: could not hash the game executable");
    return LaunchGameUnmodded(gameExe, extraArgs, log);
}
if (_wcsicmp(actual.c_str(), SUPPORTED_SHA256) != 0) {
    log.W(L"LaunchWithMod: unsupported build. expected=" + std::wstring(SUPPORTED_SHA256)
          + L" actual=" + actual);
    ShowError(L"This Chrono Trigger build is not supported by the accessibility mod.\n\n"
              L"The game will start without it.\n\nLog: " + log.Path().wstring());
    return LaunchGameUnmodded(gameExe, extraArgs, log);
}
```

**Process-local private runtime**, immediately before `CreateProcessW`. The
child inherits our environment because `lpEnvironment` is `nullptr`:

```cpp
fs::path runtimeRoot = gameRoot / L"Accessibility" / L"Runtime" / L"dotnet" / L"x86";
if (fs::exists(runtimeRoot, ec)) {
    SetEnvironmentVariableW(L"DOTNET_ROOT_X86", runtimeRoot.wstring().c_str());
    log.W(L"LaunchWithMod: private x86 runtime = " + runtimeRoot.wstring());
} else {
    log.W(L"LaunchWithMod: no private runtime at " + runtimeRoot.wstring()
          + L", relying on machine .NET");
}
```

Keep unchanged: `AppDataSwap` RAII, `InjectDll`, `DEBUG_ONLY_THIS_PROCESS |
CREATE_SUSPENDED`, `DebugActiveProcessStop`, `ResumeThread`, the recursion
guard, and the missing-file fallback to `LaunchGameUnmodded`.

- [ ] **Step 4: Write `Launcher.vcxproj`**

Copy `DSTS.Launcher.vcxproj`; set `<PlatformToolset>v143`, `Platform=Win32`,
`ConfigurationType=Application`, `SubSystem=Windows`,
`LanguageStandard=stdcpp17`, `RuntimeLibrary=MultiThreaded` (static, so no VC++
redistributable is required on the user's machine).

- [ ] **Step 5: Commit**

```bash
git add src/ChronoTriggerAccessibility.Launcher
git commit -m "feat: add x86 IFEO launcher with build verification"
```

---

## Task 5: Native installer

**Files:**
- Create: `src/ChronoTriggerAccessibility.Installer/installer.cpp`, `Installer.vcxproj`

**Interfaces:**
- Consumes: `common.h` from Task 4.
- Produces: `ChronoTriggerAccessibility.Installer.exe` (x86). No args installs; `/uninstall` removes.

- [ ] **Step 1: Write the IFEO registration**

```cpp
constexpr const wchar_t* IFEO_KEY =
    L"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Image File Execution Options\\Chrono Trigger.exe";

static bool RegisterIfeo(const fs::path& launcherExe, Logger& log) {
    HKEY key = nullptr;
    LSTATUS status = RegCreateKeyExW(HKEY_LOCAL_MACHINE, IFEO_KEY, 0, nullptr,
                                     REG_OPTION_NON_VOLATILE, KEY_SET_VALUE,
                                     nullptr, &key, nullptr);
    if (status != ERROR_SUCCESS) { log.Err(L"RegisterIfeo: RegCreateKeyExW", status); return false; }

    std::wstring value = L"\"" + launcherExe.wstring() + L"\"";
    status = RegSetValueExW(key, L"Debugger", 0, REG_SZ,
                            reinterpret_cast<const BYTE*>(value.c_str()),
                            (DWORD)((value.size() + 1) * sizeof(wchar_t)));
    RegCloseKey(key);
    if (status != ERROR_SUCCESS) { log.Err(L"RegisterIfeo: RegSetValueExW", status); return false; }
    log.W(L"RegisterIfeo: OK Debugger=" + value);
    return true;
}

static bool UnregisterIfeo(Logger& log) {
    HKEY key = nullptr;
    LSTATUS status = RegOpenKeyExW(HKEY_LOCAL_MACHINE, IFEO_KEY, 0, KEY_SET_VALUE, &key);
    if (status == ERROR_FILE_NOT_FOUND) { log.A("UnregisterIfeo: key absent, nothing to do"); return true; }
    if (status != ERROR_SUCCESS) { log.Err(L"UnregisterIfeo: RegOpenKeyExW", status); return false; }
    status = RegDeleteValueW(key, L"Debugger");
    RegCloseKey(key);
    if (status != ERROR_SUCCESS && status != ERROR_FILE_NOT_FOUND) {
        log.Err(L"UnregisterIfeo: RegDeleteValueW", status); return false;
    }
    log.A("UnregisterIfeo: OK");
    return true;
}
```

- [ ] **Step 2: Require elevation**

`HKLM` needs admin. Add an embedded manifest with
`requestedExecutionLevel level="requireAdministrator"` so Windows prompts
rather than failing with `ERROR_ACCESS_DENIED`.

- [ ] **Step 3: Guard against a wrong-target install**

Before writing the key, verify the sibling launcher exists and that
`Chrono Trigger.exe` sits two levels up with the expected SHA-256. Refuse and
show a clear message otherwise. Never register a `Debugger` pointing at a
missing file — that makes the game unlaunchable.

- [ ] **Step 4: Commit**

```bash
git add src/ChronoTriggerAccessibility.Installer
git commit -m "feat: add IFEO installer with elevation and target verification"
```

---

## Task 6: Native build script

**Files:**
- Create: `tools/Build-Native.ps1`

- [ ] **Step 1: Write it**

```powershell
[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ModPaths.psm1') -Force

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw "vswhere.exe not found. Install Visual Studio Build Tools." }

$vsRoot = & $vswhere -products * -latest -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if ([string]::IsNullOrWhiteSpace($vsRoot)) { throw 'No Visual Studio installation with the C++ x86/x64 toolset was found.' }

$msbuild = Join-Path $vsRoot 'MSBuild\Current\Bin\MSBuild.exe'
$repo = Get-ModRepositoryRoot

foreach ($project in @(
    'src\ChronoTriggerAccessibility.Launcher\Launcher.vcxproj',
    'src\ChronoTriggerAccessibility.Installer\Installer.vcxproj')) {
    & $msbuild (Join-Path $repo $project) /p:Configuration=$Configuration /p:Platform=Win32 /v:minimal
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
}
```

- [ ] **Step 2: Run and verify architecture**

```powershell
.\tools\Build-Native.ps1
```

Confirm both outputs report PE machine `0x14c`. An x64 launcher cannot inject
into the 32-bit game.

- [ ] **Step 3: Commit**

```bash
git add tools/Build-Native.ps1
git commit -m "feat: add MSVC build script for native launcher and installer"
```

---

## Task 7: Rewrite the deployer

**Files:**
- Modify: `tools/Deploy-Mod.ps1` (full rewrite)

- [ ] **Step 1: Rewrite the parameter block**

```powershell
[CmdletBinding()]
param(
    [string]$GameRoot,
    [string]$StagingRoot = (Join-Path $PSScriptRoot '..\artifacts\staging\reloaded-x86'),
    [switch]$SkipIfeo
)
Import-Module (Join-Path $PSScriptRoot 'ModPaths.psm1') -Force
if (-not $GameRoot) { $GameRoot = Get-GameRoot }
```

- [ ] **Step 2: Deploy the portable tree**

Target layout, all derived from `$GameRoot`:

```
<GameRoot>\Reloaded-II\Loader\X86\**                     from native\reloaded-ii\v1.30.3\win-x86\Loader
<GameRoot>\Reloaded-II\Mods\reloaded.sharedlib.hooks\**  from staging
<GameRoot>\Reloaded-II\Mods\chrono.trigger.accessibility\** from artifacts\package
<GameRoot>\Accessibility\Launcher\*.exe                  from build output
<GameRoot>\Accessibility\Runtime\dotnet\x86\**           from staging
```

Keep the existing transactional pattern: stage, verify, then commit; restore
byte-for-byte on any failure.

- [ ] **Step 3: Remove the ASI proxy**

Delete `<GameRoot>\winmm.dll` **only** if its SHA-256 is
`A51C630B2EA3D78AD55A330EA64D510C8C0737F620BE65AD7503B61840D59E37`, and
`<GameRoot>\Reloaded.Mod.Loader.Bootstrapper.asi` only if its SHA-256 is
`1A9F704549F66E357C0D22C395B57FE4E7BD5248521DBB40E566D2EE1CA809AB`. A hash
mismatch means the file is someone else's — leave it and warn.

- [ ] **Step 4: Register IFEO**

Unless `-SkipIfeo`, run the installer exe and check the exit code. It
self-elevates.

- [ ] **Step 5: Assert no game binary was touched**

Re-hash `Chrono Trigger.exe` after deployment and compare against
`8FE9D75E...D2E00D7`. Any difference is a fatal bug — roll back.

- [ ] **Step 6: Commit**

```bash
git add tools/Deploy-Mod.ps1
git commit -m "feat: deploy portable Reloaded layout with IFEO registration"
```

---

## Task 8: Rewrite verification, README, and delete the old launcher

**Files:**
- Modify: `tools/Verify-Deployment.ps1` (full rewrite), `README.md`
- Delete: `Launch Chrono Trigger Accessible.ps1`

- [ ] **Step 1: Rewrite the verifier**

Check, all relative to a derived `$GameRoot`: loader payload present and x86;
both `AppConfig.json` entries in `EnabledMods`; mod DLL present; launcher exe
present and x86; private runtime present; IFEO `Debugger` value matches the
deployed launcher path; game exe hash unchanged; `winmm.dll` and the `.asi`
absent or foreign. Fail loudly, listing every problem rather than the first.

- [ ] **Step 2: Delete the superseded launcher**

```bash
git rm "Launch Chrono Trigger Accessible.ps1"
```

Its 9.0.18 assertion and its `G:\`/`C:\Users\User` literals are exactly what
this plan removes. IFEO replaces its purpose.

- [ ] **Step 3: Rewrite the README sections**

Prerequisites: Windows 10+, the game, a screen reader. Remove Reloaded-II,
Spyro, `DOTNET_ROOT_X86`, and the shared-hooks install instruction — all are now
bundled or unnecessary. Rewrite Launch (Steam, nothing else) and Uninstall (run
the installer with `/uninstall`, then delete `Reloaded-II\` and
`Accessibility\`).

- [ ] **Step 4: Re-run the hardcoded-path test from Task 3**

```powershell
dotnet test tests/ChronoTriggerAccessibility.Mod.Tests --filter RepositoryScriptsContainNoAbsoluteMachinePaths
```

Expected: **PASS**. This is the gate for the portability requirement.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: portable deployment verification and docs; drop legacy launcher"
```

---

## Task 9: End-to-end verification

**REQUIRED SUB-SKILL:** superpowers:verification-before-completion. Do not
claim success without pasting command output.

- [ ] **Step 1: Record the pre-state**

```powershell
Get-Content "$env:APPDATA\Reloaded-Mod-Loader-II\ReloadedII.json" |
    Set-Content "$env:TEMP\ReloadedII.pre.json"
```

- [ ] **Step 2: Full build and deploy**

```powershell
.\tools\Build-Native.ps1
.\tools\Package-Mod.ps1
.\tools\Deploy-Mod.ps1
.\tools\Verify-Deployment.ps1
```

All four must exit 0.

- [ ] **Step 3: Launch from Steam**

Normal Steam launch. No scripts, no Reloaded window.

- [ ] **Step 4: Confirm the mod loaded**

```powershell
Get-ChildItem "$env:APPDATA\Reloaded-Mod-Loader-II\Logs" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1 |
    Get-Content | Select-String 'chrono.trigger.accessibility'
```

Required: executable check passed, hooks activated, Prism backend selected.

- [ ] **Step 5: Confirm the first announcement**

The README states the first expected announcement is **"Square Enix."** Because
injection now happens at a suspended entry point, it should be heard. If it
still is not, that is a real defect — log it, do not wave it through.

- [ ] **Step 6: Confirm AccessXI is intact**

```powershell
Compare-Object (Get-Content "$env:TEMP\ReloadedII.pre.json") `
               (Get-Content "$env:APPDATA\Reloaded-Mod-Loader-II\ReloadedII.json")
```

Expected: **no differences.** Any output means `AppDataSwap` failed to restore
and the user's AccessXI mod is broken. Fix before shipping.

- [ ] **Step 7: Confirm uninstall is clean**

Run the installer with `/uninstall`, confirm the IFEO `Debugger` value is gone,
and confirm the game launches normally without the mod.

- [ ] **Step 8: Commit the verification record**

Write `docs/verification/2026-08-17-portable-loader-verification.md` containing
the actual pasted output of every step above, then commit.

---

## Out of scope

The Ghidra scripts need a separate plan: add SHA-256 build guards so they fail
closed rather than dumping garbage against a different executable, and fix the
unchecked `getFunctionAt` dereferences in `SettingsCallSitesGhidra.java` and
`QuitBinderGhidra.java`. Neither blocks this work.
