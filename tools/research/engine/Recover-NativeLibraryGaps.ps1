param(
    [Parameter(Mandatory)][string]$OutputRoot,
    [Parameter(Mandatory)][string]$ProjectRoot,
    [Parameter(Mandatory)][string]$GhidraHeadless
)
$ErrorActionPreference = 'Stop'
$outputPath = [IO.Path]::GetFullPath($OutputRoot)
$projectPath = [IO.Path]::GetFullPath($ProjectRoot)
$recoveryPath = Join-Path $projectPath 'recovered'
New-Item -ItemType Directory -Path $recoveryPath -Force | Out-Null
$inventory = Get-Content -LiteralPath (Join-Path $outputPath 'native-libraries.json') -Raw | ConvertFrom-Json
foreach ($module in $inventory) {
    $projectName = 'ct_' + [IO.Path]::GetFileNameWithoutExtension($module.name)
    $destination = Join-Path (Join-Path $outputPath $module.name) 'recovered'
    if (Test-Path -LiteralPath (Join-Path $destination 'manifest.json')) {
        $existing = Get-Content -LiteralPath (Join-Path $destination 'manifest.json') -Raw | ConvertFrom-Json
        if ($existing.exportComplete -and $existing.executableSha256 -eq $module.sha256) { continue }
    }
    # Use separate projects: the primary analyzed libraries remain unchanged.
    if (-not (Test-Path -LiteralPath (Join-Path $recoveryPath ($projectName + '.gpr')))) {
        Copy-Item -LiteralPath (Join-Path $projectPath ($projectName + '.rep')) -Destination $recoveryPath -Recurse
        Copy-Item -LiteralPath (Join-Path $projectPath ($projectName + '.gpr')) -Destination $recoveryPath
    }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Write-Output "Recovering isolated instruction flows in $($module.name)"
    & $GhidraHeadless $recoveryPath $projectName -process $module.name -noanalysis -scriptPath $PSScriptRoot -postScript RecoverFunctionGaps.java $destination $module.sha256
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $destination 'manifest.json'))) { throw "Function discovery failed for $($module.name)" }
}
Write-Output 'Native library function discovery finished.'
