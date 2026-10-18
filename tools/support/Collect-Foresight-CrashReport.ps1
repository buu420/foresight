[CmdletBinding()]
param(
    [string]$GameRoot,
    [string]$ReloadedLogDirectory,
    [string]$OutputDirectory,
    [ValidateRange(1, 14)][int]$Days = 3,
    [ValidateRange(1, 10)][int]$GameplayLogCount = 3,
    [switch]$SkipWindowsEvents
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:ForesightCollectorHome = $PSScriptRoot

function Resolve-ForesightCollectorGameRoot {
    param([string]$Path, [string]$StartDirectory = $script:ForesightCollectorHome)
    if ($Path) {
        $full = [IO.Path]::GetFullPath($Path.Trim('"'))
        if (-not [IO.File]::Exists((Join-Path $full 'Chrono Trigger.exe'))) {
            throw "Chrono Trigger.exe was not found in: $full"
        }
        return $full
    }
    foreach ($start in @($StartDirectory, (Get-Location).Path)) {
        if (-not $start) { continue }
        $directory = [IO.DirectoryInfo]::new($start)
        while ($null -ne $directory) {
            if ([IO.File]::Exists((Join-Path $directory.FullName 'Chrono Trigger.exe'))) {
                return $directory.FullName
            }
            $directory = $directory.Parent
        }
    }
    $steamRoots = [Collections.Generic.List[string]]::new()
    try {
        $steam = Get-ItemProperty -LiteralPath 'HKCU:\Software\Valve\Steam' -ErrorAction Stop
        if ($steam.SteamPath) { $steamRoots.Add([string]$steam.SteamPath) }
    } catch { }
    $programFiles = [Environment]::GetFolderPath('ProgramFilesX86')
    if ($programFiles) { $steamRoots.Add((Join-Path $programFiles 'Steam')) }
    $libraries = [Collections.Generic.List[string]]::new()
    foreach ($steamRoot in $steamRoots) {
        $libraries.Add($steamRoot)
        $vdf = Join-Path $steamRoot 'steamapps\libraryfolders.vdf'
        if (-not [IO.File]::Exists($vdf)) { continue }
        try {
            $text = [IO.File]::ReadAllText($vdf)
            foreach ($match in [regex]::Matches($text, '"path"\s+"([^"]+)"')) {
                $libraries.Add($match.Groups[1].Value.Replace('\\', '\'))
            }
        } catch { }
    }
    foreach ($library in $libraries) {
        $candidate = Join-Path $library 'steamapps\common\Chrono Trigger'
        if ([IO.File]::Exists((Join-Path $candidate 'Chrono Trigger.exe'))) {
            return [IO.Path]::GetFullPath($candidate)
        }
    }
    return $null
}

function Copy-ForesightCollectorLog {
    param([string]$Source, [string]$Destination, [string]$ArchivePath, [string]$Kind)
    $inputStream = $null
    $outputStream = $null
    try {
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Destination)) | Out-Null
        $share = [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete
        $inputStream = [IO.File]::Open($Source, [IO.FileMode]::Open, [IO.FileAccess]::Read, $share)
        $lengthAtOpen = $inputStream.Length
        $writeTime = [IO.File]::GetLastWriteTimeUtc($Source)
        $outputStream = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
        $buffer = [byte[]]::new(65536)
        [long]$captured = 0
        while ($captured -lt $lengthAtOpen) {
            $wanted = [int][Math]::Min($buffer.Length, $lengthAtOpen - $captured)
            $read = $inputStream.Read($buffer, 0, $wanted)
            if ($read -eq 0) { break }
            $outputStream.Write($buffer, 0, $read)
            $captured += $read
        }
        $changed = $inputStream.Length -ne $lengthAtOpen -or [IO.File]::GetLastWriteTimeUtc($Source) -ne $writeTime
        $outputStream.Dispose()
        $outputStream = $null
        $status = 'copied'
        $copyError = $null
        if ($captured -ne $lengthAtOpen) {
            $status = 'partial'
            $copyError = 'The source was shortened during collection; this copy is incomplete.'
        }
        $hasher = [Security.Cryptography.SHA256]::Create()
        $hashInput = [IO.File]::OpenRead($Destination)
        try { $digest = [BitConverter]::ToString($hasher.ComputeHash($hashInput)).Replace('-', '') }
        finally { $hashInput.Dispose(); $hasher.Dispose() }
        return [pscustomobject][ordered]@{
            Kind = $Kind; Source = $Source; ArchivePath = $ArchivePath
            Status = $status; Error = $copyError; LengthAtOpen = $lengthAtOpen
            CapturedBytes = $captured; SourceChangedDuringCopy = $changed
            SourceLastWriteUtc = $writeTime.ToString('o')
            Sha256 = $digest
        }
    } finally {
        if ($null -ne $outputStream) { $outputStream.Dispose() }
        if ($null -ne $inputStream) { $inputStream.Dispose() }
    }
}

function Get-ForesightCollectorWindowsEvents {
    param([string]$StagingDirectory, [int]$Days = 3, [switch]$Skip)
    $result = [ordered]@{
        Status = 'no_matches'; Error = $null
        SinceLocal = ([datetime]::Now.AddDays(-$Days)).ToString('o')
        Records = @(); ReadErrors = @()
    }
    if ($Skip) { $result.Status = 'skipped'; return [pscustomobject]$result }
    try {
        $query = @{ LogName = 'Application'; Id = @(1000, 1001, 1026); StartTime = [datetime]::Now.AddDays(-$Days) }
        $events = @(Get-WinEvent -FilterHashtable $query -ErrorAction Stop)
    } catch {
        if ($_.FullyQualifiedErrorId -like 'NoMatchingEventsFound*') {
            return [pscustomobject]$result
        }
        $result.Status = 'unavailable'
        $result.Error = $_.Exception.Message
        return [pscustomobject]$result
    }
    $eventDirectory = Join-Path $StagingDirectory 'windows-events'
    $encoding = [Text.UTF8Encoding]::new($false)
    $records = [Collections.Generic.List[object]]::new()
    $errors = [Collections.Generic.List[string]]::new()
    foreach ($event in $events) {
        try {
            $raw = $event.ToXml()
            $document = [Xml.XmlDocument]::new()
            $document.XmlResolver = $null
            $document.LoadXml($raw)
            $fields = [ordered]@{}
            $number = 0
            foreach ($node in $document.SelectNodes("//*[local-name()='EventData']/*[local-name()='Data']")) {
                $name = $node.GetAttribute('Name')
                if (-not $name) { $name = "Data$number" }
                if ($fields.Contains($name)) { $name = "$name-$number" }
                $fields[$name] = $node.InnerText
                $number++
            }
            # An explicit application name takes precedence over incidental message text.
            $appName = $fields['AppName']
            if (-not $appName -and $event.Id -eq 1001 -and $fields.Contains('P1') -and $fields['P1'] -match '(?i)\.exe$') {
                $appName = $fields['P1']
            }
            $message = ''
            try { $message = [string]$event.Message } catch { $message = "Message unavailable: $($_.Exception.Message)" }
            if (-not $appName -and $event.Id -eq 1026) {
                foreach ($description in (@($fields.Values) + @($message))) {
                    $firstLine = ([string]$description -split '\r?\n', 2)[0].Trim()
                    # CLR records start with an application heading; the heading can be localized.
                    $heading = [regex]::Match($firstLine, '^[^:\r\n]+:\s*(?<Executable>.+\.exe)\s*$', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
                    if ($heading.Success) { $appName = $heading.Groups['Executable'].Value.Trim(); break }
                }
            }
            if ($appName) {
                $matchesGame = [IO.Path]::GetFileName([string]$appName) -ieq 'Chrono Trigger.exe'
            } else {
                $matchesGame = ($raw + [Environment]::NewLine + $message) -match '(?i)(?<![\w.-])Chrono Trigger\.exe(?![\w.-])'
            }
            if (-not $matchesGame) { continue }
            [IO.Directory]::CreateDirectory($eventDirectory) | Out-Null
            $stem = '{0:D4}-event-{1}' -f ($records.Count + 1), $event.Id
            [IO.File]::WriteAllText((Join-Path $eventDirectory ($stem + '.xml')), $raw, $encoding)
            [IO.File]::WriteAllText((Join-Path $eventDirectory ($stem + '.txt')), $message, $encoding)
            $time = $null
            if ($null -ne $event.TimeCreated) { $time = $event.TimeCreated.ToUniversalTime().ToString('o') }
            $records.Add([pscustomobject][ordered]@{
                Id = $event.Id; Provider = $event.ProviderName; TimeCreatedUtc = $time
                Fields = $fields; XmlPath = "windows-events/$stem.xml"; MessagePath = "windows-events/$stem.txt"
            })
        } catch { $errors.Add($_.Exception.Message) }
    }
    $result.Records = @($records.ToArray())
    $result.ReadErrors = @($errors.ToArray())
    if ($records.Count) { $result.Status = 'collected' }
    if ($errors.Count) {
        $result.Error = 'Some Windows event records could not be read. See ReadErrors.'
        if (-not $records.Count) { $result.Status = 'unavailable' }
    }
    return [pscustomobject]$result
}

function Invoke-ForesightCrashCollection {
    [CmdletBinding()]
    param(
        [string]$GameRoot,
        [string]$ReloadedLogDirectory,
        [string]$OutputDirectory,
        [ValidateRange(1, 14)][int]$Days = 3,
        [ValidateRange(1, 10)][int]$GameplayLogCount = 3,
        [switch]$SkipWindowsEvents
    )
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $resolvedGame = Resolve-ForesightCollectorGameRoot -Path $GameRoot
    if (-not $ReloadedLogDirectory) {
        $ReloadedLogDirectory = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Reloaded-Mod-Loader-II\Logs'
    }
    if (-not $OutputDirectory) {
        $desktop = [Environment]::GetFolderPath('DesktopDirectory')
        if (-not $desktop) { throw 'Desktop could not be located. Specify -OutputDirectory.' }
        $OutputDirectory = Join-Path $desktop 'Foresight Diagnostics'
    }
    $OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
    [IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
    $name = 'Foresight-Crash-Report-' + [datetime]::Now.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
    $staging = Join-Path $OutputDirectory $name
    [IO.Directory]::CreateDirectory($staging) | Out-Null
    $logs = [Collections.Generic.List[object]]::new()
    $missing = [Collections.Generic.List[object]]::new()
    $sources = [Collections.Generic.List[object]]::new()
    foreach ($native in @(@('Bootstrap', 'bootstrap.log'), @('Proxy', 'proxy.log'))) {
        $path = $null
        if ($resolvedGame) { $path = Join-Path $resolvedGame ('Accessibility\Logs\' + $native[1]) }
        if ($path -and [IO.File]::Exists($path)) {
            $sources.Add(@{ Kind = $native[0]; Path = $path; ArchivePath = 'logs/' + $native[1] })
        } else {
            $missing.Add(@{ Kind = $native[0]; Source = $path; Reason = 'Game folder not found, or log does not exist.' })
        }
    }
    try {
        $candidates = @(Get-ChildItem -LiteralPath $ReloadedLogDirectory -File -ErrorAction Stop |
            Where-Object { $_.Name -match '(?i) ~ Chrono Trigger(?:\.exe)?\.txt$' } |
            Sort-Object LastWriteTimeUtc -Descending | Select-Object -First $GameplayLogCount)
        foreach ($file in $candidates) {
            $sources.Add(@{ Kind = 'Gameplay'; Path = $file.FullName; ArchivePath = 'logs/gameplay/' + $file.Name })
        }
        if (-not $candidates.Count) {
            $missing.Add(@{ Kind = 'Gameplay'; Source = $ReloadedLogDirectory; Reason = 'No Chrono Trigger gameplay logs found.' })
        }
    } catch {
        $missing.Add(@{ Kind = 'Gameplay'; Source = $ReloadedLogDirectory; Reason = $_.Exception.Message })
    }
    foreach ($source in $sources) {
        try {
            $log = Copy-ForesightCollectorLog -Source $source.Path -Destination (Join-Path $staging $source.ArchivePath) -ArchivePath $source.ArchivePath -Kind $source.Kind
            $logs.Add($log)
            if ($log.Status -ne 'copied') {
                $missing.Add(@{ Kind = $source.Kind; Source = $source.Path; Reason = $log.Error })
            }
        } catch {
            $missing.Add(@{ Kind = $source.Kind; Source = $source.Path; Reason = $_.Exception.Message })
        }
    }
    $version = $null
    if ($resolvedGame) {
        try {
            $configPath = Join-Path $resolvedGame 'Reloaded-II\Mods\chrono.trigger.accessibility\ModConfig.json'
            $config = [IO.File]::ReadAllText($configPath) | ConvertFrom-Json
            $version = [string]$config.ModVersion
        } catch { }
    }
    Write-Host 'Reading Chrono Trigger Windows crash entries...'
    $windows = Get-ForesightCollectorWindowsEvents -StagingDirectory $staging -Days $Days -Skip:$SkipWindowsEvents
    $report = [ordered]@{
        CollectorVersion = '1.0.0'; CollectedUtc = [datetime]::UtcNow.ToString('o')
        GameRoot = $resolvedGame; ForesightVersion = $version
        WindowsVersion = [Environment]::OSVersion.VersionString
        GameplayDirectory = $ReloadedLogDirectory; GameplayLogLimit = $GameplayLogCount
        Logs = @($logs.ToArray()); MissingSources = @($missing.ToArray()); WindowsEvents = $windows
        Notes = @(
            'Raw logs are copied without filtering or truncation, up to their length when opened.',
            'An active writer may change a log during collection; source-change and partial-copy flags are recorded.',
            'Windows event fields and fault offsets are preserved as recorded; they do not by themselves establish a root cause.'
        )
    }
    $encoding = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText((Join-Path $staging 'report.json'), ($report | ConvertTo-Json -Depth 12), $encoding)
    $readme = @(
        'Foresight crash report'
        'Please send the ZIP together with the location, action and approximate crash time.'
        'report.json lists copied logs, missing sources and Windows event availability.'
        'Logs and Windows messages may contain local paths, usernames and gameplay text.'
        'This utility collects diagnostics; it does not repair the game or upload the report.'
    ) -join [Environment]::NewLine
    [IO.File]::WriteAllText((Join-Path $staging 'README.txt'), $readme, $encoding)
    $archive = $staging + '.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($staging, $archive, [IO.Compression.CompressionLevel]::Optimal, $false)
    Write-Host ("Copied {0} log(s); {1} missing or incomplete source(s). Windows events: {2}." -f $logs.Count, $missing.Count, $windows.Status)
    Write-Host "Report ZIP: $archive"
    return [pscustomobject]@{ ArchivePath = $archive; ReportDirectory = $staging; MissingSourceCount = $missing.Count; WindowsEventStatus = $windows.Status }
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        Invoke-ForesightCrashCollection @PSBoundParameters | Out-Null
        exit 0
    } catch {
        Write-Host "Foresight collection failed: $($_.Exception.Message)"
        Write-Host 'Run again with -GameRoot or -OutputDirectory if the indicated folder could not be found or written.'
        exit 1
    }
}
