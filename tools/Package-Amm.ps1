[CmdletBinding()]
param([string]$PortableRoot, [string]$CatalogRoot, [string]$OutputDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-ForesightAmmCatalog {
    param([Parameter(Mandatory=$true)][object]$Catalog)
    $games = @($Catalog.games | Where-Object gameId -eq 'chrono-trigger')
    if ($games.Count -ne 1 -or $Catalog.pluginId -ne 'buu420') { throw 'The AMM catalog must have one chrono-trigger game under plugin buu420.' }
    $game = $games[0]
    if ($null -ne $game.defaultPostInstall -or $null -ne $game.defaultPostUninstall) {
        throw 'Registry-free Foresight requires null post-install and post-uninstall hooks.'
    }
    $hook = $game.defaultPreInstall
    if ($null -eq $hook -or $hook.executable -ne 'files/Manager-PreInstall.ps1' -or
        $hook.needsAdmin -ne $false -or $hook.failureFatal -ne $true -or
        $hook.installToGameFolder -ne $false -or $hook.runFromGameFolder -ne $false -or $hook.runOnUpdate -ne $true) {
        throw 'Configure the Foresight staging-only fatal pre-install hook with no unconditional elevation, and run it on updates.'
    }
    foreach ($property in @('what','why','modifies')) {
        if ([string]::IsNullOrWhiteSpace($hook.$property)) { throw "The AMM pre-install hook must describe '$property'." }
    }
}

function Update-ForesightAmmManifest {
    param([Parameter(Mandatory=$true)][string]$ZipPath)
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $path = (Resolve-Path -LiteralPath $ZipPath).ProviderPath
    # Hold an exclusive handle throughout validation and the manifest-only update.
    # Read mode leaves rejected or already-correct packages byte-for-byte intact.
    $file = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $archive = $null
    try {
        $archive = [IO.Compression.ZipArchive]::new($file, [IO.Compression.ZipArchiveMode]::Read, $true)
        $manifestEntries = @($archive.Entries | Where-Object FullName -ieq 'manifest.json')
        $hookEntries = @($archive.Entries | Where-Object FullName -ieq 'files/Manager-PreInstall.ps1')
        if ($manifestEntries.Count -ne 1 -or $manifestEntries[0].FullName -cne 'manifest.json' -or
            $hookEntries.Count -ne 1 -or $hookEntries[0].FullName -cne 'files/Manager-PreInstall.ps1') {
            throw 'The AMM ZIP must contain one manifest.json and one staging pre-install script.'
        }
        $reader = [IO.StreamReader]::new($manifestEntries[0].Open())
        try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        # Validate the existing lifecycle contract; this repair changes only the
        # redundant copy action, never identity, hooks, dependencies or checks.
        $catalog = [pscustomobject]@{
            pluginId = $manifest.pluginId
            games = @([pscustomobject]@{
                gameId = $manifest.gameId
                defaultPreInstall = $manifest.preInstall
                defaultPostInstall = $manifest.postInstall
                defaultPostUninstall = $manifest.postUninstall
            })
        }
        Assert-ForesightAmmCatalog -Catalog $catalog
        if ($manifest.installActions -isnot [array]) { throw 'The AMM manifest installActions must be an array.' }
        $actions = @($manifest.installActions)
        $redundant = @($actions | Where-Object {
            $_.type -ceq 'copyFile' -and $_.source -ceq 'Manager-PreInstall.ps1' -and $_.target -ceq 'Manager-PreInstall.ps1'
        })
        if ($redundant.Count -gt 1) { throw 'The AMM manifest contains duplicate staging-script copy actions.' }
        if ($redundant.Count -eq 1) {
            $manifest.installActions = @($actions | Where-Object { -not [object]::ReferenceEquals($_, $redundant[0]) })
            $json = ($manifest | ConvertTo-Json -Depth 100) + [Environment]::NewLine
            $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
            $archive.Dispose()
            $archive = $null
            $file.Position = 0
            $archive = [IO.Compression.ZipArchive]::new($file, [IO.Compression.ZipArchiveMode]::Update, $true)
            $stream = $archive.GetEntry('manifest.json').Open()
            try {
                $stream.SetLength(0)
                $stream.Write($bytes, 0, $bytes.Length)
            } finally { $stream.Dispose() }
        }
        [pscustomobject]@{ ZipPath = $path; RemovedActions = $redundant.Count; RemainingActions = @($manifest.installActions).Count }
    } finally {
        if ($null -ne $archive) { $archive.Dispose() }
        $file.Dispose()
    }
}

function New-ForesightAmmPackage {
    param([Parameter(Mandatory=$true)][string]$PortableRoot, [Parameter(Mandatory=$true)][string]$CatalogRoot, [Parameter(Mandatory=$true)][string]$OutputDirectory)
    $repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath
    $portable = (Resolve-Path -LiteralPath $PortableRoot).ProviderPath
    $catalog = (Resolve-Path -LiteralPath $CatalogRoot).ProviderPath
    $output = [IO.Path]::GetFullPath($OutputDirectory)
    if (Test-Path -LiteralPath $output) { throw 'Use a fresh AMM output directory; existing files are preserved.' }
    $null = Get-Command amm-author -CommandType Application -ErrorAction Stop
    Assert-ForesightAmmCatalog -Catalog (Get-Content -LiteralPath (Join-Path $catalog 'index.json') -Raw | ConvertFrom-Json)
    . (Join-Path $repo 'tools\release\Setup-Foresight.ps1')
    $entries = @(Assert-ForesightPackage -GameRoot $portable)
    $listed = @{}
    foreach ($entry in $entries) { $listed[$entry.Path] = $true }
    $manifest = Join-Path $portable 'Foresight-SHA256SUMS.txt'
    foreach ($file in Get-ChildItem -LiteralPath $portable -File -Recurse -Force) {
        if ($file.FullName -ne $manifest -and -not $listed.ContainsKey($file.FullName)) { throw "Unlisted file in portable payload: $($file.FullName)" }
    }
    if (Get-ChildItem -LiteralPath $portable -Filter '*Installer.exe' -File -Recurse) { throw 'Do not package a registry-creating installer.' }
    $config = Get-Content -LiteralPath (Join-Path $portable 'Reloaded-II\Mods\chrono.trigger.accessibility\ModConfig.json') -Raw | ConvertFrom-Json
    if ($config.ModId -ne 'chrono.trigger.accessibility') { throw 'Unexpected Foresight mod identity.' }
    $version = $config.ModVersion
    $source = Join-Path $output 'source'
    New-Item -ItemType Directory -Path $source -Force | Out-Null
    Get-ChildItem -LiteralPath $portable -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $source -Recurse }
    # Retain the staging hook in the ZIP; omit its redundant install action below.
    Copy-Item -LiteralPath (Join-Path $repo 'tools\amm\Manager-PreInstall.ps1') -Destination $source
    $zip = Join-Path $output "Foresight-v$version-beta-amm.zip"
    & amm-author package build --source $source --game chrono-trigger --version $version --output $zip --project $catalog --json
    if ($LASTEXITCODE -ne 0) { throw 'AMM package build failed.' }
    $null = Update-ForesightAmmManifest -ZipPath $zip
    & amm-author package validate --zip $zip --plugin buu420 --game chrono-trigger --version $version --json
    if ($LASTEXITCODE -ne 0) { throw 'AMM package validation failed.' }
    Write-Output $zip
}

if ($MyInvocation.InvocationName -ne '.') {
    if (-not $PortableRoot -or -not $CatalogRoot -or -not $OutputDirectory) { throw 'PortableRoot, CatalogRoot and OutputDirectory are required.' }
    New-ForesightAmmPackage -PortableRoot $PortableRoot -CatalogRoot $CatalogRoot -OutputDirectory $OutputDirectory
}
