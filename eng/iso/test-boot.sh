#!/usr/bin/env bash
set -Eeuo pipefail

# -------------------------------------------------------------------------
# test-boot.sh — FortOS ISO boot smoke test (QEMU).
#
# Boots from the ISO's default boot menu (default item "FortOS Graphical Installer" →
# live environment), waits for fortos-installer-diag.service to report the
# Avalonia installer status via ttyS1, asserts that both Xorg and fortos-installer-gui
# are up, then captures a display screenshot. Both BIOS (default) and UEFI (-firmware) modes are covered.
#
# Usage: test-boot.sh <iso-path> <bios|uefi> <result-directory>
# -------------------------------------------------------------------------
readonly ISO_PATH="${1:?usage: test-boot.sh <iso-path> <bios|uefi> <result-directory>}"
readonly FIRMWARE="${2:?firmware must be bios or uefi}"
readonly RESULT_DIR="${3:?result directory is required}"
readonly OVMF_CODE="${OVMF_CODE:-/usr/share/OVMF/OVMF_CODE_4M.fd}"
readonly OVMF_VARS_TEMPLATE="${OVMF_VARS_TEMPLATE:-/usr/share/OVMF/OVMF_VARS_4M.fd}"
readonly AAVMF_CODE="${AAVMF_CODE:-/usr/share/AAVMF/AAVMF_CODE.fd}"
readonly AAVMF_VARS_TEMPLATE="${AAVMF_VARS_TEMPLATE:-/usr/share/AAVMF/AAVMF_VARS.fd}"
# Not readonly: TCG mode relaxes the diagnostics timeout.
DIAG_TIMEOUT_S="${DIAG_TIMEOUT_S:-150}"

# 目标架构:优先 ARCH 环境变量,否则从 ISO 文件名(fortos-debian12-*-<arch>.iso)推断。
readonly ISO_ARCH="${ARCH:-$(basename "${ISO_PATH}" | sed -nE 's/.*-(amd64|arm64)\.iso$/\1/p')}"
if [[ -z "${ISO_ARCH}" ]]; then
    echo "error: cannot determine target architecture from '${ISO_PATH}' — set ARCH=amd64|arm64." >&2
    exit 1
fi
case "${ISO_ARCH}" in
    amd64|arm64) ;;
    *) echo "error: unsupported ARCH '${ISO_ARCH}' (expected amd64 or arm64)." >&2; exit 1 ;;
esac

if [[ "${FIRMWARE}" != "bios" && "${FIRMWARE}" != "uefi" ]]; then
    echo "error: firmware must be bios or uefi." >&2
    exit 1
fi
# arm64 (AArch64) 无 BIOS 引导路径(live-build 生成纯 UEFI ISO),只测 uefi。
if [[ "${ISO_ARCH}" == "arm64" && "${FIRMWARE}" != "uefi" ]]; then
    echo "error: arm64 ISO boots via UEFI only (no BIOS/grub-pc on AArch64)." >&2
    exit 1
fi

# 架构相关的 QEMU 可执行文件、UEFI 固件与控制台设备名。
# amd64:qemu-system-x86_64 + OVMF,串口为 8250(ttyS0/ttyS1);
# arm64:qemu-system-aarch64 + AAVMF,virt 板的 PL011 串口为 ttyAMA0/ttyAMA1。
if [[ "${ISO_ARCH}" == "amd64" ]]; then
    readonly QEMU_BIN="qemu-system-x86_64"
    readonly UEFI_CODE="${OVMF_CODE}"
    readonly UEFI_VARS_TEMPLATE="${OVMF_VARS_TEMPLATE}"
    readonly CONSOLE_ARGS="console=tty0 console=ttyS0,115200n8 earlycon"
else
    readonly QEMU_BIN="qemu-system-aarch64"
    readonly UEFI_CODE="${AAVMF_CODE}"
    readonly UEFI_VARS_TEMPLATE="${AAVMF_VARS_TEMPLATE}"
    readonly CONSOLE_ARGS="console=ttyAMA0 earlycon=pl011,0x09000000"
fi

