<#
.SYNOPSIS
    Builds the x86 accessibility launcher and installer with MSVC.

.DESCRIPTION
    Locates Visual Studio through vswhere, imports the x86 build environment, and
    compiles both native executables into .build\native.

    The launcher MUST be x86. It injects into Chrono Trigger, which is a 32-bit
    process, and a remote thread's start address has to be valid in the target.
    This script verifies the PE machine type of every output and fails if any of
    them is not 0x14C.

    No absolute machine paths: everything derives from $PSScriptRoot or from
    vswhere's answer.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot  = (Resolve-Path (Join-Path $PSScriptRoot '..')).ProviderPath
$outputDir = Join-Path $repoRoot '.build\native'
$launcherDir = Join-Path $repoRoot 'src\ChronoTriggerAccessibility.Launcher'
$installerDir = Join-Path $repoRoot 'src\ChronoTriggerAccessibility.Installer'

# ---------------------------------------------------------------- toolchain ---

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw "vswhere.exe not found at '$vswhere'. Install Visual Studio 2022 Build Tools with the C++ toolset."
}

$vsRoot = & $vswhere -products * -latest -prerelease `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
    -property installationPath
if ([string]::IsNullOrWhiteSpace($vsRoot)) {
    throw 'No Visual Studio installation with the C++ x86/x64 toolset was found.'
}
Write-Host "Visual Studio: $vsRoot"

$vcvarsall = Join-Path $vsRoot 'VC\Auxiliary\Build\vcvarsall.bat'
if (-not (Test-Path -LiteralPath $vcvarsall)) { throw "vcvarsall.bat not found under '$vsRoot'." }

# Import the x86 environment by asking cmd for the resulting variable set.
Write-Host 'Importing the x86 build environment...'
$envDump = & $env:ComSpec /c "call `"$vcvarsall`" x86 >nul 2>&1 && set"
if ($LASTEXITCODE -ne 0) { throw "vcvarsall.bat x86 failed with exit code $LASTEXITCODE." }

foreach ($line in $envDump) {
    if ($line -match '^([^=]+)=(.*)$') {
        Set-Item -Path ("Env:" + $Matches[1]) -Value $Matches[2] -ErrorAction SilentlyContinue
    }
}

$cl = (Get-Command cl.exe -ErrorAction SilentlyContinue)
if (-not $cl) { throw 'cl.exe is not on PATH after importing the build environment.' }
Write-Host "Compiler: $($cl.Source)"

# ------------------------------------------------------------------- compile ---

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

$commonFlags = @('/nologo', '/std:c++17', '/EHsc', '/W4', '/permissive-', '/DUNICODE', '/D_UNICODE')
$commonFlags += if ($Configuration -eq 'Release') { @('/O2', '/MT', '/DNDEBUG') } else { @('/Od', '/Zi', '/MTd') }

$targets = @(
    @{ Name = 'ChronoTriggerAccessibility.Launcher';  Source = (Join-Path $launcherDir 'launcher.cpp');   Admin = $false }
    @{ Name = 'ChronoTriggerAccessibility.Installer'; Source = (Join-Path $installerDir 'installer.cpp'); Admin = $true  }
)

foreach ($target in $targets) {
    if (-not (Test-Path -LiteralPath $target.Source)) { throw "Source not found: $($target.Source)" }

    $exe = Join-Path $outputDir "$($target.Name).exe"
    $objDir = Join-Path $outputDir "obj\$($target.Name)"
    New-Item -ItemType Directory -Path $objDir -Force | Out-Null

    # The installer writes HKLM, so it embeds a requireAdministrator manifest and
    # Windows shows the UAC prompt on launch.
    $linkFlags = @('/SUBSYSTEM:WINDOWS', '/MANIFEST:EMBED')
    if ($target.Admin) { $linkFlags += "/MANIFESTUAC:level='requireAdministrator' uiAccess='false'" }

    $arguments = @()
    $arguments += $commonFlags
    $arguments += "/Fo$objDir\"
    $arguments += $target.Source
    $arguments += "/Fe$exe"
    $arguments += '/link'
    $arguments += $linkFlags

    Write-Host "Building $($target.Name) ($Configuration, x86)..."
    & $cl.Source @arguments 2>&1 | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $($target.Name) (exit $LASTEXITCODE)." }
    if (-not (Test-Path -LiteralPath $exe)) { throw "Build reported success but $exe is missing." }
}

# -------------------------------------------------------- architecture check ---

function Get-PeMachine {
    param([Parameter(Mandatory)][string]$Path)
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $reader = New-Object System.IO.BinaryReader($stream)
        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) { throw "Not a PE image: $Path" }
        return $reader.ReadUInt16()
    } finally { $stream.Dispose() }
}

Write-Host ''
$failures = @()
foreach ($target in $targets) {
    $exe = Join-Path $outputDir "$($target.Name).exe"
    $machine = Get-PeMachine -Path $exe
    $label = '0x{0:X}' -f $machine
    if ($machine -ne 0x14C) {
        $failures += "$($target.Name) is $label; it must be x86 (0x14C) to inject into the 32-bit game."
        Write-Host "FAIL  $($target.Name)  machine=$label"
    } else {
        Write-Host "OK    $($target.Name)  machine=$label  $((Get-Item $exe).Length) bytes"
    }
}
if ($failures.Count -gt 0) { throw ($failures -join [Environment]::NewLine) }

Write-Host ''
Write-Host "Native build succeeded. Output: $outputDir"
