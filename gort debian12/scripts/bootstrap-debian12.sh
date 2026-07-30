#!/usr/bin/env bash
# =============================================================================
# GORT Debian 12 — Offline Package Cache Builder
# =============================================================================
# Downloads ALL binary .deb packages (including transitive dependencies) that
# GORT needs into cache/debs/. Also downloads Debian source packages into
# cache/sources/ for license compliance / reproducibility.
#
# After running, cache/ contains a self-contained apt repository that the ISO
# build process can consume without any internet access.
#
# Usage:
#   ./bootstrap-debian12.sh [--arch amd64|arm64]
#
# Output:
#   ../cache/debs/      — binary .deb packages + Packages.gz index
#   ../cache/sources/   — Debian source packages (.dsc + tarballs)
# =============================================================================
set -Eeuo pipefail

readonly SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
readonly REPO_ROOT="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
readonly CONFIG_DIR="${REPO_ROOT}/config"
readonly CACHE_DIR="${REPO_ROOT}/cache"
readonly DEBS_DIR="${CACHE_DIR}/debs"
readonly SRC_DIR="${CACHE_DIR}/sources"
readonly ARCH="${ARCH:-amd64}"
readonly DEBIAN_MIRROR="${DEBIAN_MIRROR:-http://deb.debian.org/debian}"
readonly BUILDER_IMAGE="debian:12.11-slim"

echo "=== GORT Debian 12 Package Cache Builder ==="
echo "  Architecture:  ${ARCH}"
echo "  Mirror:        ${DEBIAN_MIRROR}"
echo "  Cache:         ${CACHE_DIR}"

# -----------------------------------------------------------------------------
# Step 1: Prepare directories
# -----------------------------------------------------------------------------
prepare() {
    echo "[1/4] Preparing cache directories..."
    mkdir -p "${DEBS_DIR}" "${SRC_DIR}"
}

# -----------------------------------------------------------------------------
# Step 2: Build a container that downloads all packages
# -----------------------------------------------------------------------------
download_packages() {
    echo "[2/4] Downloading binary .deb packages..."

    # Merge Debian-native packages (exclude Docker, third-party repos, and
    # FUTURE packages not yet in Debian stable). Each category is tried
    # separately so missing FUTURE entries don't block the whole build.
    local debian_pkgs
    debian_pkgs=$(grep -E "^\[(REQUIRED|INDIRECT)\]" "${CONFIG_DIR}/gort-packages.list" | \
        awk '{print $2}' | grep -v "^#" | sort -u | \
        grep -v -E '^(containerd\.io|docker-|minio|mc|borgbackup|restic|zfs|prometheus-)' | \
        tr '\n' ' ')

    local future_pkgs
    future_pkgs=$(grep -E "^\[FUTURE\]" "${CONFIG_DIR}/gort-packages.list" | \
        awk '{print $2}' | grep -v "^#" | sort -u | \
        grep -v -E '^(containerd\.io|docker-|minio|mc|zfs|borgbackup|restic)' | \
        tr '\n' ' ')

    local pkg_count
    pkg_count=$(echo "$debian_pkgs" | wc -w)
    echo "  Debian packages to resolve: ${pkg_count}"

    # Git Bash on Windows needs MSYS_NO_PATHCONV to stop path mangling
    MSYS_NO_PATHCONV=1 docker run --rm \
        --env "DEBIAN_MIRROR=${DEBIAN_MIRROR}" \
        --env "DEBIAN_PKGS=${debian_pkgs}" \
        --env "FUTURE_PKGS=${future_pkgs}" \
        --volume "${CACHE_DIR}:/cache:rw" \
        --workdir /cache \
        "${BUILDER_IMAGE}" \
        bash -c '
            export DEBIAN_FRONTEND=noninteractive

            # Remove the default debian.sources to avoid duplicate source warnings
            rm -f /etc/apt/sources.list.d/debian.sources

            # Configure apt
            cat > /etc/apt/sources.list << EOF
deb ${DEBIAN_MIRROR} bookworm main contrib non-free-firmware
deb ${DEBIAN_MIRROR} bookworm-updates main contrib non-free-firmware
deb ${DEBIAN_MIRROR}-security bookworm-security main contrib non-free-firmware
EOF

            mkdir -p /cache/debs
            apt-get update

            # Phase 1: Download all REQUIRED + INDIRECT packages with full
            # transitive dependency resolution. This is the core set.
            echo "=== Phase 1: Core packages ==="
            set -e
            # shellcheck disable=SC2086
            apt-get install --download-only --yes \
                --option "Dir::Cache::archives=/cache/debs" \
                --option "Acquire::Retries=5" \
                ${DEBIAN_PKGS} 2>&1 | tail -5
            set +e
            echo "Phase 1 complete (exit: $?)."

            # Phase 2: Best-effort download of FUTURE packages. Each package
            # is tried independently — one failing does not block others.
            if [ -n "${FUTURE_PKGS}" ]; then
                echo "=== Phase 2: Future packages (best effort) ==="
                for pkg in ${FUTURE_PKGS}; do
                    apt-get install --download-only --yes \
                        --option "Dir::Cache::archives=/cache/debs" \
                        --option "Acquire::Retries=2" \
                        "$pkg" 2>/dev/null && echo "  [OK]    $pkg" || echo "  [SKIP]  $pkg"
                done
                echo "Phase 2 complete."
            fi

            # Always build apt index — even if some packages failed
            local deb_count
            deb_count=$(find /cache/debs -name "*.deb" 2>/dev/null | wc -l)
            echo ""
            echo "Total packages cached: ${deb_count}"
            du -sh /cache/debs 2>/dev/null || true

            echo ""
            echo "Building local apt repository index..."
            cd /cache
            dpkg-scanpackages debs /dev/null 2>/dev/null | gzip -9c > debs/Packages.gz || {
                echo "WARNING: dpkg-scanpackages failed. Index may be incomplete."
            }
            if [ -f debs/Packages.gz ]; then
                echo "Index built: Packages.gz ($(wc -c < debs/Packages.gz) bytes)"
            fi

            # Clean up
            find /cache/debs -name "*.partial" -delete 2>/dev/null || true
        '
}

