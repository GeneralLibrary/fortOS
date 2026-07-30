#!/usr/bin/env bash
# =============================================================================
# Download Debian 12 source packages for all GORT-required packages
# Output: C:\迅雷下载\debian12-sources
# =============================================================================
set -Eeuo pipefail

readonly OUTPUT_DIR="${OUTPUT_DIR:-/c/迅雷下载/debian12-sources}"
readonly SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
readonly PKGLIST="${SCRIPT_DIR}/../config/gort-packages.list"

mkdir -p "${OUTPUT_DIR}"
echo "Output directory: ${OUTPUT_DIR}"

# Extract package names from all categories
echo "=== Extracting package names ==="
pkgs=$(grep -E '^\[(REQUIRED|INDIRECT|FUTURE)\]' "${PKGLIST}" | \
    awk '{print $2}' | grep -v "^#" | sort -u | tr '\n' ' ')
pkg_count=$(echo "$pkgs" | wc -w)
echo "Packages to fetch sources: ${pkg_count}"
echo ""

echo "=== Downloading Debian 12 source packages ==="

MSYS_NO_PATHCONV=1 docker run --rm \
    --env "PKGS=${pkgs}" \
    --volume "${OUTPUT_DIR}:/output:rw" \
    --workdir /output \
    debian:12.11-slim \
    bash -c '
        set -Eeuo pipefail
        export DEBIAN_FRONTEND=noninteractive

        rm -f /etc/apt/sources.list.d/debian.sources
        cat > /etc/apt/sources.list << EOF
deb http://deb.debian.org/debian bookworm main contrib non-free-firmware
deb-src http://deb.debian.org/debian bookworm main contrib non-free-firmware
deb http://deb.debian.org/debian bookworm-updates main contrib non-free-firmware
deb-src http://deb.debian.org/debian bookworm-updates main contrib non-free-firmware
deb http://deb.debian.org/debian-security bookworm-security main contrib non-free-firmware
deb-src http://deb.debian.org/debian-security bookworm-security main contrib non-free-firmware
EOF

        apt-get update
        apt-get install -y --no-install-recommends dpkg-dev

        total=0
        success=0
        failed=0
        total_pkg_count=$(echo "${PKGS}" | wc -w)

        for pkg in ${PKGS}; do
            total=$((total + 1))

            # Skip Docker/non-Debian packages
            if echo "$pkg" | grep -qE "^(containerd\.io|docker-|minio|mc)$"; then
                echo "[SKIP] $pkg (third-party / not in Debian)"
                continue
            fi

            printf "[%3d/%3d] %-40s " "$total" "$total_pkg_count" "$pkg"
            if apt-get source --download-only "$pkg" 2>/dev/null; then
                success=$((success + 1))
                echo "OK"
            else
                # Try using the binary package name to find the source package
                src=$(apt-cache showsrc "$pkg" 2>/dev/null | grep "^Package:" | head -1 | awk "{print \$2}" || echo "")
                if [ -n "$src" ] && [ "$src" != "$pkg" ]; then
                    if apt-get source --download-only "$src" 2>/dev/null; then
                        success=$((success + 1))
                        echo "OK (via $src)"
                        continue
                    fi
                fi
                failed=$((failed + 1))
                echo "FAILED"
            fi
        done

        echo ""
        echo "=== Download Summary ==="
        echo "  Total:     ${total}"
        echo "  Success:   ${success}"
        echo "  Failed:    ${failed}"
        echo ""

        dsc_count=$(find /output -name "*.dsc" 2>/dev/null | wc -l)
        echo "  Source packages (.dsc): ${dsc_count}"
        du -sh /output 2>/dev/null || true
    '

echo ""
echo "=== Done ==="
echo "Source packages in: ${OUTPUT_DIR}"
ls "${OUTPUT_DIR}"/*.dsc 2>/dev/null | wc -l
du -sh "${OUTPUT_DIR}" 2>/dev/null || true
