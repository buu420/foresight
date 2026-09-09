<#
.SYNOPSIS
    Verifies a deployed Chrono Trigger accessibility installation.

.DESCRIPTION
    Checks the portable Reloaded-II tree, the mod payload, the native launcher and
    installer, the IFEO redirect, and the x86 .NET runtime. Reports EVERY problem
    rather than stopping at the first, then exits non-zero if any check failed.

    All paths derive from -GameRoot (or from this script's location). Nothing here
    contains an absolute machine path.

.EXAMPLE
    .\tools\Verify-Deployment.ps1
#>
[CmdletBinding()]
param(
    [string]$GameRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'ModPaths.psm1') -Force

$PeMachineI386 = 0x014C
$ifeoKey = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\Chrono Trigger.exe'
$ownerValueName = 'ChronoTriggerAccessibilityDebuggerOwner'

$results = [System.Collections.Generic.List[object]]::new()
function Add-Check {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][bool]$Ok,
        [string]$Detail = ''
    )
    $results.Add([pscustomobject]@{ Name = $Name; Ok = $Ok; Detail = $Detail })
}

# ------------------------------------------------------------------- layout ---

try {
    $game = Get-GameRoot -GameRoot $GameRoot
} catch {
    Write-Host "FATAL: $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}

$reloaded    = Get-PortableReloadedRoot -GameRoot $game
$launcherDir = Get-LauncherRoot -GameRoot $game
$gameExe     = Join-Path $game 'Chrono Trigger.exe'
$loaderDir   = Join-Path $reloaded 'Loader\X86'
$modDir      = Join-Path $reloaded 'Mods\chrono.trigger.accessibility'
$hooksDir    = Join-Path $reloaded 'Mods\reloaded.sharedlib.hooks'
$appConfig   = Join-Path $reloaded 'Apps\chrono trigger.exe\AppConfig.json'
$launcherExe = Join-Path $launcherDir 'ChronoTriggerAccessibility.Launcher.exe'
$installerExe = Join-Path $launcherDir 'ChronoTriggerAccessibility.Installer.exe'

Write-Host "Game root: $game"
Write-Host ''

# --------------------------------------------------------------- game binary ---

if (Test-Path -LiteralPath $gameExe) {
    $actual = (Get-FileHash $gameExe -Algorithm SHA256).Hash
    $expected = Get-SupportedGameSha256
    Add-Check 'Game executable unmodified' ($actual -eq $expected) `
        $(if ($actual -eq $expected) { $actual } else { "expected $expected, found $actual" })
} else {
    Add-Check 'Game executable present' $false $gameExe
}

# ----------------------------------------------------------- loader payload ---

$requiredLoaderFiles = @(
    'Reloaded.Mod.Loader.dll'
    'Reloaded.Mod.Loader.IO.dll'
    'Reloaded.Mod.Loader.runtimeconfig.json'
    'Reloaded.Mod.Loader.deps.json'
    'Reloaded.Mod.Interfaces.dll'
    'Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll'
)
foreach ($rel in $requiredLoaderFiles) {
    $p = Join-Path $loaderDir $rel
    Add-Check "Loader file: $rel" (Test-Path -LiteralPath $p) $p
}

if (Test-Path -LiteralPath $loaderDir) {
    $nonX86 = @()
    Get-ChildItem $loaderDir -Recurse -File -Include *.dll, *.exe | ForEach-Object {
        $m = Get-PeMachine -Path $_.FullName
        if ($m -ne $PeMachineI386) { $nonX86 += ('{0} (0x{1:X})' -f $_.Name, $m) }
    }
    Add-Check 'Every loader binary is x86 (0x14C)' ($nonX86.Count -eq 0) `
        $(if ($nonX86.Count) { $nonX86 -join ', ' } else { 'all x86' })
}

# -------------------------------------------------------------- mod payload ---

foreach ($rel in @('ModConfig.json', 'ChronoTriggerAccessibility.Mod.dll', 'prism.dll')) {
    $p = Join-Path $modDir $rel
    Add-Check "Mod file: $rel" (Test-Path -LiteralPath $p) $p
}

# reloaded.sharedlib.hooks is a universal mod: x64 files here are expected.
Add-Check 'Shared hooks mod present' (Test-Path -LiteralPath (Join-Path $hooksDir 'ModConfig.json')) $hooksDir
Add-Check 'Shared hooks x86 payload present' (Test-Path -LiteralPath (Join-Path $hooksDir 'x86')) (Join-Path $hooksDir 'x86')

# -------------------------------------------------------------- app config ---

if (Test-Path -LiteralPath $appConfig) {
    try {
        $cfg = Get-Content $appConfig -Raw | ConvertFrom-Json
        $enabled = @($cfg.EnabledMods)
        # Both must be listed. Without a Reloaded GUI there is nothing else to
        # resolve the mod's declared dependency on the shared hooks library.
        foreach ($id in 'reloaded.sharedlib.hooks', 'chrono.trigger.accessibility') {
            Add-Check "AppConfig enables $id" ($enabled -contains $id) ($enabled -join ', ')
        }
        Add-Check 'AppConfig AppId is "chrono trigger.exe"' ($cfg.AppId -eq 'chrono trigger.exe') $cfg.AppId
        Add-Check 'AppConfig AppLocation exists' (Test-Path -LiteralPath $cfg.AppLocation) $cfg.AppLocation
    } catch {
        Add-Check 'AppConfig is valid JSON' $false $_.Exception.Message
    }
} else {
    Add-Check 'AppConfig present' $false $appConfig
}

# ----------------------------------------------------------------- natives ---

foreach ($exe in @($launcherExe, $installerExe)) {
    $name = Split-Path $exe -Leaf
    if (Test-Path -LiteralPath $exe) {
        $m = Get-PeMachine -Path $exe
        # The launcher injects into a 32-bit process, so it must be 32-bit itself.
        Add-Check "$name is x86 (0x14C)" ($m -eq $PeMachineI386) ('0x{0:X}' -f $m)
    } else {
        Add-Check "$name present" $false $exe
    }
}

# -------------------------------------------------------------------- IFEO ---

if (Test-Path $ifeoKey) {
    $props = Get-ItemProperty $ifeoKey
    $names = (Get-Item $ifeoKey).Property
    $debugger = if ($names -contains 'Debugger') { $props.Debugger } else { $null }

    if ($debugger) {
        # The registry value is written by an elevated process, which does not
        # inherit mapped network drives, so it is often a UNC path while $launcherExe
        # is a drive letter. Comparing strings would produce a false failure.
        # Compare file identity by hash instead.
        $debuggerPath = $debugger.Trim('"')
        if (Test-Path -LiteralPath $debuggerPath) {
            $sameFile = $false
            if (Test-Path -LiteralPath $launcherExe) {
                $sameFile = (Get-FileHash $debuggerPath -Algorithm SHA256).Hash -eq
                            (Get-FileHash $launcherExe  -Algorithm SHA256).Hash
            }
            Add-Check 'IFEO Debugger points at the deployed launcher' $sameFile $debuggerPath
        } else {
            # This is the dangerous state: the game becomes unlaunchable.
            Add-Check 'IFEO Debugger target exists' $false "MISSING: $debuggerPath"
        }
        Add-Check 'IFEO ownership marker present' ($names -contains $ownerValueName) `
            'lets uninstall avoid deleting a redirect belonging to another tool'
    } else {
        Add-Check 'IFEO Debugger value present' $false 'run the installer to register the mod'
    }
} else {
    Add-Check 'IFEO key present' $false 'run ChronoTriggerAccessibility.Installer.exe'
}

# ----------------------------------------------------------------- runtime ---

$runtime = Test-X86Runtime
Add-Check 'x86 .NET 9 runtime available' $runtime.Satisfied `
    $(if ($runtime.Satisfied) { "found $($runtime.Found -join ', ') under $($runtime.Root)" }
      else { "no 9.x under $($runtime.Root)\shared\Microsoft.NETCore.App" })

# ------------------------------------------------- superseded ASI artifacts ---

$asiLoaderSha = 'A51C630B2EA3D78AD55A330EA64D510C8C0737F620BE65AD7503B61840D59E37'
foreach ($legacy in @(
    @{ Name = 'winmm.dll'; Sha = $asiLoaderSha }
    @{ Name = 'Reloaded.Mod.Loader.Bootstrapper.asi'; Sha = $null }
)) {
    $p = Join-Path $game $legacy.Name
    if (-not (Test-Path -LiteralPath $p)) {
        Add-Check "Superseded $($legacy.Name) removed" $true 'absent'
    } elseif ($legacy.Sha -and (Get-FileHash $p -Algorithm SHA256).Hash -ne $legacy.Sha) {
        # Someone else's file with the same name. Not ours to remove.
        Add-Check "Superseded $($legacy.Name) removed" $true 'present but not ours; left alone'
    } else {
        Add-Check "Superseded $($legacy.Name) removed" $false `
            'still present; the launcher now injects, so this would inject Reloaded twice'
    }
}

# A lease backup left behind means a launcher was killed mid-session.
$ptr = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)) 'Reloaded-Mod-Loader-II\ReloadedII.json'
Add-Check 'No leftover Reloaded pointer backup' (-not (Test-Path -LiteralPath "$ptr.chrono_trigger_backup")) `
    'a leftover backup is recovered automatically on the next launch'

# ------------------------------------------------------------------ report ---

$pass = @($results | Where-Object Ok).Count
$fail = @($results | Where-Object { -not $_.Ok }).Count

foreach ($r in $results) {
    if ($r.Ok) {
        Write-Host ('  ok   ' + $r.Name) -ForegroundColor DarkGray
    } else {
        Write-Host ('  FAIL ' + $r.Name) -ForegroundColor Red
        if ($r.Detail) { Write-Host ('         ' + $r.Detail) -ForegroundColor Red }
    }
}

Write-Host ''
Write-Host "$pass passed, $fail failed."
if ($fail -gt 0) {
    Write-Host 'Deployment is NOT verified.' -ForegroundColor Red
    exit 1
}
Write-Host 'Deployment verified.' -ForegroundColor Green
exit 0