for command in "${QEMU_BIN}" python3 xorriso isoinfo; do
    if ! command -v "${command}" >/dev/null 2>&1; then
        echo "error: ${command} is required for the boot test (apt-get install genisoimage)." >&2
        exit 1
    fi
done

mkdir -p "${RESULT_DIR}"
readonly BOOT_DIR="${RESULT_DIR}/boot"
readonly SCREENSHOT="${RESULT_DIR}/${FIRMWARE}.ppm"
readonly MONITOR_LOG="${RESULT_DIR}/${FIRMWARE}-monitor.log"
readonly SERIAL_LOG="${RESULT_DIR}/${FIRMWARE}-serial.log"
readonly DIAG_LOG="${RESULT_DIR}/${FIRMWARE}-diag.log"
readonly MONITOR_SOCK="${RESULT_DIR}/${FIRMWARE}-monitor.sock"
readonly VARS_FILE="${RESULT_DIR}/${FIRMWARE}-vars.fd"
readonly VMLINUZ="${BOOT_DIR}/vmlinuz"
readonly INITRD="${BOOT_DIR}/initrd.img"
# arm64 TCG 模拟比 x86 慢,放宽 diag 服务等待 GUI 的轮询上限(内核 cmdline 参数)。
extra_boot_args=""
if [[ "${ISO_ARCH}" == "arm64" ]]; then
    extra_boot_args="FORTOS_DIAG_WAIT_S=480"
fi
readonly LIVE_BOOT_APPEND="boot=live components hostname=fortos locales=en_US.UTF-8,zh_CN.UTF-8 keyboard-layouts=us ${extra_boot_args} ${CONSOLE_ARGS}"

rm -f "${SCREENSHOT}" "${MONITOR_LOG}" "${SERIAL_LOG}" "${DIAG_LOG}" "${MONITOR_SOCK}"

# -------------------------------------------------------------------------
# Extract the live kernel and initrd from the ISO and pass them directly to QEMU (-kernel/-initrd/-append),
# bypassing the ISO bootloader (isolinux/GRUB). Under TCG software emulation the ISO bootloader can stop
# responding because of vesamenu.c32 initialization or timeout configuration issues, so the kernel never
# boots and the serial log stays empty. Direct boot reproduces the same kernel command line as the bootloader
# (boot=live ...), advancing kernel startup by 30-40 seconds (skipping the boot menu countdown) and giving
# the 420 s diagnostics window ample room.
# -------------------------------------------------------------------------
mkdir -p "${BOOT_DIR}"
# 提取 live 内核/initrd 直接传给 QEMU(-kernel/-initrd/-append),绕过 ISO 引导器
# (isolinux/GRUB),避免 TCG 模拟下引导器卡在菜单倒计时。
# amd64 的 live-build 固定命名 /live/vmlinuz、/live/initrd.img;
# arm64 的命名带内核版本后缀(vmlinuz-<ver>-arm64 / initrd.img-<ver>-arm64),
# 需先从 ISO 的 RockRidge 目录里解析实际文件名,再交给 xorriso 提取。
_iso_extract() {
    xorriso -osirrox on -indev "${ISO_PATH}" -extract "${1}" "${2}" \
        >/dev/null 2>&1
}
if [[ "${ISO_ARCH}" == "arm64" ]]; then
    iso_listing="$(isoinfo -i "${ISO_PATH}" -R -f 2>/dev/null | tr '[:upper:]' '[:lower:]' | sed 's/;[0-9]*$//')"
    arm64_vmlinuz="$(grep '^/live/vmlinuz-' <<<"${iso_listing}" | head -1 || true)"
    arm64_initrd="$(grep '^/live/initrd.img-' <<<"${iso_listing}" | head -1 || true)"
    if [[ -z "${arm64_vmlinuz}" || -z "${arm64_initrd}" ]]; then
        echo "error: arm64 live kernel/initrd not found in ISO." >&2
        exit 1
    fi
    _iso_extract "${arm64_vmlinuz}" "${VMLINUZ}" \
        || { echo "error: failed to extract live vmlinuz from ISO." >&2; exit 1; }
    _iso_extract "${arm64_initrd}" "${INITRD}" \
        || { echo "error: failed to extract live initrd.img from ISO." >&2; exit 1; }
