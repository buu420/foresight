<#
.SYNOPSIS
    Deploys Foresight's portable Reloaded-II payload and registry-free bootstrap.
.DESCRIPTION
    Normal Steam launch loads the x86 winmm proxy and its private-runtime helper.
    The developer deployer requires Accessibility\Runtime\dotnet\x86 to exist;
    the full public package supplies it. Managed files are backed up, narration
    and saves are preserved, and only owned legacy registry redirects are removed.
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [string]$GameRoot,
    [string]$PackageDirectory,
    [switch]$SkipNativeBuild,
    # Compatibility for isolated fixtures: skip registry migration/verification.
    [switch]$SkipIfeo,
    [ValidateSet('None', 'AfterMoveAside', 'AfterPayloadCopy', 'BeforeVerification')]
    [string]$FailureInjectionPoint = 'None'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ModPaths.psm1') -Force
$repo = Get-ModRepositoryRoot
$game = Get-GameRoot -GameRoot $GameRoot
$gamePrefix = $game.TrimEnd('\') + '\'
if (-not $PackageDirectory) { $PackageDirectory = Join-Path $repo 'artifacts\package\chrono.trigger.accessibility' }
$vendored = Get-VendoredReloadedRoot -RepositoryRoot $repo
$reloaded = Get-PortableReloadedRoot -GameRoot $game
$gameExe = Join-Path $game 'Chrono Trigger.exe'
$nativeOutput = Join-Path $repo '.build\native'
$bootstrapExe = Join-Path $game 'Accessibility\Bootstrap\Foresight.Bootstrap.exe'
$proxy = Join-Path $game 'winmm.dll'
$manifestPath = Join-Path $game 'Foresight-SHA256SUMS.txt'
$runtimeRoot = Join-Path $game 'Accessibility\Runtime\dotnet\x86'
$appConfigPath = Join-Path $reloaded 'Apps\chrono trigger.exe\AppConfig.json'
$backupRoot = Join-Path $game ('Accessibility\Backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))

function Assert-Exists {
    param([string]$Path, [string]$What)
    if (-not (Test-Path -LiteralPath $Path)) { throw "$What not found: $Path" }
}
function Get-ManagedPath {
    param([string]$RelativePath)
    if ([IO.Path]::IsPathRooted($RelativePath) -or $RelativePath -match '(^|[\\/])\.\.([\\/]|$)|:') { throw "Unsafe managed path: $RelativePath" }
    $path = [IO.Path]::GetFullPath((Join-Path $game $RelativePath))
    if (-not $path.StartsWith($gamePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Path escapes game folder: $RelativePath" }
    $ancestor = $path
    while ($ancestor.Length -gt $game.Length) {
        if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Managed path uses a reparse point: $ancestor" }
        $ancestor = Split-Path $ancestor -Parent
    }
    return $path
}
function Read-InstalledHashes {
    $hashes = @{}
    if (-not (Test-Path -LiteralPath $manifestPath)) { return $hashes }
    foreach ($line in Get-Content -LiteralPath $manifestPath) {
        if ($line -notmatch '^([A-Fa-f0-9]{64})  (.+)$') { throw 'Invalid existing Foresight-SHA256SUMS.txt.' }
        $sha = $Matches[1].ToUpperInvariant()
        $relative = $Matches[2].Replace('/', '\')
        $null = Get-ManagedPath $relative
        if ($hashes.ContainsKey($relative)) { throw "Duplicate manifest path: $relative" }
        $hashes[$relative] = $sha
    }
    return $hashes
}

Write-Host "Repository : $repo"
Write-Host "Game root  : $game"
$beforeHash = (Get-FileHash -LiteralPath $gameExe -Algorithm SHA256).Hash
if ($beforeHash -ne (Get-SupportedGameSha256)) { throw 'Unsupported Chrono Trigger executable. Verify the game files in Steam; an unknown build cannot be hooked.' }
Assert-Exists (Join-Path $vendored 'Loader\X86') 'Vendored Reloaded loader payload'
Assert-Exists (Join-Path $vendored 'mods\reloaded.sharedlib.hooks') 'Vendored shared hooks mod'
Assert-Exists (Join-Path $PackageDirectory 'ChronoTriggerAccessibility.Mod.dll') 'Packaged mod DLL'
$runtime = Test-X86Runtime -DotnetX86Root $runtimeRoot
if (-not $runtime.Satisfied) { throw "Private x86 .NET 9 runtime missing from $runtimeRoot. Install the complete Foresight package first." }
foreach ($relative in 'dotnet.exe', 'host\fxr\9.0.20\hostfxr.dll', 'shared\Microsoft.NETCore.App\9.0.20\coreclr.dll') {
    $runtimeFile = Join-Path $runtimeRoot $relative
    Assert-Exists $runtimeFile 'Private runtime file'
    if ((Get-PeMachine -Path $runtimeFile) -ne 0x14C) { throw "Private runtime file must be x86: $runtimeFile" }
}
if (-not $SkipNativeBuild) {
    & (Join-Path $PSScriptRoot 'Build-Native.ps1') -Configuration Release
    if ($LASTEXITCODE -ne 0) { throw "Build-Native.ps1 failed with exit code $LASTEXITCODE." }
}
foreach ($name in 'winmm.dll', 'Foresight.Bootstrap.exe') {
    $source = Join-Path $nativeOutput $name
    Assert-Exists $source "Native binary $name (run tools\Build-Native.ps1)"
    if ((Get-PeMachine -Path $source) -ne 0x14C) { throw "Native binary $name must be x86." }
}

$installedHashes = Read-InstalledHashes
function Test-OwnedFile {
    param([string]$Relative, [string[]]$KnownHashes = @())
    $path = Get-ManagedPath $Relative
    if (-not (Test-Path -LiteralPath $path)) { return $false }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    return ($KnownHashes -contains $hash) -or ($installedHashes.ContainsKey($Relative) -and $installedHashes[$Relative] -eq $hash)
}
$proxyHashes = @('A51C630B2EA3D78AD55A330EA64D510C8C0737F620BE65AD7503B61840D59E37',
    (Get-FileHash -LiteralPath (Join-Path $nativeOutput 'winmm.dll') -Algorithm SHA256).Hash)
if ((Test-Path -LiteralPath $proxy) -and -not (Test-OwnedFile 'winmm.dll' $proxyHashes)) { throw 'Existing winmm.dll is not a recognized Foresight proxy or reviewed legacy ASI loader. It was left unchanged.' }
$legacyAsi = 'Reloaded.Mod.Loader.Bootstrapper.asi'
$asiHashes = @((Get-FileHash -LiteralPath (Join-Path $vendored 'Loader\X86\Bootstrapper\Reloaded.Mod.Loader.Bootstrapper.dll') -Algorithm SHA256).Hash)
if ((Test-Path -LiteralPath (Join-Path $game $legacyAsi)) -and -not (Test-OwnedFile $legacyAsi $asiHashes)) { throw 'Existing Reloaded.Mod.Loader.Bootstrapper.asi is not recognized. It was left unchanged.' }

# Exact hashes of reviewed legacy releases, plus a matching legacy build or
# previous installation manifest, authorize retiring these two files only.
$legacyHashes = @{
    'ChronoTriggerAccessibility.Launcher.exe' = @('CABD7990618F0806E69027B279245B4966B3B97BA809347D327FDE0AA582790D', 'CCAECD5B9F74A5E994BB375DE8FF9E66FDCCC0EF3482A1AA53458D69F6CDF7E5')
    'ChronoTriggerAccessibility.Installer.exe' = @('09EA9454A2E566DD987DFC50D7E7015B7B4978685877006BF54E260EF06DEEE6', '8B4A8C2AAB65D1F451C3013E82393ED6481EE3BD237AEEE3FC991B7FD2A1DD2B')
}
$retired = @($legacyAsi)
foreach ($name in $legacyHashes.Keys) {
    $build = Join-Path $nativeOutput $name
    $hashes = @($legacyHashes[$name])
    if (Test-Path -LiteralPath $build) { $hashes += (Get-FileHash -LiteralPath $build -Algorithm SHA256).Hash }
    $relative = "Accessibility\Launcher\$name"
    if (Test-OwnedFile $relative $hashes) { $retired += $relative }
}
$managed = @('Reloaded-II\Loader', 'Reloaded-II\Mods\reloaded.sharedlib.hooks',
    'Reloaded-II\Mods\chrono.trigger.accessibility', 'Reloaded-II\Apps\chrono trigger.exe\AppConfig.json',
    'Accessibility\Bootstrap\Foresight.Bootstrap.exe', 'winmm.dll', 'Foresight-SHA256SUMS.txt')
foreach ($relative in ($managed + $retired)) {
    $path = Get-ManagedPath $relative
    if (Test-Path -LiteralPath $path -PathType Container) {
        $links = @(Get-ChildItem -LiteralPath $path -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })
        if ($links.Count) { throw "Managed directory contains a reparse point: $path" }
    }
}
$null = Get-ManagedPath 'Accessibility\Backups'
if (-not $PSCmdlet.ShouldProcess($game, 'Deploy Foresight portable bootstrap')) { return }

$moved = [Collections.Generic.List[object]]::new()
$touched = [Collections.Generic.List[string]]::new()
function Move-Aside {
    param([string]$Relative)
    $path = Get-ManagedPath $Relative
    if (Test-Path -LiteralPath $path) {
        $destination = Join-Path $backupRoot $Relative
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        Move-Item -LiteralPath $path -Destination $destination
        $moved.Add([pscustomobject]@{ Relative = $Relative; Backup = $destination })
    }
    # Record only after a successful move. A locked original must not be deleted
    # by rollback when Move-Item itself failed.
    $touched.Add($Relative)
}

try {
    # Migration is one-time cleanup. Do not recreate redirects during rollback:
    # their absence permits the original game to launch even after a failure.
    if (-not $SkipIfeo) {
        . (Join-Path $PSScriptRoot 'release\Remove-LegacyRegistration.ps1')
        Invoke-ForesightLegacyMigration -GameRoot $game -BackupDirectory $backupRoot
    } else { Write-Warning 'Fixture mode: legacy registry migration and verification skipped (-SkipIfeo).' }
    foreach ($relative in ($managed + $retired)) { Move-Aside $relative }
    if ($FailureInjectionPoint -eq 'AfterMoveAside') { throw 'Injected failure: AfterMoveAside' }
    foreach ($directory in @('Reloaded-II\Loader\X86', 'Reloaded-II\Mods', 'Reloaded-II\Apps\chrono trigger.exe',
        'Reloaded-II\Plugins', 'Reloaded-II\User\Mods', 'Reloaded-II\User\Misc', 'Accessibility\Bootstrap')) {
        New-Item -ItemType Directory -Path (Get-ManagedPath $directory) -Force | Out-Null
    }
    Copy-Item -Path (Join-Path $vendored 'Loader\X86\*') -Destination (Join-Path $reloaded 'Loader\X86') -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $vendored 'mods\reloaded.sharedlib.hooks') -Destination (Join-Path $reloaded 'Mods') -Recurse -Force
    $modDestination = Join-Path $reloaded 'Mods\chrono.trigger.accessibility'
    New-Item -ItemType Directory -Path $modDestination -Force | Out-Null
    Copy-Item -Path (Join-Path $PackageDirectory '*') -Destination $modDestination -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $nativeOutput 'Foresight.Bootstrap.exe') -Destination $bootstrapExe -Force
    if ($FailureInjectionPoint -eq 'AfterPayloadCopy') { throw 'Injected failure: AfterPayloadCopy' }

    $appConfig = [ordered]@{
        AppId = 'chrono trigger.exe'; AppName = 'Chrono Trigger'; AppLocation = $gameExe
        AppArguments = ''; AppIcon = ''; AutoInject = $false
        EnabledMods = @('reloaded.sharedlib.hooks', 'chrono.trigger.accessibility')
        WorkingDirectory = $game; PluginData = @{}
        SortedMods = @('reloaded.sharedlib.hooks', 'chrono.trigger.accessibility')
        PreserveDisabledModOrder = $true; DontInject = $false; IsMsStore = $false
    }
    [IO.File]::WriteAllText($appConfigPath, ($appConfig | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
    # Activate after the helper/payload exist and migration has succeeded.
    Copy-Item -LiteralPath (Join-Path $nativeOutput 'winmm.dll') -Destination $proxy -Force
    $newHashes = @{}
    foreach ($relative in $installedHashes.Keys) {
        $replaced = $false
        foreach ($prefix in ($managed + $retired)) {
            if ($relative -eq $prefix -or $relative.StartsWith($prefix + '\', [StringComparison]::OrdinalIgnoreCase)) { $replaced = $true; break }
        }
        if (-not $replaced) { $newHashes[$relative] = $installedHashes[$relative] }
    }
    foreach ($relative in $managed) {
        # AppConfig is mutable: the helper rewrites it when the game folder moves.
        if ($relative -in @('Foresight-SHA256SUMS.txt', 'Reloaded-II\Apps\chrono trigger.exe\AppConfig.json')) { continue }
        foreach ($file in Get-ChildItem -LiteralPath (Get-ManagedPath $relative) -Recurse -File) {
            $key = $file.FullName.Substring($gamePrefix.Length)
            $newHashes[$key] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        }
    }
    $lines = @($newHashes.Keys | Sort-Object | ForEach-Object { $newHashes[$_] + '  ' + $_.Replace('\', '/') })
    [IO.File]::WriteAllLines($manifestPath, [string[]]$lines, [Text.UTF8Encoding]::new($false))
    if ((Get-FileHash -LiteralPath $gameExe -Algorithm SHA256).Hash -ne $beforeHash) { throw 'FATAL: the game executable changed during deployment.' }
    if ($FailureInjectionPoint -eq 'BeforeVerification') { throw 'Injected failure: BeforeVerification' }
    $verifyArguments = @{ GameRoot = $game }
    if ($SkipIfeo) { $verifyArguments.SkipRegistryCheck = $true }
    & (Join-Path $PSScriptRoot 'Verify-Deployment.ps1') @verifyArguments
    if ($LASTEXITCODE -ne 0) { throw "Deployment verification failed (exit $LASTEXITCODE)." }
} catch {
    Write-Host "Deployment failed: $($_.Exception.Message). Rolling back files..." -ForegroundColor Red
    foreach ($relative in ($touched | Sort-Object { $_.Length } -Descending)) {
        try {
            $path = Get-ManagedPath $relative
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
            $previous = @($moved | Where-Object { $_.Relative -eq $relative })
            if ($previous.Count) { Move-Item -LiteralPath $previous[0].Backup -Destination $path }
        } catch { Write-Warning "Could not restore $relative. Previous files remain in $backupRoot. $($_.Exception.Message)" }
    }
    throw
}
Write-Host 'Deployed. Start Chrono Trigger from Steam as usual; start your screen reader first.'
if ($moved.Count) { Write-Host "Previous files kept at: $backupRoot" }
