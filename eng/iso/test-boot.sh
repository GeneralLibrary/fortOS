#!/usr/bin/env bash
set -Eeuo pipefail

# -------------------------------------------------------------------------
# test-boot.sh — FortOS ISO 引导冒烟测试(QEMU)。
#
# 从 ISO 的默认 boot 菜单启动(默认项「FortOS Graphical Installer」→
# live 环境),等待 fortos-installer-diag.service 通过 ttyS1 报告
# Avalonia 安装器状态,断言 Xorg 与 fortos-installer-gui 均已拉起,
# 最后抓取显示截图。BIOS(默认)与 UEFI(-firmware)两种模式都覆盖。
#
# 用法:test-boot.sh <iso-path> <bios|uefi> <result-directory>
# -------------------------------------------------------------------------
readonly ISO_PATH="${1:?usage: test-boot.sh <iso-path> <bios|uefi> <result-directory>}"
readonly FIRMWARE="${2:?firmware must be bios or uefi}"
readonly RESULT_DIR="${3:?result directory is required}"
readonly OVMF_CODE="${OVMF_CODE:-/usr/share/OVMF/OVMF_CODE_4M.fd}"
readonly OVMF_VARS_TEMPLATE="${OVMF_VARS_TEMPLATE:-/usr/share/OVMF/OVMF_VARS_4M.fd}"
# 非 readonly:TCG 模式会放宽诊断超时。
DIAG_TIMEOUT_S="${DIAG_TIMEOUT_S:-150}"

if [[ "${FIRMWARE}" != "bios" && "${FIRMWARE}" != "uefi" ]]; then
    echo "error: firmware must be bios or uefi." >&2
    exit 1
fi

for command in qemu-system-x86_64 python3 xorriso; do
    if ! command -v "${command}" >/dev/null 2>&1; then
        echo "error: ${command} is required for the boot test." >&2
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
readonly LIVE_BOOT_APPEND="boot=live components hostname=fortos locales=en_US.UTF-8,zh_CN.UTF-8 keyboard-layouts=us console=tty0 console=ttyS0,115200n8 earlycon"

rm -f "${SCREENSHOT}" "${MONITOR_LOG}" "${SERIAL_LOG}" "${DIAG_LOG}" "${MONITOR_SOCK}"

# -------------------------------------------------------------------------
# 从 ISO 中提取 live 内核和 initrd,直接传给 QEMU(-kernel/-initrd/-append),
# 绕过 ISO 引导器(isolinux/GRUB)。ISO 引导器在 TCG 软件仿真模式下会因
# vesamenu.c32 初始化或超时配置问题停止响应,导致内核从未启动、串口日志
# 为空。直接引导可复现与引导器相同的内核命令行(boot=live …),同时将
# 内核启动推前 30-40 秒(省去引导菜单倒计时),让 420 s 诊断窗口充裕。
# -------------------------------------------------------------------------
mkdir -p "${BOOT_DIR}"
_iso_extract() {
    xorriso -osirrox on -indev "${ISO_PATH}" -extract "${1}" "${2}" \
        >/dev/null 2>&1
}
_iso_extract /live/vmlinuz "${VMLINUZ}" \
    || _iso_extract /isolinux/live/vmlinuz "${VMLINUZ}" \
    || { echo "error: live vmlinuz not found in ISO." >&2; exit 1; }
_iso_extract /live/initrd.img "${INITRD}" \
    || _iso_extract /isolinux/live/initrd.img "${INITRD}" \
    || { echo "error: live initrd.img not found in ISO." >&2; exit 1; }

# 提取后校验:空文件或非内核镜像会令 QEMU 静默挂起 420 s 而无任何输出,
# 尽早失败并把实际内容(类型/大小)写进日志,便于定位提取环节的问题。
if [[ ! -s "${VMLINUZ}" ]]; then
    echo "error: extracted vmlinuz is empty or missing: ${VMLINUZ}" >&2
    exit 1
