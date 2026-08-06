#Requires -Version 5.1
<#
.SYNOPSIS
    Build the FortOS Debian 12 ISO image on Windows via WSL (native WSL build,
    no Docker required).

.DESCRIPTION
    live-build / debootstrap / xorriso / mksquashfs only run on Linux, so the
    ISO is built inside a WSL distribution. This script:

      1. Checks the environment (WSL, root access, tools, disk, network).
      2. Copies the repository from the Windows side into WSL
         ($HOME/fortos-iso, root user) via a tar archive, excluding build
         artifacts. Building inside WSL avoids 9p filesystem issues (chroot
         mounts need real Linux permissions) and slow /mnt I/O.
      3. Runs eng/iso/build-local-wsl.sh inside WSL (the existing native WSL
         build path, equivalent to eng/iso/build-in-container.sh).
      4. Copies the resulting ISO and .sha256 back to artifacts/iso (override
         with -OutputDir).

    Environment checks run first:
    [required] WSL installed with a usable distribution and root access
    [optional] build tools inside WSL (live-build, xorriso, ...), dotnet SDK 10,
               >= 20 GB free disk, reachable Debian/Docker hosts

.PARAMETER Version
    Version embedded in the ISO file name and in /etc/fortos/version. When
    omitted, `git describe --tags --always --dirty` is used; falls back to
    'dev' when git is unavailable or the checkout is not a git repo.

.PARAMETER OutputDir
    Output directory on the Windows side, default <repo root>/artifacts/iso.

.PARAMETER WslDistro
    WSL distribution to use, e.g. 'Debian'. When omitted, the WSL default
    distribution is used. All build steps run as root inside that distro.

.PARAMETER DryRun
    Only print the commands that would be executed (environment checks still
    run first).

.EXAMPLE
    build-iso.cmd                      # auto version, WSL default distro
    build-iso.cmd -Version v1.2.3      # explicit version
    build-iso.cmd -WslDistro Ubuntu    # pick a distro
    build-iso.ps1 -DryRun              # preview the commands only

.NOTES
    Prerequisites: WSL with a Linux distribution installed (wsl --list).
    The build runs as root (chroot mounts, apt-get, live-build); WSL allows
    `-u root` without a password.

    Inside WSL the following must be installed (this script warns about
    missing ones):
      apt-get install live-build xorriso squashfs-tools mtools dosfstools curl
      dotnet SDK 10, e.g.:
        curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- \
          --version 10.0.302 --install-dir "$HOME/.dotnet"

    Paths containing spaces are passed as single arguments to wsl.exe and
    inside generated scripts they are quoted. The build takes a long time
    (30-60 min on first run); leave at least 20 GB free in WSL.

    Behavior is equivalent to eng/iso/build-local-wsl.sh (and therefore to
    eng/iso/build-in-container.sh); artifact is named
    fortos-debian12-<version>-amd64.iso.
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$OutputDir,
    [string]$WslDistro,
    [switch]$DryRun
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$RepoRoot = $PSScriptRoot
$WslUserName = 'root'                     # chroot mounts / apt / live-build need root
$TempDir = Join-Path $env:TEMP 'fortos-iso-build'
$TarFile = Join-Path $env:TEMP 'fortos-iso-src.tar'
$ResultFile = Join-Path $TempDir 'wsl-result.txt'
# Windows ships bsdtar at System32; use the full path so a PATH entry such as
# Git's GNU tar can never hijack the call.
$TarExe = Join-Path $env:SystemRoot 'System32\tar.exe'

# =====================================================================
# Helpers
# =====================================================================
function Write-EnvLine {
    param([string]$State, [string]$Text)
    $Color = switch ($State) {
        'OK'   { 'Green' }
        'WARN' { 'Yellow' }
        'ERR'  { 'Red' }
        'INFO' { 'Cyan' }
        default { 'White' }
    }
    Write-Host ('[{0}] {1}' -f $State.PadRight(4), $Text) -ForegroundColor $Color
}

function Test-TcpPort {
    param([string]$HostName, [int]$Port, [int]$TimeoutMs = 3000)
    $Client = New-Object System.Net.Sockets.TcpClient
    try {
        $Async = $Client.BeginConnect($HostName, $Port, $null, $null)
        if (-not $Async.AsyncWaitHandle.WaitOne($TimeoutMs)) { return $false }
        $Client.EndConnect($Async)
        return $true
    } catch {
        return $false
    } finally {
        $Client.Close()
    }
}

function ConvertTo-WslPath {
    param([string]$WinPath)
    $Full = [System.IO.Path]::GetFullPath($WinPath)
    $Drive = $Full.Substring(0, 1).ToLower()
    return "/mnt/$Drive/" + ($Full.Substring(3) -replace '\\', '/')
}

