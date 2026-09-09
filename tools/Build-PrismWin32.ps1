[CmdletBinding()]
param(
    [switch]$Rebuild,
    [string]$SourceDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) 'prism-source-research'),
    [string]$ReviewedArtifact = (Join-Path $PSScriptRoot '..\.build\prism-dist-win32\bin\prism.dll')
)

$ErrorActionPreference = 'Stop'
$expectedCommit = '9911156998B52FEE91FB2CB4F71AC793D4E546C7'
$expectedHash = '6A84322E42D1B4123E2E66E9887CFDF0CDEA2A972FA40FC7B7185AEC77F5178A'
$stageDirectory = Join-Path $PSScriptRoot '..\native\prism\v0.17.3'
$stageDll = Join-Path $stageDirectory 'win-x86\prism.dll'

function Get-PeMachine([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 0x40 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) { throw "Not a PE file: $Path" }
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($peOffset -lt 0 -or $peOffset + 6 -gt $bytes.Length -or [BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x00004550) { throw "Invalid PE header: $Path" }
    return [BitConverter]::ToUInt16($bytes, $peOffset + 4)
}

git -C $SourceDirectory checkout --detach v0.17.3
$commit = (git -C $SourceDirectory rev-parse HEAD).Trim().ToUpperInvariant()
if ($commit -ne $expectedCommit) { throw "Prism tag commit mismatch: $commit" }
Write-Host 'Prism v0.17.3 README:'
Get-Content -LiteralPath (Join-Path $SourceDirectory 'README.md')

if ($Rebuild) {
    $buildDirectory = Join-Path $SourceDirectory 'build-win32-release'
    $vsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $vsInstall = (& $vsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath).Trim()
    if (-not $vsInstall) { throw 'Visual Studio 2022 C++ x86 tools are not installed.' }
    $msvcDirectory = Get-ChildItem -LiteralPath (Join-Path $vsInstall 'VC\Tools\MSVC') -Directory |
        Sort-Object Name -Descending | Select-Object -First 1
    $prismLibTool = Join-Path $msvcDirectory.FullName 'bin\Hostx64\x86\lib.exe'
    if (-not (Test-Path -LiteralPath $prismLibTool)) { throw "Win32 librarian not found: $prismLibTool" }
    cmake -S $SourceDirectory -B $buildDirectory -G 'Visual Studio 17 2022' -A Win32 `
        "-DPRISM_LIB_TOOL=$prismLibTool" `
        -DPRISM_ENABLE_TESTS=OFF -DPRISM_ENABLE_DEMOS=OFF -DPRISM_ENABLE_SHIMS=OFF `
        -DPRISM_ENABLE_LEGACY_BACKENDS=OFF -DPRISM_ENABLE_JAWS_BACKEND=OFF `
        -DPRISM_ENABLE_SAPI_BACKEND=OFF -DPRISM_ENABLE_SENSE_READER_BACKEND=OFF `
        -DPRISM_ENABLE_ZOOM_TEXT_BACKEND=OFF -DBUILD_SHARED_LIBS=ON
    cmake --build $buildDirectory --config Release
    $rebuiltArtifact = Get-ChildItem -LiteralPath $buildDirectory -Filter prism.dll -Recurse | Select-Object -First 1
    if (-not $rebuiltArtifact) { throw 'The pinned Prism source build did not produce prism.dll.' }
    if ((Get-PeMachine $rebuiltArtifact.FullName) -ne 0x014C) { throw "The rebuilt Prism candidate is not Win32: $($rebuiltArtifact.FullName)" }
    $rebuiltHash = (Get-FileHash -LiteralPath $rebuiltArtifact.FullName -Algorithm SHA256).Hash
    Write-Host "Rebuilt Win32 Prism review candidate: $($rebuiltArtifact.FullName)"
    Write-Host "Rebuilt candidate SHA-256: $rebuiltHash (not staged)"
}

# Only the separately reviewed, hash-pinned artifact is eligible for staging.
if ((Get-FileHash -LiteralPath $ReviewedArtifact -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'Prism artifact hash does not match the reviewed Win32 build.'
}

New-Item -ItemType Directory -Force (Split-Path -Parent $stageDll) | Out-Null
Copy-Item -LiteralPath $ReviewedArtifact -Destination $stageDll -Force
Copy-Item -LiteralPath (Join-Path $SourceDirectory 'LICENSE') -Destination (Join-Path $stageDirectory 'LICENSE') -Force
Copy-Item -LiteralPath (Join-Path $SourceDirectory 'NOTICE') -Destination (Join-Path $stageDirectory 'NOTICE') -Force
Write-Host "Staged verified Prism DLL: $stageDll"
