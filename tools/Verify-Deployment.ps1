<#
.SYNOPSIS
    Verifies a deployed Chrono Trigger accessibility installation.

.DESCRIPTION
    Checks the portable Reloaded-II tree, mod payload, native proxy and helper,
    immutable-file checksums, absence of legacy redirects, and private runtime. Reports EVERY problem
    rather than stopping at the first, then exits non-zero if any check failed.

    All paths derive from -GameRoot (or from this script's location). Nothing here
    contains an absolute machine path.

.EXAMPLE
    .\tools\Verify-Deployment.ps1
#>
[CmdletBinding()]
param(
    [string]$GameRoot,
    # Isolated fixtures must not inspect the host's game registry registration.
    [switch]$SkipRegistryCheck
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'ModPaths.psm1') -Force

$PeMachineI386 = 0x014C

$results = [System.Collections.Generic.List[object]]::new()
function Add-Check {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][bool]$Ok,
        [string]$Detail = ''
    )
    $results.Add([pscustomobject]@{ Name = $Name; Ok = $Ok; Detail = $Detail })
}

function Get-DeploymentCanonicalPath {
    param([Parameter(Mandatory)][string]$Path)
    $fullPath = [IO.Path]::GetFullPath($Path)
    if ($fullPath -match '^([A-Za-z]):\\') {
        $drive = Get-PSDrive -Name $Matches[1] -PSProvider FileSystem -ErrorAction SilentlyContinue
        # Steam can launch through a mapped drive while developer tools use the
        # UNC share. DisplayRoot is the filesystem provider's actual mapping.
        if ($null -ne $drive -and $drive.DisplayRoot -and $drive.DisplayRoot.StartsWith('\\')) {
            $fullPath = [IO.Path]::GetFullPath((Join-Path $drive.DisplayRoot $fullPath.Substring(3)))
        }
    }
    return $fullPath
}

# ------------------------------------------------------------------- layout ---

try {
    $game = Get-GameRoot -GameRoot $GameRoot
} catch {
    Write-Host "FATAL: $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}

$reloaded    = Get-PortableReloadedRoot -GameRoot $game
$gameExe     = Join-Path $game 'Chrono Trigger.exe'
$loaderDir   = Join-Path $reloaded 'Loader\X86'
$modDir      = Join-Path $reloaded 'Mods\chrono.trigger.accessibility'
$hooksDir    = Join-Path $reloaded 'Mods\reloaded.sharedlib.hooks'
$appConfig   = Join-Path $reloaded 'Apps\chrono trigger.exe\AppConfig.json'
$proxy = Join-Path $game 'winmm.dll'
$bootstrapExe = Join-Path $game 'Accessibility\Bootstrap\Foresight.Bootstrap.exe'
$manifestPath = Join-Path $game 'Foresight-SHA256SUMS.txt'

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
        $binary = $_
        try {
            $m = Get-PeMachine -Path $binary.FullName
            if ($m -ne $PeMachineI386) { $nonX86 += ('{0} (0x{1:X})' -f $binary.Name, $m) }
        } catch { $nonX86 += ($binary.Name + ' (invalid PE image)') }
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
        Add-Check 'AppConfig AppLocation is the deployed game' ([string]::Equals((Get-DeploymentCanonicalPath $cfg.AppLocation), (Get-DeploymentCanonicalPath $gameExe), [StringComparison]::OrdinalIgnoreCase)) $cfg.AppLocation
    } catch {
        Add-Check 'AppConfig is valid JSON' $false $_.Exception.Message
    }
} else {
    Add-Check 'AppConfig present' $false $appConfig
}

# ---------------------------------------------------------- native bootstrap ---

foreach ($native in @($proxy, $bootstrapExe)) {
    $name = Split-Path $native -Leaf
    if (Test-Path -LiteralPath $native -PathType Leaf) {
        try {
            $machine = Get-PeMachine -Path $native
            Add-Check "$name is x86 (0x14C)" ($machine -eq $PeMachineI386) ('0x{0:X}' -f $machine)
        } catch { Add-Check "$name is a valid PE image" $false $_.Exception.Message }
    } else { Add-Check "$name present" $false $native }
}

