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
    # Install Docker as local packages through live-build's packages.chroot
    # mechanism. Packages added later from a hook are classified as live-only
    # and Debian Installer removes them from the installed target.
    install -d -m 0755 /etc/apt/keyrings
    curl --fail --show-error --silent --location \
        --retry 5 \
        --retry-delay 2 \
        --retry-all-errors \
        https://download.docker.com/linux/debian/gpg \
        --output /etc/apt/keyrings/docker.asc
    printf '%s\n' \
        "deb [arch=${ARCHITECTURE} signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/debian bookworm stable" \
        > /etc/apt/sources.list.d/docker.list
    apt-get -o Acquire::Retries=5 update
    mkdir -p "${LIVE_ROOT}/config/packages.chroot"
    (
        cd "${LIVE_ROOT}/config/packages.chroot"
        apt-get download \
            containerd.io \
            docker-buildx-plugin \
            docker-ce \
            docker-ce-cli \
            docker-compose-plugin
    )
    mkdir -p "${LIVE_ROOT}/config/includes.chroot/etc/apt/keyrings"
    cp /etc/apt/keyrings/docker.asc \
        "${LIVE_ROOT}/config/includes.chroot/etc/apt/keyrings/docker.asc"
    mkdir -p "${LIVE_ROOT}/config/includes.chroot/opt/gnas"
    cp -a "${PUBLISH_ROOT}/api" "${LIVE_ROOT}/config/includes.chroot/opt/gnas/"
    cp -a "${PUBLISH_ROOT}/cli" "${LIVE_ROOT}/config/includes.chroot/opt/gnas/"
    printf '%s\n' "${VERSION}" > "${LIVE_ROOT}/config/includes.chroot/etc/gnas/version"
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
