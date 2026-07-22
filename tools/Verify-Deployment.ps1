[CmdletBinding()]
param(
    [string]$ReloadedRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Spyro Reignited Trilogy\mod-tools\reloaded-ii\Release',

    [string]$GameExecutable = 'G:\SteamLibrary\steamapps\common\Chrono Trigger\Chrono Trigger.exe',

    [string]$RuntimeRoot = 'C:\Users\User\AppData\Local\ChronoTriggerAccessibility\dotnet-x86',

    [string]$LauncherPath = 'G:\SteamLibrary\steamapps\common\Chrono Trigger\Launch Chrono Trigger Accessible.ps1',

    [string]$ReloadedConfigPath = (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)) 'Reloaded-Mod-Loader-II\ReloadedII.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ModId = 'chrono.trigger.accessibility'
$AppId = 'chrono trigger.exe'
$SupportedGameSha256 = '8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7'
$SupportedPrismSha256 = '6A84322E42D1B4123E2E66E9887CFDF0CDEA2A972FA40FC7B7185AEC77F5178A'
$SupportedAsiLoaderSha256 = 'A51C630B2EA3D78AD55A330EA64D510C8C0737F620BE65AD7503B61840D59E37'
$SupportedReloadedBootstrapperSha256 = '1A9F704549F66E357C0D22C395B57FE4E7BD5248521DBB40E566D2EE1CA809AB'
$PeMachineI386 = 0x014c
$ReloadedRoot = [System.IO.Path]::GetFullPath($ReloadedRoot)
$GameExecutable = [System.IO.Path]::GetFullPath($GameExecutable)
$RuntimeRoot = [System.IO.Path]::GetFullPath($RuntimeRoot)
$LauncherPath = [System.IO.Path]::GetFullPath($LauncherPath)
$ReloadedConfigPath = [System.IO.Path]::GetFullPath($ReloadedConfigPath)

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

