[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath
& (Join-Path $PSScriptRoot 'Build-Native.ps1') -Configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
$output = Join-Path $repo '.build\native\InstallerRegistryTests.exe'
$source = Join-Path $repo 'tests\native\InstallerRegistryTests.cpp'
$obj = Join-Path $repo '.build\native\InstallerRegistryTests.obj'
& cl.exe /nologo /std:c++17 /EHsc /DUNICODE /D_UNICODE /MT $source "/Fe$output" "/Fo$obj" /link /SUBSYSTEM:CONSOLE /ENTRY:mainCRTStartup /MANIFEST:EMBED "/MANIFESTUAC:level='asInvoker' uiAccess='false'"
if ($LASTEXITCODE -ne 0) { throw 'Native registry test compilation failed.' }
& $output
if ($LASTEXITCODE -ne 0) { throw "Native registry tests failed: $LASTEXITCODE" }
