<#
.SYNOPSIS
    Single source of truth for path derivation across the mod's scripts.

.DESCRIPTION
    Every path is derived at runtime from $PSScriptRoot or from a parameter the
    caller supplies. Nothing here may contain an absolute machine path, so the
    repository can be deployed by any user from any location.
    tests\...\NoHardcodedPathsTests.cs enforces that.
#>

Set-StrictMode -Version Latest

function Get-ModRepositoryRoot {
    [CmdletBinding()]
    param()
    return (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath
}

function Get-GameRoot {
    <#
    .SYNOPSIS
        Locates the Chrono Trigger install folder.
    .DESCRIPTION
        The repository normally lives inside the game folder as
        <game>\accessibility-mod, so the parent directory is checked first. Pass
        -GameRoot explicitly when the repository lives somewhere else.
    #>
    [CmdletBinding()]
    param(
        [string]$GameRoot,
        [string]$RepositoryRoot = (Get-ModRepositoryRoot)
    )

    if ($GameRoot) {
        if (-not (Test-Path -LiteralPath (Join-Path $GameRoot 'Chrono Trigger.exe') -PathType Leaf)) {
            throw "No 'Chrono Trigger.exe' in the supplied -GameRoot: $GameRoot"
        }
        return (Resolve-Path -LiteralPath $GameRoot).ProviderPath
    }

    $candidate = (Resolve-Path -LiteralPath (Join-Path $RepositoryRoot '..')).ProviderPath
    if (-not (Test-Path -LiteralPath (Join-Path $candidate 'Chrono Trigger.exe') -PathType Leaf)) {
        throw ("Could not find 'Chrono Trigger.exe' in the expected game root '$candidate'. " +
               'Pass -GameRoot explicitly.')
    }
    return $candidate
}

function Get-PortableReloadedRoot {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$GameRoot)
    return (Join-Path $GameRoot 'Reloaded-II')
}

function Get-AccessibilityRoot {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$GameRoot)
    return (Join-Path $GameRoot 'Accessibility')
}

function Get-LauncherRoot {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$GameRoot)
    return (Join-Path (Get-AccessibilityRoot -GameRoot $GameRoot) 'Launcher')
}

function Get-VendoredReloadedRoot {
    [CmdletBinding()]
    param([string]$RepositoryRoot = (Get-ModRepositoryRoot))
    return (Join-Path $RepositoryRoot 'native\reloaded-ii\v1.30.3')
}

function Get-SupportedGameSha256 {
    [CmdletBinding()]
    param()
    return '8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7'
}

function Get-PeMachine {
    <#
    .SYNOPSIS
        Reads a PE image's machine type. 0x14C is x86, 0x8664 is x64.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $reader = New-Object System.IO.BinaryReader($stream)
        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        if ($peOffset -le 0 -or $peOffset -ge $stream.Length) { return 0 }
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) { return 0 }
        return $reader.ReadUInt16()
    } finally {
        $stream.Dispose()
    }
}

function Test-X86Runtime {
    <#
    .SYNOPSIS
        Confirms an x86 .NET 9 runtime the Reloaded loader can use is installed.
    .DESCRIPTION
        The loader's runtimeconfig asks for Microsoft.NETCore.App 9.0.0 with
        rollForward LatestMinor, so ANY installed 9.0.x satisfies it. Do not pin a
        patch version here; an earlier revision of this repo hardcoded 9.0.18,
        which was never installed and made the launcher throw.
    #>
    [CmdletBinding()]
    param([string]$DotnetX86Root = (Join-Path ${env:ProgramFiles(x86)} 'dotnet'))

    $shared = Join-Path $DotnetX86Root 'shared\Microsoft.NETCore.App'
    if (-not (Test-Path -LiteralPath $shared)) {
        return [pscustomobject]@{ Satisfied = $false; Found = @(); Root = $DotnetX86Root }
    }
    $nine = Get-ChildItem $shared -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^9\.' } |
        Select-Object -ExpandProperty Name
    return [pscustomobject]@{
        Satisfied = (@($nine).Count -gt 0)
        Found     = @($nine)
        Root      = $DotnetX86Root
    }
}

Export-ModuleMember -Function Get-ModRepositoryRoot, Get-GameRoot, Get-PortableReloadedRoot,
    Get-AccessibilityRoot, Get-LauncherRoot, Get-VendoredReloadedRoot,
    Get-SupportedGameSha256, Get-PeMachine, Test-X86Runtime
