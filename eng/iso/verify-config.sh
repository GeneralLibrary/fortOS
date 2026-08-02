#!/usr/bin/env bash
set -Eeuo pipefail

readonly SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
readonly CONFIG_ROOT="${SCRIPT_DIR}/config"

required_files=(
    "package-lists/fortos.list.chroot"
    "includes.chroot/etc/apt/sources.list.d/docker.list"
    "includes.chroot/etc/fortos/fortos.env"
    "includes.chroot/etc/systemd/system/fortos.service"
    "includes.chroot/etc/tmpfiles.d/fortos.conf"
    "hooks/live/0100-fortos-runtime.hook.chroot"
    "bootloaders/syslinux_common/menu.cfg"
    "bootloaders/syslinux_common/live.cfg.in"
    "bootloaders/syslinux_common/install_gui.cfg"
    "bootloaders/grub-pc/grub.cfg"
)

for relative_path in "${required_files[@]}"; do
    if [[ ! -s "${CONFIG_ROOT}/${relative_path}" ]]; then
        echo "error: missing ISO configuration file: ${relative_path}" >&2
        exit 1
    fi
done

grep -q '^ExecStart=/opt/fortos/api/FortOS.Api$' \
    "${CONFIG_ROOT}/includes.chroot/etc/systemd/system/fortos.service"
grep -q '^FortOS_DATA_ROOT=/srv/nas$' \
    "${CONFIG_ROOT}/includes.chroot/etc/fortos/fortos.env"
grep -q 'download.docker.com/linux/debian' \
    "${CONFIG_ROOT}/includes.chroot/etc/apt/sources.list.d/docker.list"
grep -q 'download.docker.com/linux/debian/gpg' "${SCRIPT_DIR}/build-in-container.sh"
grep -q 'config/packages.chroot' "${SCRIPT_DIR}/build-in-container.sh"
grep -q 'docker-compose-plugin' "${SCRIPT_DIR}/build-in-container.sh"
grep -q 'CACHE_DEBS' "${SCRIPT_DIR}/build-in-container.sh"
grep -q 'fortos-cache.list.chroot' "${SCRIPT_DIR}/build-in-container.sh"
grep -q '^netplan.io$' "${CONFIG_ROOT}/package-lists/fortos.list.chroot"
grep -q '^network-manager$' "${CONFIG_ROOT}/package-lists/fortos.list.chroot"
grep -q '^libicu72$' "${CONFIG_ROOT}/package-lists/fortos.list.chroot"
grep -q -- '--artifacts-path' "${SCRIPT_DIR}/build-in-container.sh"

if grep -q '^task-.*-desktop$' "${CONFIG_ROOT}/package-lists/fortos.list.chroot"; then
    echo "error: desktop task packages make the release ISO too large." >&2
    exit 1
fi

# Avalonia installer runtime library packages — keep in sync with the
# required_libs assertion in hooks/live/0150-installer-gui.hook.chroot.
avalon_libs=(
    libice6 libsm6 libx11-6 libxcomposite1 libxcursor1 libxdamage1
    libxext6 libxfixes3 libxi6 libxinerama1 libxrandr2 libxrender1
    libxtst6 libxkbcommon0 libxkbcommon-x11-0 libfontconfig1 libfreetype6
    libgl1 libegl1
)
for libpkg in "${avalon_libs[@]}"; do
    if ! grep -q "^${libpkg}$" "${CONFIG_ROOT}/package-lists/fortos.list.chroot"; then
        echo "error: package missing from fortos.list.chroot: ${libpkg}" >&2
        exit 1
    fi
done

echo "Debian ISO configuration is complete."
