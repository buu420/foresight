[CmdletBinding()]
param([ValidateSet('Install','Uninstall','Verify')][string]$Mode = 'Install')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
try {
    # PowerShell 7 can pass its module paths to Windows PowerShell. Load the
    # matching built-in Utility module explicitly so Get-FileHash stays usable.
    Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1') -ErrorAction Stop
    $gameRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
    $installer = Join-Path $gameRoot 'Accessibility\Launcher\ChronoTriggerAccessibility.Installer.exe'
    if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) { throw 'The Foresight installer is missing. Recopy the complete beta ZIP.' }
    if ($Mode -ne 'Uninstall') {
        $gameExe = Join-Path $gameRoot 'Chrono Trigger.exe'
        if (-not (Test-Path -LiteralPath $gameExe -PathType Leaf)) { throw 'Copy the ZIP contents beside Chrono Trigger.exe before running this command.' }
        if ((Get-FileHash -LiteralPath $gameExe -Algorithm SHA256).Hash -ne '8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7') {
            throw 'This game executable is not the supported Steam build. See Foresight-README.md.'
        }
        $prefix = $gameRoot.TrimEnd('\') + '\'
        foreach ($line in Get-Content -LiteralPath (Join-Path $gameRoot 'Foresight-SHA256SUMS.txt')) {
            if ($line -notmatch '^([A-Fa-f0-9]{64})  (.+)$') { throw 'Invalid Foresight checksum manifest.' }
            $expected = $Matches[1]
            $path = [IO.Path]::GetFullPath((Join-Path $gameRoot $Matches[2]))
            if (-not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'A package path escapes the game folder.' }
            if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $expected) { throw "Missing or changed package file: $($Matches[2]). Recopy the complete ZIP." }
        }
        if ($Mode -eq 'Verify') { Write-Output 'Foresight payload and supported game verified.'; exit 0 }
        foreach ($oldLoader in @('winmm.dll','Reloaded.Mod.Loader.Bootstrapper.asi')) {
            if (Test-Path -LiteralPath (Join-Path $gameRoot $oldLoader)) {
                throw "An existing loader ($oldLoader) needs migration before installing Foresight. See the README; it was not changed."
            }
        }
    }
    if (Get-Process -Name 'Chrono Trigger' -ErrorAction SilentlyContinue) { throw 'Close Chrono Trigger before installing or uninstalling Foresight.' }
    $start = @{ FilePath = $installer; Verb = 'RunAs'; Wait = $true; PassThru = $true }
    if ($Mode -eq 'Uninstall') { $start.ArgumentList = '/uninstall' }
    # This is the user-requested interactive installer, including accessible dialogs.
    $process = Start-Process @start
    if ($process.ExitCode -ne 0) { throw "Foresight $Mode did not complete (exit $($process.ExitCode)). Files were left in place; see the installer log." }
    Write-Output "Foresight $Mode completed."
    exit 0
} catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
