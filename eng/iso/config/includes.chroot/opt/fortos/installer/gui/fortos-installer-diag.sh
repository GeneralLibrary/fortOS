#!/bin/sh
# -------------------------------------------------------------------------
# FortOS installer GUI diagnostics — for CI smoke testing.
# fortos-installer-diag.service runs after the installer service starts, writing the Avalonia wizard's
# process status to ttyS1. The QEMU boot test (test-boot.sh) collects it via a second -serial
# file: and asserts gui=alive; exits silently on real hardware (no ttyS1).
# -------------------------------------------------------------------------
set -eu

xorg_alive() {
    pgrep -x Xorg >/dev/null 2>&1 || pgrep -x Xorg.wrap >/dev/null 2>&1
}

# 把诊断输出写到所有可用的 CI 串口:amd64 QEMU 用 8250 串口 /dev/ttyS1,
# arm64 QEMU virt 用 PL011 串口 /dev/ttyAMA1;真实硬件无这些串口时静默跳过。
# 先缓冲到临时文件再逐个写出,避免管道被第一个串口消费后其余串口收到空。
diag_tty() {
    local tmp
    tmp="$(mktemp)"
    cat > "${tmp}"
    for tty in /dev/ttyS1 /dev/ttyAMA1; do
        if [ -w "${tty}" ]; then
            cat "${tmp}" > "${tty}" 2>/dev/null || true
        fi
    done
    rm -f "${tmp}"
}

# Mark immediately: even if later steps fail, test-boot.sh can confirm this service ran.
{
    echo "=== FORTOS_INSTALLER_DIAG_START ==="
} | diag_tty

# Wait for Xorg and the Avalonia installer to start.
# Under TCG software emulation, JIT and graphics initialization are far slower than on real hardware; the default
# polling limit is 240 s, so even if the GUI is slow to come up, CI can still collect the final diagnostics before
# the outer timeout. The kernel command line can override this value with FORTOS_DIAG_WAIT_S=<seconds>
# (test-boot.sh 为 arm64 TCG 传入 FORTOS_DIAG_WAIT_S=480)。cmdline 键值对不会自动进入
# systemd 服务的环境变量,因此显式解析 /proc/cmdline。
WAIT_LIMIT="${FORTOS_DIAG_WAIT_S:-240}"
for kv in $(cat /proc/cmdline 2>/dev/null); do
    case "${kv}" in
        FORTOS_DIAG_WAIT_S=*) WAIT_LIMIT="${kv#*=}" ;;
    esac
done
elapsed=0
while [ "${elapsed}" -lt "${WAIT_LIMIT}" ]; do
    if xorg_alive && pgrep -f fortos-installer-gui >/dev/null 2>&1; then
        break
    fi
    sleep 5
    elapsed=$((elapsed + 5))
done

{
    echo "=== FORTOS_INSTALLER_DIAG ==="
    echo "service=$(systemctl is-active fortos-installer.service 2>/dev/null)"
    echo "xorg=$(xorg_alive && echo alive || echo dead)"
    echo "xorg_pid=$(pgrep -x Xorg -o 2>/dev/null || pgrep -x Xorg.wrap -o 2>/dev/null || echo none)"
    echo "gui=$(pgrep -f fortos-installer-gui >/dev/null 2>&1 && echo alive || echo dead)"
    echo "gui_pid=$(pgrep -f fortos-installer-gui -o 2>/dev/null || echo none)"
    echo "--- fortos-installer.service journal (last 40 lines) ---"
    journalctl -u fortos-installer.service -n 40 --no-pager 2>/dev/null \
        | tail -40 || echo "(journal unavailable)"
    echo "--- kiosk / X / installer processes ---"
    ps -ef | grep -E 'xinit|Xorg|openbox|fortos-installer' | grep -v grep \
        | head -15 || true
    echo "--- fortos-installer-gui stderr/stdout (journalctl 全部) ---"
    journalctl -u fortos-installer.service --no-pager 2>/dev/null \
        | grep -iE 'error|exception|fail|killed|crash' | tail -15 || true
    echo "=== FORTOS_INSTALLER_DIAG_END ==="
} | diag_tty
