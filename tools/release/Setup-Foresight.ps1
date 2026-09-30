[CmdletBinding()]
param([ValidateSet('Install','Uninstall','Verify')][string]$Mode = 'Install')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-ForesightGameClosed {
    if (Get-Process -Name 'Chrono Trigger' -ErrorAction SilentlyContinue) {
        throw 'Close Chrono Trigger before installing, updating, or uninstalling Foresight.'
    }
}

function Assert-ForesightGame {
    param([Parameter(Mandatory=$true)][string]$GameRoot)
    $gameExe = Join-Path $GameRoot 'Chrono Trigger.exe'
    if (-not (Test-Path -LiteralPath $gameExe -PathType Leaf)) {
        throw 'Copy the complete ZIP beside Chrono Trigger.exe before running this command.'
    }
    if ((Get-FileHash -LiteralPath $gameExe -Algorithm SHA256).Hash -ne '8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7') {
        throw 'This game executable is not the supported Steam build. See Foresight-README.md.'
    }
}

function Resolve-ForesightPackagePath {
    param([Parameter(Mandatory=$true)][string]$GameRoot, [Parameter(Mandatory=$true)][string]$RelativePath)
    $relative = $RelativePath.Replace('/', '\')
    if ([IO.Path]::IsPathRooted($relative) -or $relative.Contains(':')) { throw "Unsafe package path: $RelativePath" }
    $parts = $relative.Split('\')
    foreach ($part in $parts) {
        if ([string]::IsNullOrWhiteSpace($part) -or $part -in @('.', '..') -or $part -ne $part.TrimEnd(' ', '.') -or $part.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) {
            throw "Unsafe package path: $RelativePath"
        }
    }
    $root = [IO.Path]::GetFullPath($GameRoot).TrimEnd('\')
    $path = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $path.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'A package path escapes the game folder.' }
    # Lexical confinement is insufficient when a package directory is a junction.
    $current = $root
    foreach ($part in @('') + $parts) {
        if ($part) { $current = Join-Path $current $part }
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "A package path uses a link or junction: $RelativePath"
            }
        }
    }
    return $path
}

function Get-ForesightPackageManifest {
    param([Parameter(Mandatory=$true)][string]$GameRoot)
    $manifest = Resolve-ForesightPackagePath -GameRoot $GameRoot -RelativePath 'Foresight-SHA256SUMS.txt'
    if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) { throw 'The Foresight checksum manifest is missing. Recopy the complete ZIP.' }
    $seen = @{}
    $entries = @(foreach ($line in [IO.File]::ReadAllLines($manifest)) {
        if ($line -notmatch '^([A-Fa-f0-9]{64})  (.+)$') { throw 'Invalid Foresight checksum manifest.' }
        $hash = $Matches[1]
        $relative = $Matches[2].Replace('/', '\')
        $path = Resolve-ForesightPackagePath -GameRoot $GameRoot -RelativePath $relative
        if ($seen.ContainsKey($path)) { throw "Duplicate package path: $relative" }
        $seen[$path] = $true
        # A receipt must not authorize deletion of the game, saves, audio or backups.
        $ownedRootFile = $relative -in @('winmm.dll','Foresight-README.md','Install Foresight.cmd','Uninstall Foresight.cmd')
        $ownedTree = $relative -match '^(Accessibility\\(?:Bootstrap|Runtime|Foresight)|Reloaded-II\\(?:Loader|Mods\\(?:chrono\.trigger\.accessibility|reloaded\.sharedlib\.hooks)))\\'
        $userData = $relative -match '(^|\\)(?:saves?|backups?|audio|logs?)(\\|$)' -or $relative -match '\.(?:sav|srm|bak|mp3|mp4|wav|ogg|flac|log)$'
        if ((-not $ownedRootFile -and -not $ownedTree) -or $userData) { throw "Not an owned Foresight package file: $relative" }
        [pscustomobject]@{ RelativePath = $relative; Path = $path; Sha256 = $hash }
    })
    if ($entries.Count -eq 0) { throw 'The Foresight checksum manifest is empty.' }
    return $entries
}

