param([string]$CollectorPath = (Join-Path $PSScriptRoot '..\..\tools\support\Collect-Foresight-CrashReport.ps1'))
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path -LiteralPath $CollectorPath) { . $CollectorPath }
else { function Invoke-ForesightCrashCollection { throw 'The crash collector has not been implemented.' } }

$script:Passed = 0
$script:Failed = 0
function Assert-True([bool]$Value, [string]$Message) { if (-not $Value) { throw $Message } }
function Test-Case([string]$Name, [scriptblock]$Action) {
    try { & $Action; $script:Passed++; Write-Output "PASS $Name" }
    catch { $script:Failed++; Write-Output "FAIL $Name -- $($_.Exception.Message)" }
}
function Write-Fixture([string]$Path, [string]$Text) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}
function Read-ZipText([string]$Zip, [string]$Name) {
    $archive = [IO.Compression.ZipFile]::OpenRead($Zip)
    try {
        $entry = $archive.GetEntry($Name)
        Assert-True ($null -ne $entry) "ZIP is missing $Name"
        $reader = [IO.StreamReader]::new($entry.Open())
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $archive.Dispose() }
}
function New-CrashEvent([int]$Id, [string]$App, [string]$Provider = 'Application Error') {
    $event = [pscustomobject]@{
        Id = $Id; ProviderName = $Provider; TimeCreated = [datetime]::Now
        Message = "Application: $App. Access violation in libcocos2d.dll at offset 00001234."
        RawXml = "<Event><System><EventID>$Id</EventID></System><EventData><Data Name='AppName'>$App</Data><Data Name='ModuleName'>libcocos2d.dll</Data><Data Name='ExceptionCode'>c0000005</Data><Data Name='FaultingOffset'>00001234</Data></EventData></Event>"
    }
    $event | Add-Member -MemberType ScriptMethod -Name ToXml -Value { return $this.RawXml }
    return $event
}
$script:EventMode = 'matching'
$script:EventCalls = 0
function Get-WinEvent {
    [CmdletBinding()] param([hashtable]$FilterHashtable)
    $script:EventCalls++
    Assert-True ($FilterHashtable.LogName -eq 'Application') 'Wrong Windows event log queried.'
    Assert-True ($FilterHashtable.Id -contains 1000 -and $FilterHashtable.Id -contains 1001 -and $FilterHashtable.Id -contains 1026) 'Crash event types missing.'
    if ($script:EventMode -eq 'denied') { throw [UnauthorizedAccessException]::new('fixture event access denied') }
    if ($script:EventMode -eq 'none') { return }
    if ($script:EventMode -eq 'formats') {
        $wer = New-CrashEvent 1001 'Chrono Trigger.exe' 'Windows Error Reporting'
        $wer.RawXml = '<Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event"><EventData><Data Name="P1">Chrono Trigger.exe</Data><Data Name="P4">libcocos2d.dll</Data></EventData></Event>'
        $wer
        $managed = New-CrashEvent 1026 'Chrono Trigger.exe' '.NET Runtime'
        $managed.RawXml = '<Event><EventData><Data>Application: Chrono Trigger.exe</Data></EventData></Event>'
        $managed
        $other = New-CrashEvent 1000 'Other Game.exe'
        $other.Message = 'Unrelated game opened C:\Chrono Trigger.exe in diagnostic text.'
        $other
        $otherWer = New-CrashEvent 1001 'Other Game.exe' 'Windows Error Reporting'
        $otherWer.RawXml = '<Event><EventData><Data Name="P1">Other Game.exe</Data><Data Name="P4">C:\Chrono Trigger.exe</Data></EventData></Event>'
        $otherWer
        $otherManaged = New-CrashEvent 1026 'Other Game.exe' '.NET Runtime'
        $otherManaged.RawXml = '<Event><EventData><Data>Application: Other Game.exe&#10;Exception: File not found: C:\Chrono Trigger.exe</Data></EventData></Event>'
        $otherManaged
        return
    }
    New-CrashEvent 1000 'Chrono Trigger.exe'
    New-CrashEvent 1001 'Chrono Trigger.exe' 'Windows Error Reporting'
    New-CrashEvent 1026 'Chrono Trigger.exe' '.NET Runtime'
    New-CrashEvent 1000 'Other Game.exe'
}
$script:SteamFixtureRoot = $null
function Get-ItemProperty {
    [CmdletBinding()] param([string]$LiteralPath)
    if ($script:SteamFixtureRoot -and $LiteralPath -eq 'HKCU:\Software\Valve\Steam') {
        return [pscustomobject]@{ SteamPath = $script:SteamFixtureRoot }
    }
    Microsoft.PowerShell.Management\Get-ItemProperty -LiteralPath $LiteralPath -ErrorAction Stop
}
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$testRoot = Join-Path $repo ('artifacts\research\crash-collector-tests-' + [guid]::NewGuid().ToString('N'))
$game = Join-Path $testRoot 'game [literal] with spaces'
$logs = Join-Path $testRoot 'raw Reloaded logs'
$out = Join-Path $testRoot 'reports'
Write-Fixture (Join-Path $game 'Chrono Trigger.exe') 'fixture executable; never started'
Write-Fixture (Join-Path $game 'Accessibility\Logs\bootstrap.log') '[2026-10-17 23:28:23.354] Game exited with code 0xC0000005; restoring Reloaded pointer.'
Write-Fixture (Join-Path $game 'Accessibility\Logs\proxy.log') 'native ready'
Write-Fixture (Join-Path $game 'Reloaded-II\Mods\chrono.trigger.accessibility\ModConfig.json') '{"ModId":"chrono.trigger.accessibility","ModVersion":"0.3.67"}'
Write-Fixture (Join-Path $game 'save.dat') 'keep my save'
Write-Fixture (Join-Path $game 'settings.ini') 'keep my settings'
Write-Fixture (Join-Path $game 'private-recording.wav') 'do not collect voice recordings'
$gameplayName = '2026-10-18 02.26.13 ~ Chrono Trigger.txt'
$gameplay = Join-Path $logs $gameplayName
$fullText = '[21:26:14] loader' + [Environment]::NewLine + ('complete-log-content ' * 180000) + [Environment]::NewLine + '[23:28:23] END-OF-CRASH-SESSION'
Write-Fixture $gameplay $fullText
Write-Fixture (Join-Path $logs '2026-10-18 03.26.13 ~ Other Game.txt') 'do not collect other games'
Write-Fixture (Join-Path $logs 'ff7_accessibility_reloaded.log') 'private unrelated log'
$before = @{}
Get-ChildItem -LiteralPath $game -File -Recurse | ForEach-Object { $before[$_.FullName] = (Get-FileHash -LiteralPath $_.FullName).Hash }
$script:Result = $null

