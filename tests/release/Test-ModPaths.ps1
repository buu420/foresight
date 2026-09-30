$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Import-Module (Join-Path $repo 'tools/ModPaths.psm1') -Force
if ((Get-ModRepositoryRoot) -ne $repo) { throw 'Repository root retained a provider prefix or dot segments.' }
$root = Join-Path ([IO.Path]::GetTempPath()) ('Foresight-path-test-' + [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path (Join-Path $root 'source') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $root 'Chrono Trigger.exe'), 'path-only fixture')
    if ((Get-GameRoot -GameRoot (Join-Path $root 'source/..')) -ne $root) { throw 'Explicit game path retained dot segments.' }
    if ((Get-GameRoot -RepositoryRoot (Join-Path $root 'source')) -ne $root) { throw 'Discovered game path retained dot segments.' }
    Write-Output 'PASS canonical repository, explicit game, and discovered game paths.'
} finally {
    $prefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not [IO.Path]::GetFullPath($root).StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup path.' }
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
