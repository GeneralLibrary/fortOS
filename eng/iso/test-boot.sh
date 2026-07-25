#!/usr/bin/env bash
set -Eeuo pipefail

readonly ISO_PATH="${1:?usage: test-boot.sh <iso-path> <bios|uefi> <result-directory>}"
readonly FIRMWARE="${2:?firmware must be bios or uefi}"
readonly RESULT_DIR="${3:?result directory is required}"
readonly OVMF_CODE="${OVMF_CODE:-/usr/share/OVMF/OVMF_CODE_4M.fd}"
readonly OVMF_VARS_TEMPLATE="${OVMF_VARS_TEMPLATE:-/usr/share/OVMF/OVMF_VARS_4M.fd}"

if [[ "${FIRMWARE}" != "bios" && "${FIRMWARE}" != "uefi" ]]; then
    echo "error: firmware must be bios or uefi." >&2
    exit 1
fi

mkdir -p "${RESULT_DIR}"
readonly SCREENSHOT="${RESULT_DIR}/${FIRMWARE}.ppm"
readonly MONITOR_LOG="${RESULT_DIR}/${FIRMWARE}-monitor.log"
readonly SERIAL_LOG="${RESULT_DIR}/${FIRMWARE}-serial.log"
readonly VARS_FILE="${RESULT_DIR}/${FIRMWARE}-vars.fd"

firmware_args=()
if [[ "${FIRMWARE}" == "uefi" ]]; then
    cp "${OVMF_VARS_TEMPLATE}" "${VARS_FILE}"
    firmware_args=(
        -drive "if=pflash,format=raw,readonly=on,file=${OVMF_CODE}"
        -drive "if=pflash,format=raw,file=${VARS_FILE}"
    )
fi

{
    sleep 25
    printf 'info status\n'
    printf 'screendump %s\n' "${SCREENSHOT}"
    sleep 2
    printf 'quit\n'
} | timeout 45s qemu-system-x86_64 \
    -machine q35,accel=kvm \
    -cpu host \
    -m 2048 \
    -smp 2 \
    -boot order=d \
    -cdrom "${ISO_PATH}" \
    -display vnc=127.0.0.1:99 \
    -monitor stdio \
    -serial "file:${SERIAL_LOG}" \
    -no-reboot \
    "${firmware_args[@]}" \
    >"${MONITOR_LOG}" 2>&1

grep -q 'VM status: running' "${MONITOR_LOG}"
if [[ ! -s "${SCREENSHOT}" ]]; then
    echo "error: ${FIRMWARE} boot did not produce a display frame." >&2
    exit 1
fi

echo "${FIRMWARE} firmware boot reached an active display."

