@echo off
rem =====================================================================
rem  FortOS Debian 12 ISO - cleanup entry point
rem  Thin launcher for clean-iso.ps1 (removes intermediate build artifacts,
rem  keeps the final ISO in artifacts\iso).
rem
rem  Usage:
rem    clean-iso.cmd                  clean with the WSL default distro
rem    clean-iso.cmd -WslDistro Ubuntu
rem    clean-iso.cmd -DryRun          preview only
rem
rem  All arguments are forwarded to clean-iso.ps1.
rem  See header comment of clean-iso.ps1 for details.
rem =====================================================================
setlocal
set "SCRIPT_DIR=%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%clean-iso.ps1" %*
set "EXIT_CODE=%ERRORLEVEL%"
endlocal & exit /b %EXIT_CODE%
