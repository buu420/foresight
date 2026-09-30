[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath
& (Join-Path $PSScriptRoot 'Build-Native.ps1') -Configuration Release
$output = Join-Path $repo '.build\native\WinmmForwardingTests.exe'
$source = Join-Path $repo 'tests\native\WinmmForwardingTests.cpp'
$obj = Join-Path $repo '.build\native\WinmmForwardingTests.obj'
& cl.exe /nologo /std:c++17 /EHsc /DUNICODE /D_UNICODE /MT $source "/Fe$output" "/Fo$obj" /link /SUBSYSTEM:CONSOLE /MANIFEST:EMBED "/MANIFESTUAC:level='asInvoker' uiAccess='false'"
if ($LASTEXITCODE -ne 0) { throw 'Native forwarding test compilation failed.' }
$fixture = Join-Path $repo '.build\native\WinmmInjectedFixture.dll'
& cl.exe /nologo /std:c++17 /EHsc /DUNICODE /D_UNICODE /MT (Join-Path $repo 'tests/native/WinmmInjectedFixture.cpp') "/Fe$fixture" ('/Fo' + (Join-Path $repo '.build/native/WinmmInjectedFixture.obj')) /link /DLL /MANIFEST:EMBED
if ($LASTEXITCODE -ne 0) { throw 'Injected fixture compilation failed.' }
& $output (Join-Path $repo '.build\native\winmm.dll') $fixture
if ($LASTEXITCODE -ne 0) { throw "Native forwarding tests failed: $LASTEXITCODE" }
