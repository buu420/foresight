[CmdletBinding()]
param([switch]$VerifyOnly)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RuntimeRoot = 'C:\Users\User\AppData\Local\ChronoTriggerAccessibility\dotnet-x86'
$ReloadedPath = 'C:\Program Files (x86)\Steam\steamapps\common\Spyro Reignited Trilogy\mod-tools\reloaded-ii\Release\Reloaded-II.exe'
$GameExecutable = 'G:\SteamLibrary\steamapps\common\Chrono Trigger\Chrono Trigger.exe'
$SupportedGameSha256 = '8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7'
$PeMachineI386 = 0x014c

function Assert-FileExists {
    param([Parameter(Mandatory = $true)][string]$LiteralPath)

    if (-not (Test-Path -LiteralPath $LiteralPath -PathType Leaf)) {
        throw "Required file is missing: $LiteralPath"
    }
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

function Get-PeMachine {
    param([Parameter(Mandatory = $true)][string]$LiteralPath)

    Assert-FileExists $LiteralPath
    $bytes = [System.IO.File]::ReadAllBytes($LiteralPath)
    if ($bytes.Length -lt 0x40) {
        throw "File is too small to be a PE image: $LiteralPath"
    }
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
    if ($peOffset -lt 0 -or ($peOffset + 6) -gt $bytes.Length -or [BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x00004550) {
        throw "PE header is invalid: $LiteralPath"
    }
    return [BitConverter]::ToUInt16($bytes, $peOffset + 4)
}

$dotnetHost = Join-Path $RuntimeRoot 'dotnet.exe'
$hostFxr = Join-Path $RuntimeRoot 'host\fxr\9.0.18\hostfxr.dll'
$netCoreFramework = Join-Path $RuntimeRoot 'shared\Microsoft.NETCore.App\9.0.18'
$windowsDesktopFramework = Join-Path $RuntimeRoot 'shared\Microsoft.WindowsDesktop.App\9.0.18'

Assert-FileExists $dotnetHost
Assert-FileExists $hostFxr
Assert-FileExists $ReloadedPath
Assert-FileExists $GameExecutable
if (-not (Test-Path -LiteralPath $netCoreFramework -PathType Container)) {
    throw "The x86 Microsoft.NETCore.App 9.0.18 framework is missing: $netCoreFramework"
}
if (-not (Test-Path -LiteralPath $windowsDesktopFramework -PathType Container)) {
    throw "The x86 Microsoft.WindowsDesktop.App 9.0.18 framework is missing: $windowsDesktopFramework"
}
if ((Get-PeMachine $dotnetHost) -ne $PeMachineI386) {
    throw "The configured .NET host is not 32-bit x86: $dotnetHost"
}
if ((Get-PeMachine $hostFxr) -ne $PeMachineI386) {
    throw "The configured .NET hostfxr is not 32-bit x86: $hostFxr"
}
Assert-FileExists (Join-Path $netCoreFramework 'coreclr.dll')
Assert-FileExists (Join-Path $windowsDesktopFramework 'PresentationFramework.dll')
if ((Get-PeMachine $GameExecutable) -ne $PeMachineI386) {
    throw "Chrono Trigger.exe is not the required 32-bit x86 image."
}

$actualGameHash = Get-Sha256 $GameExecutable
if ($actualGameHash -ne $SupportedGameSha256) {
    throw "Unsupported Chrono Trigger.exe. Expected SHA-256 $SupportedGameSha256 but found $actualGameHash."
}

if ($VerifyOnly) {
    Write-Output 'Accessible launcher prerequisites verified; the game was not launched.'
    return
}

$hadDotNetRootX86 = Test-Path Env:DOTNET_ROOT_X86
$previousDotNetRootX86 = $env:DOTNET_ROOT_X86
try {
    $env:DOTNET_ROOT_X86 = $RuntimeRoot
    & $ReloadedPath --launch $GameExecutable
    if ($LASTEXITCODE -ne 0) {
        throw "Reloaded-II exited with code $LASTEXITCODE."
    }
}
finally {
    if ($hadDotNetRootX86) {
        $env:DOTNET_ROOT_X86 = $previousDotNetRootX86
    }
    else {
        Remove-Item Env:DOTNET_ROOT_X86 -ErrorAction SilentlyContinue
    }
}