else
    _iso_extract /live/vmlinuz "${VMLINUZ}" \
        || _iso_extract /isolinux/live/vmlinuz "${VMLINUZ}" \
        || { echo "error: live vmlinuz not found in ISO." >&2; exit 1; }
    _iso_extract /live/initrd.img "${INITRD}" \
        || _iso_extract /isolinux/live/initrd.img "${INITRD}" \
        || { echo "error: live initrd.img not found in ISO." >&2; exit 1; }
fi

# Validate after extraction: an empty file or a non-kernel image makes QEMU hang silently for 420 s with no output,
# so fail early and write the actual content (type/size) to the log to help diagnose extraction problems.
# arm64 的 Debian live 内核是 gzip 压缩的 ARM64 Image(file 输出 gzip 而非 'Linux kernel'),
# 先解压为未压缩 Image 再校验/传给 QEMU,兼容性最好。
if [[ ! -s "${VMLINUZ}" ]]; then
    echo "error: extracted vmlinuz is empty or missing: ${VMLINUZ}" >&2
    exit 1
fi
if [[ "${ISO_ARCH}" == "arm64" ]]; then
    if file "${VMLINUZ}" | grep -q 'gzip compressed data'; then
        gunzip -c "${VMLINUZ}" > "${BOOT_DIR}/vmlinuz.image"
        readonly VMLINUZ_KERNEL="${BOOT_DIR}/vmlinuz.image"
    else
        readonly VMLINUZ_KERNEL="${VMLINUZ}"
    fi
    if ! file "${VMLINUZ_KERNEL}" | grep -Eq 'Linux kernel ARM64|PE32\+ executable.*Aarch64'; then
        echo "error: extracted vmlinuz is not an arm64 kernel image: $(file "${VMLINUZ_KERNEL}")" >&2
        exit 1
    fi
else
    readonly VMLINUZ_KERNEL="${VMLINUZ}"
    if ! file "${VMLINUZ_KERNEL}" | grep -q 'Linux kernel'; then
        echo "error: extracted vmlinuz is not a Linux kernel image: $(file "${VMLINUZ_KERNEL}")" >&2
        exit 1
    fi
fi
if [[ ! -s "${INITRD}" ]]; then
    echo "error: extracted initrd.img is empty or missing: ${INITRD}" >&2
    exit 1
fi

firmware_args=()
if [[ "${FIRMWARE}" == "uefi" ]]; then
    cp "${UEFI_VARS_TEMPLATE}" "${VARS_FILE}"
    firmware_args=(
        -drive "if=pflash,format=raw,readonly=on,file=${UEFI_CODE}"
        -drive "if=pflash,format=raw,file=${VARS_FILE}"
    )
fi

# -------------------------------------------------------------------------
# Accelerator selection: nested virtualization on GitHub-hosted runners is experimental (/dev/kvm
# is not guaranteed to exist); fall back to TCG software emulation when KVM is unavailable (slower, relaxed diagnostics timeout).
# amd64 用 q35 + qemu64 CPU;arm64 用 virt + cortex-a57(virt 板没有 q35)。
# -------------------------------------------------------------------------
if [[ "${ISO_ARCH}" == "amd64" ]]; then
    accel_args=(-machine q35,accel=tcg -cpu qemu64)
    accel_name="tcg"
    if [[ -e /dev/kvm && -r /dev/kvm ]]; then
        accel_args=(-machine q35,accel=kvm -cpu host)
        accel_name="kvm"
    fi
else
    accel_args=(-machine virt,accel=tcg -cpu cortex-a57)
    accel_name="tcg"
    if [[ -e /dev/kvm && -r /dev/kvm ]]; then
        accel_args=(-machine virt,accel=kvm -cpu host)
        accel_name="kvm"
    fi
fi
echo "Boot test: ${FIRMWARE} firmware (${ISO_ARCH}), accelerator=${accel_name}"

if [[ "${accel_name}" == "tcg" ]]; then
    DIAG_TIMEOUT_S=420
    QEMU_TIMEOUT_S=600
    if [[ "${ISO_ARCH}" == "arm64" ]]; then
        # arm64 在 TCG 下比 x86 更慢(Xorg/Avalonia 启动),放宽诊断窗口。
        DIAG_TIMEOUT_S=600
        QEMU_TIMEOUT_S=900
    fi