# -----------------------------------------------------------------------------
# Step 3: Download Docker packages (from Docker apt repo, not Debian)
# -----------------------------------------------------------------------------
download_docker() {
    echo "[3/4] Downloading Docker packages..."

    MSYS_NO_PATHCONV=1 docker run --rm \
        --volume "${CACHE_DIR}:/cache:rw" \
        --workdir /cache \
        "${BUILDER_IMAGE}" \
        bash -c '
            set -Eeuo pipefail
            export DEBIAN_FRONTEND=noninteractive

            rm -f /etc/apt/sources.list.d/debian.sources
            apt-get update
            apt-get install -y --no-install-recommends curl ca-certificates gnupg

            install -d -m 0755 /etc/apt/keyrings
            curl -fsSL https://download.docker.com/linux/debian/gpg \
                --output /etc/apt/keyrings/docker.asc
            chmod a+r /etc/apt/keyrings/docker.asc

            echo "deb [arch=amd64 signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/debian bookworm stable" \
                > /etc/apt/sources.list.d/docker.list

            apt-get update

            apt-get install --download-only --yes \
                --option "Dir::Cache::archives=/cache/debs" \
                containerd.io \
                docker-buildx-plugin \
                docker-ce \
                docker-ce-cli \
                docker-compose-plugin 2>&1 | tail -10

            # Save Docker GPG key alongside cache for ISO build
            cp /etc/apt/keyrings/docker.asc /cache/docker-key.asc

            # Rebuild apt index
            cd /cache
            dpkg-scanpackages debs /dev/null | gzip -9c > debs/Packages.gz

            local deb_count
            deb_count=$(find /cache/debs -name "*.deb" | wc -l)
            echo ""
            echo "Total packages after Docker: ${deb_count} .deb packages."
            du -sh /cache/debs
        '
}

# -----------------------------------------------------------------------------
# Step 4: Download Debian source packages (for license compliance)
# -----------------------------------------------------------------------------
download_sources() {
    echo "[4/4] Downloading Debian source packages..."

    MSYS_NO_PATHCONV=1 docker run --rm \
        --volume "${CACHE_DIR}:/cache:rw" \
        --workdir /cache \
        "${BUILDER_IMAGE}" \
        bash -c '
            set -Eeuo pipefail
            export DEBIAN_FRONTEND=noninteractive

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

            mkdir -p /cache/sources
            cd /cache/sources

            # Get the list of installed binary packages from the cache
            local pkgs
            pkgs=$(cd /cache/debs && ls *.deb 2>/dev/null | sed "s/_.*//" | sort -u)

            local downloaded=0
            local skipped=0
            for pkg in $pkgs; do
                # Skip lib* packages to save space — their sources are in main packages
                if [[ "$pkg" == lib* ]]; then
                    skipped=$((skipped + 1))
                    continue
                fi
                apt-get source --download-only "$pkg" 2>/dev/null && downloaded=$((downloaded + 1)) || true
            done

            echo ""
            echo "Source packages: ${downloaded} downloaded, ${skipped} skipped (libs)."
            du -sh /cache/sources
        ' 2>&1 | tail -30
}

# -----------------------------------------------------------------------------
# Summary
# -----------------------------------------------------------------------------
summary() {
    echo ""
    echo "=== Build Complete ==="
    echo ""
    if [ -d "${DEBS_DIR}" ]; then
        local deb_count
        deb_count=$(find "${DEBS_DIR}" -name "*.deb" 2>/dev/null | wc -l)
        echo "  Binary .deb packages: ${deb_count}"
        du -sh "${DEBS_DIR}" 2>/dev/null
    fi
    if [ -d "${SRC_DIR}" ]; then
        local src_count
        src_count=$(find "${SRC_DIR}" -name "*.dsc" 2>/dev/null | wc -l)
        echo "  Source packages:      ${src_count}"
        du -sh "${SRC_DIR}" 2>/dev/null
    fi
    echo ""
    echo "  Total cache size:"
    du -sh "${CACHE_DIR}" 2>/dev/null
    echo ""
    echo "Cache is ready at: ${CACHE_DIR}"
    echo "ISO build will auto-detect and use it."
}

# -----------------------------------------------------------------------------
# Main
# -----------------------------------------------------------------------------
case "${1:-all}" in
    prepare)    prepare ;;
    debs)       prepare && download_packages && download_docker && summary ;;
    sources)    download_sources && summary ;;
    all)        prepare && download_packages && download_docker && download_sources && summary ;;
    *)
        echo "Usage: $0 {prepare|debs|sources|all}"
        echo ""
        echo "  prepare   — Create cache directories"
        echo "  debs      — Download binary .deb packages only"
        echo "  sources   — Download Debian source packages only"
        echo "  all       — Download everything (debs + sources)"
        exit 1
        ;;
esac
