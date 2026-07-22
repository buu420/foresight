[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [string]$PackageDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\package\chrono.trigger.accessibility'),

    [string]$ReloadedRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Spyro Reignited Trilogy\mod-tools\reloaded-ii\Release',

    [string]$GameExecutable = 'G:\SteamLibrary\steamapps\common\Chrono Trigger\Chrono Trigger.exe',

    [string]$RuntimeRoot = 'C:\Users\User\AppData\Local\ChronoTriggerAccessibility\dotnet-x86',

    [string]$LauncherDestinationDirectory = 'G:\SteamLibrary\steamapps\common\Chrono Trigger',

    [string]$ReloadedConfigPath = (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)) 'Reloaded-Mod-Loader-II\ReloadedII.json'),

    [ValidateSet('None', 'AfterModSwap', 'AfterProfileCommit', 'AfterLauncherCommit', 'AfterAsiLoaderCommit', 'AfterAutoLaunchCommit', 'FinalVerification')]
    [string]$FailureInjectionPoint = 'None'
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
$RepositoryRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$PackageDirectory = [System.IO.Path]::GetFullPath($PackageDirectory)
$ReloadedRoot = [System.IO.Path]::GetFullPath($ReloadedRoot)
$GameExecutable = [System.IO.Path]::GetFullPath($GameExecutable)
$RuntimeRoot = [System.IO.Path]::GetFullPath($RuntimeRoot)
$LauncherDestinationDirectory = [System.IO.Path]::GetFullPath($LauncherDestinationDirectory)
$ReloadedConfigPath = [System.IO.Path]::GetFullPath($ReloadedConfigPath)
$LauncherSource = Join-Path $RepositoryRoot 'Launch Chrono Trigger Accessible.ps1'
$AsiLoaderSource = Join-Path $RepositoryRoot 'native\ultimate-asi-loader\v6.9.0\win-x86\UltimateAsiLoader.dll'
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

function Invoke-FailureInjection {
    param([Parameter(Mandatory = $true)][string]$Point)

    if ($FailureInjectionPoint -eq $Point) {
        throw "Injected deployment failure at $Point"
    }
}

function Remove-ExactPath {
    param(
        [Parameter(Mandatory = $true)][string]$LiteralPath,
        [Parameter(Mandatory = $true)][string]$ParentPath
    )

    Assert-ChildPath $LiteralPath $ParentPath
    if (-not (Test-Path -LiteralPath $LiteralPath)) {
        return
    }

    $item = Get-Item -LiteralPath $LiteralPath -Force
    if ($item.PSIsContainer) {
        Remove-Item -LiteralPath $LiteralPath -Recurse -Force
    }
    else {
        Remove-Item -LiteralPath $LiteralPath -Force
    }
}

function Restore-TransactionArtifact {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$TargetPath,
        [Parameter(Mandatory = $true)][string]$BackupPath,
        [Parameter(Mandatory = $true)][string]$ParentPath,
        [Parameter(Mandatory = $true)][bool]$HadOriginal,
        [Parameter(Mandatory = $true)][bool]$OriginalMoved,
        [Parameter(Mandatory = $true)][bool]$NewInstalled
    )

    if (-not $OriginalMoved -and -not $NewInstalled) {
        return
    }

    if ($NewInstalled -and (Test-Path -LiteralPath $TargetPath)) {
        Remove-ExactPath $TargetPath $ParentPath
    }

    if ($OriginalMoved) {
        if (-not $HadOriginal -or -not (Test-Path -LiteralPath $BackupPath)) {
            throw "Cannot restore $Name because its transaction backup is missing: $BackupPath"
        }
        if (Test-Path -LiteralPath $TargetPath) {
            Remove-ExactPath $TargetPath $ParentPath
        }
        Move-Item -LiteralPath $BackupPath -Destination $TargetPath
        if (-not (Test-Path -LiteralPath $TargetPath)) {
            throw "Rollback did not restore ${Name}: $TargetPath"
        }
    }
    elseif (-not $HadOriginal -and (Test-Path -LiteralPath $TargetPath)) {
        Remove-ExactPath $TargetPath $ParentPath
    }
}