function Assert-ReloadedBootstrapConfiguration {
    Assert-FileExists $ReloadedConfigPath
    try {
        $bootstrapConfiguration = Get-Content -LiteralPath $ReloadedConfigPath -Raw | ConvertFrom-Json
    }
    catch {
        throw "ReloadedII.json is not valid JSON: $ReloadedConfigPath. $($_.Exception.Message)"
    }

    $expectedPaths = [ordered]@{
        LoaderPath32 = Join-Path $ReloadedRoot 'Loader\X86\Reloaded.Mod.Loader.dll'
        LauncherPath = Join-Path $ReloadedRoot 'Reloaded-II.exe'
        Bootstrapper32Path = Join-Path $ReloadedRoot 'Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll'
        ApplicationConfigDirectory = Join-Path $ReloadedRoot 'Apps'
        ModConfigDirectory = Join-Path $ReloadedRoot 'Mods'
    }
    foreach ($entry in $expectedPaths.GetEnumerator()) {
        $property = $bootstrapConfiguration.PSObject.Properties[$entry.Key]
        if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
            throw "ReloadedII.json is missing required path property $($entry.Key): $ReloadedConfigPath"
        }
        $actualPath = [System.IO.Path]::GetFullPath([string]$property.Value).TrimEnd('\')
        $expectedPath = [System.IO.Path]::GetFullPath([string]$entry.Value).TrimEnd('\')
        if (-not [string]::Equals($actualPath, $expectedPath, [StringComparison]::OrdinalIgnoreCase)) {
            throw "ReloadedII.json $($entry.Key) points to '$actualPath' instead of '$expectedPath': $ReloadedConfigPath"
        }
    }

    Assert-FileExists $expectedPaths.LoaderPath32
    Assert-FileExists ([System.IO.Path]::ChangeExtension($expectedPaths.LoaderPath32, '.runtimeconfig.json'))
}

function Assert-PackageHashes {
    param([Parameter(Mandatory = $true)][string]$ModDirectory)

    $manifestPath = Join-Path $ModDirectory 'SHA256SUMS.txt'
    Assert-FileExists $manifestPath
    $manifestEntries = [System.Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)

    foreach ($line in [System.IO.File]::ReadAllLines($manifestPath)) {
        if ($line -notmatch '^([0-9A-Fa-f]{64})  (.+)$') {
            throw "Malformed SHA-256 manifest line: $line"
        }

        $expectedHash = $Matches[1].ToUpperInvariant()
        $relativePath = $Matches[2].Replace('/', '\')
        if ([System.IO.Path]::IsPathRooted($relativePath) -or $relativePath.Split('\') -contains '..') {
            throw "Unsafe path in SHA-256 manifest: $relativePath"
        }
        if ($relativePath -ieq 'SHA256SUMS.txt' -or $manifestEntries.ContainsKey($relativePath)) {
            throw "Duplicate or self-referential SHA-256 manifest path: $relativePath"
        }

        $fullPath = [System.IO.Path]::GetFullPath((Join-Path $ModDirectory $relativePath))
        $modPrefix = $ModDirectory.TrimEnd('\') + '\'
        if (-not $fullPath.StartsWith($modPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "SHA-256 manifest path escaped the mod directory: $relativePath"
        }
        Assert-FileExists $fullPath
        $actualHash = Get-Sha256 $fullPath
        if ($actualHash -ne $expectedHash) {
            throw "SHA-256 mismatch for $relativePath. Expected $expectedHash but found $actualHash."
        }
        $manifestEntries.Add($relativePath, $expectedHash)
    }

    $actualRelativePaths = @(
        Get-ChildItem -LiteralPath $ModDirectory -Recurse -File |
            Where-Object { $_.Name -ne 'SHA256SUMS.txt' } |
            ForEach-Object {
                $prefix = $ModDirectory.TrimEnd('\') + '\'
                $_.FullName.Substring($prefix.Length)
            }
    )
    if ($actualRelativePaths.Count -ne $manifestEntries.Count) {
        throw "SHA-256 manifest lists $($manifestEntries.Count) files, but the deployed mod contains $($actualRelativePaths.Count) payload files."
    }
    foreach ($relativePath in $actualRelativePaths) {
        if (-not $manifestEntries.ContainsKey($relativePath)) {
            throw "Deployed file is not listed in SHA256SUMS.txt: $relativePath"
        }
    }
}

Assert-DirectoryExists $ReloadedRoot
Assert-FileExists (Join-Path $ReloadedRoot 'Reloaded-II.exe')
Assert-ReloadedBootstrapConfiguration
Assert-FileExists $GameExecutable
Assert-I386Pe $GameExecutable
$actualGameHash = Get-Sha256 $GameExecutable
if ($actualGameHash -ne $SupportedGameSha256) {
    throw "Unsupported Chrono Trigger.exe. Expected SHA-256 $SupportedGameSha256 but found $actualGameHash."
}

$gameDirectory = [System.IO.Path]::GetDirectoryName($GameExecutable)
$asiLoaderPath = Join-Path $gameDirectory 'winmm.dll'
$bootstrapperPath = Join-Path $gameDirectory 'Reloaded.Mod.Loader.Bootstrapper.asi'
Assert-FileExists $asiLoaderPath
Assert-I386Pe $asiLoaderPath
$actualAsiLoaderHash = Get-Sha256 $asiLoaderPath
if ($actualAsiLoaderHash -ne $SupportedAsiLoaderSha256) {
    throw "Reviewed Ultimate ASI Loader hash mismatch. Expected $SupportedAsiLoaderSha256 but found $actualAsiLoaderHash."
}
Assert-FileExists $bootstrapperPath
Assert-I386Pe $bootstrapperPath
$actualBootstrapperHash = Get-Sha256 $bootstrapperPath
if ($actualBootstrapperHash -ne $SupportedReloadedBootstrapperSha256) {
    throw "Reviewed Reloaded x86 bootstrapper hash mismatch. Expected $SupportedReloadedBootstrapperSha256 but found $actualBootstrapperHash."
}

$modDirectory = Join-Path $ReloadedRoot "Mods\$ModId"
$requiredModFiles = @(
    'ChronoTriggerAccessibility.Mod.deps.json',
    'ChronoTriggerAccessibility.Mod.dll',
    'ChronoTriggerAccessibility.Native.dll',
    'ChronoTriggerAccessibility.Prism.dll',
    'ModConfig.json',
    'prism.dll',
    'LICENSE',
    'NOTICE',
    'LICENSES\GNU-GPL-3.0.txt',
    'LICENSES\Reloaded.Hooks.Definitions-LGPL-3.0.txt',
    'LICENSES\Reloaded.SharedLib.Hooks-LGPL-3.0.txt',
    'LICENSES\Ultimate-ASI-Loader-MIT.txt',
    'LICENSES\concurrentqueue\LICENSE.md',
    'LICENSES\djinni\LICENSE',
    'LICENSES\dr_wav\LICENSE',
    'LICENSES\fmt\LICENSE',
    'LICENSES\moderncom\AUTHORS.md',
    'LICENSES\moderncom\LICENSE',
    'LICENSES\nvdaController\lgpl-2.1.txt',
    'LICENSES\nvgt\LICENSE.md',
    'LICENSES\prism\mpl-2.0.txt',
    'LICENSES\simdutf\apache-2.0.txt',
    'README.md',
    'THIRD-PARTY-NOTICES.md',
    'SHA256SUMS.txt'
)
Assert-DirectoryExists $modDirectory
foreach ($fileName in $requiredModFiles) {
    Assert-FileExists (Join-Path $modDirectory $fileName)
}

$forbiddenPayload = Get-ChildItem -LiteralPath $modDirectory -Recurse -File | Where-Object {
    $_.Extension -ieq '.exe' -or $_.FullName.IndexOf('x64', [StringComparison]::OrdinalIgnoreCase) -ge 0
}
if ($forbiddenPayload) {
    throw "The deployed mod contains a forbidden executable or x64 payload: $($forbiddenPayload.FullName -join ', ')"
}

Assert-PackageHashes $modDirectory
$prismPath = Join-Path $modDirectory 'prism.dll'
Assert-I386Pe $prismPath
$actualPrismHash = Get-Sha256 $prismPath
if ($actualPrismHash -ne $SupportedPrismSha256) {
    throw "Reviewed Prism hash mismatch. Expected $SupportedPrismSha256 but found $actualPrismHash."
}
foreach ($managedAssembly in @('ChronoTriggerAccessibility.Mod.dll', 'ChronoTriggerAccessibility.Native.dll', 'ChronoTriggerAccessibility.Prism.dll')) {
    Assert-I386Pe (Join-Path $modDirectory $managedAssembly)
}

$modConfigPath = Join-Path $modDirectory 'ModConfig.json'
$modConfig = Get-Content -LiteralPath $modConfigPath -Raw | ConvertFrom-Json
if ($modConfig.ModId -ne $ModId -or $modConfig.ModDll -ne 'ChronoTriggerAccessibility.Mod.dll') {
    throw 'Deployed ModConfig.json has an unexpected mod ID or entry assembly.'
}
if (@($modConfig.ModDependencies) -notcontains 'reloaded.sharedlib.hooks') {
    throw 'Deployed ModConfig.json does not declare reloaded.sharedlib.hooks.'
}
if (@($modConfig.SupportedAppId) -notcontains $AppId) {
    throw "Deployed ModConfig.json does not support app ID $AppId."
}

$depsPath = Join-Path $modDirectory 'ChronoTriggerAccessibility.Mod.deps.json'
$deps = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json
foreach ($target in $deps.targets.PSObject.Properties) {
    foreach ($library in $target.Value.PSObject.Properties) {
        $runtimeProperty = $library.Value.PSObject.Properties['runtime']
        if ($null -eq $runtimeProperty) {
            continue
        }
        foreach ($asset in $runtimeProperty.Value.PSObject.Properties) {
            Assert-FileExists (Join-Path $modDirectory ([System.IO.Path]::GetFileName($asset.Name)))
        }
    }
}

$profilePath = Join-Path $ReloadedRoot "Apps\$AppId\AppConfig.json"
Assert-FileExists $profilePath
$profile = Get-Content -LiteralPath $profilePath -Raw | ConvertFrom-Json
$expectedWorkingDirectory = [System.IO.Path]::GetDirectoryName($GameExecutable)
if ($profile.AppId -ne $AppId -or $profile.AppLocation -ne $GameExecutable -or $profile.WorkingDirectory -ne $expectedWorkingDirectory) {
    throw 'Reloaded AppConfig.json does not contain the exact Chrono Trigger app ID, executable, and working directory.'
}
if ($null -eq $profile.PSObject.Properties['AutoInject'] -or $profile.AutoInject -ne $false) {
    throw 'Reloaded AppConfig.json must keep AutoInject disabled when the ASI loader owns automatic startup.'
}
$enabledModMatches = @(@($profile.EnabledMods) | Where-Object { $_ -eq $ModId })
if ($enabledModMatches.Count -ne 1) {
    throw "$ModId must appear exactly once in EnabledMods."
}
$sortedModMatches = @(@($profile.SortedMods) | Where-Object { $_ -eq $ModId })
if ($sortedModMatches.Count -ne 1) {
    throw "$ModId must appear exactly once in SortedMods."
}

$sharedHookDirectory = Join-Path $ReloadedRoot 'Mods\reloaded.sharedlib.hooks'
$sharedHookConfigPath = Join-Path $sharedHookDirectory 'ModConfig.json'
Assert-FileExists $sharedHookConfigPath
$sharedHookConfig = Get-Content -LiteralPath $sharedHookConfigPath -Raw | ConvertFrom-Json
if ($sharedHookConfig.ModId -ne 'reloaded.sharedlib.hooks') {
    throw 'Installed shared-hook dependency has an unexpected ModId.'
}
$sharedHookX86Path = Join-Path $sharedHookDirectory $sharedHookConfig.ModR2RManagedDll32
Assert-FileExists $sharedHookX86Path
Assert-I386Pe $sharedHookX86Path

$dotnetHost = Join-Path $RuntimeRoot 'dotnet.exe'
$hostFxr = Join-Path $RuntimeRoot 'host\fxr\9.0.18\hostfxr.dll'
$netCoreFramework = Join-Path $RuntimeRoot 'shared\Microsoft.NETCore.App\9.0.18'
$windowsDesktopFramework = Join-Path $RuntimeRoot 'shared\Microsoft.WindowsDesktop.App\9.0.18'
Assert-FileExists $dotnetHost
Assert-I386Pe $dotnetHost
Assert-FileExists $hostFxr
Assert-I386Pe $hostFxr
Assert-DirectoryExists $netCoreFramework
Assert-FileExists (Join-Path $netCoreFramework 'coreclr.dll')
Assert-DirectoryExists $windowsDesktopFramework
Assert-FileExists (Join-Path $windowsDesktopFramework 'PresentationFramework.dll')
$userDotNetRootX86 = [Environment]::GetEnvironmentVariable('DOTNET_ROOT_X86', [EnvironmentVariableTarget]::User)
if (-not [string]::Equals($userDotNetRootX86, $RuntimeRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The user DOTNET_ROOT_X86 value must be '$RuntimeRoot' for direct Steam startup, but it is '$userDotNetRootX86'."
}

Assert-FileExists $LauncherPath
$repositoryLauncher = Join-Path (Split-Path -Parent $PSScriptRoot) 'Launch Chrono Trigger Accessible.ps1'
if (Test-Path -LiteralPath $repositoryLauncher -PathType Leaf) {
    if ((Get-Sha256 $LauncherPath) -ne (Get-Sha256 $repositoryLauncher)) {
        throw 'The deployed accessible launcher does not match the reviewed repository launcher.'
    }
}

Write-Output 'Chrono Trigger accessibility deployment verified.'
Write-Output "Game SHA-256: $SupportedGameSha256"
Write-Output "Prism SHA-256: $SupportedPrismSha256"
Write-Output "Ultimate ASI Loader SHA-256: $SupportedAsiLoaderSha256"
Write-Output "Reloaded bootstrapper SHA-256: $SupportedReloadedBootstrapperSha256"
Write-Output "Reloaded profile: $profilePath"
Write-Output "Mod directory: $modDirectory"
Write-Output "x86 .NET runtime: $RuntimeRoot"
Write-Output "Automatic Steam startup: $asiLoaderPath -> $bootstrapperPath"
