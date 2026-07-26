#!/usr/bin/env bash
set -Eeuo pipefail

readonly ARCHITECTURE="amd64"
readonly DOTNET_RUNTIME="linux-x64"
readonly VERSION="${GNAS_VERSION:?GNAS_VERSION must be set by build.sh}"
readonly SAFE_VERSION="${VERSION//[^a-zA-Z0-9._-]/-}"
readonly IMAGE_BASENAME="gnas-debian12-${SAFE_VERSION}-${ARCHITECTURE}"
readonly BUILD_ROOT="/build/gnas-iso"
readonly LIVE_ROOT="${BUILD_ROOT}/live"
readonly PUBLISH_ROOT="${BUILD_ROOT}/publish"
readonly DOTNET_ROOT="/opt/dotnet"

export DEBIAN_FRONTEND=noninteractive
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export NUGET_PACKAGES="${BUILD_ROOT}/nuget"
export PATH="${DOTNET_ROOT}:${PATH}"

install_builder_dependencies() {
    apt-get -o Acquire::Retries=5 update
    apt-get -o Acquire::Retries=5 install -y --no-install-recommends \
        ca-certificates \
        cpio \
        curl \
        debootstrap \
        dosfstools \
        isolinux \
        libicu72 \
        libssl3 \
        libunwind8 \
        live-build \
        mtools \
        squashfs-tools \
        syslinux-common \
        xorriso
    rm -rf /var/lib/apt/lists/*
}

install_dotnet_sdk() {
    curl --fail --show-error --silent --location \
        --retry 5 \
        --retry-delay 2 \
        --retry-all-errors \
        https://dot.net/v1/dotnet-install.sh \
        --output /tmp/dotnet-install.sh
    chmod 0755 /tmp/dotnet-install.sh
    /tmp/dotnet-install.sh \
        --version "${DOTNET_SDK_VERSION:?DOTNET_SDK_VERSION must be set}" \
        --install-dir "${DOTNET_ROOT}" \
        --no-path
}

publish_gnas() {
    mkdir -p "${PUBLISH_ROOT}/api" "${PUBLISH_ROOT}/cli"

    dotnet publish /workspace/src/GNAS.Api/GNAS.Api.csproj \
        --configuration Release \
        --runtime "${DOTNET_RUNTIME}" \
        --self-contained true \
        --artifacts-path "${BUILD_ROOT}/artifacts" \
        --output "${PUBLISH_ROOT}/api"

    dotnet publish /workspace/src/GNAS.Cli/GNAS.Cli.csproj \
        --configuration Release \
        --runtime "${DOTNET_RUNTIME}" \
        --self-contained true \
        --artifacts-path "${BUILD_ROOT}/artifacts" \
        --output "${PUBLISH_ROOT}/cli"
}

configure_live_image() {
    mkdir -p "${LIVE_ROOT}"
    cd "${LIVE_ROOT}"

    # -----------------------------------------------------------------
    # Detect local package cache (pre-built by bootstrap-debian12.sh)
    # When present, the ISO build avoids downloading packages from the
    # internet — all .deb files are resolved from the local cache.
    # -----------------------------------------------------------------
    local CACHE_DEBS="/workspace/gnas debian12/cache/debs"
    local USE_LOCAL_CACHE=false
    if [[ -d "${CACHE_DEBS}" ]] && compgen -G "${CACHE_DEBS}/*.deb" > /dev/null; then
        USE_LOCAL_CACHE=true
        local CACHED_COUNT
        CACHED_COUNT=$(find "${CACHE_DEBS}" -maxdepth 1 -name '*.deb' | wc -l)
        echo "=== Using local Debian package cache (${CACHED_COUNT} .deb files) ==="
    else
        echo "=== No local cache at '${CACHE_DEBS}'. Packages will be downloaded. ==="
        echo "    To create the cache: cd 'gnas debian12' && bash scripts/bootstrap-debian12.sh debs"
    fi

    lb config \
        --mode debian \
        --distribution bookworm \
        --architecture "${ARCHITECTURE}" \
        --archive-areas "main contrib non-free-firmware" \
        --binary-image iso-hybrid \
        --checksums sha256 \
        --debian-installer live \
        --debian-installer-distribution bookworm \
        --debian-installer-gui true \
        --bootappend-live "boot=live components hostname=gnas locales=en_US.UTF-8,zh_CN.UTF-8 keyboard-layouts=us" \
        --iso-application "GNAS Debian 12 Installer" \
        --iso-publisher "GNAS Project" \
        --iso-volume "GNAS_${SAFE_VERSION:0:20}" \
        --uefi-secure-boot auto

    cp -a /workspace/eng/iso/config/. "${LIVE_ROOT}/config/"
    # Windows checkouts may expose CRLF files through the read-only bind mount.
    # live-build treats the trailing CR as part of package names and unit values,
    # so normalize the copied configuration before it is consumed.
    find "${LIVE_ROOT}/config" -type f -exec sed -i 's/\r$//' {} +
    find "${LIVE_ROOT}/config/hooks" -type f -name '*.hook.chroot' -exec chmod 0755 {} +

    # -----------------------------------------------------------------
    # Package staging — use local cache when available. This replaces
    # per-package 'apt-get download' calls with pre-resolved transitive
    # dependency trees stored in the repository.
    # -----------------------------------------------------------------
    mkdir -p "${LIVE_ROOT}/config/packages.chroot"

    if ${USE_LOCAL_CACHE}; then
        # Stage 1: Copy all cached .deb files into packages.chroot.
        # live-build installs these via dpkg during chroot creation.
        # Because the cache contains the full transitive closure, no
        # network access is needed during lb build.
        cp -a "${CACHE_DEBS}"/*.deb "${LIVE_ROOT}/config/packages.chroot/"

        # Stage 2: Mirror cache into the chroot at a known location
        # and register it as a local apt source. This lets apt satisfy
        # any remaining dependency edges without reaching the network.
        mkdir -p "${LIVE_ROOT}/config/includes.chroot/var/cache/gnas-packages"
        cp -a "${CACHE_DEBS}"/*.deb "${LIVE_ROOT}/config/includes.chroot/var/cache/gnas-packages/"
        if [[ -f "${CACHE_DEBS}/Packages.gz" ]]; then
            cp -a "${CACHE_DEBS}/Packages.gz" "${LIVE_ROOT}/config/includes.chroot/var/cache/gnas-packages/"
        fi
        # Register the local repo inside the build chroot so apt picks it up.
        mkdir -p "${LIVE_ROOT}/config/archives"
        cat > "${LIVE_ROOT}/config/archives/gnas-cache.list.chroot" << 'APTEOF'
deb [trusted=yes] file:/var/cache/gnas-packages ./
APTEOF
        echo "Local cache staged: packages.chroot + file:// apt source."

        # Cache may not include Docker packages (from third-party repo).
        # When missing, download them now so the ISO has a complete set.
        if ! ls "${LIVE_ROOT}/config/packages.chroot/containerd.io_*.deb" >/dev/null 2>&1; then
            echo "Docker packages missing from cache — downloading..."
            install -d -m 0755 /etc/apt/keyrings
            curl --fail --show-error --silent --location \
                --retry 5 --retry-delay 2 --retry-all-errors \
                https://download.docker.com/linux/debian/gpg \
                --output /etc/apt/keyrings/docker.asc
            printf '%s\n' \
                "deb [arch=${ARCHITECTURE} signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/debian bookworm stable" \
                > /etc/apt/sources.list.d/docker.list
            apt-get -o Acquire::Retries=5 update
            (
                cd "${LIVE_ROOT}/config/packages.chroot"
                apt-get download \
                    containerd.io \
                    docker-buildx-plugin \
                    docker-ce \
                    docker-ce-cli \
                    docker-compose-plugin
            )
        fi
    fi

    # -----------------------------------------------------------------
    # Docker GPG key and apt source — always provisioned for the
    # installed system so Docker can receive updates post-install.
    # When the cache is active, Docker packages come from cache/debs/
    # (included above) and no separate download step is needed.
    # -----------------------------------------------------------------
    install -d -m 0755 /etc/apt/keyrings
    if [[ -f "${CACHE_DEBS}/../docker-key.asc" ]]; then
        cp "${CACHE_DEBS}/../docker-key.asc" /etc/apt/keyrings/docker.asc
    else
        curl --fail --show-error --silent --location \
            --retry 5 \
            --retry-delay 2 \
            --retry-all-errors \
            https://download.docker.com/linux/debian/gpg \
            --output /etc/apt/keyrings/docker.asc
    fi
    printf '%s\n' \
        "deb [arch=${ARCHITECTURE} signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/debian bookworm stable" \
        > /etc/apt/sources.list.d/docker.list

    mkdir -p "${LIVE_ROOT}/config/includes.chroot/etc/apt/keyrings"
    cp /etc/apt/keyrings/docker.asc \
        "${LIVE_ROOT}/config/includes.chroot/etc/apt/keyrings/docker.asc"

    # Fallback: download Docker packages from network when cache is absent.
    if ! ${USE_LOCAL_CACHE}; then
        apt-get -o Acquire::Retries=5 update
        (
            cd "${LIVE_ROOT}/config/packages.chroot"
            apt-get download \
                containerd.io \
                docker-buildx-plugin \
                docker-ce \
                docker-ce-cli \
                docker-compose-plugin
        )
    fi

    mkdir -p "${LIVE_ROOT}/config/includes.chroot/opt/gnas"
    cp -a "${PUBLISH_ROOT}/api" "${LIVE_ROOT}/config/includes.chroot/opt/gnas/"
    cp -a "${PUBLISH_ROOT}/cli" "${LIVE_ROOT}/config/includes.chroot/opt/gnas/"
    printf '%s\n' "${VERSION}" > "${LIVE_ROOT}/config/includes.chroot/etc/gnas/version"

    # Stage service trimming script — applies the enabled-services whitelist
    # inside the chroot so only GNAS-required services start at boot.
    local TRIM_SCRIPT="/workspace/gnas debian12/scripts/trim-services.sh"
    if [[ -f "${TRIM_SCRIPT}" ]]; then
        sed -i 's/\r$//' "${TRIM_SCRIPT}" 2>/dev/null || true
        mkdir -p "${LIVE_ROOT}/config/includes.chroot/opt/gnas/scripts"
        cp "${TRIM_SCRIPT}" "${LIVE_ROOT}/config/includes.chroot/opt/gnas/scripts/trim-services.sh"
        chmod 0755 "${LIVE_ROOT}/config/includes.chroot/opt/gnas/scripts/trim-services.sh"
    fi
}

build_image() {
    cd "${LIVE_ROOT}"
    lb build

    local built_image="${LIVE_ROOT}/live-image-${ARCHITECTURE}.hybrid.iso"
    if [[ ! -s "${built_image}" ]]; then
        echo "error: live-build did not produce ${built_image}." >&2
        exit 1
    fi

    install -m 0644 "${built_image}" "/output/${IMAGE_BASENAME}.iso"
    cd /output
    sha256sum "${IMAGE_BASENAME}.iso" > "${IMAGE_BASENAME}.iso.sha256"
    chown "${OUTPUT_UID:?OUTPUT_UID must be set}:${OUTPUT_GID:?OUTPUT_GID must be set}" \
        "${IMAGE_BASENAME}.iso" "${IMAGE_BASENAME}.iso.sha256"
}

install_builder_dependencies
install_dotnet_sdk
publish_gnas
configure_live_image
build_image

echo "Created /output/${IMAGE_BASENAME}.iso"
