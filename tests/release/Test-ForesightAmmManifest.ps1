# Manifest repair tests use temporary ZIPs only; never run AMM or touch a candidate.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
. (Join-Path $repo 'tools\Package-Amm.ps1')
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$script:Passed = 0
$root = Join-Path ([IO.Path]::GetTempPath()) ('Foresight-amm-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Assert-Throws {
    param([scriptblock]$Action, [string]$Pattern)
    $message = $null
    try { & $Action | Out-Null } catch { $message = $_.Exception.Message }
    Assert-True ($null -ne $message -and $message -match $Pattern) "Expected '$Pattern'; got '$message'."
}
function Test-Case {
    param([string]$Name, [scriptblock]$Action)
    & $Action
    $script:Passed++
    Write-Output "PASS $Name"
}
function New-Manifest {
    return [pscustomobject]@{
        gameId='chrono-trigger'; pluginId='buu420'; modVersion='0.3.41'
        installActions=@(
            [pscustomobject]@{ type='copyFolder'; sourceDir='Accessibility'; targetDir='Accessibility' }
            [pscustomobject]@{ type='copyFile'; source='Foresight-README.md'; target='Foresight-README.md' }
            [pscustomobject]@{ type='copyFile'; source='Foresight-SHA256SUMS.txt'; target='Foresight-SHA256SUMS.txt' }
            [pscustomobject]@{ type='copyFile'; source='Install Foresight.cmd'; target='Install Foresight.cmd' }
            [pscustomobject]@{ type='copyFile'; source='Manager-PreInstall.ps1'; target='Manager-PreInstall.ps1' }
            [pscustomobject]@{ type='copyFolder'; sourceDir='Reloaded-II'; targetDir='Reloaded-II' }
            [pscustomobject]@{ type='copyFile'; source='Uninstall Foresight.cmd'; target='Uninstall Foresight.cmd' }
            [pscustomobject]@{ type='copyFile'; source='winmm.dll'; target='winmm.dll' }
        )
        dependencies=@([pscustomobject]@{ id='preserve-dependency'; optional=$true })
        verify=@([pscustomobject]@{ path='Chrono Trigger.exe'; sha256=('A' * 64) })
        preInstall=[pscustomobject]@{
            executable='files/Manager-PreInstall.ps1'; needsAdmin=$false; failureFatal=$true
            what='Validate game and migrate legacy Foresight'; why='Registry-free launch'
            modifies='Only matching obsolete Foresight registry values'
            installToGameFolder=$false; runOnUpdate=$true; runFromGameFolder=$false
        }
        postInstall=$null; postUninstall=$null
    }
}
function Write-Entry {
    param([IO.Compression.ZipArchive]$Archive, [string]$Name, [byte[]]$Bytes)
    $entry = $Archive.CreateEntry($Name)
    $stream = $entry.Open()
    try { $stream.Write($Bytes, 0, $Bytes.Length) } finally { $stream.Dispose() }
}
function New-Fixture {
    param([string]$Name, [object]$Manifest, [switch]$OmitHook, [switch]$DuplicateManifest)
    $path = Join-Path $root $Name
    $archive = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $json = [Text.Encoding]::UTF8.GetBytes(($Manifest | ConvertTo-Json -Depth 100))
        Write-Entry $archive 'manifest.json' $json
        if ($DuplicateManifest) { Write-Entry $archive 'manifest.json' $json }
        if (-not $OmitHook) { Write-Entry $archive 'files/Manager-PreInstall.ps1' ([Text.Encoding]::UTF8.GetBytes('staged hook')) }
        Write-Entry $archive 'files/winmm.dll' ([byte[]]@(0,1,128,255,23,42))
        Write-Entry $archive 'files/Accessibility/Bootstrap/Foresight.Bootstrap.exe' ([byte[]]@(77,90,0,255,127))
        Write-Entry $archive 'files/Reloaded-II/Mods/chrono.trigger.accessibility/ModConfig.json' ([Text.Encoding]::UTF8.GetBytes('{"fixture":true}'))
    } finally { $archive.Dispose() }
    return $path
}
function Read-Fixture {
    param([string]$Path)
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    $entries = @{}
    try {
        foreach ($entry in $archive.Entries) {
            $stream = $entry.Open()
            $memory = [IO.MemoryStream]::new()
            try {
                $stream.CopyTo($memory)
                $entries[$entry.FullName] = [Convert]::ToBase64String($memory.ToArray())
            } finally { $stream.Dispose(); $memory.Dispose() }
        }
    } finally { $archive.Dispose() }
    $manifest = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($entries['manifest.json'])) | ConvertFrom-Json
    return [pscustomobject]@{ Manifest=$manifest; Entries=$entries }
}
try {
    Test-Case 'repair removes only redundant action and preserves staging script and all payload bytes' {
        $before = New-Manifest
        $path = New-Fixture 'repair.zip' $before
        $original = Read-Fixture $path
        $result = Update-ForesightAmmManifest -ZipPath $path
        $actual = Read-Fixture $path
        Assert-True ($result.RemovedActions -eq 1 -and $result.RemainingActions -eq 7) 'Expected eight actions to become seven.'
        $before.installActions = @($before.installActions | Where-Object { $_.type -ne 'copyFile' -or $_.source -ne 'Manager-PreInstall.ps1' })
        Assert-True (($actual.Manifest | ConvertTo-Json -Depth 100 -Compress) -ceq ($before | ConvertTo-Json -Depth 100 -Compress)) 'Another manifest field or action changed.'
        Assert-True ($actual.Entries.Count -eq $original.Entries.Count) 'ZIP entries were removed or added.'
        foreach ($key in $original.Entries.Keys) {
            if ($key -ne 'manifest.json') { Assert-True ($actual.Entries[$key] -ceq $original.Entries[$key]) "Payload changed: $key" }
        }
        Assert-True ($actual.Entries.ContainsKey('files/Manager-PreInstall.ps1')) 'Staging hook was removed.'
    }
    Test-Case 'repeating repair is byte-for-byte idempotent' {
        $path = New-Fixture 'repeat.zip' (New-Manifest)
        $null = Update-ForesightAmmManifest -ZipPath $path
        $before = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        $result = Update-ForesightAmmManifest -ZipPath $path
        Assert-True ($result.RemovedActions -eq 0 -and $result.RemainingActions -eq 7) 'Repeated repair removed another action.'
        Assert-True ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $before) 'Already-correct ZIP was rewritten.'
    }
    Test-Case 'similar file names and different destinations remain unchanged' {
        $manifest = New-Manifest
        $manifest.installActions += [pscustomobject]@{ type='copyFile'; source='Manager-PreInstall.ps1.backup'; target='Manager-PreInstall.ps1.backup' }
        $manifest.installActions += [pscustomobject]@{ type='copyFile'; source='Manager-PreInstall.ps1'; target='archive/Manager-PreInstall.ps1' }
        $path = New-Fixture 'similar.zip' $manifest
        $null = Update-ForesightAmmManifest -ZipPath $path
        $actual = (Read-Fixture $path).Manifest
        Assert-True (@($actual.installActions).Count -eq 9) 'Removed more than the exact redundant action.'
        Assert-True (@($actual.installActions | Where-Object { $_.type -eq 'copyFile' -and $_.target -eq 'archive/Manager-PreInstall.ps1' }).Count -eq 1) 'Removed an unrelated target.'
    }
    Test-Case 'unexpected identity or lifecycle hook is rejected without modifying archive' {
        foreach ($kind in @('identity','post-hook','elevation')) {
            $manifest = New-Manifest
            if ($kind -eq 'identity') { $manifest.gameId='another-game' }
            if ($kind -eq 'post-hook') { $manifest.postUninstall=[pscustomobject]@{ executable='remove.ps1' } }
            if ($kind -eq 'elevation') { $manifest.preInstall.needsAdmin=$true }
            $path = New-Fixture ($kind + '.zip') $manifest
            $before = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
            Assert-Throws { Update-ForesightAmmManifest -ZipPath $path } 'chrono-trigger|post-install|elevation'
            Assert-True ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $before) 'Rejected ZIP was modified.'
        }
    }
    Test-Case 'missing hook or duplicate manifest is rejected without modification' {
        foreach ($kind in @('missing-hook','duplicate-manifest')) {
            if ($kind -eq 'missing-hook') { $path = New-Fixture ($kind + '.zip') (New-Manifest) -OmitHook }
            else { $path = New-Fixture ($kind + '.zip') (New-Manifest) -DuplicateManifest }
            $before = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
            Assert-Throws { Update-ForesightAmmManifest -ZipPath $path } 'one manifest.json and one staging'
            Assert-True ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $before) 'Rejected ZIP was modified.'
        }
    }
    Test-Case 'duplicate redundant actions are rejected without modification' {
        $manifest = New-Manifest
        $manifest.installActions += [pscustomobject]@{ type='copyFile'; source='Manager-PreInstall.ps1'; target='Manager-PreInstall.ps1' }
        $path = New-Fixture 'duplicate-action.zip' $manifest
        $before = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        Assert-Throws { Update-ForesightAmmManifest -ZipPath $path } 'duplicate staging-script copy'
        Assert-True ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $before) 'Rejected ZIP was modified.'
    }
    Write-Output "$script:Passed AMM manifest repair tests passed."
} finally {
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $resolved = [IO.Path]::GetFullPath($root)
    if (-not $resolved.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolved) -notlike 'Foresight-amm-tests-*') { throw 'Refusing unsafe fixture cleanup.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