function Assert-ProfileRegistration {
    param([Parameter(Mandatory = $true)][object]$Profile)

    $expectedWorkingDirectory = [System.IO.Path]::GetDirectoryName($GameExecutable)
    if ($Profile.AppId -ne $AppId -or $Profile.AppLocation -ne $GameExecutable -or $Profile.WorkingDirectory -ne $expectedWorkingDirectory) {
        throw 'Staged Reloaded profile does not contain the exact Chrono Trigger app ID, executable, and working directory.'
    }
    if (@(@($Profile.EnabledMods) | Where-Object { $_ -eq $ModId }).Count -ne 1) {
        throw "$ModId must appear exactly once in the staged EnabledMods list."
    }
    if (@(@($Profile.SortedMods) | Where-Object { $_ -eq $ModId }).Count -ne 1) {
        throw "$ModId must appear exactly once in the staged SortedMods list."
    }
    if ($null -eq $Profile.PSObject.Properties['AutoInject'] -or $Profile.AutoInject -ne $false) {
        throw 'Staged Reloaded profile must keep AutoInject disabled when the ASI loader owns automatic startup.'
    }
}

function Remove-EmptyDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$LiteralPath,
        [Parameter(Mandatory = $true)][string]$ParentPath
    )

    Assert-ChildPath $LiteralPath $ParentPath
    if (-not (Test-Path -LiteralPath $LiteralPath)) {
        return
    }
    if (-not (Test-Path -LiteralPath $LiteralPath -PathType Container)) {
        throw "Rollback expected a directory but found another item: $LiteralPath"
    }
    if (@(Get-ChildItem -LiteralPath $LiteralPath -Force).Count -ne 0) {
        return
    }

    Remove-Item -LiteralPath $LiteralPath -Force
    if (Test-Path -LiteralPath $LiteralPath) {
        throw "Rollback could not remove the empty directory created by deployment: $LiteralPath"
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

Assert-DirectoryExists $PackageDirectory
Assert-DirectoryExists $ReloadedRoot
Assert-FileExists (Join-Path $ReloadedRoot 'Reloaded-II.exe')
$bootstrapperSource = Join-Path $ReloadedRoot 'Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll'
Assert-FileExists $AsiLoaderSource
Assert-FileExists $bootstrapperSource
Assert-I386Pe $AsiLoaderSource
if ((Get-Sha256 $AsiLoaderSource) -ne $SupportedAsiLoaderSha256) {
    throw 'Repository does not contain the reviewed Ultimate ASI Loader x86 payload.'
}
Assert-I386Pe $bootstrapperSource
if ((Get-Sha256 $bootstrapperSource) -ne $SupportedReloadedBootstrapperSha256) {
    throw 'Reloaded-II does not contain the reviewed x86 bootstrapper.'
}
Assert-ReloadedBootstrapConfiguration
Assert-FileExists $GameExecutable
Assert-FileExists $LauncherSource
Assert-FileExists $VerifyScript
Assert-PackageManifest $PackageDirectory

$requiredPackageFiles = @(
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
foreach ($relativePath in $requiredPackageFiles) {
    Assert-FileExists (Join-Path $PackageDirectory $relativePath)
}

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
$gameDirectory = [System.IO.Path]::GetDirectoryName($GameExecutable)
$asiLoaderDestination = Join-Path $gameDirectory 'winmm.dll'
$bootstrapperDestination = Join-Path $gameDirectory 'Reloaded.Mod.Loader.Bootstrapper.asi'
Assert-ChildPath $modDestination $modsRoot
Assert-ChildPath $profilePath $appsRoot
Assert-ChildPath $launcherDestination $LauncherDestinationDirectory
Assert-ChildPath $asiLoaderDestination $gameDirectory
Assert-ChildPath $bootstrapperDestination $gameDirectory
if ([string]::Equals($launcherDestination, $GameExecutable, [StringComparison]::OrdinalIgnoreCase) -or [System.IO.Path]::GetExtension($launcherDestination) -ine '.ps1') {
    throw 'Accessible launcher destination could overwrite a game binary.'
}

if ((Test-Path -LiteralPath $modDestination) -and -not (Test-Path -LiteralPath $modDestination -PathType Container)) {
    throw "Reloaded mod destination exists but is not a directory: $modDestination"
}
if ((Test-Path -LiteralPath $profilePath) -and -not (Test-Path -LiteralPath $profilePath -PathType Leaf)) {
    throw "Reloaded profile destination exists but is not a file: $profilePath"
}
if ((Test-Path -LiteralPath $launcherDestination) -and -not (Test-Path -LiteralPath $launcherDestination -PathType Leaf)) {
    throw "Accessible launcher destination exists but is not a file: $launcherDestination"
}
if ((Test-Path -LiteralPath $asiLoaderDestination) -and -not (Test-Path -LiteralPath $asiLoaderDestination -PathType Leaf)) {
    throw "ASI loader destination exists but is not a file: $asiLoaderDestination"
}
if ((Test-Path -LiteralPath $bootstrapperDestination) -and -not (Test-Path -LiteralPath $bootstrapperDestination -PathType Leaf)) {
    throw "Reloaded bootstrapper destination exists but is not a file: $bootstrapperDestination"
}
if ((Test-Path -LiteralPath $asiLoaderDestination -PathType Leaf) -and (Get-Sha256 $asiLoaderDestination) -ne $SupportedAsiLoaderSha256) {
    throw "Refusing to replace an unrelated pre-existing file: $asiLoaderDestination"
}
if ((Test-Path -LiteralPath $bootstrapperDestination -PathType Leaf) -and (Get-Sha256 $bootstrapperDestination) -ne $SupportedReloadedBootstrapperSha256) {
    throw "Refusing to replace an unrelated pre-existing file: $bootstrapperDestination"
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
Set-JsonProperty $profile 'AutoInject' $false
$existingEnabledMods = if ($null -eq $profile.PSObject.Properties['EnabledMods']) { @() } else { @($profile.EnabledMods) }
$existingSortedMods = if ($null -eq $profile.PSObject.Properties['SortedMods']) { @() } else { @($profile.SortedMods) }
Set-JsonProperty $profile 'EnabledMods' (Add-ModIdOnce $existingEnabledMods $ModId)
Set-JsonProperty $profile 'SortedMods' (Add-ModIdOnce $existingSortedMods $ModId)
$profileJson = $profile | ConvertTo-Json -Depth 100

$launcherDestinationParent = [System.IO.Path]::GetDirectoryName($LauncherDestinationDirectory)
if ([string]::IsNullOrWhiteSpace($launcherDestinationParent)) {
    throw "Accessible launcher destination must have a parent directory: $LauncherDestinationDirectory"
}
Assert-DirectoryExists $launcherDestinationParent
$affectedRoots = "$ReloadedRoot; $gameDirectory; $LauncherDestinationDirectory"
if (-not $PSCmdlet.ShouldProcess($affectedRoots, "Deploy $ModId, register Chrono Trigger, and install its automatic startup loaders")) {
    Write-Output 'Deployment preflight passed; no files were changed because WhatIf/confirmation declined the operation.'
    return
}

$hadModsRoot = Test-Path -LiteralPath $modsRoot -PathType Container
$hadAppsRoot = Test-Path -LiteralPath $appsRoot -PathType Container
$hadProfileDirectory = Test-Path -LiteralPath $profileDirectory -PathType Container
$hadLauncherDestinationDirectory = Test-Path -LiteralPath $LauncherDestinationDirectory -PathType Container

$transactionId = [Guid]::NewGuid().ToString('N')
$stagingDirectory = Join-Path $modsRoot ('.{0}.staging.{1}' -f $ModId, $transactionId)
$modBackup = Join-Path $modsRoot ('.{0}.backup.{1}' -f $ModId, $transactionId)
$profileTemporaryPath = Join-Path $profileDirectory ('.AppConfig.transaction.{0}.tmp' -f $transactionId)
$profileBackup = Join-Path $profileDirectory ('.AppConfig.backup.{0}.json' -f $transactionId)
$launcherTemporaryPath = Join-Path $LauncherDestinationDirectory ('.Launch Chrono Trigger Accessible.transaction.{0}.ps1' -f $transactionId)
$launcherBackup = Join-Path $LauncherDestinationDirectory ('.Launch Chrono Trigger Accessible.backup.{0}.ps1' -f $transactionId)
$asiLoaderTemporaryPath = Join-Path $gameDirectory ('.winmm.transaction.{0}.dll' -f $transactionId)
$asiLoaderBackup = Join-Path $gameDirectory ('.winmm.backup.{0}.dll' -f $transactionId)
$bootstrapperTemporaryPath = Join-Path $gameDirectory ('.Reloaded.Mod.Loader.Bootstrapper.transaction.{0}.asi' -f $transactionId)
$bootstrapperBackup = Join-Path $gameDirectory ('.Reloaded.Mod.Loader.Bootstrapper.backup.{0}.asi' -f $transactionId)
Assert-ChildPath $stagingDirectory $modsRoot
Assert-ChildPath $modBackup $modsRoot
Assert-ChildPath $profileTemporaryPath $profileDirectory
Assert-ChildPath $profileBackup $profileDirectory
Assert-ChildPath $launcherTemporaryPath $LauncherDestinationDirectory
Assert-ChildPath $launcherBackup $LauncherDestinationDirectory
Assert-ChildPath $asiLoaderTemporaryPath $gameDirectory
Assert-ChildPath $asiLoaderBackup $gameDirectory
Assert-ChildPath $bootstrapperTemporaryPath $gameDirectory
Assert-ChildPath $bootstrapperBackup $gameDirectory

$hadMod = Test-Path -LiteralPath $modDestination -PathType Container
$hadProfile = Test-Path -LiteralPath $profilePath -PathType Leaf
$hadLauncher = Test-Path -LiteralPath $launcherDestination -PathType Leaf
$hadAsiLoader = Test-Path -LiteralPath $asiLoaderDestination -PathType Leaf
$hadBootstrapper = Test-Path -LiteralPath $bootstrapperDestination -PathType Leaf
$modOriginalMoved = $false
$modNewInstalled = $false
$profileOriginalMoved = $false
$profileNewInstalled = $false
$launcherOriginalMoved = $false
$launcherNewInstalled = $false
$asiLoaderOriginalMoved = $false
$asiLoaderNewInstalled = $false
$bootstrapperOriginalMoved = $false
$bootstrapperNewInstalled = $false
$transactionSucceeded = $false

try {
    [void][System.IO.Directory]::CreateDirectory($modsRoot)
    [void][System.IO.Directory]::CreateDirectory($appsRoot)
    [void][System.IO.Directory]::CreateDirectory($profileDirectory)
    [void][System.IO.Directory]::CreateDirectory($LauncherDestinationDirectory)

    Copy-Item -LiteralPath $PackageDirectory -Destination $stagingDirectory -Recurse
    Assert-PackageManifest $stagingDirectory

    [System.IO.File]::WriteAllText($profileTemporaryPath, $profileJson + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    $stagedProfile = Get-Content -LiteralPath $profileTemporaryPath -Raw | ConvertFrom-Json
    Assert-ProfileRegistration $stagedProfile

    Copy-Item -LiteralPath $LauncherSource -Destination $launcherTemporaryPath
    if ((Get-Sha256 $launcherTemporaryPath) -ne (Get-Sha256 $LauncherSource)) {
        throw 'Staged accessible launcher does not match the reviewed repository launcher.'
    }

    Copy-Item -LiteralPath $AsiLoaderSource -Destination $asiLoaderTemporaryPath
    Assert-I386Pe $asiLoaderTemporaryPath
    if ((Get-Sha256 $asiLoaderTemporaryPath) -ne $SupportedAsiLoaderSha256) {
        throw 'Staged Ultimate ASI Loader does not match the reviewed Reloaded-II payload.'
    }
    Copy-Item -LiteralPath $bootstrapperSource -Destination $bootstrapperTemporaryPath
    Assert-I386Pe $bootstrapperTemporaryPath
    if ((Get-Sha256 $bootstrapperTemporaryPath) -ne $SupportedReloadedBootstrapperSha256) {
        throw 'Staged Reloaded x86 bootstrapper does not match the reviewed source.'
    }

    if ($hadMod) {
        Move-Item -LiteralPath $modDestination -Destination $modBackup
        $modOriginalMoved = $true
    }
    Move-Item -LiteralPath $stagingDirectory -Destination $modDestination
    $modNewInstalled = $true
    Invoke-FailureInjection 'AfterModSwap'

    if ($hadProfile) {
        Move-Item -LiteralPath $profilePath -Destination $profileBackup
        $profileOriginalMoved = $true
    }
    Move-Item -LiteralPath $profileTemporaryPath -Destination $profilePath
    $profileNewInstalled = $true
    Invoke-FailureInjection 'AfterProfileCommit'

    if ($hadLauncher) {
        Move-Item -LiteralPath $launcherDestination -Destination $launcherBackup
        $launcherOriginalMoved = $true
    }
    Move-Item -LiteralPath $launcherTemporaryPath -Destination $launcherDestination
    $launcherNewInstalled = $true
    Invoke-FailureInjection 'AfterLauncherCommit'

    if ($hadAsiLoader) {
        if (-not (Test-Path -LiteralPath $asiLoaderDestination -PathType Leaf) -or (Get-Sha256 $asiLoaderDestination) -ne $SupportedAsiLoaderSha256) {
            throw "The pre-existing automatic startup loader changed during deployment; refusing to replace it: $asiLoaderDestination"
        }
        Remove-ExactPath $asiLoaderTemporaryPath $gameDirectory
    }
    else {
        if (Test-Path -LiteralPath $asiLoaderDestination) {
            throw "An automatic startup loader appeared during deployment; refusing to replace it: $asiLoaderDestination"
        }
        [System.IO.File]::Move($asiLoaderTemporaryPath, $asiLoaderDestination)
        $asiLoaderNewInstalled = $true
    }
    Invoke-FailureInjection 'AfterAsiLoaderCommit'

    if ($hadBootstrapper) {
        if (-not (Test-Path -LiteralPath $bootstrapperDestination -PathType Leaf) -or (Get-Sha256 $bootstrapperDestination) -ne $SupportedReloadedBootstrapperSha256) {
            throw "The pre-existing Reloaded bootstrapper changed during deployment; refusing to replace it: $bootstrapperDestination"
        }
        Remove-ExactPath $bootstrapperTemporaryPath $gameDirectory
    }
    else {
        if (Test-Path -LiteralPath $bootstrapperDestination) {
            throw "A Reloaded bootstrapper appeared during deployment; refusing to replace it: $bootstrapperDestination"
        }
        [System.IO.File]::Move($bootstrapperTemporaryPath, $bootstrapperDestination)
        $bootstrapperNewInstalled = $true
    }
    Invoke-FailureInjection 'AfterAutoLaunchCommit'
    Invoke-FailureInjection 'FinalVerification'

    & $VerifyScript -ReloadedRoot $ReloadedRoot -GameExecutable $GameExecutable -RuntimeRoot $RuntimeRoot -LauncherPath $launcherDestination -ReloadedConfigPath $ReloadedConfigPath
    $transactionSucceeded = $true
}
catch {
    $deploymentException = $_.Exception
    $rollbackExceptions = [System.Collections.Generic.List[System.Exception]]::new()

    try {
        Restore-TransactionArtifact 'Reloaded bootstrapper' $bootstrapperDestination $bootstrapperBackup $gameDirectory $hadBootstrapper $bootstrapperOriginalMoved $bootstrapperNewInstalled
    }
    catch {
        $rollbackExceptions.Add($_.Exception)
    }
    try {
        Restore-TransactionArtifact 'Ultimate ASI Loader' $asiLoaderDestination $asiLoaderBackup $gameDirectory $hadAsiLoader $asiLoaderOriginalMoved $asiLoaderNewInstalled
    }
    catch {
        $rollbackExceptions.Add($_.Exception)
    }
    try {
        Restore-TransactionArtifact 'accessible launcher' $launcherDestination $launcherBackup $LauncherDestinationDirectory $hadLauncher $launcherOriginalMoved $launcherNewInstalled
    }
    catch {
        $rollbackExceptions.Add($_.Exception)
    }
    try {
        Restore-TransactionArtifact 'Reloaded profile' $profilePath $profileBackup $profileDirectory $hadProfile $profileOriginalMoved $profileNewInstalled
    }
    catch {
        $rollbackExceptions.Add($_.Exception)
    }
    try {
        Restore-TransactionArtifact 'Reloaded mod directory' $modDestination $modBackup $modsRoot $hadMod $modOriginalMoved $modNewInstalled
    }
    catch {
        $rollbackExceptions.Add($_.Exception)
    }

    foreach ($temporaryArtifact in @(
        [PSCustomObject]@{ Path = $stagingDirectory; Parent = $modsRoot },
        [PSCustomObject]@{ Path = $profileTemporaryPath; Parent = $profileDirectory },
        [PSCustomObject]@{ Path = $launcherTemporaryPath; Parent = $LauncherDestinationDirectory },
        [PSCustomObject]@{ Path = $asiLoaderTemporaryPath; Parent = $gameDirectory },
        [PSCustomObject]@{ Path = $bootstrapperTemporaryPath; Parent = $gameDirectory }
    )) {
        try {
            Remove-ExactPath $temporaryArtifact.Path $temporaryArtifact.Parent
        }
        catch {
            $rollbackExceptions.Add($_.Exception)
        }
    }

    foreach ($createdDirectory in @(
        [PSCustomObject]@{ Path = $profileDirectory; Parent = $appsRoot; Existed = $hadProfileDirectory },
        [PSCustomObject]@{ Path = $appsRoot; Parent = $ReloadedRoot; Existed = $hadAppsRoot },
        [PSCustomObject]@{ Path = $LauncherDestinationDirectory; Parent = $launcherDestinationParent; Existed = $hadLauncherDestinationDirectory },
        [PSCustomObject]@{ Path = $modsRoot; Parent = $ReloadedRoot; Existed = $hadModsRoot }
    )) {
        if (-not $createdDirectory.Existed) {
            try {
                Remove-EmptyDirectory $createdDirectory.Path $createdDirectory.Parent
            }
            catch {
                $rollbackExceptions.Add($_.Exception)
            }
        }
    }

    if ($rollbackExceptions.Count -gt 0) {
        $allExceptions = [System.Exception[]]::new($rollbackExceptions.Count + 1)
        $allExceptions[0] = $deploymentException
        for ($index = 0; $index -lt $rollbackExceptions.Count; $index++) {
            $allExceptions[$index + 1] = $rollbackExceptions[$index]
        }
        throw [System.AggregateException]::new('Deployment failed and one or more exact targets could not be rolled back. Transaction backups were preserved where possible.', $allExceptions)
    }

    throw $deploymentException
}
finally {
    foreach ($temporaryArtifact in @(
        [PSCustomObject]@{ Path = $stagingDirectory; Parent = $modsRoot },
        [PSCustomObject]@{ Path = $profileTemporaryPath; Parent = $profileDirectory },
        [PSCustomObject]@{ Path = $launcherTemporaryPath; Parent = $LauncherDestinationDirectory },
        [PSCustomObject]@{ Path = $asiLoaderTemporaryPath; Parent = $gameDirectory },
        [PSCustomObject]@{ Path = $bootstrapperTemporaryPath; Parent = $gameDirectory }
    )) {
        try {
            Remove-ExactPath $temporaryArtifact.Path $temporaryArtifact.Parent
        }
        catch {
            Write-Warning "Could not remove transaction staging artifact $($temporaryArtifact.Path): $($_.Exception.Message)"
        }
    }
}

if (-not $transactionSucceeded) {
    throw 'Deployment transaction ended without success or a reported failure.'
}

foreach ($backupArtifact in @(
    [PSCustomObject]@{ Path = $modBackup; Parent = $modsRoot },
    [PSCustomObject]@{ Path = $profileBackup; Parent = $profileDirectory },
    [PSCustomObject]@{ Path = $launcherBackup; Parent = $LauncherDestinationDirectory },
    [PSCustomObject]@{ Path = $asiLoaderBackup; Parent = $gameDirectory },
    [PSCustomObject]@{ Path = $bootstrapperBackup; Parent = $gameDirectory }
)) {
    try {
        Remove-ExactPath $backupArtifact.Path $backupArtifact.Parent
    }
    catch {
        Write-Warning "Deployment verified successfully, but its obsolete transaction backup could not be removed: $($backupArtifact.Path). $($_.Exception.Message)"
    }
}

Write-Output "Deployed mod: $modDestination"
Write-Output "Updated Reloaded profile: $profilePath"
Write-Output "Accessible launcher: $launcherDestination"
Write-Output "Automatic startup ASI loader: $asiLoaderDestination"
Write-Output "Automatic startup Reloaded bootstrapper: $bootstrapperDestination"
