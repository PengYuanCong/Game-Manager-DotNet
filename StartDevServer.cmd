@echo off
setlocal

set "ROOT=%~dp0"
set "PORT=5214"

if /I "%~1"=="-NoRun" (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%ROOT%StartDevServer.ps1" -NoRun
    exit /b %ERRORLEVEL%
)

if /I "%~1"=="-Port" (
    set "PORT=%~2"
)

echo Starting Proposal dev server at http://localhost:%PORT%
echo Project: %ROOT%Proposal\Proposal.csproj
echo Logs: %ROOT%proposal-dev.out.log / proposal-dev.err.log

wscript.exe "%ROOT%StartDevServer.vbs" -Port "%PORT%"
