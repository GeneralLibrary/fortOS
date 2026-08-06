#Requires -Version 5.1
<#
.SYNOPSIS
    Clean up all intermediate artifacts produced by the FortOS ISO build
    (build-iso.cmd / build-iso.ps1).

.DESCRIPTION
    Removes the files created during an ISO build while KEEPING the final
    artifacts and the WSL system state:

      [Windows side]
        %TEMP%\fortos-iso-build      WSL staging scripts
        %TEMP%\fortos-iso-src.tar    source tar archive
        <repo>\artifacts\iso-build   intermediate build root (if any)

      [WSL side]
        $HOME/fortos-iso             source copy + all intermediate build
                                     output (publish/, live/, nuget/, ...)

    Not touched by design:
        artifacts\iso                final .iso and .sha256 (kept)
        WSL /etc/apt                 docker keyring / sources / apt pin (kept)

.PARAMETER WslDistro
    WSL distribution to use, e.g. 'Debian'. When omitted, the WSL default
    distribution is used. Pass the same distro as the build.

.PARAMETER DryRun
    Only print what would be deleted, without deleting anything.

.EXAMPLE
    clean-iso.cmd                  # clean with the WSL default distro
    clean-iso.cmd -WslDistro Ubuntu
    clean-iso.ps1 -DryRun          # preview only

.NOTES
    Requires WSL with the same distribution used for the build. The WSL
    cleanup runs as root (mirrors build-iso.ps1).
#>
[CmdletBinding()]
param(
    [string]$WslDistro,
    [switch]$DryRun
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$RepoRoot = $PSScriptRoot
$WslUserName = 'root'                     # matches build-iso.ps1
$TempDir = Join-Path $env:TEMP 'fortos-iso-build'
$TarFile = Join-Path $env:TEMP 'fortos-iso-src.tar'
$WindowsBuildRoot = Join-Path $RepoRoot 'artifacts\iso-build'
$IsoOutputDir = Join-Path $RepoRoot 'artifacts\iso'

# =====================================================================
# Helpers
# =====================================================================
function Write-Result {
    param([string]$Path, [string]$State)
    $Color = switch ($State) {
        'deleted'     { 'Green' }
        'skipped'     { 'Yellow' }
        'will-delete' { 'Cyan' }
        default       { 'White' }
    }
    Write-Host ('[{0}] {1}' -f $State.PadRight(11), $Path) -ForegroundColor $Color
}

function Get-WslBaseArgs {
    $a = @()
    if ($WslDistro) { $a += @('-d', $WslDistro) }
    $a += @('-u', $WslUserName)
    return $a
}

function ConvertTo-WslPath {
    param([string]$WinPath)
    $Full = [System.IO.Path]::GetFullPath($WinPath)
    $Drive = $Full.Substring(0, 1).ToLower()
    return "/mnt/$Drive/" + ($Full.Substring(3) -replace '\\', '/')
}

# Write a bash script (LF line endings) into the temp dir.
function New-WslScript {
    param([string]$Name, [string]$Content)
    New-Item -ItemType Directory -Force -Path $TempDir | Out-Null
    $Path = Join-Path $TempDir $Name
    $Content = $Content -replace "`r`n", "`n"
    [System.IO.File]::WriteAllText($Path, $Content, (New-Object System.Text.UTF8Encoding($false)))
    return $Path
}

# Run a bash script inside WSL as root, fully silent; returns the exit code.
function Invoke-WslSilent {
    param([string]$ScriptPath)
    $OldEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & wsl.exe @(Get-WslBaseArgs) @('bash', (ConvertTo-WslPath $ScriptPath)) 2>&1 | Out-Null
        return $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $OldEap
    }
}

# =====================================================================
# Main
# =====================================================================
Write-Host '=== FortOS ISO build cleanup ===' -ForegroundColor Cyan
Write-Host "Repository root: $RepoRoot"
if ($DryRun) { Write-Host '(DryRun) Nothing will actually be deleted.' -ForegroundColor Yellow }
Write-Host ''

