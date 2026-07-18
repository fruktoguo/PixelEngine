@echo off
setlocal EnableExtensions
cd /d "%~dp0"

where pwsh.exe >nul 2>&1
if errorlevel 1 goto :missing_pwsh

pwsh.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\run-final-output-one-click.ps1" %*
set "EXIT_CODE=%ERRORLEVEL%"
if /i "%~1"=="-CheckOnly" exit /b %EXIT_CODE%

echo.
echo Press any key to close this window.
pause >nul
exit /b %EXIT_CODE%

:missing_pwsh
echo [FAILED] PowerShell 7 was not found. Install pwsh.exe and try again.
echo.
echo Press any key to close this window.
pause >nul
exit /b 1