# The installed manifest recognizes previous Foresight builds without assuming
# that this checkout's latest native output is the version already deployed.
$hashes = @{}
if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
    try {
        $gamePrefix = $game.TrimEnd('\') + '\'
        foreach ($line in Get-Content -LiteralPath $manifestPath) {
            if ($line -notmatch '^([A-Fa-f0-9]{64})  (.+)$') { throw 'Invalid checksum manifest line.' }
            $sha = $Matches[1]
            $relative = $Matches[2].Replace('/', '\')
            if ([IO.Path]::IsPathRooted($relative) -or $relative -match '(^|[\\/])\.\.([\\/]|$)|:') { throw "Unsafe manifest path: $relative" }
            $path = [IO.Path]::GetFullPath((Join-Path $game $relative))
            if (-not $path.StartsWith($gamePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Manifest path escapes game folder: $relative" }
            if ($hashes.ContainsKey($relative)) { throw "Duplicate manifest entry: $relative" }
            $hashes[$relative] = $sha
            $matchesHash = (Test-Path -LiteralPath $path -PathType Leaf) -and
                ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $sha)
            Add-Check "SHA256: $relative" $matchesHash 'missing or changed immutable package file'
        }
    } catch { Add-Check 'Foresight checksum manifest is valid' $false $_.Exception.Message }
} else { Add-Check 'Foresight checksum manifest present' $false $manifestPath }
foreach ($relative in 'winmm.dll', 'Accessibility\Bootstrap\Foresight.Bootstrap.exe') {
    Add-Check "SHA256 manifest covers $relative" ($hashes.ContainsKey($relative)) 'a native binary without an ownership checksum cannot be verified'
}

# ---------------------------------------------------------------- legacy IFEO ---

if ($SkipRegistryCheck) {
    Write-Warning 'Registry checks skipped for an isolated fixture.'
} else {
    try {
        . (Join-Path $PSScriptRoot 'release\Remove-LegacyRegistration.ps1')
        foreach ($state in @(Get-ForesightLegacyRegistrationState)) {
            Add-Check "No legacy Debugger in $($state.View)" ([string]::IsNullOrWhiteSpace([string]$state.Debugger)) ([string]$state.Debugger)
        }
    } catch { Add-Check 'Legacy registry state readable' $false $_.Exception.Message }
}

# ------------------------------------------------------------- private runtime ---

$runtimeRoot = Join-Path $game 'Accessibility\Runtime\dotnet\x86'
$runtime = Test-X86Runtime -DotnetX86Root $runtimeRoot
Add-Check 'Private x86 .NET 9 runtime available' $runtime.Satisfied $runtime.Root
foreach ($relative in 'dotnet.exe', 'host\fxr\9.0.20\hostfxr.dll', 'shared\Microsoft.NETCore.App\9.0.20\coreclr.dll') {
    $path = Join-Path $runtimeRoot $relative
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        try { Add-Check "Private runtime x86: $relative" ((Get-PeMachine -Path $path) -eq $PeMachineI386) $path }
        catch { Add-Check "Private runtime PE: $relative" $false $_.Exception.Message }
    } else { Add-Check "Private runtime present: $relative" $false $path }
}

$legacyAsi = Join-Path $game 'Reloaded.Mod.Loader.Bootstrapper.asi'
Add-Check 'Superseded ASI bootstrapper absent' (-not (Test-Path -LiteralPath $legacyAsi)) 'migrate the old injection chain before activating the proxy'

# A lease backup left behind means a bootstrap helper was killed mid-session.
$ptr = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)) 'Reloaded-Mod-Loader-II\ReloadedII.json'
Add-Check 'No leftover Reloaded pointer backup' (-not (Test-Path -LiteralPath "$ptr.chrono_trigger_backup")) `
    'a leftover backup requires recovery before launch'

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