# --- WSL probe: can we reach the target distro as root? ---
if (-not (Get-Command wsl.exe -ErrorAction SilentlyContinue)) {
    Write-Host 'Error: wsl.exe not found. Enable WSL and install a Linux distribution (wsl --install).' -ForegroundColor Red
    exit 1
}
$ProbeScript = New-WslScript 'clean-probe.sh' @'
#!/usr/bin/env bash
export PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/usr/lib/wsl/lib
echo wsl-probe-ok
'@
$ProbeCode = Invoke-WslSilent $ProbeScript
if ($ProbeCode -ne 0) {
    $Hint = if ($WslDistro) { " Check the distro name (wsl --list) and pass -WslDistro." } else { ' Check that WSL has a default distribution (wsl --list).' }
    Write-Host "Error: cannot run WSL as root user '$WslUserName'.$Hint" -ForegroundColor Red
    exit 1
}

Write-Host '=== Windows side ===' -ForegroundColor Cyan

# %TEMP%\fortos-iso-build (WSL staging scripts)
if (Test-Path $TempDir) {
    Write-Result '%TEMP%\fortos-iso-build (WSL staging scripts)' $(if ($DryRun) { 'will-delete' } else { 'deleted' })
    if (-not $DryRun) { Remove-Item $TempDir -Recurse -Force }
} else {
    Write-Result '%TEMP%\fortos-iso-build (WSL staging scripts)' 'skipped'
}

# %TEMP%\fortos-iso-src.tar (source tar archive)
if (Test-Path $TarFile) {
    Write-Result '%TEMP%\fortos-iso-src.tar (source tar archive)' $(if ($DryRun) { 'will-delete' } else { 'deleted' })
    if (-not $DryRun) { Remove-Item $TarFile -Force }
} else {
    Write-Result '%TEMP%\fortos-iso-src.tar (source tar archive)' 'skipped'
}

# <repo>\artifacts\iso-build (intermediate build root)
if (Test-Path $WindowsBuildRoot) {
    Write-Result 'artifacts\iso-build (intermediate build root)' $(if ($DryRun) { 'will-delete' } else { 'deleted' })
    if (-not $DryRun) { Remove-Item $WindowsBuildRoot -Recurse -Force }
} else {
    Write-Result 'artifacts\iso-build (intermediate build root)' 'skipped'
}

Write-Host ''
Write-Host '=== WSL side ===' -ForegroundColor Cyan

# $HOME/fortos-iso (source copy + all intermediate build output inside WSL)
if ($DryRun) {
    Write-Result '$HOME/fortos-iso (WSL: source copy + build output)' 'will-delete'
} else {
    # Detect existence, delete, and report the outcome in one bash -c call.
    $WslClean = 'if [ -d "$HOME/fortos-iso" ]; then rm -rf "$HOME/fortos-iso" && echo FORTOS_WSL_CLEAN:deleted; else echo FORTOS_WSL_CLEAN:skipped; fi'
    $OldEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $Out = (& wsl.exe @(Get-WslBaseArgs) bash -c $WslClean 2>&1 | Out-String)
        $Code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $OldEap
    }
    if ($Code -ne 0) {
        Write-Host "Error: failed to clean WSL (exit code $Code)." -ForegroundColor Red
        exit $Code
    }
    if ($Out -match 'deleted') {
        Write-Result '$HOME/fortos-iso (WSL: source copy + build output)' 'deleted'
    } else {
        Write-Result '$HOME/fortos-iso (WSL: source copy + build output)' 'skipped'
    }
}

Write-Host ''
Write-Host '=== Cleanup finished ===' -ForegroundColor Green
Write-Host "Final ISO kept: $IsoOutputDir (not removed by design)"
Write-Host 'WSL /etc/apt docker sources kept (not removed by design)'