Test-Case 'Copies complete raw gameplay and helper logs while excluding saves and other games' {
    $script:Result = Invoke-ForesightCrashCollection -GameRoot $game -ReloadedLogDirectory $logs -OutputDirectory $out
    Assert-True (Test-Path -LiteralPath $script:Result.ArchivePath) 'No report ZIP produced.'
    $report = Read-ZipText $script:Result.ArchivePath 'report.json' | ConvertFrom-Json
    $raw = @($report.Logs | Where-Object Kind -eq 'Gameplay')
    Assert-True ($raw.Count -eq 1) 'Did not collect exactly the Chrono gameplay log.'
    Assert-True ((Read-ZipText $script:Result.ArchivePath $raw[0].ArchivePath) -ceq $fullText) 'Gameplay log was truncated or changed.'
    Assert-True (@($report.Logs | Where-Object Kind -eq 'Bootstrap').Count -eq 1) 'Missing bootstrap log.'
    Assert-True (@($report.Logs | Where-Object Kind -eq 'Proxy').Count -eq 1) 'Missing proxy log.'
    $zip = [IO.Compression.ZipFile]::OpenRead($script:Result.ArchivePath)
    try {
        Assert-True (@($zip.Entries | Where-Object FullName -match 'save|settings|recording|Other Game|ff7|\.exe$').Count -eq 0) 'Unrelated/private game files were collected.'
    } finally { $zip.Dispose() }
}
Test-Case 'Preserves raw Windows fault details and excludes other applications' {
    if ($null -eq $script:Result) { throw 'Collector did not produce a report.' }
    $report = Read-ZipText $script:Result.ArchivePath 'report.json' | ConvertFrom-Json
    Assert-True ($report.WindowsEvents.Status -eq 'collected') 'Windows status is wrong.'
    Assert-True (@($report.WindowsEvents.Records).Count -eq 3) 'Wrong game crash events collected.'
    $fault = @($report.WindowsEvents.Records | Where-Object Id -eq 1000)[0]
    Assert-True ($fault.Fields.ExceptionCode -eq 'c0000005') 'Lost exception code.'
    Assert-True ($fault.Fields.FaultingOffset -eq '00001234') 'Changed module-relative fault offset.'
    Assert-True ((Read-ZipText $script:Result.ArchivePath $fault.XmlPath) -ceq (New-CrashEvent 1000 'Chrono Trigger.exe').ToXml()) 'Raw event XML was changed.'
}
Test-Case 'Reads an open shared log without launching or stopping the game' {
    $stream = [IO.File]::Open($gameplay, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::ReadWrite)
    try {
        $r = Invoke-ForesightCrashCollection -GameRoot $game -ReloadedLogDirectory $logs -OutputDirectory $out -SkipWindowsEvents
        $report = Read-ZipText $r.ArchivePath 'report.json' | ConvertFrom-Json
        $raw = @($report.Logs | Where-Object Kind -eq 'Gameplay')[0]
        Assert-True ($raw.CapturedBytes -eq $stream.Length) 'Open log was not copied completely.'
        Assert-True ($report.WindowsEvents.Status -eq 'skipped') 'Explicit skip was not recorded.'
    } finally { $stream.Dispose() }
}
Test-Case 'Event access errors still produce logs and an explicit unavailable status' {
    $script:EventMode = 'denied'
    try {
        $r = Invoke-ForesightCrashCollection -GameRoot $game -ReloadedLogDirectory $logs -OutputDirectory $out
        $report = Read-ZipText $r.ArchivePath 'report.json' | ConvertFrom-Json
        Assert-True ($report.WindowsEvents.Status -eq 'unavailable') 'Denied event access was reported as no crashes.'
        Assert-True ($report.WindowsEvents.Error -match 'access denied') 'Event access failure was lost.'
        Assert-True (@($report.Logs).Count -eq 3) 'An event error prevented collecting the logs.'
    } finally { $script:EventMode = 'matching' }
}
Test-Case 'Missing gameplay and native logs are reported instead of silently omitted' {
    $emptyGame = Join-Path $testRoot 'empty game'
    $emptyLogs = Join-Path $testRoot 'empty logs'
    Write-Fixture (Join-Path $emptyGame 'Chrono Trigger.exe') 'fixture'
    [IO.Directory]::CreateDirectory($emptyLogs) | Out-Null
    $script:EventMode = 'none'
    try {
        $r = Invoke-ForesightCrashCollection -GameRoot $emptyGame -ReloadedLogDirectory $emptyLogs -OutputDirectory $out
        $report = Read-ZipText $r.ArchivePath 'report.json' | ConvertFrom-Json
        Assert-True (@($report.MissingSources).Count -ge 3) 'Missing log coverage was hidden.'
        Assert-True ($report.WindowsEvents.Status -eq 'no_matches') 'No matching events was not distinguished from access failure.'
    } finally { $script:EventMode = 'matching' }
}
Test-Case 'Collection leaves every source and protected file byte-identical' {
    if ($null -eq $script:Result) { throw 'No collection completed.' }
    foreach ($path in $before.Keys) {
        Assert-True ((Get-FileHash -LiteralPath $path).Hash -eq $before[$path]) "Source was changed: $path"
    }
    Assert-True ([IO.File]::ReadAllText($gameplay) -ceq $fullText) 'Raw gameplay source changed.'
}
Test-Case 'Collects native WER and managed event formats without misattributing another application' {
    $script:EventMode = 'formats'
    try {
        $r = Invoke-ForesightCrashCollection -GameRoot $game -ReloadedLogDirectory $logs -OutputDirectory $out
        $report = Read-ZipText $r.ArchivePath 'report.json' | ConvertFrom-Json
        Assert-True (@($report.WindowsEvents.Records).Count -eq 2) 'WER/managed filtering was incorrect.'
        Assert-True ($report.WindowsEvents.Records[0].Fields.P4 -eq 'libcocos2d.dll') 'Lost WER native field names.'
        Assert-True ($report.WindowsEvents.Records[1].Fields.Data0 -match 'Chrono Trigger.exe') 'Lost unnamed .NET event data.'
    } finally { $script:EventMode = 'matching' }
}
Test-Case 'An exclusively locked log is explicit while other sources still collect' {
    $stream = [IO.File]::Open($gameplay, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $r = Invoke-ForesightCrashCollection -GameRoot $game -ReloadedLogDirectory $logs -OutputDirectory $out -SkipWindowsEvents
        $report = Read-ZipText $r.ArchivePath 'report.json' | ConvertFrom-Json
        Assert-True (@($report.Logs).Count -eq 2) 'Helper logs were lost after one source was locked.'
        $unreadable = @($report.MissingSources | Where-Object Kind -eq 'Gameplay')
        Assert-True ($unreadable.Count -eq 1 -and $unreadable[0].Reason.Length -gt 0) 'Locked log was silently omitted.'
    } finally { $stream.Dispose() }
}
Test-Case 'Detects the game from a nested support folder and rejects an invalid explicit root' {
    $nested = Join-Path $game 'Accessibility\Support\CrashCollector'
    [IO.Directory]::CreateDirectory($nested) | Out-Null
    Assert-True ((Resolve-ForesightCollectorGameRoot -StartDirectory $nested) -ceq $game) 'Nested deployment did not locate its game.'
    $rejected = $false
    try { Resolve-ForesightCollectorGameRoot -Path $logs | Out-Null }
    catch { $rejected = $_.Exception.Message -match 'Chrono Trigger.exe was not found' }
    Assert-True $rejected 'An invalid explicit game folder was accepted.'
}
Test-Case 'Defaults to the three newest complete Chrono sessions' {
    $many = Join-Path $testRoot 'many sessions'
    foreach ($i in 1..5) {
        $path = Join-Path $many ("2026-10-18 0$i.00.00 ~ Chrono Trigger.txt")
        Write-Fixture $path ("complete session $i")
        [IO.File]::SetLastWriteTimeUtc($path, [datetime]::UtcNow.AddMinutes($i))
    }
    $r = Invoke-ForesightCrashCollection -GameRoot $game -ReloadedLogDirectory $many -OutputDirectory $out -SkipWindowsEvents
    $report = Read-ZipText $r.ArchivePath 'report.json' | ConvertFrom-Json
    $sessions = @($report.Logs | Where-Object Kind -eq 'Gameplay')
    Assert-True ($sessions.Count -eq 3) 'Default session limit is wrong.'
    Assert-True ((Read-ZipText $r.ArchivePath $sessions[0].ArchivePath) -ceq 'complete session 5') 'Newest session was not first.'
    Assert-True ((Read-ZipText $r.ArchivePath $sessions[2].ArchivePath) -ceq 'complete session 3') 'An older session displaced the recent sessions.'
}
Test-Case 'An unwritable output fails clearly without modifying the source' {
    $fileAsDirectory = Join-Path $testRoot 'not a directory'
    Write-Fixture $fileAsDirectory 'keep this file'
    $rejected = $false
    try { Invoke-ForesightCrashCollection -GameRoot $game -ReloadedLogDirectory $logs -OutputDirectory $fileAsDirectory -SkipWindowsEvents | Out-Null }
    catch { $rejected = $true }
    Assert-True $rejected 'An invalid output directory reported success.'
    Assert-True ([IO.File]::ReadAllText($fileAsDirectory) -ceq 'keep this file') 'An existing output file was changed.'
}
Test-Case 'A standalone copy discovers a custom Steam library from its VDF' {
    # This must be outside the real game tree, whose ancestors are also searched.
    $externalRoot = Join-Path ([IO.Path]::GetTempPath()) ('Foresight discovery ' + [guid]::NewGuid().ToString('N'))
    $steam = Join-Path $externalRoot 'fixture Steam'
    $library = Join-Path $externalRoot 'library [literal]'
    $libraryGame = Join-Path $library 'steamapps\common\Chrono Trigger'
    Write-Fixture (Join-Path $libraryGame 'Chrono Trigger.exe') 'fixture; never launched'
    Write-Fixture (Join-Path $steam 'steamapps\libraryfolders.vdf') ('"libraryfolders" { "1" { "path" "' + $library.Replace('\', '\\') + '" } }')
    $script:SteamFixtureRoot = $steam
    Push-Location -LiteralPath $steam
    try {
        Assert-True ((Resolve-ForesightCollectorGameRoot -StartDirectory $steam) -ceq $libraryGame) 'Standalone discovery missed the custom library.'
    } finally {
        Pop-Location
        $script:SteamFixtureRoot = $null
    }
}
Test-Case 'The CMD launcher forwards literal paths, writes a ZIP and propagates failures' {
    $launcher = Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($CollectorPath))) 'Collect Foresight Crash Logs.cmd'
    $cliOutput = Join-Path $out 'CLI [literal] reports'
    foreach ($shouldFail in @($false, $true)) {
        $cliGame = $game
        if ($shouldFail) { $cliGame = $logs }
        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = $env:ComSpec
        $start.Arguments = '/d /c call "' + $launcher + '" -GameRoot "' + $cliGame + '" -ReloadedLogDirectory "' + $logs + '" -OutputDirectory "' + $cliOutput + '" -SkipWindowsEvents <nul'
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $process = [Diagnostics.Process]::Start($start)
        try {
            $stdout = $process.StandardOutput.ReadToEnd()
            $stderr = $process.StandardError.ReadToEnd()
            $process.WaitForExit()
            if ($shouldFail) {
                Assert-True ($process.ExitCode -eq 1) 'Launcher hid the failed collection exit status.'
                Assert-True ($stdout -match 'Chrono Trigger.exe was not found') 'CLI failure did not explain the missing game.'
            } else {
                Assert-True ($process.ExitCode -eq 0) ("CLI failed: $stdout $stderr")
                $archives = @(Get-ChildItem -LiteralPath $cliOutput -File -Filter '*.zip')
                Assert-True ($archives.Count -eq 1) 'Launcher did not produce its report ZIP.'
                $report = Read-ZipText $archives[0].FullName 'report.json' | ConvertFrom-Json
                Assert-True (@($report.Logs).Count -eq 3) 'CLI did not forward the supplied paths.'
                Assert-True ($report.WindowsEvents.Status -eq 'skipped') 'CLI did not forward the event option.'
            }
        } finally { $process.Dispose() }
    }
}
Write-Output "$script:Passed passed; $script:Failed failed. Artifacts: $testRoot"
if ($script:Failed) { exit 1 }
