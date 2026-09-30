[CmdletBinding()]
param([string]$gameFolder, [string]$modFolder)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-ForesightManagerLoader {
    param([Parameter(Mandatory=$true)][string]$GameRoot)
    if (Test-Path -LiteralPath (Join-Path $GameRoot 'Reloaded.Mod.Loader.Bootstrapper.asi')) {
        throw 'An old ASI loader is present. Migrate it before installing Foresight; no loader was removed.'
    }
    $proxy = Join-Path $GameRoot 'winmm.dll'
    if (Test-Path -LiteralPath $proxy) {
        # A DLL name or a matching incoming build is not proof of ownership.
        # Verify the prior installed package and its recorded proxy checksum.
        try {
            $entries = @(Assert-ForesightPackage -GameRoot $GameRoot)
            $entry = @($entries | Where-Object RelativePath -eq 'winmm.dll')
            if ($entry.Count -ne 1 -or (Get-FileHash -LiteralPath $proxy -Algorithm SHA256).Hash -ne $entry[0].Sha256) {
                throw 'Proxy checksum mismatch.'
            }
        } catch {
            throw 'An unrecognized or changed winmm.dll is already installed. It was preserved; resolve the loader conflict before installing Foresight.'
        }
    }
}

function Invoke-ForesightManagerPreInstall {
    param([Parameter(Mandatory=$true)][string]$GameRoot, [Parameter(Mandatory=$true)][string]$PayloadRoot)
    $support = Join-Path $PayloadRoot 'Accessibility\Foresight'
    . (Join-Path $support 'Setup-Foresight.ps1')
    Assert-ForesightGameClosed
    Assert-ForesightGame -GameRoot $GameRoot
    $null = Assert-ForesightPackage -GameRoot $PayloadRoot
    Assert-ForesightManagerLoader -GameRoot $GameRoot
    . (Join-Path $support 'Remove-LegacyRegistration.ps1')
    Invoke-ForesightLegacyMigration -GameRoot $GameRoot
    Write-Output 'Foresight package, supported game, closed-process and loader checks passed. The manager can copy the files.'
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1') -ErrorAction Stop
        if ([string]::IsNullOrWhiteSpace($gameFolder)) { throw 'The manager did not provide the game folder.' }
        # AMM runs files/Manager-PreInstall.ps1 from its verified staging tree.
        Invoke-ForesightManagerPreInstall -GameRoot $gameFolder -PayloadRoot $PSScriptRoot
        exit 0
    } catch {
        Write-Error $_ -ErrorAction Continue
        exit 1
    }
}