function Assert-ForesightPackage {
    param([Parameter(Mandatory=$true)][string]$GameRoot)
    $entries = @(Get-ForesightPackageManifest -GameRoot $GameRoot)
    foreach ($entry in $entries) {
        if (-not (Test-Path -LiteralPath $entry.Path -PathType Leaf) -or (Get-FileHash -LiteralPath $entry.Path -Algorithm SHA256).Hash -ne $entry.Sha256) {
            throw "Missing or changed package file: $($entry.RelativePath). Recopy the complete ZIP."
        }
    }
    foreach ($required in @('winmm.dll','Accessibility\Bootstrap\Foresight.Bootstrap.exe','Accessibility\Foresight\Remove-LegacyRegistration.ps1','Accessibility\Foresight\Setup-Foresight.ps1')) {
        if ($required -notin $entries.RelativePath) { throw "Required package file is not in the checksum manifest: $required" }
    }
    return $entries
}

function Remove-ForesightPackage {
    param([Parameter(Mandatory=$true)][string]$GameRoot)
    Assert-ForesightGameClosed
    # Parse the entire receipt before removing the first file.
    $entries = @(Get-ForesightPackageManifest -GameRoot $GameRoot)
    $preserved = 0
    $removed = 0
    # Disable the proxy first; defer this script and launch commands until last.
    $ordered = $entries | Sort-Object @{ Expression = {
        if ($_.RelativePath -eq 'winmm.dll') { 0 }
        elseif ($_.RelativePath -eq 'Accessibility\Foresight\Setup-Foresight.ps1') { 3 }
        elseif ($_.RelativePath -in @('Install Foresight.cmd','Uninstall Foresight.cmd')) { 2 }
        else { 1 }
    } }, RelativePath
    foreach ($entry in $ordered) {
        $path = Resolve-ForesightPackagePath -GameRoot $GameRoot -RelativePath $entry.RelativePath
        if (-not (Test-Path -LiteralPath $path)) { continue }
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.Sha256) {
            Write-Warning "Preserved changed package file: $($entry.RelativePath)"
            $preserved++
            continue
        }
        Remove-Item -LiteralPath $path -Force
        $removed++
    }
    # Keep the receipt for retained files. Never recursively remove directories.
    Write-Output "Foresight removed $removed unchanged package files; preserved $preserved changed files. The checksum receipt and user files were kept."
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1') -ErrorAction Stop
        $gameRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
        if ($Mode -eq 'Uninstall') {
            Remove-ForesightPackage -GameRoot $gameRoot
        } else {
            Assert-ForesightGame -GameRoot $gameRoot
            $null = Assert-ForesightPackage -GameRoot $gameRoot
            if (Test-Path -LiteralPath (Join-Path $gameRoot 'Reloaded.Mod.Loader.Bootstrapper.asi')) {
                throw 'An old ASI loader is still present. Migrate it before installing Foresight; it was preserved.'
            }
            . (Join-Path $gameRoot 'Accessibility\Foresight\Remove-LegacyRegistration.ps1')
            if ($Mode -eq 'Install') {
                Assert-ForesightGameClosed
                Invoke-ForesightLegacyMigration -GameRoot $gameRoot
                Write-Output 'Foresight is ready. Launch Chrono Trigger normally through Steam. No registry launch hook or global runtime settings are required.'
            } else {
                $state = @(Get-ForesightLegacyRegistrationState)
                if (@($state | Where-Object { $null -ne $_.Debugger }).Count -ne 0) { throw 'A legacy or foreign launch registration is still present. Run Install Foresight.cmd to migrate a Foresight-owned registration.' }
                Write-Output 'Foresight payload, supported game, and registry-free launch verified.'
            }
        }
        exit 0
    } catch {
        Write-Error $_ -ErrorAction Continue
        exit 1
    }
}