fi
if ! file "${VMLINUZ}" | grep -q 'Linux kernel'; then
    echo "error: extracted vmlinuz is not a Linux kernel image: $(file "${VMLINUZ}")" >&2
    exit 1
fi
if [[ ! -s "${INITRD}" ]]; then
    echo "error: extracted initrd.img is empty or missing: ${INITRD}" >&2
    exit 1
fi

firmware_args=()
if [[ "${FIRMWARE}" == "uefi" ]]; then
    cp "${OVMF_VARS_TEMPLATE}" "${VARS_FILE}"
    firmware_args=(
        -drive "if=pflash,format=raw,readonly=on,file=${OVMF_CODE}"
        -drive "if=pflash,format=raw,file=${VARS_FILE}"
    )
fi

# -------------------------------------------------------------------------
# 加速器选择:GitHub-hosted runner 的嵌套虚拟化是实验性支持(/dev/kvm
# 不保证存在),KVM 不可用时回退 TCG 软件模拟(慢,放宽诊断超时)。
# -------------------------------------------------------------------------
accel_args=(-machine q35,accel=tcg -cpu qemu64)
accel_name="tcg"
if [[ -e /dev/kvm && -r /dev/kvm ]]; then
    accel_args=(-machine q35,accel=kvm -cpu host)
    accel_name="kvm"
fi
echo "Boot test: ${FIRMWARE} firmware, accelerator=${accel_name}"

if [[ "${accel_name}" == "tcg" ]]; then
    DIAG_TIMEOUT_S=420
    QEMU_TIMEOUT_S=600
else
    QEMU_TIMEOUT_S=240
fi

# 后台启动 QEMU:ttyS0 留内核日志,ttyS1 收 GUI 诊断输出,monitor 走 unix socket。
# -cdrom 挂载 ISO 供 live-boot initramfs 定位 squashfs;引导由 -kernel/-initrd 完成。
# earlycon 在串行驱动完整初始化之前就输出内核消息,便于诊断早期崩溃。
timeout "${QEMU_TIMEOUT_S}s" qemu-system-x86_64 \
    "${accel_args[@]}" \
    -m 2048 \
    -smp 2 \
    -kernel "${VMLINUZ}" \
    -initrd "${INITRD}" \
    -append "${LIVE_BOOT_APPEND}" \
    -cdrom "${ISO_PATH}" \
    -display vnc=127.0.0.1:99 \
    -monitor "unix:${MONITOR_SOCK},server,nowait" \
    -serial "file:${SERIAL_LOG}" \
    -serial "file:${DIAG_LOG}" \
    -no-reboot \
    "${firmware_args[@]}" \
    >"${MONITOR_LOG}" 2>&1 &
qemu_pid=$!

cleanup() {
    kill "${qemu_pid}" 2>/dev/null || true
    wait "${qemu_pid}" 2>/dev/null || true
}
trap cleanup EXIT

# 通过 QEMU monitor socket 执行动作(screendump <file> / quit)。
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

# 等待 fortos-installer-diag.service 报告 GUI 状态。
# 等待期间每 ~60s 截一张图,超时后可凭截图判断卡在哪个阶段。
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
    echo "--- ttyS1 (diag) ---" >&2
    cat "${DIAG_LOG}" 2>/dev/null >&2 || true
    echo "--- ttyS0 (kernel) tail ---" >&2
    tail -40 "${SERIAL_LOG}" 2>/dev/null >&2 || true
    echo "--- screenshots (see which stage the boot reached) ---" >&2
    ls -1 "${RESULT_DIR}"/${FIRMWARE}-shot-*.ppm 2>/dev/null >&2 || echo "(none)" >&2
    exit 1
fi

# 抓取最终显示帧并退出虚拟机。
monitor_cmd screendump "${SCREENSHOT}"
monitor_rc=$?
monitor_cmd quit
wait "${qemu_pid}" 2>/dev/null || true
trap - EXIT

if [[ ${monitor_rc} -ne 0 ]]; then
    echo "error: could not reach the QEMU monitor socket." >&2
    exit 1
fi

# 断言:GUI 与 Xorg 均已拉起,且有显示帧。
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
