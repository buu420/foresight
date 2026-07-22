[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',

    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\package\chrono.trigger.accessibility'),

    [string]$DotNetPath = 'C:\Program Files (x86)\Steam\steamapps\common\Yu-Gi-Oh! Legacy of the Duelist Link Evolution\YuGiOhAccessibility\.worktrees\accessibility-native-uia\.tools\dotnet\dotnet.exe',

    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$SupportedPrismSha256 = '6A84322E42D1B4123E2E66E9887CFDF0CDEA2A972FA40FC7B7185AEC77F5178A'
$PeMachineI386 = 0x014c
$RepositoryRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$ProjectPath = Join-Path $RepositoryRoot 'src\ChronoTriggerAccessibility.Mod\ChronoTriggerAccessibility.Mod.csproj'
$BuildDirectory = Join-Path $RepositoryRoot "src\ChronoTriggerAccessibility.Mod\bin\$Configuration\net9.0-windows"
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

function Assert-FileExists {
    param([Parameter(Mandatory = $true)][string]$LiteralPath)

    if (-not (Test-Path -LiteralPath $LiteralPath -PathType Leaf)) {
        throw "Required file is missing: $LiteralPath"
    }
}

function Assert-DirectoryExists {
    param([Parameter(Mandatory = $true)][string]$LiteralPath)

    if (-not (Test-Path -LiteralPath $LiteralPath -PathType Container)) {
        throw "Required directory is missing: $LiteralPath"
    }
}

function Get-PeMachine {
    param([Parameter(Mandatory = $true)][string]$LiteralPath)

    Assert-FileExists $LiteralPath
    $bytes = [System.IO.File]::ReadAllBytes($LiteralPath)
    if ($bytes.Length -lt 0x40) {
        throw "File is too small to be a PE image: $LiteralPath"
    }

    $peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
    if ($peOffset -lt 0 -or ($peOffset + 6) -gt $bytes.Length) {
        throw "PE header is outside the file: $LiteralPath"
    }

    if ([BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x00004550) {
        throw "PE signature is invalid: $LiteralPath"
    }

    return [BitConverter]::ToUInt16($bytes, $peOffset + 4)
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$LiteralPath)

    Assert-FileExists $LiteralPath
    $stream = [System.IO.File]::OpenRead($LiteralPath)
    try {
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            return [BitConverter]::ToString($sha256.ComputeHash($stream)).Replace('-', '')
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Assert-SafeOutputDirectory {
    param([Parameter(Mandatory = $true)][string]$LiteralPath)

    $pathRoot = [System.IO.Path]::GetPathRoot($LiteralPath)
    if ([string]::IsNullOrWhiteSpace($LiteralPath) -or
        [string]::Equals($LiteralPath.TrimEnd('\'), $pathRoot.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals($LiteralPath.TrimEnd('\'), $RepositoryRoot.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to use an unsafe package output directory: $LiteralPath"
    }

    if (Test-Path -LiteralPath $LiteralPath) {
        $safeCleanRoot = [System.IO.Path]::GetFullPath((Join-Path $RepositoryRoot 'artifacts\package')).TrimEnd('\') + '\'
        if (-not $LiteralPath.StartsWith($safeCleanRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to replace an existing directory outside the repository package staging root: $LiteralPath"
        }

        Remove-Item -LiteralPath $LiteralPath -Recurse -Force
    }
}

Assert-FileExists $ProjectPath
Assert-FileExists $DotNetPath

if (-not $SkipBuild) {
    & $DotNetPath build $ProjectPath --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Release build failed with exit code $LASTEXITCODE."
    }
}

$depsPath = Join-Path $BuildDirectory 'ChronoTriggerAccessibility.Mod.deps.json'
$modConfigPath = Join-Path $BuildDirectory 'ModConfig.json'
$prismSourcePath = Join-Path $RepositoryRoot 'native\prism\v0.17.3\win-x86\prism.dll'
$prismLicensePath = Join-Path $RepositoryRoot 'native\prism\v0.17.3\LICENSE'
$prismNoticePath = Join-Path $RepositoryRoot 'native\prism\v0.17.3\NOTICE'
$prismLicensesPath = Join-Path $RepositoryRoot 'native\prism\v0.17.3\LICENSES'
$asiLoaderLicensePath = Join-Path $RepositoryRoot 'native\ultimate-asi-loader\v6.9.0\LICENSE'
$readmePath = Join-Path $RepositoryRoot 'README.md'
$thirdPartyNoticePath = Join-Path $RepositoryRoot 'THIRD-PARTY-NOTICES.md'
$userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
$nugetPackagesRoot = if ([string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
    Join-Path $userProfile '.nuget\packages'
}
else {
    [System.IO.Path]::GetFullPath($env:NUGET_PACKAGES)
}
$gplLicensePath = Join-Path $nugetPackagesRoot 'reloaded.mod.interfaces\2.5.0\LICENSE.md'
$hooksDefinitionsLicensePath = Join-Path $nugetPackagesRoot 'reloaded.hooks.definitions\1.15.0\LICENSE.md'
$sharedHooksLicensePath = Join-Path $nugetPackagesRoot 'reloaded.sharedlib.hooks\1.9.0\LICENSE'

foreach ($requiredPath in @(
    $depsPath,
    $modConfigPath,
    $prismSourcePath,
    $prismLicensePath,
    $prismNoticePath,
    $asiLoaderLicensePath,
    $readmePath,
    $thirdPartyNoticePath,
    $gplLicensePath,
    $hooksDefinitionsLicensePath,
    $sharedHooksLicensePath
)) {
    Assert-FileExists $requiredPath
}
Assert-DirectoryExists $prismLicensesPath

$actualPrismHash = Get-Sha256 $prismSourcePath
if ($actualPrismHash -ne $SupportedPrismSha256) {
    throw "Reviewed Prism hash mismatch. Expected $SupportedPrismSha256 but found $actualPrismHash."
}

if ((Get-PeMachine $prismSourcePath) -ne $PeMachineI386) {
    throw "The reviewed Prism binary is not 32-bit x86 (PE machine 0x014c)."
}

$modConfig = Get-Content -LiteralPath $modConfigPath -Raw | ConvertFrom-Json
if ($modConfig.ModId -ne 'chrono.trigger.accessibility') {
    throw "Unexpected Reloaded mod ID in $modConfigPath."
}

if (@($modConfig.ModDependencies) -notcontains 'reloaded.sharedlib.hooks') {
    throw 'ModConfig.json does not declare the required reloaded.sharedlib.hooks dependency.'
}

$deps = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json
$runtimeFileNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($target in $deps.targets.PSObject.Properties) {
    foreach ($library in $target.Value.PSObject.Properties) {
        $runtimeProperty = $library.Value.PSObject.Properties['runtime']
        if ($null -eq $runtimeProperty) {
            continue
        }

        foreach ($asset in $runtimeProperty.Value.PSObject.Properties) {
            [void]$runtimeFileNames.Add([System.IO.Path]::GetFileName($asset.Name))
        }
    }
}

if ($runtimeFileNames.Count -eq 0) {
    throw "No runtime assets were found in $depsPath."
}

foreach ($fileName in $runtimeFileNames) {
    Assert-FileExists (Join-Path $BuildDirectory $fileName)
}

Assert-SafeOutputDirectory $OutputDirectory
[void][System.IO.Directory]::CreateDirectory($OutputDirectory)

foreach ($fileName in $runtimeFileNames) {
    Copy-Item -LiteralPath (Join-Path $BuildDirectory $fileName) -Destination (Join-Path $OutputDirectory $fileName)
}

Copy-Item -LiteralPath $depsPath -Destination (Join-Path $OutputDirectory 'ChronoTriggerAccessibility.Mod.deps.json')
Copy-Item -LiteralPath $modConfigPath -Destination (Join-Path $OutputDirectory 'ModConfig.json')
Copy-Item -LiteralPath $prismSourcePath -Destination (Join-Path $OutputDirectory 'prism.dll')
Copy-Item -LiteralPath $prismLicensePath -Destination (Join-Path $OutputDirectory 'LICENSE')
Copy-Item -LiteralPath $prismNoticePath -Destination (Join-Path $OutputDirectory 'NOTICE')
Copy-Item -LiteralPath $readmePath -Destination (Join-Path $OutputDirectory 'README.md')
Copy-Item -LiteralPath $thirdPartyNoticePath -Destination (Join-Path $OutputDirectory 'THIRD-PARTY-NOTICES.md')
Copy-Item -LiteralPath $prismLicensesPath -Destination $OutputDirectory -Recurse
$licenseOutputDirectory = Join-Path $OutputDirectory 'LICENSES'
Copy-Item -LiteralPath $gplLicensePath -Destination (Join-Path $licenseOutputDirectory 'GNU-GPL-3.0.txt')
Copy-Item -LiteralPath $hooksDefinitionsLicensePath -Destination (Join-Path $licenseOutputDirectory 'Reloaded.Hooks.Definitions-LGPL-3.0.txt')
Copy-Item -LiteralPath $sharedHooksLicensePath -Destination (Join-Path $licenseOutputDirectory 'Reloaded.SharedLib.Hooks-LGPL-3.0.txt')
Copy-Item -LiteralPath $asiLoaderLicensePath -Destination (Join-Path $licenseOutputDirectory 'Ultimate-ASI-Loader-MIT.txt')

$forbiddenFiles = Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File | Where-Object {
    $_.Extension -ieq '.exe' -or $_.FullName.IndexOf('x64', [StringComparison]::OrdinalIgnoreCase) -ge 0
}
if ($forbiddenFiles) {
    throw "Forbidden executable or x64 payload entered the package: $($forbiddenFiles.FullName -join ', ')"
}

$manifestLines = @(
    Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File |
        Where-Object { $_.Name -ne 'SHA256SUMS.txt' } |
        ForEach-Object {
            $packagePrefix = $OutputDirectory.TrimEnd('\') + '\'
            if (-not $_.FullName.StartsWith($packagePrefix, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Packaged file resolved outside the output directory: $($_.FullName)"
            }
            $relativePath = $_.FullName.Substring($packagePrefix.Length).Replace('\', '/')
            $hash = Get-Sha256 $_.FullName
            "$hash  $relativePath"
        }
)
[Array]::Sort($manifestLines, [StringComparer]::Ordinal)
[System.IO.File]::WriteAllLines((Join-Path $OutputDirectory 'SHA256SUMS.txt'), $manifestLines, [Text.UTF8Encoding]::new($false))

Write-Output "Package created: $OutputDirectory"
Write-Output "Reviewed Prism SHA-256: $SupportedPrismSha256"