function Get-WslBaseArgs {
    $a = @()
    if ($WslDistro) { $a += @('-d', $WslDistro) }
    $a += @('-u', $WslUserName)
    return $a
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

# Run a bash script inside WSL as root; output streams to the console.
# External commands writing to stderr would raise NativeCommandError under
# $ErrorActionPreference='Stop', so EAP is lowered and streams are merged.
function Invoke-WslScript {
    param([string]$ScriptPath)
    $OldEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & wsl.exe @(Get-WslBaseArgs) @('bash', (ConvertTo-WslPath $ScriptPath)) 2>&1 | Out-Host
        return $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $OldEap
    }
}

# Like Invoke-WslScript but fully silent (results are written to a file by
# the script itself); returns the exit code.
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
# Environment check
# =====================================================================
$EnvErrors = @()
$EnvWarnings = @()

Write-Host '=== FortOS Debian 12 ISO Build (Windows / WSL native) ===' -ForegroundColor Cyan
Write-Host "Repository root: $RepoRoot"
Write-Host ''
Write-Host '=== 1/4 Environment check ===' -ForegroundColor Cyan
Write-EnvLine OK "PowerShell $($PSVersionTable.PSVersion)"

# --- Hard checks: all required, abort on failure ---
if (-not (Get-Command wsl.exe -ErrorAction SilentlyContinue)) {
    $EnvErrors += 'wsl.exe not found. Enable WSL and install a Linux distribution (wsl --install).'
    Write-EnvLine ERR 'wsl.exe not found -> enable WSL (wsl --install) and install a distribution'
}

if (-not $EnvErrors) {
    # Probe: can we reach the target distro as root?
    $ProbeScript = New-WslScript 'stage0-probe.sh' @'
#!/usr/bin/env bash
export PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/usr/lib/wsl/lib
cd "$HOME"
echo wsl-probe-ok
'@
    $ProbeCode = Invoke-WslSilent $ProbeScript
    if ($ProbeCode -ne 0) {
        $Hint = if ($WslDistro) { " Check the distro name (wsl --list) and pass -WslDistro." } else { ' Check that WSL has a default distribution (wsl --list).' }
        $EnvErrors += "Cannot run WSL as root user '$WslUserName'.$Hint"
        Write-EnvLine ERR "Cannot run WSL as root (distro: $(if ($WslDistro) { $WslDistro } else { 'default' }))$Hint"
    } else {
        $DistroName = if ($WslDistro) { $WslDistro } else { 'default' }
        Write-EnvLine OK "WSL distro '$DistroName' is available (root access works)"
    }
}

if ($EnvErrors.Count -gt 0) {
    Write-Host ''
    Write-Host 'The following environment problems prevent the build:' -ForegroundColor Red
    $EnvErrors | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    Write-Host 'Please resolve the issues above and run this script again.' -ForegroundColor Yellow
    exit 1
}

# --- Soft checks: informational only, non-blocking ---
# Tools inside WSL
$ToolTemplate = @'
#!/usr/bin/env bash
export PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/usr/lib/wsl/lib
cd "$HOME"
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
OUT="__RESULT_WSL__"
: > "$OUT"
for c in lb xorriso mksquashfs mtools dosfstools curl apt-get git dotnet; do
  if command -v "$c" >/dev/null 2>&1; then echo "TOOL_OK $c" >> "$OUT"; else echo "TOOL_MISSING $c" >> "$OUT"; fi
done
'@
$ToolTemplate = $ToolTemplate.Replace('__RESULT_WSL__', (ConvertTo-WslPath $ResultFile))
$ToolScript = New-WslScript 'stage1-tools.sh' $ToolTemplate
$ToolCode = Invoke-WslSilent $ToolScript
if ($ToolCode -eq 0 -and (Test-Path $ResultFile)) {
    $MissingTools = @()
    foreach ($Line in [System.IO.File]::ReadAllLines($ResultFile)) {
        if ($Line -like 'TOOL_OK *') {
            Write-EnvLine OK ("WSL tool present: " + $Line.Substring(8))
        } else {
            $MissingTools += $Line.Substring(13)
        }
    }
    if ($MissingTools.Count -gt 0) {
        $AptMissing = @($MissingTools | Where-Object { $_ -ne 'dotnet' })
        if ($AptMissing.Count -gt 0) {
            $PkgList = ($AptMissing -join ' ')
            $EnvWarnings += "WSL is missing: $PkgList. Install with: apt-get install live-build xorriso squashfs-tools mtools dosfstools curl"
            Write-EnvLine WARN "WSL tools missing: $PkgList -> apt-get install live-build xorriso squashfs-tools mtools dosfstools curl"
        }
        if ($MissingTools -contains 'dotnet') {
            $EnvWarnings += 'WSL is missing the dotnet SDK 10. Install with: curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --version 10.0.302 --install-dir "$HOME/.dotnet"'
            Write-EnvLine WARN 'WSL dotnet SDK 10 missing -> install via dotnet-install.sh into $HOME/.dotnet (see script header)'
        }
    }
} else {
    Write-EnvLine WARN 'Cannot inspect WSL tools; the build may fail if prerequisites are missing'
}

