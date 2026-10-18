@echo off
setlocal
set "collector=%~dp0Collect-Foresight-CrashReport.ps1"
if not exist "%collector%" set "collector=%~dp0Accessibility\Support\CrashCollector\Collect-Foresight-CrashReport.ps1"
if not exist "%collector%" (
    echo Foresight collector script was not found. Extract all files from the collector ZIP first.
    pause
    exit /b 1
)
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%collector%" %*
set "collectorResult=%errorlevel%"
pause
exit /b %collectorResult%
