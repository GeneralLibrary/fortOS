#!/usr/bin/env bash
set -Eeuo pipefail

readonly SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
readonly CONFIG_ROOT="${SCRIPT_DIR}/config"

required_files=(
    "package-lists/gnas.list.chroot"
    "includes.chroot/etc/apt/sources.list.d/docker.list"
    "includes.chroot/etc/gnas/gnas.env"
    "includes.chroot/etc/systemd/system/gnas.service"
    "includes.chroot/etc/tmpfiles.d/gnas.conf"
    "hooks/live/0100-gnas-runtime.hook.chroot"
)

for relative_path in "${required_files[@]}"; do
    if [[ ! -s "${CONFIG_ROOT}/${relative_path}" ]]; then
        echo "error: missing ISO configuration file: ${relative_path}" >&2
        exit 1
    fi
done

grep -q '^ExecStart=/opt/gnas/api/GNAS.Api$' \
    "${CONFIG_ROOT}/includes.chroot/etc/systemd/system/gnas.service"
grep -q '^GNAS_DATA_ROOT=/srv/nas$' \
    "${CONFIG_ROOT}/includes.chroot/etc/gnas/gnas.env"
grep -q 'download.docker.com/linux/debian' \
    "${CONFIG_ROOT}/includes.chroot/etc/apt/sources.list.d/docker.list"
grep -q 'download.docker.com/linux/debian/gpg' "${SCRIPT_DIR}/build-in-container.sh"
grep -q 'config/packages.chroot' "${SCRIPT_DIR}/build-in-container.sh"
grep -q 'docker-compose-plugin' "${SCRIPT_DIR}/build-in-container.sh"
grep -q 'CACHE_DEBS' "${SCRIPT_DIR}/build-in-container.sh"
grep -q 'gnas-cache.list.chroot' "${SCRIPT_DIR}/build-in-container.sh"
grep -q '^netplan.io$' "${CONFIG_ROOT}/package-lists/gnas.list.chroot"
grep -q '^network-manager$' "${CONFIG_ROOT}/package-lists/gnas.list.chroot"
grep -q '^libicu72$' "${CONFIG_ROOT}/package-lists/gnas.list.chroot"
grep -q -- '--artifacts-path' "${SCRIPT_DIR}/build-in-container.sh"

if grep -q '^task-.*-desktop$' "${CONFIG_ROOT}/package-lists/gnas.list.chroot"; then
    echo "error: desktop task packages make the release ISO too large." >&2
    exit 1
fi

echo "Debian ISO configuration is complete."
