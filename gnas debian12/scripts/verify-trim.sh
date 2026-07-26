#!/usr/bin/env bash
# =============================================================================
# GNAS Debian 12 — Verify Trimming
# =============================================================================
# Verifies that the trimmed rootfs has:
#   1. Only whitelisted services enabled
#   2. No desktop/GUI services running
#   3. Expected packages installed
#   4. No unnecessary packages
# =============================================================================
set -euo pipefail

readonly SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
readonly REPO_ROOT="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
readonly ROOTFS_DIR="${REPO_ROOT}/rootfs"
readonly CONFIG_DIR="${REPO_ROOT}/config"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

check_rootfs() {
    if [ ! -d "${ROOTFS_DIR}/etc" ]; then
        echo -e "${RED}[ERROR]${NC} Rootfs not found at ${ROOTFS_DIR}"
        echo "Run bootstrap-debian12.sh first."
        exit 1
    fi
}

verify_services() {
    echo "=== Service Verification ==="

    # Read whitelist
    local whitelist=()
    if [ -f "${CONFIG_DIR}/enabled-services.conf" ]; then
        while IFS= read -r line; do
            # Skip comments and empty lines
            [[ "$line" =~ ^#.*$ ]] && continue
            [[ -z "$line" ]] && continue
            # Extract service name (first word before any comment)
            local svc
            svc=$(echo "$line" | awk '{print $1}')
            [[ -n "$svc" ]] && whitelist+=("$svc")
        done < "${CONFIG_DIR}/enabled-services.conf"
    fi

    # Check services enabled in multi-user.target.wants
    if [ -d "${ROOTFS_DIR}/etc/systemd/system/multi-user.target.wants" ]; then
        local enabled_services
        enabled_services=$(ls "${ROOTFS_DIR}/etc/systemd/system/multi-user.target.wants/" 2>/dev/null || echo "")

        local violations=0
        for svc in $enabled_services; do
            local found=false
            for allowed in "${whitelist[@]}"; do
                if [ "$svc" = "$allowed" ]; then
                    found=true
                    break
                fi
            done

            if ! $found; then
                echo -e "  ${RED}[VIOLATION]${NC} $svc is enabled but NOT in whitelist"
                violations=$((violations + 1))
            else
                echo -e "  ${GREEN}[OK]${NC} $svc"
            fi
        done

        if [ $violations -gt 0 ]; then
            echo -e "  ${RED}$violations service violations found${NC}"
        else
            echo -e "  ${GREEN}All enabled services are whitelisted${NC}"
        fi
    fi
}

verify_no_desktop() {
    echo ""
    echo "=== Desktop/GUI Check ==="

    local desktop_pkgs=(
        "gnome-shell" "kde-plasma-desktop" "xfce4" "lxde"
        "wayland" "xorg" "xserver-xorg" "gdm3" "lightdm" "sddm"
        "libreoffice" "firefox" "chromium" "pulseaudio" "cups"
        "bluetooth" "network-manager-gnome"
    )

    local found_desktop=false
    for pkg in "${desktop_pkgs[@]}"; do
        if grep -q "^Package: ${pkg}$" "${ROOTFS_DIR}/var/lib/dpkg/status" 2>/dev/null; then
            echo -e "  ${RED}[DESKTOP]${NC} $pkg is installed — should be removed"
            found_desktop=true
        fi
    done

    if ! $found_desktop; then
        echo -e "  ${GREEN}No desktop/GUI packages found — clean${NC}"
    fi
}

verify_packages() {
    echo ""
    echo "=== Package Verification ==="

    # Count installed packages
    local pkg_count
    pkg_count=$(grep -c "^Package:" "${ROOTFS_DIR}/var/lib/dpkg/status" 2>/dev/null || echo "0")
    echo "  Installed packages: $pkg_count"

    # Check required packages are present
    local missing=0
    if [ -f "${CONFIG_DIR}/gnas-packages.list" ]; then
        while IFS= read -r line; do
            [[ "$line" =~ ^\[REQUIRED\] ]] || continue
            local pkg
            pkg=$(echo "$line" | awk '{print $2}')
            if ! grep -q "^Package: ${pkg}$" "${ROOTFS_DIR}/var/lib/dpkg/status" 2>/dev/null; then
                echo -e "  ${YELLOW}[MISSING]${NC} $pkg (REQUIRED) not installed"
                missing=$((missing + 1))
            fi
        done < "${CONFIG_DIR}/gnas-packages.list"
    fi

    if [ $missing -gt 0 ]; then
        echo -e "  ${YELLOW}$missing required packages missing${NC}"
    else
        echo -e "  ${GREEN}All REQUIRED packages installed${NC}"
    fi
}

verify_size() {
    echo ""
    echo "=== Size Analysis ==="
    echo "  Rootfs total size:"
    du -sh "${ROOTFS_DIR}" 2>/dev/null || echo "  N/A"

    echo ""
    echo "  Top 10 largest packages:"
    if [ -f "${ROOTFS_DIR}/var/lib/dpkg/status" ]; then
        # This won't be perfectly accurate from outside chroot, but gives an idea
        du -sh "${ROOTFS_DIR}/usr/lib/"* 2>/dev/null | sort -rh | head -10 | sed 's/^/    /' || true
    fi
}

# -----------------------------------------------------------------------------
# Main
# -----------------------------------------------------------------------------
check_rootfs
verify_services
verify_no_desktop
verify_packages
verify_size

echo ""
echo "=== Verification Complete ==="