# Free disk space inside WSL
$DfTemplate = @'
#!/usr/bin/env bash
export PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/usr/lib/wsl/lib
cd "$HOME"
df --output=avail / | tail -1 > "__RESULT_WSL__"
'@
$DfTemplate = $DfTemplate.Replace('__RESULT_WSL__', (ConvertTo-WslPath $ResultFile))
$DfScript = New-WslScript 'stage2-df.sh' $DfTemplate
$DfCode = Invoke-WslSilent $DfScript
if ($DfCode -eq 0 -and (Test-Path $ResultFile)) {
    $AvailKb = [double]([System.IO.File]::ReadAllText($ResultFile).Trim())
    $AvailGiB = [math]::Round($AvailKb / 1MB, 1)
    if ($AvailKb -lt 20GB / 1KB) {
        $EnvWarnings += "Only $AvailGiB GB free inside WSL; at least 20 GB is recommended."
        Write-EnvLine WARN "Only $AvailGiB GB free inside WSL (< 20 GB recommended)"
    } else {
        Write-EnvLine OK "$AvailGiB GB free inside WSL"
    }
} else {
    Write-EnvLine WARN 'Cannot read free disk space inside WSL; make sure enough space is available'
}

# Network (Windows side probe)
foreach ($Ep in @(
        @{ Host = 'deb.debian.org';        Port = 443 }
        @{ Host = 'download.docker.com';   Port = 443 }
    )) {
    if (Test-TcpPort -HostName $Ep.Host -Port $Ep.Port) {
        Write-EnvLine OK "reachable $($Ep.Host):443"
    } else {
        $EnvWarnings += "Cannot reach $($Ep.Host):443. The build downloads the .NET SDK and Debian/Docker packages."
        Write-EnvLine WARN "unreachable $($Ep.Host):443 -> dependency downloads may fail during the build"
    }
}

Write-EnvLine INFO 'Source is copied into WSL ($HOME/fortos-iso) and the ISO is copied back to artifacts\iso'

if ($EnvWarnings.Count -gt 0) {
    Write-Host ''
    Write-Host 'Environment hints (non-blocking, please note):' -ForegroundColor Yellow
    $EnvWarnings | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
}
Write-Host ''

# =====================================================================
# 2/4 Resolve version (same git describe logic as build.sh)
# =====================================================================
if (-not $Version) {
    $Version = 'dev'
    try {
        $describe = (& git -C $RepoRoot describe --tags --always --dirty 2>$null) |
            Select-Object -First 1
        if ($describe) { $Version = $describe.Trim() }
    } catch {
        # git unavailable / not a git checkout: keep 'dev'
    }
}
$SafeVersion = $Version -replace '[^a-zA-Z0-9._-]', '-'
$ImageBaseName = "fortos-debian12-$SafeVersion-amd64"
Write-Host "Version: $Version"

