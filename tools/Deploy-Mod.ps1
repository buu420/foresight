<#
.SYNOPSIS
    Deploys the Chrono Trigger accessibility mod as a self-contained portable
    Reloaded-II installation inside the game folder.

.DESCRIPTION
    Produces this layout, every path derived from -GameRoot:

        <game>\Reloaded-II\Loader\X86\**                        vendored loader 1.30.3
        <game>\Reloaded-II\Mods\reloaded.sharedlib.hooks\**      vendored 1.16.3
        <game>\Reloaded-II\Mods\chrono.trigger.accessibility\**  packaged mod
        <game>\Reloaded-II\Apps\chrono trigger.exe\AppConfig.json
        <game>\Accessibility\Launcher\*.exe                      native launcher + installer

    There is no external Reloaded-II installation, no GUI, no Ultimate ASI Loader
    and no machine-wide environment variable. A normal Steam launch runs the
    launcher through an IFEO redirect, which injects the loader.

    Deployment is transactional: existing directories are moved aside, and if any
    step fails they are moved back before the script rethrows.

    The game executable is never written to. Its hash is checked before and after.

.EXAMPLE
    .\tools\Deploy-Mod.ps1

.EXAMPLE
    .\tools\Deploy-Mod.ps1 -SkipIfeo    # files only, register the redirect later
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [string]$GameRoot,
    [string]$PackageDirectory,
    [switch]$SkipNativeBuild,
    [switch]$SkipIfeo,

    # Test-only hook. Forces a failure at a chosen stage so the rollback path can
    # be exercised by the deployment tests. Never used in a real deployment.
    [ValidateSet('None', 'AfterMoveAside', 'AfterPayloadCopy', 'BeforeVerification')]
    [string]$FailureInjectionPoint = 'None'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'ModPaths.psm1') -Force

$repo = Get-ModRepositoryRoot
$game = Get-GameRoot -GameRoot $GameRoot
if (-not $PackageDirectory) {
    $PackageDirectory = Join-Path $repo 'artifacts\package\chrono.trigger.accessibility'
}

