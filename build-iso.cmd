@echo off
rem =====================================================================
rem  FortOS Debian 12 ISO - Windows build entry point
rem  Thin launcher for build-iso.ps1 (Docker Desktop based build).
rem
rem  Usage:
rem    build-iso.cmd                     auto version, output to artifacts\iso
rem    build-iso.cmd -Version v1.2.3     explicit version
rem    build-iso.cmd -DryRun             print the docker command only
rem
rem  All arguments are forwarded to build-iso.ps1.
rem  See header comment of build-iso.ps1 for prerequisites and options.
rem =====================================================================
setlocal
set "SCRIPT_DIR=%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%build-iso.ps1" %*
set "EXIT_CODE=%ERRORLEVEL%"
endlocal & exit /b %EXIT_CODE%
