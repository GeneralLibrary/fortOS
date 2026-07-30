#!/usr/bin/env bash
# =============================================================================
# GORT Debian 12 — Update Package List from GORT Source
# =============================================================================
# Scans the GORT source code for external command invocations and updates
# the package manifest. Run this when GORT adds new features.
#
# Usage:
#   ./update-package-list.sh [--gort-repo /path/to/gort]
# =============================================================================
set -euo pipefail

readonly SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
readonly REPO_ROOT="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
readonly GORT_REPO="${GORT_REPO:-$(cd -- "${SCRIPT_DIR}/../.." && pwd)}"
readonly CONFIG_DIR="${REPO_ROOT}/config"

echo "=== GORT Dependency Scanner ==="
echo "  GORT repo: ${GORT_REPO}"
echo "  Config:    ${CONFIG_DIR}"

# -----------------------------------------------------------------------------
# Known command -> Debian package mapping
# -----------------------------------------------------------------------------
declare -A CMD_TO_PKG
CMD_TO_PKG=(
    # Storage
    ["lsblk"]="util-linux"
    ["wipefs"]="util-linux"
    ["findmnt"]="util-linux"
    ["parted"]="parted"
    ["mdadm"]="mdadm"
    ["smartctl"]="smartmontools"
    ["mount"]="util-linux"
    ["umount"]="util-linux"
    ["mkfs.ext4"]="e2fsprogs"
    ["mkfs.xfs"]="xfsprogs"
    ["mkfs.btrfs"]="btrfs-progs"
    ["btrfs"]="btrfs-progs"
    ["zfs"]="zfsutils-linux"
    ["zpool"]="zfsutils-linux"
    ["xfs_quota"]="xfsprogs"
    ["df"]="coreutils"
    ["dd"]="coreutils"
    ["lvcreate"]="lvm2"
    ["lvremove"]="lvm2"
    ["vgcreate"]="lvm2"
    ["pvcreate"]="lvm2"
    ["dmsetup"]="dmsetup"
    ["cryptsetup"]="cryptsetup"

    # File sharing
    ["smbd"]="samba"
    ["nmbd"]="samba"
    ["smbpasswd"]="samba-common-bin"
    ["exportfs"]="nfs-kernel-server"
    ["rpcbind"]="rpcbind"
    ["vsftpd"]="vsftpd"

    # Network
    ["ip"]="iproute2"
    ["nft"]="nftables"
    ["iptables"]="iptables"
    ["netplan"]="netplan.io"
    ["NetworkManager"]="network-manager"
    ["nmcli"]="network-manager"

    # Process management
    ["systemctl"]="systemd"
    ["kill"]="procps"
    ["ps"]="procps"

    # Backup
    ["rsync"]="rsync"
    ["rclone"]="rclone"

    # Docker
    ["docker"]="docker-ce-cli"
    ["containerd"]="containerd.io"

    # General
    ["curl"]="curl"
    ["wget"]="wget"
    ["ssh"]="openssh-server"
    ["getent"]="libc-bin"  # Part of glibc
    ["sudo"]="sudo"
    ["vcgencmd"]="libraspberrypi-bin"  # Raspberry Pi only
)

# -----------------------------------------------------------------------------
# Scan GORT C# source for command invocations
# -----------------------------------------------------------------------------
scan_commands() {
    echo ""
    echo "=== Scanning GORT source for command invocations ==="

    local found_pkgs=()

    # Look for shells out to external commands in C# files
    for cmd in "${!CMD_TO_PKG[@]}"; do
        local matches
        matches=$(grep -r "\"${cmd}\"" "${GORT_REPO}/src" --include="*.cs" -l 2>/dev/null | wc -l || echo "0")
        if [ "$matches" -gt 0 ]; then
            local pkg="${CMD_TO_PKG[$cmd]}"
            echo "  Found: $cmd (provided by $pkg) — $matches file(s)"
            found_pkgs+=("$pkg")
        fi
    done

    # Also check the Dockerfile for apt packages
    echo ""
    echo "=== Scanning Dockerfile ==="
    if [ -f "${GORT_REPO}/Dockerfile" ]; then
        grep "apt-get install" "${GORT_REPO}/Dockerfile" -A 30 | grep -oP '^\s+\K\S+' | \
            grep -v "^#" | grep -v "update\|rm\|clean\|&&\|\\\\" | sort -u | while read -r pkg; do
            echo "  Dockerfile: $pkg"
        done
    fi

    # Check gort.list.chroot
    echo ""
    echo "=== Scanning gort.list.chroot ==="
    local chroot_list="${GORT_REPO}/eng/iso/config/package-lists/gort.list.chroot"
    if [ -f "${chroot_list}" ]; then
        grep -v "^#" "${chroot_list}" | grep -v "^$" | while read -r pkg; do
            echo "  ISO chroot: $pkg"
        done
    fi

    # Unique packages
    local unique_pkgs
    unique_pkgs=$(printf '%s\n' "${found_pkgs[@]}" | sort -u)
    echo ""
    echo "=== Summary: $(echo "$unique_pkgs" | wc -l) unique Debian packages required ==="
    echo "$unique_pkgs" | sed 's/^/  /'
}

# -----------------------------------------------------------------------------
# Check for new packages not yet in the manifest
# -----------------------------------------------------------------------------
check_missing() {
    echo ""
    echo "=== Checking for packages missing from manifest ==="

    if [ ! -f "${CONFIG_DIR}/gort-packages.list" ]; then
        echo "  Manifest not found. Run bootstrap-debian12.sh first."
        return
    fi

    # Extract all found packages from scan
    local scanned=()
    for cmd in "${!CMD_TO_PKG[@]}"; do
        if grep -r "\"${cmd}\"" "${GORT_REPO}/src" --include="*.cs" -q 2>/dev/null; then
            scanned+=("${CMD_TO_PKG[$cmd]}")
        fi
    done

    # Check each against the manifest
    for pkg in $(printf '%s\n' "${scanned[@]}" | sort -u); do
        if ! grep -q "^\[.*\] ${pkg}$" "${CONFIG_DIR}/gort-packages.list" 2>/dev/null; then
            echo -e "  \033[1;33m[NEW] $pkg not in manifest — add to gort-packages.list\033[0m"
        fi
    done
}

# -----------------------------------------------------------------------------
# Main
# -----------------------------------------------------------------------------
case "${1:-scan}" in
    scan)
        scan_commands
        check_missing
        ;;
    check)
        check_missing
        ;;
    *)
        echo "Usage: $0 {scan|check}"
        echo ""
        echo "  scan   — Scan GORT source and check for missing packages"
        echo "  check  — Only check for missing packages (faster)"
        ;;
esac
