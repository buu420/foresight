# Self-contained tests: fake registry objects only; never opens real HKLM.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
. (Join-Path $repo 'tools\release\Setup-Foresight.ps1')
. (Join-Path $repo 'tools\release\Remove-LegacyRegistration.ps1')
. (Join-Path $repo 'tools\amm\Manager-PreInstall.ps1')
. (Join-Path $repo 'tools\Package-Amm.ps1')

function Open-ForesightLegacyBaseKey { throw 'TEST SAFETY: real HKLM access is forbidden.' }
function Start-Process { throw 'TEST SAFETY: starting an elevated process is forbidden.' }
$global:ForesightTestGameRunning = $false
function Get-Process { param([string]$Name) if ($global:ForesightTestGameRunning) { [pscustomobject]@{ ProcessName = $Name } } }

Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using Microsoft.Win32;
public sealed class ForesightFakeKey {
    public readonly Dictionary<string, object> Values = new Dictionary<string, object>();
    public readonly List<string> Deleted = new List<string>();
    public object GetValue(string name, object fallback, RegistryValueOptions options) {
        object value;
        return Values.TryGetValue(name, out value) ? value : fallback;
    }
    public void DeleteValue(string name, bool throwOnMissing) { Deleted.Add(name); Values.Remove(name); }
    public void Dispose() { }
}
public sealed class ForesightFakeBase {
    public ForesightFakeKey Key;
    public int WriteOpens;
    public bool ChangeOnWrite;
    public ForesightFakeKey OpenSubKey(string path, bool writable) {
        if (writable) {
            WriteOpens++;
            if (ChangeOnWrite && Key != null) Key.Values["Debugger"] = "foreign-during-migration";
        }
        return Key;
    }
    public void Dispose() { }
}
"@