$vendored     = Get-VendoredReloadedRoot -RepositoryRoot $repo
$reloaded     = Get-PortableReloadedRoot -GameRoot $game
$launcherDir  = Get-LauncherRoot -GameRoot $game
$gameExe      = Join-Path $game 'Chrono Trigger.exe'
$nativeOutput = Join-Path $repo '.build\native'
$backupRoot   = Join-Path (Get-AccessibilityRoot -GameRoot $game) ('Backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))

Write-Host "Repository : $repo"
Write-Host "Game root  : $game"
Write-Host ''

# ------------------------------------------------------------ preconditions ---

function Assert-Exists {
    param([string]$Path, [string]$What)
    if (-not (Test-Path -LiteralPath $Path)) { throw "$What not found: $Path" }
}

$expectedHash = Get-SupportedGameSha256
$beforeHash = (Get-FileHash $gameExe -Algorithm SHA256).Hash
if ($beforeHash -ne $expectedHash) {
    throw ("This is not the supported Chrono Trigger build.`n" +
           "  expected $expectedHash`n  found    $beforeHash`n" +
           'Verify the game files in Steam. The mod refuses to hook an unknown build.')
}
Write-Host "Game executable verified: $beforeHash"

Assert-Exists (Join-Path $vendored 'Loader\X86') 'Vendored Reloaded loader payload'
Assert-Exists (Join-Path $vendored 'mods\reloaded.sharedlib.hooks') 'Vendored shared hooks mod'
Assert-Exists $PackageDirectory 'Packaged mod'
Assert-Exists (Join-Path $PackageDirectory 'ChronoTriggerAccessibility.Mod.dll') 'Packaged mod DLL'

$runtime = Test-X86Runtime
if (-not $runtime.Satisfied) {
    throw ("No x86 .NET 9 runtime found under $($runtime.Root).`n" +
           'Install the 32-bit .NET 9 Desktop Runtime; any 9.0.x revision works.')
}
Write-Host "x86 .NET 9 runtime: $($runtime.Found -join ', ')"

# ------------------------------------------------------------- native build ---

if (-not $SkipNativeBuild) {
    Write-Host ''
    Write-Host 'Building native launcher and installer...'
    & (Join-Path $PSScriptRoot 'Build-Native.ps1') -Configuration Release
    if ($LASTEXITCODE -ne 0) { throw "Build-Native.ps1 failed with exit code $LASTEXITCODE." }
}
foreach ($n in 'ChronoTriggerAccessibility.Launcher.exe', 'ChronoTriggerAccessibility.Installer.exe') {
    Assert-Exists (Join-Path $nativeOutput $n) "Native binary $n (run tools\Build-Native.ps1)"
}

if (-not $PSCmdlet.ShouldProcess($game, 'Deploy the accessibility mod')) { return }

# ---------------------------------------------------------------- transaction ---

$moved = [System.Collections.Generic.List[object]]::new()

function Move-Aside {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $dest = Join-Path $backupRoot (Split-Path $Path -Leaf)
    New-Item -ItemType Directory -Path (Split-Path $dest -Parent) -Force | Out-Null
    Move-Item -LiteralPath $Path -Destination $dest -Force
    $moved.Add([pscustomobject]@{ Original = $Path; Backup = $dest })
    Write-Host "  moved aside: $Path"
}

function Undo-Moves {
    foreach ($m in ($moved | Sort-Object -Descending { $_.Original.Length })) {
        try {
            if (Test-Path -LiteralPath $m.Original) { Remove-Item $m.Original -Recurse -Force }
            Move-Item -LiteralPath $m.Backup -Destination $m.Original -Force
            Write-Host "  restored: $($m.Original)" -ForegroundColor Yellow
        } catch {
            Write-Host "  COULD NOT RESTORE $($m.Original): $($_.Exception.Message)" -ForegroundColor Red
            Write-Host "  the previous copy is still at $($m.Backup)" -ForegroundColor Red
        }
    }
}

try {
    Write-Host ''
    Write-Host 'Staging...'
    Move-Aside (Join-Path $reloaded 'Loader')
    Move-Aside (Join-Path $reloaded 'Mods\reloaded.sharedlib.hooks')
    Move-Aside (Join-Path $reloaded 'Mods\chrono.trigger.accessibility')
    Move-Aside $launcherDir

    foreach ($d in @(
        (Join-Path $reloaded 'Loader\X86')
        (Join-Path $reloaded 'Mods')
        (Join-Path $reloaded 'Apps\chrono trigger.exe')
        (Join-Path $reloaded 'Plugins')
        (Join-Path $reloaded 'User\Mods')
        (Join-Path $reloaded 'User\Misc')
        $launcherDir
    )) { New-Item -ItemType Directory -Path $d -Force | Out-Null }

    if ($FailureInjectionPoint -eq 'AfterMoveAside') {
        throw 'Injected failure: AfterMoveAside'
    }

    Write-Host 'Copying the Reloaded loader payload...'
    Copy-Item (Join-Path $vendored 'Loader\X86\*') (Join-Path $reloaded 'Loader\X86') -Recurse -Force

    Write-Host 'Copying reloaded.sharedlib.hooks...'
    Copy-Item (Join-Path $vendored 'mods\reloaded.sharedlib.hooks') (Join-Path $reloaded 'Mods') -Recurse -Force

    Write-Host 'Copying the accessibility mod...'
    Copy-Item $PackageDirectory (Join-Path $reloaded 'Mods') -Recurse -Force

    Write-Host 'Copying the native launcher and installer...'
    Copy-Item (Join-Path $nativeOutput 'ChronoTriggerAccessibility.Launcher.exe')  $launcherDir -Force
    Copy-Item (Join-Path $nativeOutput 'ChronoTriggerAccessibility.Installer.exe') $launcherDir -Force

    if ($FailureInjectionPoint -eq 'AfterPayloadCopy') {
        throw 'Injected failure: AfterPayloadCopy'
    }

    # AppConfig is also written by the launcher at every start, so this copy only
    # has to be good enough for verification and for a first launch.
    Write-Host 'Writing AppConfig.json...'
    $appConfig = [ordered]@{
        AppId                    = 'chrono trigger.exe'
        AppName                  = 'Chrono Trigger'
        AppLocation              = $gameExe
        AppArguments             = ''
        AppIcon                  = ''
        AutoInject               = $false
        EnabledMods              = @('reloaded.sharedlib.hooks', 'chrono.trigger.accessibility')
        WorkingDirectory         = $game
        PluginData               = @{}
        SortedMods               = @('reloaded.sharedlib.hooks', 'chrono.trigger.accessibility')
        PreserveDisabledModOrder = $true
        DontInject               = $false
        IsMsStore                = $false
    }
    $appConfigPath = Join-Path $reloaded 'Apps\chrono trigger.exe\AppConfig.json'
    [System.IO.File]::WriteAllText($appConfigPath,
        ($appConfig | ConvertTo-Json -Depth 5),
        (New-Object System.Text.UTF8Encoding($false)))

    # The launcher injects Reloaded itself now. Leaving the ASI chain in place
    # would inject it a second time. Only remove the exact reviewed files.
    Write-Host 'Removing the superseded ASI injection chain...'
    $asiGuards = @{
        'winmm.dll' = 'A51C630B2EA3D78AD55A330EA64D510C8C0737F620BE65AD7503B61840D59E37'
    }
    foreach ($name in $asiGuards.Keys) {
        $p = Join-Path $game $name
        if (-not (Test-Path -LiteralPath $p)) { continue }
        if ((Get-FileHash $p -Algorithm SHA256).Hash -eq $asiGuards[$name]) {
            Copy-Item $p (Join-Path $backupRoot $name) -Force
            Remove-Item $p -Force
            Write-Host "  removed $name (backup in $backupRoot)"
        } else {
            Write-Warning "  $name is not the file this mod installed; leaving it alone."
        }
    }
    # Any .asi here is a Reloaded bootstrapper copy this mod placed; it has no
    # other purpose in this folder.
    $asi = Join-Path $game 'Reloaded.Mod.Loader.Bootstrapper.asi'
    if (Test-Path -LiteralPath $asi) {
        Copy-Item $asi (Join-Path $backupRoot 'Reloaded.Mod.Loader.Bootstrapper.asi') -Force
        Remove-Item $asi -Force
        Write-Host "  removed Reloaded.Mod.Loader.Bootstrapper.asi (backup in $backupRoot)"
    }

    # Never let deployment touch the game binary.
    $afterHash = (Get-FileHash $gameExe -Algorithm SHA256).Hash
    if ($afterHash -ne $beforeHash) {
        throw "FATAL: the game executable changed during deployment ($beforeHash -> $afterHash)."
    }

    if ($FailureInjectionPoint -eq 'BeforeVerification') {
        throw 'Injected failure: BeforeVerification'
    }

    if (-not $SkipIfeo) {
        Write-Host ''
        Write-Host 'Registering the launch redirect. Approve the Windows security prompt,'
        Write-Host 'then choose Yes in the installer dialog.'
        $installer = Join-Path $launcherDir 'ChronoTriggerAccessibility.Installer.exe'
        $proc = Start-Process -FilePath $installer -PassThru -Wait
        if ($proc.ExitCode -ne 0) {
            Write-Warning ("The installer exited with code $($proc.ExitCode). The files are " +
                           'deployed; re-run the installer to finish registering the redirect.')
        }
    } else {
        Write-Host ''
        Write-Host 'Skipped IFEO registration (-SkipIfeo). Run this to finish:'
        Write-Host "  & '$launcherDir\ChronoTriggerAccessibility.Installer.exe'"
    }
} catch {
    Write-Host ''
    Write-Host "Deployment failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Rolling back...' -ForegroundColor Yellow
    Undo-Moves
    throw
}

Write-Host ''
Write-Host 'Verifying...'
& (Join-Path $PSScriptRoot 'Verify-Deployment.ps1') -GameRoot $game
if ($LASTEXITCODE -ne 0) {
    Write-Host ''
    Write-Host 'Verification failed after deployment. The previous copy is preserved at:' -ForegroundColor Red
    Write-Host "  $backupRoot" -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host 'Deployed. Start Chrono Trigger from Steam as usual; start your screen reader first.'
if ($moved.Count -gt 0) { Write-Host "Previous copy kept at: $backupRoot" }
