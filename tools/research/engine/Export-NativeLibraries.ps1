param(
    [Parameter(Mandatory)][string]$GameRoot,
    [Parameter(Mandatory)][string]$OutputRoot,
    [Parameter(Mandatory)][string]$ProjectRoot,
    [Parameter(Mandatory)][string]$GhidraHeadless
)
$ErrorActionPreference = 'Stop'
$gamePath = (Resolve-Path -LiteralPath $GameRoot).Path
$outputPath = [IO.Path]::GetFullPath($OutputRoot)
$projectPath = [IO.Path]::GetFullPath($ProjectRoot)
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
New-Item -ItemType Directory -Path $projectPath -Force | Out-Null
$modules = Get-ChildItem -LiteralPath $gamePath -File -Filter '*.dll' | Sort-Object Name
$inventory = foreach ($module in $modules) {
    [ordered]@{name=$module.Name; bytes=$module.Length; sha256=(Get-FileHash -LiteralPath $module.FullName -Algorithm SHA256).Hash}
}
$inventory | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputPath 'native-libraries.json') -Encoding utf8
foreach ($module in $modules) {
    $identity = $inventory | Where-Object name -eq $module.Name
    $destination = Join-Path $outputPath $module.Name
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    if (Test-Path -LiteralPath (Join-Path $destination 'manifest.json')) {
        $existing = Get-Content -LiteralPath (Join-Path $destination 'manifest.json') -Raw | ConvertFrom-Json
        if ($existing.exportComplete -and $existing.executableSha256 -eq $identity.sha256 -and $existing.failed -eq 0) { continue }
    }
    $projectName = 'ct_' + $module.BaseName
    $projectFile = Join-Path $projectPath ($projectName + '.gpr')
    $programArguments = if (Test-Path -LiteralPath $projectFile) { @('-process', $module.Name) } else { @('-import', $module.FullName) }
    Write-Output "Analyzing and exporting $($module.Name)"
    & $GhidraHeadless $projectPath $projectName @programArguments -analysisTimeoutPerFile 1800 -max-cpu 2 -scriptPath $PSScriptRoot -postScript ExportGameEngine.java $destination $identity.sha256 -log (Join-Path $destination 'ghidra-analysis.log') -scriptlog (Join-Path $destination 'ghidra-export.log')
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $destination 'manifest.json'))) { throw "Export failed for $($module.Name)" }
}
Write-Output 'Native library exports finished.'