else
    QEMU_TIMEOUT_S=240
fi

# Start QEMU in the background: UART0 keeps kernel logs, UART1 receives GUI diagnostics output, monitor over a unix socket.
# 光驱:amd64/q35 支持 -cdrom(if=ide→AHCI);arm64/virt 无 IDE 总线,需显式
# virtio-scsi + scsi-cd(guest 中为 /dev/sr0,live-boot 按 ISO label 找到 squashfs)。
# -cdrom 让 live-boot initramfs 定位 squashfs;内核用 -kernel/-initrd 直启。
cdrom_args=(-cdrom "${ISO_PATH}")
if [[ "${ISO_ARCH}" == "arm64" ]]; then
    cdrom_args=(
        -drive "file=${ISO_PATH},media=cdrom,readonly=on,if=none,id=fortos-cd"
        -device "virtio-scsi-device,id=fortos-scsi"
        -device "scsi-cd,drive=fortos-cd"
    )
fi
# earlycon prints kernel messages before the serial driver is fully initialized, helping diagnose early crashes.
# 第二个输出通道:amd64 的 q35 板有第二个 8250 串口(ttyS1)直接对应第二个 -serial;
# arm64 的 virt 板只有一个 NS PL011(ttyAMA0),第二个 -serial 不会产生任何设备,
# 因此 arm64 改用 virtio-serial 端口(guest 中为 /dev/vport0p1),diag 服务写它。
if [[ "${ISO_ARCH}" == "arm64" ]]; then
    diag_args=(
        -chardev "file,id=fortos-diag,path=${DIAG_LOG}"
        -device "virtio-serial-device,id=fortos-vser"
        -device "virtio-serial-port,chardev=fortos-diag,name=fortos.diag"
    )
else
    diag_args=(-serial "file:${DIAG_LOG}")
fi
timeout "${QEMU_TIMEOUT_S}s" "${QEMU_BIN}" \
    "${accel_args[@]}" \
    -m 2048 \
    -smp 2 \
    -kernel "${VMLINUZ_KERNEL}" \
    -initrd "${INITRD}" \
    -append "${LIVE_BOOT_APPEND}" \
    "${cdrom_args[@]}" \
    -display vnc=127.0.0.1:99 \
    -monitor "unix:${MONITOR_SOCK},server,nowait" \
    -serial "file:${SERIAL_LOG}" \
    "${diag_args[@]}" \
    -no-reboot \
    "${firmware_args[@]}" \
    >"${MONITOR_LOG}" 2>&1 &
qemu_pid=$!

cleanup() {
    kill "${qemu_pid}" 2>/dev/null || true
    wait "${qemu_pid}" 2>/dev/null || true
}
trap cleanup EXIT

# Execute actions through the QEMU monitor socket (screendump <file> / quit).
monitor_cmd() {
    local action="${1:?action}"
    local arg="${2:-}"
    python3 - "${MONITOR_SOCK}" "${action}" "${arg}" <<'PYEOF'
import socket, sys, time
sock_path, action = sys.argv[1], sys.argv[2]
arg = sys.argv[3] if len(sys.argv) > 3 else ""
s = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
for _ in range(50):
    try:
        s.connect(sock_path)
        break
    except OSError:
        time.sleep(0.2)
else:
    sys.exit(2)
time.sleep(0.5)
if action == "screendump":
    s.sendall(("screendump %s\n" % arg).encode())
elif action == "quit":
    s.sendall(b"info status\n")
    s.sendall(b"quit\n")
time.sleep(1)
s.close()
PYEOF
}

