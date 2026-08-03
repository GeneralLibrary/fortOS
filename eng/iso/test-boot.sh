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

for command in qemu-system-x86_64 python3; do
    if ! command -v "${command}" >/dev/null 2>&1; then
        echo "error: ${command} is required for the boot test." >&2
        exit 1
    fi
done

mkdir -p "${RESULT_DIR}"
readonly SCREENSHOT="${RESULT_DIR}/${FIRMWARE}.ppm"
readonly MONITOR_LOG="${RESULT_DIR}/${FIRMWARE}-monitor.log"
readonly SERIAL_LOG="${RESULT_DIR}/${FIRMWARE}-serial.log"
readonly DIAG_LOG="${RESULT_DIR}/${FIRMWARE}-diag.log"
readonly MONITOR_SOCK="${RESULT_DIR}/${FIRMWARE}-monitor.sock"
readonly VARS_FILE="${RESULT_DIR}/${FIRMWARE}-vars.fd"

rm -f "${SCREENSHOT}" "${MONITOR_LOG}" "${SERIAL_LOG}" "${DIAG_LOG}" "${MONITOR_SOCK}"

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
timeout "${QEMU_TIMEOUT_S}s" qemu-system-x86_64 \
    "${accel_args[@]}" \
    -m 2048 \
    -smp 2 \
    -boot order=d \
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