$script:Passed = 0
function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Assert-Throws {
    param([scriptblock]$Action, [string]$Pattern)
    $message = $null
    try { & $Action | Out-Null } catch { $message = $_.Exception.Message }
    if ($null -eq $message -or $message -notmatch $Pattern) { throw "Expected failure '$Pattern', got '$message'." }
}
function Test-Case {
    param([string]$Name, [scriptblock]$Action)
    & $Action
    $script:Passed++
    Write-Output "PASS $Name"
}
function New-FakeBase {
    param([AllowNull()][object]$Debugger, [AllowNull()][object]$Owner)
    $base = [ForesightFakeBase]::new()
    $base.Key = [ForesightFakeKey]::new()
    if ($null -ne $Debugger) { $base.Key.Values['Debugger'] = $Debugger }
    if ($null -ne $Owner) { $base.Key.Values['ChronoTriggerAccessibilityDebuggerOwner'] = $Owner }
    $base.Key.Values['GlobalFlag'] = 'preserve-me'
    return $base
}
$owner = '"C:\A game with spaces\Accessibility\Launcher\ChronoTriggerAccessibility.Launcher.exe"'
$factory = { param($view) $script:FakeViews[$view.ToString()] }
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('Foresight-release-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
function New-Payload {
    param([string]$Name)
    $root = Join-Path $testRoot $Name
    New-Item -ItemType Directory -Path $root | Out-Null
    foreach ($name in @('winmm.dll','Accessibility\Bootstrap\Foresight.Bootstrap.exe','Accessibility\Foresight\Setup-Foresight.ps1','Accessibility\Foresight\Remove-LegacyRegistration.ps1','Reloaded-II\Mods\chrono.trigger.accessibility\ModConfig.json')) {
        $path = Join-Path $root $name
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        [IO.File]::WriteAllText($path, "fixture $name")
    }
    $lines = @(Get-ChildItem -LiteralPath $root -Recurse -File | ForEach-Object {
        (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash + '  ' + $_.FullName.Substring($root.Length + 1).Replace('\','/')
    })
    [IO.File]::WriteAllLines((Join-Path $root 'Foresight-SHA256SUMS.txt'), $lines)
    return $root
}
try {
    Test-Case 'ownership requires exact owner and quoted absolute legacy launcher' {
        Assert-True (Test-ForesightLegacyOwnership $owner $owner) 'Expected owned legacy launcher.'
        $unc = '"\\server\share\Game\Accessibility\Launcher\ChronoTriggerAccessibility.Launcher.exe"'
        Assert-True (Test-ForesightLegacyOwnership $unc $unc) 'Expected UNC launcher ownership.'
        foreach ($bad in @($owner.Trim('"'), ($owner + ' --argument'), '"Accessibility\Launcher\ChronoTriggerAccessibility.Launcher.exe"', '"C:\Other.exe"', '"C:\Game\Accessibility\Bootstrap\Foresight.Bootstrap.exe"')) {
            Assert-True (-not (Test-ForesightLegacyOwnership $bad $bad)) "Unexpected ownership: $bad"
        }
        Assert-True (-not (Test-ForesightLegacyOwnership $owner '"C:\Other.exe"')) 'Mismatched owner was accepted.'
        Assert-True (-not (Test-ForesightLegacyOwnership $null $owner)) 'Orphan owner was accepted.'
        Assert-True (-not (Test-ForesightLegacyOwnership $owner $null)) 'Missing owner was accepted.'
    }
    Test-Case 'clean install scan opens both views read-only and creates nothing' {
        $script:FakeViews = @{ Registry32 = (New-FakeBase $null $null); Registry64 = (New-FakeBase $null $null) }
        $states = @(Get-ForesightLegacyRegistrationState -BaseKeyFactory $factory)
        Assert-True ($states.Count -eq 2) 'Both views were not scanned.'
        Remove-ForesightLegacyRegistration -BaseKeyFactory $factory
        foreach ($base in $script:FakeViews.Values) {
            Assert-True ($base.WriteOpens -eq 0 -and $base.Key.Deleted.Count -eq 0) 'Clean state triggered a write.'
        }
    }
    Test-Case 'owned migration removes exact pair in both views and preserves unrelated values' {
        $script:FakeViews = @{ Registry32 = (New-FakeBase $owner $owner); Registry64 = (New-FakeBase $owner $owner) }
        Remove-ForesightLegacyRegistration -BaseKeyFactory $factory -BackupDirectory (Join-Path $testRoot 'registry-backup')
        foreach ($base in $script:FakeViews.Values) {
            Assert-True ($base.Key.Deleted.Count -eq 2 -and $base.Key.Values.Count -eq 1 -and $base.Key.Values['GlobalFlag'] -eq 'preserve-me') 'Removed an unrelated value or retained owned registration.'
        }
        Assert-True (@(Get-ChildItem -LiteralPath (Join-Path $testRoot 'registry-backup') -Filter '*.json').Count -eq 1) 'Missing migration backup.'
    }
    Test-Case 'shared registry views tolerate the pair already removed by the other view' {
        $shared = New-FakeBase $owner $owner
        $script:FakeViews = @{ Registry32 = $shared; Registry64 = $shared }
        Remove-ForesightLegacyRegistration -BaseKeyFactory $factory
        Assert-True ($shared.Key.Deleted.Count -eq 2) 'Shared views deleted the pair more than once.'
    }
    Test-Case 'foreign debugger blocks mutation in either view' {
        $script:FakeViews = @{ Registry32 = (New-FakeBase $owner $owner); Registry64 = (New-FakeBase 'foreign' $owner) }
        Assert-Throws { Remove-ForesightLegacyRegistration -BaseKeyFactory $factory } 'different tool'
        foreach ($base in $script:FakeViews.Values) { Assert-True ($base.WriteOpens -eq 0) 'A mixed foreign state was partially changed.' }
    }
    Test-Case 'orphan owner remains untouched' {
        $script:FakeViews = @{ Registry32 = (New-FakeBase $null $owner); Registry64 = (New-FakeBase $null $null) }
        Remove-ForesightLegacyRegistration -BaseKeyFactory $factory
        Assert-True ($script:FakeViews.Registry32.Key.Values['ChronoTriggerAccessibilityDebuggerOwner'] -eq $owner) 'Orphan owner was removed.'
    }
    Test-Case 'ownership is checked again immediately before deletion' {
        $changed = New-FakeBase $owner $owner
        $changed.ChangeOnWrite = $true
        $script:FakeViews = @{ Registry32 = $changed; Registry64 = (New-FakeBase $null $null) }
        Assert-Throws { Remove-ForesightLegacyRegistration -BaseKeyFactory $factory } 'changed during migration'
        Assert-True ($changed.Key.Deleted.Count -eq 0) 'Changed foreign values were removed.'
    }
    Test-Case 'package verification validates every checksum and required loader files' {
        $root = New-Payload 'valid'
        Assert-True (@(Assert-ForesightPackage -GameRoot $root).Count -eq 5) 'Complete package did not verify.'
        [IO.File]::WriteAllText((Join-Path $root 'winmm.dll'), 'changed')
        Assert-Throws { Assert-ForesightPackage -GameRoot $root } 'Missing or changed'
    }
    Test-Case 'manifest rejects traversal absolute ADS duplicate empty and malformed entries' {
        $root = New-Payload 'malformed'
        $manifest = Join-Path $root 'Foresight-SHA256SUMS.txt'
        $hash = 'A' * 64
        foreach ($bad in @('../outside','Accessibility/../outside','C:/outside','//server/share/file','winmm.dll:stream','Accessibility/Foresight/file.','Chrono Trigger.exe','Accessibility/Foresight/audio/voice.wav','Reloaded-II/Mods/chrono.trigger.accessibility/save.sav','Accessibility/Foresight/backups/original.dll')) {
            [IO.File]::WriteAllText($manifest, "$hash  $bad" + [Environment]::NewLine)
            Assert-Throws { Get-ForesightPackageManifest -GameRoot $root } 'Unsafe|owned'
        }
        [IO.File]::WriteAllLines($manifest, @("$hash  winmm.dll", "$hash  WINMM.DLL"))
        Assert-Throws { Get-ForesightPackageManifest -GameRoot $root } 'Duplicate'
        [IO.File]::WriteAllText($manifest, '')
        Assert-Throws { Get-ForesightPackageManifest -GameRoot $root } 'empty'
        [IO.File]::WriteAllText($manifest, 'bad')
        Assert-Throws { Get-ForesightPackageManifest -GameRoot $root } 'Invalid'
    }
    Test-Case 'uninstall removes listed unchanged files only and preserves user data and changed files' {
        $root = New-Payload 'uninstall'
        foreach ($name in @('save.sav','description.mp3','backup.dll','Chrono Trigger.exe','Reloaded-II\Mods\chrono.trigger.accessibility\user-options.json')) {
            [IO.File]::WriteAllText((Join-Path $root $name), 'user data')
        }
        [IO.File]::WriteAllText((Join-Path $root 'Accessibility\Bootstrap\Foresight.Bootstrap.exe'), 'modified')
        Remove-ForesightPackage -GameRoot $root
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $root 'winmm.dll'))) 'Proxy still installed.'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $root 'Accessibility\Foresight\Setup-Foresight.ps1'))) 'Setup was not removed last.'
        foreach ($name in @('save.sav','description.mp3','backup.dll','Chrono Trigger.exe','Reloaded-II\Mods\chrono.trigger.accessibility\user-options.json','Accessibility\Bootstrap\Foresight.Bootstrap.exe','Foresight-SHA256SUMS.txt')) {
            Assert-True (Test-Path -LiteralPath (Join-Path $root $name)) "Lost preserved file: $name"
        }
    }
    Test-Case 'bad trailing manifest entry prevents all uninstall removal' {
        $root = New-Payload 'invalid-uninstall'
        [IO.File]::AppendAllText((Join-Path $root 'Foresight-SHA256SUMS.txt'), ('A' * 64) + '  ../outside' + [Environment]::NewLine)
        Assert-Throws { Remove-ForesightPackage -GameRoot $root } 'Unsafe'
        Assert-True (Test-Path -LiteralPath (Join-Path $root 'winmm.dll')) 'Deleted a file before validating full manifest.'
    }
    Test-Case 'junction in a listed package path is rejected before uninstall' {
        $root = New-Payload 'junction'
        $target = Join-Path $testRoot 'outside-junction-package'
        New-Item -ItemType Directory -Path $target | Out-Null
        [IO.File]::WriteAllText((Join-Path $target 'retain.dll'), 'external data')
        New-Item -ItemType Junction -Path (Join-Path $root 'Accessibility\Runtime') -Target $target | Out-Null
        [IO.File]::AppendAllText((Join-Path $root 'Foresight-SHA256SUMS.txt'), ('A' * 64) + '  Accessibility/Runtime/retain.dll' + [Environment]::NewLine)
        Assert-Throws { Remove-ForesightPackage -GameRoot $root } 'link or junction'
        Assert-True (Test-Path -LiteralPath (Join-Path $root 'winmm.dll')) 'Removed proxy before rejecting junction.'
        Assert-True (Test-Path -LiteralPath (Join-Path $target 'retain.dll')) 'Removed external junction target.'
    }
    Test-Case 'standalone uninstall completes after removing its own executing script' {
        $root = New-Payload 'self-removal'
        $setup = Join-Path $root 'Accessibility\Foresight\Setup-Foresight.ps1'
        Copy-Item -LiteralPath (Join-Path $repo 'tools\release\Setup-Foresight.ps1') -Destination $setup -Force
        $lines = @(Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object Name -ne 'Foresight-SHA256SUMS.txt' | ForEach-Object {
            (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash + '  ' + $_.FullName.Substring($root.Length + 1).Replace('\','/')
        })
        [IO.File]::WriteAllLines((Join-Path $root 'Foresight-SHA256SUMS.txt'), $lines)
        & $setup -Mode Uninstall
        Assert-True ($LASTEXITCODE -eq 0 -and -not (Test-Path -LiteralPath $setup)) 'Executing uninstall did not remove itself successfully.'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $root 'winmm.dll'))) 'Executing uninstall retained the proxy.'
    }
    Test-Case 'running game blocks uninstall and install guards' {
        $root = New-Payload 'running'
        $global:ForesightTestGameRunning = $true
        try {
            Assert-Throws { Remove-ForesightPackage -GameRoot $root } 'Close Chrono Trigger'
            Assert-Throws { Assert-ForesightGameClosed } 'Close Chrono Trigger'
        } finally { $global:ForesightTestGameRunning = $false }
        Assert-True (Test-Path -LiteralPath (Join-Path $root 'winmm.dll')) 'Running-game uninstall deleted a file.'
    }
    Test-Case 'unsupported executable is rejected' {
        $root = New-Payload 'unsupported'
        [IO.File]::WriteAllText((Join-Path $root 'Chrono Trigger.exe'), 'unsupported')
        Assert-Throws { Assert-ForesightGame -GameRoot $root } 'supported Steam build'
    }
    Test-Case 'manager permits intact prior Foresight package and rejects changed or unowned proxy' {
        $root = New-Payload 'manager'
        Assert-ForesightManagerLoader -GameRoot $root
        [IO.File]::WriteAllText((Join-Path $root 'winmm.dll'), 'foreign')
        Assert-Throws { Assert-ForesightManagerLoader -GameRoot $root } 'unrecognized or changed'
        Assert-True ([IO.File]::ReadAllText((Join-Path $root 'winmm.dll')) -eq 'foreign') 'Foreign proxy was modified.'
        [IO.File]::Delete((Join-Path $root 'Foresight-SHA256SUMS.txt'))
        Assert-Throws { Assert-ForesightManagerLoader -GameRoot $root } 'unrecognized or changed'
    }
    Test-Case 'manager rejects old ASI without deleting it' {
        $root = New-Payload 'old-asi'
        [IO.File]::WriteAllText((Join-Path $root 'Reloaded.Mod.Loader.Bootstrapper.asi'), 'old')
        Assert-Throws { Assert-ForesightManagerLoader -GameRoot $root } 'old ASI'
        Assert-True (Test-Path -LiteralPath (Join-Path $root 'Reloaded.Mod.Loader.Bootstrapper.asi')) 'ASI was deleted.'
    }
    Test-Case 'AMM contract is preinstall only without unconditional elevation or uninstall hook' {
        $game = [pscustomobject]@{
            gameId = 'chrono-trigger'; defaultPostInstall = $null; defaultPostUninstall = $null
            defaultPreInstall = [pscustomobject]@{
                executable='files/Manager-PreInstall.ps1'; needsAdmin=$false; failureFatal=$true
                installToGameFolder=$false; runFromGameFolder=$false; runOnUpdate=$true
                what='Validate and migrate old Foresight registration'; why='Normal proxy launch'; modifies='Only Foresight-owned legacy values if present'
            }
        }
        $catalog = [pscustomobject]@{ pluginId='buu420'; games=@($game) }
        Assert-ForesightAmmCatalog -Catalog $catalog
        $game.defaultPostUninstall = [pscustomobject]@{ executable='uninstall.ps1' }
        Assert-Throws { Assert-ForesightAmmCatalog -Catalog $catalog } 'null post-install and post-uninstall'
        $game.defaultPostUninstall = $null
        $game.defaultPreInstall.needsAdmin = $true
        Assert-Throws { Assert-ForesightAmmCatalog -Catalog $catalog } 'no unconditional elevation'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $repo 'tools/amm/Manager-Uninstall.ps1'))) 'Obsolete uninstall wrapper remains.'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $repo 'tools/amm/Manager-PostInstall.ps1'))) 'Obsolete post-install wrapper remains.'
    }
    Write-Output "$script:Passed Foresight release tests passed."
} finally {
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $resolvedTest = [IO.Path]::GetFullPath($testRoot)
    if (-not $resolvedTest.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolvedTest) -notlike 'Foresight-release-tests-*') { throw 'Refusing unsafe test-directory cleanup.' }
    Remove-Item -LiteralPath $resolvedTest -Recurse -Force
}