# =====================================================================
# 3/4 Resolve output directory (Windows side)
# =====================================================================
if (-not $OutputDir) {
    $OutputDir = Join-Path $RepoRoot 'artifacts\iso'
} else {
    $OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$OutputWsl = ConvertTo-WslPath $OutputDir
Write-Host "Output directory: $OutputDir"

# =====================================================================
# 4/4 Assemble the build commands
# =====================================================================
$VersionForScript = $Version -replace '"', ''

# stage 3: unpack the source archive inside WSL
$UnpackTemplate = @'
#!/usr/bin/env bash
set -Eeuo pipefail
export PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/usr/lib/wsl/lib
cd "$HOME"
rm -rf "$HOME/fortos-iso"
mkdir -p "$HOME/fortos-iso"
tar -xf "__TAR_WSL__" -C "$HOME/fortos-iso"
echo "SOURCE_COPY_OK"
'@
$UnpackTemplate = $UnpackTemplate.Replace('__TAR_WSL__', (ConvertTo-WslPath $TarFile))
$UnpackScript = New-WslScript 'stage3-unpack.sh' $UnpackTemplate

# stage 4: run the native WSL build
$BuildTemplate = @'
#!/usr/bin/env bash
set -Eeuo pipefail
export PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/usr/lib/wsl/lib
cd "$HOME"
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
cd "$HOME/fortos-iso"
VERSION="__VERSION__" bash eng/iso/build-local-wsl.sh
echo "BUILD_DONE"
'@
$BuildTemplate = $BuildTemplate.Replace('__VERSION__', $VersionForScript)
$BuildScript = New-WslScript 'stage4-build.sh' $BuildTemplate

# stage 5: copy artifacts back to the Windows side
$CopyOutTemplate = @'
#!/usr/bin/env bash
set -Eeuo pipefail
export PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/usr/lib/wsl/lib
cd "$HOME"
mkdir -p "__OUTPUT_WSL__"
cp "$HOME/fortos-iso/artifacts/iso/"fortos-debian12-*.iso* "__OUTPUT_WSL__"/
echo "COPY_OUT_DONE"
ls -la "__OUTPUT_WSL__"
'@
$CopyOutTemplate = $CopyOutTemplate.Replace('__OUTPUT_WSL__', $OutputWsl)
$CopyOutScript = New-WslScript 'stage5-copyout.sh' $CopyOutTemplate

$TarExcludes = @(
    '--no-fflags',                        # do not emit SCHILY.fflags headers
    '--exclude=bin', '--exclude=obj', '--exclude=node_modules',
    '--exclude=artifacts', '--exclude=TestResults', '--exclude=.reasonix',
    '--exclude=mnt', '--exclude=.vs', '--exclude=.idea'
)

$TarCommand = "$TarExe -cf `"$TarFile`" -C `"$RepoRoot`" $($TarExcludes -join ' ') ."

Write-Host ''
Write-Host '=== Commands to execute ===' -ForegroundColor Cyan
Write-Host '1) Pack the source (excluding build artifacts, keeping .git):'
Write-Host "   $TarCommand"
Write-Host '2) Copy source into WSL (root):'
Write-Host "   wsl.exe $((Get-WslBaseArgs) -join ' ') bash $(ConvertTo-WslPath $UnpackScript)"
Write-Host '3) Run the native WSL build (eng/iso/build-local-wsl.sh):'
Write-Host "   wsl.exe $((Get-WslBaseArgs) -join ' ') bash $(ConvertTo-WslPath $BuildScript)"
Write-Host '4) Copy ISO + .sha256 back to Windows:'
Write-Host "   wsl.exe $((Get-WslBaseArgs) -join ' ') bash $(ConvertTo-WslPath $CopyOutScript)"
Write-Host ''

if ($DryRun) {
    Write-Host '(DryRun) Build not executed.' -ForegroundColor Yellow
    exit 0
}

try {
    # --- pack the source on the Windows side ---
    Write-Host '=== Step 1/4: packing source ===' -ForegroundColor Cyan
    if (Test-Path $TarFile) { Remove-Item $TarFile -Force }
    & $TarExe -cf $TarFile -C $RepoRoot @($TarExcludes) .
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to pack the source (tar exit code $LASTEXITCODE)."
        exit $LASTEXITCODE
    }
    $TarSize = [math]::Round((Get-Item $TarFile).Length / 1MB, 1)
    Write-Host "Source packed: $TarFile ($TarSize MB)"

    # --- copy source into WSL ---
    Write-Host ''
    Write-Host '=== Step 2/4: copying source into WSL ===' -ForegroundColor Cyan
    $Code = Invoke-WslScript $UnpackScript
    if ($Code -ne 0) {
        Write-Error "Failed to copy the source into WSL (exit code $Code)."
        exit $Code
    }
    Write-Host 'Source copied into WSL ($HOME/fortos-iso).'

    # --- build ---
    Write-Host ''
    Write-Host '=== Step 3/4: building the ISO inside WSL (30-60 min on first run) ===' -ForegroundColor Cyan
    $Code = Invoke-WslScript $BuildScript
    if ($Code -ne 0) {
        Write-Error "Build failed inside WSL (exit code $Code). Inspect the log above."
        exit $Code
    }

    # --- copy artifacts back ---
    Write-Host ''
    Write-Host '=== Step 4/4: copying artifacts back to Windows ===' -ForegroundColor Cyan
    $Code = Invoke-WslScript $CopyOutScript
    if ($Code -ne 0) {
        Write-Error "Failed to copy the artifacts back to Windows (exit code $Code)."
        exit $Code
    }

    $IsoPath = Join-Path $OutputDir "$ImageBaseName.iso"
    if (-not (Test-Path $IsoPath)) {
        Write-Error "Build finished but the expected artifact was not found: $IsoPath"
        exit 1
    }

    Write-Host ''
    Write-Host '=== Build succeeded ===' -ForegroundColor Green
    Write-Host "ISO:     $IsoPath"
    Write-Host "Checksum:  $IsoPath.sha256"
} finally {
    # clean up temp artifacts
    if (Test-Path $TarFile) { Remove-Item $TarFile -Force }
    if (Test-Path $TempDir) { Remove-Item $TempDir -Recurse -Force }
}
