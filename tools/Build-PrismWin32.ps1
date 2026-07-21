[CmdletBinding()]
param(
    [switch]$Rebuild,
    [string]$SourceDirectory = 'C:\Users\User\AppData\Local\Temp\prism-source-research',
    [string]$ReviewedArtifact = 'G:\SteamLibrary\steamapps\common\Chrono Trigger\accessibility-mod\.build\prism-dist-win32\bin\prism.dll'
)

$ErrorActionPreference = 'Stop'
$expectedCommit = '9911156998B52FEE91FB2CB4F71AC793D4E546C7'
$expectedHash = '6A84322E42D1B4123E2E66E9887CFDF0CDEA2A972FA40FC7B7185AEC77F5178A'
$stageDirectory = Join-Path $PSScriptRoot '..\native\prism\v0.17.3'
$stageDll = Join-Path $stageDirectory 'win-x86\prism.dll'

Write-Host 'Prism v0.17.3 README:'
Get-Content -LiteralPath (Join-Path $SourceDirectory 'README.md')
git -C $SourceDirectory checkout --detach v0.17.3
$commit = (git -C $SourceDirectory rev-parse HEAD).Trim().ToUpperInvariant()
if ($commit -ne $expectedCommit) { throw "Prism tag commit mismatch: $commit" }

if ($Rebuild) {
    $buildDirectory = Join-Path $SourceDirectory 'build-win32-release'
    cmake -S $SourceDirectory -B $buildDirectory -G 'Visual Studio 17 2022' -A Win32 `
        -DPRISM_ENABLE_TESTS=OFF -DPRISM_ENABLE_DEMOS=OFF -DPRISM_ENABLE_SHIMS=OFF `
        -DPRISM_ENABLE_LEGACY_BACKENDS=OFF -DPRISM_ENABLE_JAWS_BACKEND=OFF `
        -DPRISM_ENABLE_SAPI_BACKEND=OFF -DPRISM_ENABLE_SENSE_READER_BACKEND=OFF `
        -DPRISM_ENABLE_ZOOM_TEXT_BACKEND=OFF -DBUILD_SHARED_LIBS=ON
    cmake --build $buildDirectory --config Release
    $ReviewedArtifact = (Get-ChildItem -LiteralPath $buildDirectory -Filter prism.dll -Recurse | Select-Object -First 1).FullName
}

if ((Get-FileHash -LiteralPath $ReviewedArtifact -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'Prism artifact hash does not match the reviewed Win32 build.'
}

New-Item -ItemType Directory -Force (Split-Path -Parent $stageDll) | Out-Null
Copy-Item -LiteralPath $ReviewedArtifact -Destination $stageDll -Force
Copy-Item -LiteralPath (Join-Path $SourceDirectory 'LICENSE') -Destination (Join-Path $stageDirectory 'LICENSE') -Force
Copy-Item -LiteralPath (Join-Path $SourceDirectory 'NOTICE') -Destination (Join-Path $stageDirectory 'NOTICE') -Force
Write-Host "Staged verified Prism DLL: $stageDll"
