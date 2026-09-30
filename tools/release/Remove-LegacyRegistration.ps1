[CmdletBinding()]
param([Alias('Elevated')][switch]$LegacyElevated, [Alias('GameRoot')][string]$LegacyGameRoot, [Alias('BackupDirectory')][string]$LegacyBackupDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:ForesightLegacyHelperPath = $PSCommandPath
$script:ForesightLegacyKeyPath = 'SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\Chrono Trigger.exe'
$script:ForesightLegacyOwnerName = 'ChronoTriggerAccessibilityDebuggerOwner'

function Test-ForesightLegacyOwnership {
    param([AllowNull()][object]$Debugger, [AllowNull()][object]$Owner)
    if ($Debugger -isnot [string] -or $Owner -isnot [string] -or -not [string]::Equals($Debugger, $Owner, [StringComparison]::OrdinalIgnoreCase)) { return $false }
    if ($Debugger -notmatch '^"([^"\r\n]+)"$') { return $false }
    $launcher = $Matches[1]
    if ($launcher -notmatch '^(?:[A-Za-z]:\\|\\\\[^\\]+\\[^\\]+\\)') { return $false }
    return $launcher.EndsWith('\Accessibility\Launcher\ChronoTriggerAccessibility.Launcher.exe', [StringComparison]::OrdinalIgnoreCase)
}

function Open-ForesightLegacyBaseKey {
    param([Microsoft.Win32.RegistryView]$View)
    return [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $View)
}

function Get-ForesightLegacyRegistrationState {
    # Tests inject isolated fake keys. Production always uses HKLM.
    param([scriptblock]$BaseKeyFactory = { param($view) Open-ForesightLegacyBaseKey -View $view })
    foreach ($view in @([Microsoft.Win32.RegistryView]::Registry32, [Microsoft.Win32.RegistryView]::Registry64)) {
        $base = & $BaseKeyFactory $view
        $key = $null
        try {
            $key = $base.OpenSubKey($script:ForesightLegacyKeyPath, $false)
            $debugger = $null
            $owner = $null
            if ($null -ne $key) {
                $debugger = $key.GetValue('Debugger', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                $owner = $key.GetValue($script:ForesightLegacyOwnerName, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            }
            $owned = Test-ForesightLegacyOwnership -Debugger $debugger -Owner $owner
            [pscustomobject]@{ View = $view; KeyPresent = ($null -ne $key); Debugger = $debugger; Owner = $owner; Owned = $owned; ForeignDebugger = ($null -ne $debugger -and -not $owned) }
        } finally {
            if ($null -ne $key) { $key.Dispose() }
            $base.Dispose()
        }
    }
}

function Remove-ForesightLegacyRegistration {
    param([scriptblock]$BaseKeyFactory = { param($view) Open-ForesightLegacyBaseKey -View $view }, [string]$BackupDirectory)
    $states = @(Get-ForesightLegacyRegistrationState -BaseKeyFactory $BaseKeyFactory)
    if (@($states | Where-Object ForeignDebugger).Count) { throw 'A different tool owns a Chrono Trigger launch registration. It was preserved; resolve it before enabling Foresight.' }
    if ($BackupDirectory -and @($states | Where-Object Owned).Count) {
        New-Item -ItemType Directory -Path $BackupDirectory -Force | Out-Null
        $backupPath = Join-Path $BackupDirectory ('Foresight-legacy-registration-' + [Guid]::NewGuid().ToString('N') + '.json')
        $states | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $backupPath -Encoding UTF8
    }
    foreach ($state in $states) {
        if (-not $state.Owned) { continue }
        $base = & $BaseKeyFactory $state.View
        $key = $null
        try {
            $key = $base.OpenSubKey($script:ForesightLegacyKeyPath, $true)
            if ($null -eq $key) { continue }
            $debugger = $key.GetValue('Debugger', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            $owner = $key.GetValue($script:ForesightLegacyOwnerName, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            # IFEO is shared between registry views on some Windows versions.
            if ($null -eq $debugger -and $null -eq $owner) { continue }
            if (-not (Test-ForesightLegacyOwnership -Debugger $debugger -Owner $owner)) { throw 'The launch registration changed during migration. Its current values were preserved.' }
            # Delete the matched pair only; keep the key and every unrelated value.
            $key.DeleteValue('Debugger', $false)
            $key.DeleteValue($script:ForesightLegacyOwnerName, $false)
        } finally {
            if ($null -ne $key) { $key.Dispose() }
            $base.Dispose()
        }
    }
    if (@(Get-ForesightLegacyRegistrationState -BaseKeyFactory $BaseKeyFactory | Where-Object { $null -ne $_.Debugger }).Count) { throw 'A launch registration remains after migration.' }
}

function Invoke-ForesightLegacyMigration {
    param([string]$GameRoot, [string]$BackupDirectory)
    if (Get-Process -Name 'Chrono Trigger' -ErrorAction SilentlyContinue) { throw 'Close Chrono Trigger before migrating the old Foresight installation.' }
    $states = @(Get-ForesightLegacyRegistrationState)
    if (@($states | Where-Object ForeignDebugger).Count) { throw 'A different tool owns a Chrono Trigger launch registration. It was preserved; resolve it before enabling Foresight.' }
    if (-not @($states | Where-Object Owned).Count) { return }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try { $admin = ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) }
    finally { $identity.Dispose() }
    if ($admin) {
        Remove-ForesightLegacyRegistration -BackupDirectory $BackupDirectory
    } else {
        Write-Output 'Removing the old Foresight launch registration requires one Windows administrator confirmation.'
        # EncodedCommand preserves spaces, apostrophes, UNC paths and metacharacters
        # without exposing a path to cmd.exe or Start-Process argument rejoining.
        $command = "& '" + $script:ForesightLegacyHelperPath.Replace("'", "''") + "' -Elevated"
        if ($BackupDirectory) { $command += " -BackupDirectory '" + [IO.Path]::GetFullPath($BackupDirectory).Replace("'", "''") + "'" }
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
        $powerShell = (Get-Process -Id $PID).Path
        $process = Start-Process -FilePath $powerShell -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-EncodedCommand',$encoded) -Verb RunAs -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "Legacy Foresight migration failed (exit $($process.ExitCode))." }
    }
    if (@(Get-ForesightLegacyRegistrationState | Where-Object { $null -ne $_.Debugger }).Count) { throw 'A launch registration remains after migration; Foresight installation did not complete.' }
    Write-Output 'The old Foresight launch registration was removed. Future installs and uninstalls need no registry changes.'
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        if ($LegacyElevated) {
            if (Get-Process -Name 'Chrono Trigger' -ErrorAction SilentlyContinue) { throw 'Close Chrono Trigger before migration.' }
            Remove-ForesightLegacyRegistration -BackupDirectory $LegacyBackupDirectory
        } else { Invoke-ForesightLegacyMigration -GameRoot $LegacyGameRoot -BackupDirectory $LegacyBackupDirectory }
        exit 0
    } catch {
        Write-Error $_ -ErrorAction Continue
        exit 1
    }
}
