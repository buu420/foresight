[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [string]$PackageDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\package\chrono.trigger.accessibility'),

    [string]$ReloadedRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Spyro Reignited Trilogy\mod-tools\reloaded-ii\Release',

    [string]$GameExecutable = 'G:\SteamLibrary\steamapps\common\Chrono Trigger\Chrono Trigger.exe',

    [string]$RuntimeRoot = 'C:\Users\User\AppData\Local\ChronoTriggerAccessibility\dotnet-x86',

    [string]$LauncherDestinationDirectory = 'G:\SteamLibrary\steamapps\common\Chrono Trigger'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ModId = 'chrono.trigger.accessibility'
$AppId = 'chrono trigger.exe'
$SupportedGameSha256 = '8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7'
$SupportedPrismSha256 = '6A84322E42D1B4123E2E66E9887CFDF0CDEA2A972FA40FC7B7185AEC77F5178A'
$PeMachineI386 = 0x014c
$RepositoryRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$PackageDirectory = [System.IO.Path]::GetFullPath($PackageDirectory)
$ReloadedRoot = [System.IO.Path]::GetFullPath($ReloadedRoot)
$GameExecutable = [System.IO.Path]::GetFullPath($GameExecutable)
$RuntimeRoot = [System.IO.Path]::GetFullPath($RuntimeRoot)
$LauncherDestinationDirectory = [System.IO.Path]::GetFullPath($LauncherDestinationDirectory)
$LauncherSource = Join-Path $RepositoryRoot 'Launch Chrono Trigger Accessible.ps1'
$VerifyScript = Join-Path $PSScriptRoot 'Verify-Deployment.ps1'

function Assert-FileExists {
    param([Parameter(Mandatory = $true)][string]$LiteralPath)

    if (-not (Test-Path -LiteralPath $LiteralPath -PathType Leaf)) {
        throw "Required file is missing: $LiteralPath"
    }
}

function Assert-DirectoryExists {
    param([Parameter(Mandatory = $true)][string]$LiteralPath)

    if (-not (Test-Path -LiteralPath $LiteralPath -PathType Container)) {
        throw "Required directory is missing: $LiteralPath"
    }
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$LiteralPath)

    Assert-FileExists $LiteralPath
    $stream = [System.IO.File]::OpenRead($LiteralPath)
    try {
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            return [BitConverter]::ToString($sha256.ComputeHash($stream)).Replace('-', '')
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-PeMachine {
    param([Parameter(Mandatory = $true)][string]$LiteralPath)

    Assert-FileExists $LiteralPath
    $bytes = [System.IO.File]::ReadAllBytes($LiteralPath)
    if ($bytes.Length -lt 0x40) {
        throw "File is too small to be a PE image: $LiteralPath"
    }
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
    if ($peOffset -lt 0 -or ($peOffset + 6) -gt $bytes.Length -or [BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x00004550) {
        throw "PE header is invalid: $LiteralPath"
    }
    return [BitConverter]::ToUInt16($bytes, $peOffset + 4)
}

function Assert-I386Pe {
    param([Parameter(Mandatory = $true)][string]$LiteralPath)

    $machine = Get-PeMachine $LiteralPath
    if ($machine -ne $PeMachineI386) {
        throw ('Expected a 32-bit x86 PE image at {0}, but machine was 0x{1:X4}.' -f $LiteralPath, $machine)
    }
}

function Assert-ChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$LiteralPath,
        [Parameter(Mandatory = $true)][string]$ParentPath
    )

    $fullPath = [System.IO.Path]::GetFullPath($LiteralPath)
    $parentPrefix = [System.IO.Path]::GetFullPath($ParentPath).TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($parentPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside its intended parent directory: $fullPath"
    }
}

function Assert-PackageManifest {
    param([Parameter(Mandatory = $true)][string]$ModDirectory)

    $manifestPath = Join-Path $ModDirectory 'SHA256SUMS.txt'
    Assert-FileExists $manifestPath
    $entries = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

    foreach ($line in [System.IO.File]::ReadAllLines($manifestPath)) {
        if ($line -notmatch '^([0-9A-Fa-f]{64})  (.+)$') {
            throw "Malformed SHA-256 manifest line: $line"
        }
        $expectedHash = $Matches[1].ToUpperInvariant()
        $relativePath = $Matches[2].Replace('/', '\')
        if ([System.IO.Path]::IsPathRooted($relativePath) -or $relativePath.Split('\') -contains '..' -or -not $entries.Add($relativePath)) {
            throw "Unsafe or duplicate SHA-256 manifest path: $relativePath"
        }
        $fullPath = [System.IO.Path]::GetFullPath((Join-Path $ModDirectory $relativePath))
        Assert-ChildPath $fullPath $ModDirectory
        Assert-FileExists $fullPath
        $actualHash = Get-Sha256 $fullPath
        if ($actualHash -ne $expectedHash) {
            throw "SHA-256 mismatch for $relativePath. Expected $expectedHash but found $actualHash."
        }
    }

    $actualFiles = @(
        Get-ChildItem -LiteralPath $ModDirectory -Recurse -File |
            Where-Object { $_.Name -ne 'SHA256SUMS.txt' }
    )
    if ($actualFiles.Count -ne $entries.Count) {
        throw "Package manifest lists $($entries.Count) files, but the package contains $($actualFiles.Count) payload files."
    }
    foreach ($file in $actualFiles) {
        $prefix = $ModDirectory.TrimEnd('\') + '\'
        $relativePath = $file.FullName.Substring($prefix.Length)
        if (-not $entries.Contains($relativePath)) {
            throw "Package file is not listed in SHA256SUMS.txt: $relativePath"
        }
    }
}

function Set-JsonProperty {
    param(
        [Parameter(Mandatory = $true)][object]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [AllowNull()][object]$Value
    )

    $existingProperty = $Object.PSObject.Properties[$Name]
    if ($null -eq $existingProperty) {
        $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value
    }
    else {
        $existingProperty.Value = $Value
    }
}

function Add-ModIdOnce {
    param(
        [AllowNull()][object[]]$Existing,
        [Parameter(Mandatory = $true)][string]$RequiredModId
    )

    $result = [System.Collections.Generic.List[string]]::new()
    foreach ($item in @($Existing)) {
        if ($null -eq $item -or [string]::Equals([string]$item, $RequiredModId, [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }
        $result.Add([string]$item)
    }
    $result.Add($RequiredModId)
    return ,$result.ToArray()
}

Assert-DirectoryExists $PackageDirectory
Assert-DirectoryExists $ReloadedRoot
Assert-FileExists (Join-Path $ReloadedRoot 'Reloaded-II.exe')
Assert-FileExists $GameExecutable
Assert-FileExists $LauncherSource
Assert-FileExists $VerifyScript
Assert-PackageManifest $PackageDirectory

$forbiddenPayload = Get-ChildItem -LiteralPath $PackageDirectory -Recurse -File | Where-Object {
    $_.Extension -ieq '.exe' -or $_.FullName.IndexOf('x64', [StringComparison]::OrdinalIgnoreCase) -ge 0
}
if ($forbiddenPayload) {
    throw "Package contains a forbidden executable or x64 payload: $($forbiddenPayload.FullName -join ', ')"
}

$packagePrism = Join-Path $PackageDirectory 'prism.dll'
Assert-I386Pe $packagePrism
if ((Get-Sha256 $packagePrism) -ne $SupportedPrismSha256) {
    throw 'Package does not contain the reviewed Prism 0.17.3 x86 binary.'
}

$packageModConfigPath = Join-Path $PackageDirectory 'ModConfig.json'
Assert-FileExists $packageModConfigPath
$packageModConfig = Get-Content -LiteralPath $packageModConfigPath -Raw | ConvertFrom-Json
if ($packageModConfig.ModId -ne $ModId -or @($packageModConfig.ModDependencies) -notcontains 'reloaded.sharedlib.hooks') {
    throw 'Package ModConfig.json has an unexpected ID or is missing reloaded.sharedlib.hooks.'
}

Assert-I386Pe $GameExecutable
$actualGameHash = Get-Sha256 $GameExecutable
if ($actualGameHash -ne $SupportedGameSha256) {
    throw "Unsupported Chrono Trigger.exe. Expected SHA-256 $SupportedGameSha256 but found $actualGameHash."
}

$dotnetHost = Join-Path $RuntimeRoot 'dotnet.exe'
$hostFxr = Join-Path $RuntimeRoot 'host\fxr\9.0.18\hostfxr.dll'
Assert-FileExists $dotnetHost
Assert-I386Pe $dotnetHost
Assert-FileExists $hostFxr
Assert-I386Pe $hostFxr
$netCoreFramework = Join-Path $RuntimeRoot 'shared\Microsoft.NETCore.App\9.0.18'
$windowsDesktopFramework = Join-Path $RuntimeRoot 'shared\Microsoft.WindowsDesktop.App\9.0.18'
Assert-DirectoryExists $netCoreFramework
Assert-FileExists (Join-Path $netCoreFramework 'coreclr.dll')
Assert-DirectoryExists $windowsDesktopFramework
Assert-FileExists (Join-Path $windowsDesktopFramework 'PresentationFramework.dll')

$sharedHookConfigPath = Join-Path $ReloadedRoot 'Mods\reloaded.sharedlib.hooks\ModConfig.json'
Assert-FileExists $sharedHookConfigPath
$sharedHookConfig = Get-Content -LiteralPath $sharedHookConfigPath -Raw | ConvertFrom-Json
if ($sharedHookConfig.ModId -ne 'reloaded.sharedlib.hooks') {
    throw 'The installed Reloaded shared-hook dependency has an unexpected ModId.'
}
$sharedHookX86Path = Join-Path (Split-Path -Parent $sharedHookConfigPath) $sharedHookConfig.ModR2RManagedDll32
Assert-FileExists $sharedHookX86Path
Assert-I386Pe $sharedHookX86Path

$modsRoot = Join-Path $ReloadedRoot 'Mods'
$modDestination = Join-Path $modsRoot $ModId
$appsRoot = Join-Path $ReloadedRoot 'Apps'
$profileDirectory = Join-Path $appsRoot $AppId
$profilePath = Join-Path $profileDirectory 'AppConfig.json'
$launcherDestination = Join-Path $LauncherDestinationDirectory 'Launch Chrono Trigger Accessible.ps1'
Assert-ChildPath $modDestination $modsRoot
Assert-ChildPath $profilePath $appsRoot
Assert-ChildPath $launcherDestination $LauncherDestinationDirectory
if ([string]::Equals($launcherDestination, $GameExecutable, [StringComparison]::OrdinalIgnoreCase) -or [System.IO.Path]::GetExtension($launcherDestination) -ine '.ps1') {
    throw 'Accessible launcher destination could overwrite a game binary.'
}

if (-not $PSCmdlet.ShouldProcess($ReloadedRoot, "Deploy $ModId and register Chrono Trigger")) {
    Write-Output 'Deployment preflight passed; no files were changed because WhatIf/confirmation declined the operation.'
    return
}

[void][System.IO.Directory]::CreateDirectory($modsRoot)
[void][System.IO.Directory]::CreateDirectory($appsRoot)
[void][System.IO.Directory]::CreateDirectory($LauncherDestinationDirectory)

$stagingDirectory = Join-Path $modsRoot ('.{0}.staging.{1}' -f $ModId, [Guid]::NewGuid().ToString('N'))
Assert-ChildPath $stagingDirectory $modsRoot
try {
    Copy-Item -LiteralPath $PackageDirectory -Destination $stagingDirectory -Recurse
    Assert-PackageManifest $stagingDirectory

    if (Test-Path -LiteralPath $modDestination) {
        Remove-Item -LiteralPath $modDestination -Recurse -Force
    }
    Move-Item -LiteralPath $stagingDirectory -Destination $modDestination
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
}

if (Test-Path -LiteralPath $profilePath -PathType Leaf) {
    $profile = Get-Content -LiteralPath $profilePath -Raw | ConvertFrom-Json
}
else {
    $profile = [PSCustomObject][ordered]@{
        AppId = $AppId
        AppName = 'Chrono Trigger'
        AppLocation = $GameExecutable
        AppArguments = ''
        AppIcon = ''
        AutoInject = $false
        DontInject = $false
        EnabledMods = @()
        SortedMods = @()
        WorkingDirectory = [System.IO.Path]::GetDirectoryName($GameExecutable)
        PluginData = [PSCustomObject]@{}
        PreserveDisabledModOrder = $false
        IsMsStore = $false
    }
}

Set-JsonProperty $profile 'AppId' $AppId
Set-JsonProperty $profile 'AppLocation' $GameExecutable
Set-JsonProperty $profile 'WorkingDirectory' ([System.IO.Path]::GetDirectoryName($GameExecutable))
$existingEnabledMods = if ($null -eq $profile.PSObject.Properties['EnabledMods']) { @() } else { @($profile.EnabledMods) }
$existingSortedMods = if ($null -eq $profile.PSObject.Properties['SortedMods']) { @() } else { @($profile.SortedMods) }
Set-JsonProperty $profile 'EnabledMods' (Add-ModIdOnce $existingEnabledMods $ModId)
Set-JsonProperty $profile 'SortedMods' (Add-ModIdOnce $existingSortedMods $ModId)

[void][System.IO.Directory]::CreateDirectory($profileDirectory)
$profileJson = $profile | ConvertTo-Json -Depth 100
$profileTemporaryPath = Join-Path $profileDirectory ('.AppConfig.{0}.tmp' -f [Guid]::NewGuid().ToString('N'))
[System.IO.File]::WriteAllText($profileTemporaryPath, $profileJson + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $profileTemporaryPath -Destination $profilePath -Force

Copy-Item -LiteralPath $LauncherSource -Destination $launcherDestination -Force

& $VerifyScript -ReloadedRoot $ReloadedRoot -GameExecutable $GameExecutable -RuntimeRoot $RuntimeRoot -LauncherPath $launcherDestination

Write-Output "Deployed mod: $modDestination"
Write-Output "Updated Reloaded profile: $profilePath"
Write-Output "Accessible launcher: $launcherDestination"