# Wait for fortos-installer-diag.service to report GUI status.
# Take a screenshot every ~60s while waiting; on timeout the screenshots show which stage it is stuck on.
shot_no=0
loop=0
deadline=$((SECONDS + DIAG_TIMEOUT_S))
while (( SECONDS < deadline )); do
    if grep -q 'FORTOS_INSTALLER_DIAG_END' "${DIAG_LOG}" 2>/dev/null; then
        break
    fi
    if ! kill -0 "${qemu_pid}" 2>/dev/null; then
        echo "error: QEMU exited before diagnostics — ${MONITOR_LOG}:" >&2
        cat "${MONITOR_LOG}" 2>/dev/null >&2 || true
        echo "--- ttyS0 (kernel) tail ---" >&2
        tail -40 "${SERIAL_LOG}" 2>/dev/null >&2 || true
        exit 1
    fi
    if (( loop > 0 && loop % 20 == 0 && shot_no < 6 )); then
        shot_no=$((shot_no + 1))
        monitor_cmd screendump "${RESULT_DIR}/${FIRMWARE}-shot-${shot_no}.ppm" \
            || echo "warning: screenshot ${shot_no} failed" >&2
    fi
    loop=$((loop + 1))
    sleep 3
done

if ! grep -q 'FORTOS_INSTALLER_DIAG_END' "${DIAG_LOG}" 2>/dev/null; then
    echo "error: FortOS installer GUI diagnostics never completed within ${DIAG_TIMEOUT_S}s." >&2
    echo "--- QEMU monitor log (qemu stdout/stderr) ---" >&2
    cat "${MONITOR_LOG}" 2>/dev/null >&2 || echo "(no QEMU output captured)" >&2
    echo "--- QEMU process state ---" >&2
    if kill -0 "${qemu_pid}" 2>/dev/null; then
        echo "qemu still running (pid ${qemu_pid})" >&2
    else
        echo "qemu exited — monitor log above shows why" >&2
    fi
    # Read the guest CPU state via the monitor socket: if RIP is stuck near the reset vector
    # (0xfffffff0), the kernel was never jumped to by QEMU (-kernel was loaded but the guest never
    # ran); if RIP is inside the kernel code segment, the kernel is stuck while executing. This is the
    # decisive evidence distinguishing "QEMU did not start the guest" from "the guest crashed after boot".
    echo "--- QEMU guest state (via monitor socket) ---" >&2
    python3 - "${MONITOR_SOCK}" <<'PYEOF' || echo "(monitor socket unavailable)" >&2
import socket, sys, time
s = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
try:
    s.connect(sys.argv[1])
except OSError:
    sys.exit(1)
s.settimeout(5)
for cmd in (b"info status\n", b"info registers\n"):
    try:
        s.sendall(cmd)
        time.sleep(0.5)
        data = s.recv(65536)
        print(data.decode(errors="replace"))
    except Exception as e:
        print("(error reading %r: %s)" % (cmd, e))
s.close()
PYEOF
    echo "--- ttyS1 (diag) ---" >&2
    cat "${DIAG_LOG}" 2>/dev/null >&2 || true
    echo "--- ttyS0 (kernel) tail ---" >&2
    tail -40 "${SERIAL_LOG}" 2>/dev/null >&2 || true
    echo "--- screenshots (see which stage the boot reached) ---" >&2
    ls -1 "${RESULT_DIR}"/${FIRMWARE}-shot-*.ppm 2>/dev/null >&2 || echo "(none)" >&2
    exit 1
fi

# Capture the final display frame and exit the virtual machine.
monitor_cmd screendump "${SCREENSHOT}"
monitor_rc=$?
monitor_cmd quit
wait "${qemu_pid}" 2>/dev/null || true
trap - EXIT

if [[ ${monitor_rc} -ne 0 ]]; then
    echo "error: could not reach the QEMU monitor socket." >&2
    exit 1
fi

# Assertions: both the GUI and Xorg are up, and a display frame was produced.
echo "--- FortOS installer GUI diagnostics ---"
cat "${DIAG_LOG}"
grep -q 'gui=alive' "${DIAG_LOG}" \
    || { echo "error: fortos-installer-gui is not running." >&2; exit 1; }
grep -q 'xorg=alive' "${DIAG_LOG}" \
    || { echo "error: Xorg is not running." >&2; exit 1; }
if [[ ! -s "${SCREENSHOT}" ]]; then
    echo "error: ${FIRMWARE} boot did not produce a display frame." >&2
    exit 1
fi

echo "${FIRMWARE} firmware boot reached the FortOS graphical installer (GUI alive)."
