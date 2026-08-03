#!/usr/bin/env bash
set -Eeuo pipefail

readonly ISO_PATH="${1:?usage: test-install.sh <iso-path> <result-directory>}"
readonly RESULT_DIR="${2:?result directory is required}"
readonly SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
readonly PRESEED_PATH="${PRESEED_PATH:-${SCRIPT_DIR}/tests/preseed.cfg}"
readonly INSTALL_DIR="${RESULT_DIR}/installer"
readonly DISK_PATH="${RESULT_DIR}/fortos-installed.qcow2"
readonly SERIAL_LOG="${RESULT_DIR}/installer-serial.log"
readonly KERNEL_PATH="${INSTALL_DIR}/vmlinuz"
readonly INITRD_PATH="${INSTALL_DIR}/initrd.gz"
readonly PRESEEDED_INITRD="${INSTALL_DIR}/initrd-preseed.gz"

for command in xorriso qemu-img qemu-system-x86_64 cpio gzip timeout; do
    if ! command -v "${command}" >/dev/null 2>&1; then
        echo "error: ${command} is required for the installation test." >&2
        exit 1
    fi
done

rm -rf "${INSTALL_DIR}"
mkdir -p "${INSTALL_DIR}/preseed"

xorriso -osirrox on -indev "${ISO_PATH}" \
    -extract /install/vmlinuz "${KERNEL_PATH}" \
    -extract /install/initrd.gz "${INITRD_PATH}" \
    >/dev/null 2>&1

cp "${INITRD_PATH}" "${PRESEEDED_INITRD}"
chmod u+w "${PRESEEDED_INITRD}"
cp "${PRESEED_PATH}" "${INSTALL_DIR}/preseed/preseed.cfg"
(
    cd "${INSTALL_DIR}/preseed"
    printf '%s\n' preseed.cfg | cpio --quiet -o -H newc | gzip -9
) >> "${PRESEEDED_INITRD}"

rm -f "${DISK_PATH}" "${SERIAL_LOG}"
qemu-img create -q -f qcow2 "${DISK_PATH}" 20G

# GitHub runner 的嵌套虚拟化不保证可用(/dev/kvm 可能缺失),回退 TCG。
if [[ -e /dev/kvm && -r /dev/kvm ]]; then
    accel_args=(-machine q35,accel=kvm -cpu host)
else
    accel_args=(-machine q35,accel=tcg -cpu qemu64)
    echo "note: /dev/kvm unavailable — using TCG software emulation (slower)."
fi

set +e
timeout 30m qemu-system-x86_64 \
    "${accel_args[@]}" \
    -m 3072 \
    -smp 2 \
    -drive "file=${DISK_PATH},if=virtio,format=qcow2" \
    -cdrom "${ISO_PATH}" \
    -kernel "${KERNEL_PATH}" \
    -initrd "${PRESEEDED_INITRD}" \
    -append "auto=true priority=critical preseed/file=/preseed.cfg console=ttyS0,115200n8 ---" \
    -nic user,model=virtio-net-pci \
    -nographic \
    -no-reboot \
    >"${SERIAL_LOG}" 2>&1
qemu_exit=$?
set -e

if [[ ${qemu_exit} -eq 124 ]]; then
    echo "error: Debian Installer timed out." >&2
    exit 1
fi

if [[ ! -s "${DISK_PATH}" ]]; then
    echo "error: Debian Installer did not produce a virtual disk." >&2
    exit 1
fi

if grep -Eq 'Installation step failed|No root file system|Base system installation error' "${SERIAL_LOG}"; then
    echo "error: Debian Installer reported a failure." >&2
    exit 1
fi

echo "Debian Installer completed and powered off the virtual machine."
