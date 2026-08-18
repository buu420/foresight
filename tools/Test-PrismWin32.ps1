[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$dll = Join-Path $PSScriptRoot '..\native\prism\v0.17.3\win-x86\prism.dll'
$vsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vsInstall = (& $vsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath).Trim()
$vsDevCmd = Join-Path $vsInstall 'Common7\Tools\VsDevCmd.bat'
if (-not (Test-Path -LiteralPath $vsDevCmd)) { throw 'Visual Studio 2022 C++ x86 tools are not installed.' }
$temp = Join-Path ([IO.Path]::GetTempPath()) ('chrono-trigger-prism-smoke-' + [Guid]::NewGuid())
New-Item -ItemType Directory -Path $temp | Out-Null

try {
    $source = Join-Path $temp 'prism_smoke.c'
    @'
#include <windows.h>
#include <stdio.h>
typedef void* (__cdecl *init_fn)(void*);
typedef void (__cdecl *shutdown_fn)(void*);
typedef void* (__cdecl *create_best_fn)(void*);
typedef int (__cdecl *output_fn)(void*, const char*, int);
typedef void (__cdecl *free_fn)(void*);
typedef const char* (__cdecl *error_string_fn)(int);
int main(int argc, char** argv) {
  HMODULE dll; void* context; void* backend; int result;
  if (argc != 2) return 64;
  dll = LoadLibraryA(argv[1]);
  if (!dll) { fprintf(stderr, "LoadLibrary failed: %lu\n", GetLastError()); return 65; }
  init_fn init = (init_fn)GetProcAddress(dll, "prism_init");
  shutdown_fn shutdown = (shutdown_fn)GetProcAddress(dll, "prism_shutdown");
  create_best_fn create_best = (create_best_fn)GetProcAddress(dll, "prism_registry_create_best");
  output_fn output = (output_fn)GetProcAddress(dll, "prism_backend_output");
  free_fn free_backend = (free_fn)GetProcAddress(dll, "prism_backend_free");
  error_string_fn error_string = (error_string_fn)GetProcAddress(dll, "prism_error_string");
  if (!init || !shutdown || !create_best || !output || !free_backend || !error_string) return 66;
  context = init(NULL);
  if (!context) { fprintf(stderr, "%s\n", error_string(16)); return 67; }
  backend = create_best(context);
  if (!backend) { fprintf(stderr, "%s\n", error_string(16)); shutdown(context); return 68; }
  result = output(backend, "Chrono Trigger accessibility test", 1);
  free_backend(backend); shutdown(context);
  if (result != 0) { fprintf(stderr, "%s\n", error_string(result)); return 69; }
  return 0;
}
'@ | Set-Content -LiteralPath $source -NoNewline
    $helper = Join-Path $temp 'prism_smoke.exe'
    cmd /c "call `"$vsDevCmd`" -arch=x86 && cl /nologo /TC /W4 /WX /Fo`"$temp\prism_smoke.obj`" /Fe`"$helper`" `"$source`"" | Write-Host
    if ($LASTEXITCODE -ne 0) { throw 'Could not compile the x86 Prism smoke helper.' }
    $stdout = Join-Path $temp 'stdout.txt'
    $stderr = Join-Path $temp 'stderr.txt'
    $process = Start-Process -FilePath $helper -ArgumentList ('"' + (Resolve-Path $dll) + '"') -PassThru -NoNewWindow -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if (-not $process.WaitForExit(15000)) { $process.Kill(); throw 'Prism smoke helper timed out after 15 seconds.' }
    $nativeOutput = (Get-Content -LiteralPath $stdout,$stderr -ErrorAction SilentlyContinue) -join [Environment]::NewLine
    if ($nativeOutput) { Write-Host $nativeOutput }
    if ($process.ExitCode -ne 0) { throw "Prism smoke helper failed with exit code $($process.ExitCode)." }
    Write-Host 'Prism x86 smoke test passed.'
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
